using System.IO;
using Lumenfall.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Renders the devs' room developers (<see cref="DevChibis"/>) as they are built in play, dressed and in their idle
    /// pose, to <c>Logs/DevPreviews</c>: each one from the front and at an angle, and the three side by side. It uses a
    /// scratch scene, so the open scene is left as it was. Also runs headless with
    /// <c>-executeMethod Lumenfall.EditorTools.DevChibiPreview.Render</c>.
    /// </summary>
    public static class DevChibiPreview
    {
        private const string PreviewFolder = "Logs/DevPreviews";
        private const int PreviewLayer = 31;
        private const float IdleSampleTime = 1.2f;

        [MenuItem("Lumenfall/Secrets/Render Dev Previews")]
        public static void Render()
        {
            SecretRoomArt art = SecretRoomArt.Load();
            if (art == null) { Debug.LogError("SecretRoomArt is missing from Resources."); return; }
            Directory.CreateDirectory(PreviewFolder);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            bool batcher = urp != null && urp.useSRPBatcher;
            // Single-frame editor renders can reuse one material's constant buffer across draws with the SRP batcher.
            if (urp != null) urp.useSRPBatcher = false;
            // An untitled, unchanged scene (as in a headless run) is used as is; otherwise a scratch scene is added.
            Scene previous = SceneManager.GetActiveScene();
            bool reuseUntitled = string.IsNullOrEmpty(previous.path) && !previous.isDirty;
            Scene scratch = reuseUntitled ? previous : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scratch);
            try { RenderAll(art); }
            finally
            {
                if (!reuseUntitled)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scratch, true);
                }
                if (urp != null) urp.useSRPBatcher = batcher;
            }
            Debug.Log($"Rendered the devs to {Path.GetFullPath(PreviewFolder)}.");
        }

        private static void RenderAll(SecretRoomArt art)
        {
            var light = new GameObject("Key Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(40f, 160f, 0f);
            light.cullingMask = 1 << PreviewLayer;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.58f, 0.64f);

            var camera = new GameObject("Preview Camera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.86f, 0.88f, 0.92f);
            camera.cullingMask = 1 << PreviewLayer;
            camera.orthographic = true;

            (DevChibis.Look look, GameObject prefab)[] devs =
            {
                (DevChibis.Glasses, art.devGlasses), (DevChibis.Nick, art.devNick), (DevChibis.Curls, art.devCurls),
            };
            GameObject[] built = new GameObject[devs.Length];
            try
            {
                for (int i = 0; i < devs.Length; i++)
                {
                    built[i] = DevChibis.Create(devs[i].prefab, devs[i].look, null);
                    if (built[i] == null) { Debug.LogError(devs[i].look.Name + " has no base model in SecretRoomArt."); continue; }
                    PoseIdle(built[i]);
                    DevChibis.Dress(built[i], devs[i].look);
                    foreach (Transform t in built[i].GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;
                    built[i].SetActive(false);
                }

                // Each on their own: from the front, and turned a little.
                for (int i = 0; i < devs.Length; i++)
                {
                    if (built[i] == null) continue;
                    built[i].SetActive(true);
                    string name = devs[i].look.Name.Replace(" ", "");
                    Shoot(camera, 180f, 1.9f, 560, 720, name + "_front.png");
                    Shoot(camera, 215f, 1.9f, 560, 720, name + "_angle.png");
                    built[i].SetActive(false);
                }

                // The three side by side, in their desk order.
                for (int i = 0; i < devs.Length; i++)
                {
                    if (built[i] == null) continue;
                    built[i].transform.position = new Vector3((i - 1) * 1.3f, 0f, 0f);
                    built[i].SetActive(true);
                }
                Shoot(camera, 190f, 2f, 1200, 720, "Devs.png");
            }
            finally
            {
                foreach (GameObject dev in built) if (dev != null) Object.DestroyImmediate(dev);
                Object.DestroyImmediate(light.gameObject);
                Object.DestroyImmediate(camera.gameObject);
            }
        }

        private static void Shoot(Camera camera, float yaw, float size, int width, int height, string file)
        {
            var target = new RenderTexture(width, height, 24) { antiAliasing = 4 };
            camera.targetTexture = target;
            camera.aspect = width / (float)height;
            camera.orthographicSize = size;
            camera.transform.rotation = Quaternion.Euler(10f, yaw, 0f);
            camera.transform.position = new Vector3(0f, 1.0f, 0f) - camera.transform.forward * 12f;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(PreviewFolder, file), texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            target.Release();
            Object.DestroyImmediate(target);
        }

        // The rig's idle pose, sampled once, so the dressing is fitted as it is in play.
        private static void PoseIdle(GameObject npc)
        {
            Animator animator = npc.GetComponentInChildren<Animator>();
            if (animator == null) return;
            var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
            var idle = controller?.layers[0].stateMachine.defaultState?.motion as AnimationClip;
            if (idle == null) return;
            Transform root = animator.transform;
            root.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            idle.SampleAnimation(animator.gameObject, IdleSampleTime);
            root.SetLocalPositionAndRotation(position, rotation);
        }
    }
}
