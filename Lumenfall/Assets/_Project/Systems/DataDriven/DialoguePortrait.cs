using System;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// Renders the speaking NPC's head live into a texture for the dialogue box. While a speaker is shown, its
    /// renderers move to a dedicated layer that only the portrait camera draws, so the portrait has a clean background;
    /// the world cameras draw every layer, so the NPC still appears in the scene. Layers are restored on hide.
    /// </summary>
    internal sealed class DialoguePortrait : IDisposable
    {
        public const int Layer = 31;
        private const float FieldOfView = 22f;

        private readonly Camera camera;
        private readonly RenderTexture texture;
        private Transform subject;
        private Animator animator;
        private Renderer[] renderers = Array.Empty<Renderer>();
        private Renderer eyes;
        private int[] originalLayers = Array.Empty<int>();

        public Texture Texture => texture;
        public Transform Subject => subject;

        public DialoguePortrait(Transform parent)
        {
            texture = new RenderTexture(512, 512, 24) { name = "Dialogue Portrait", antiAliasing = 4 };
            var host = new GameObject("Dialogue Portrait Camera");
            host.transform.SetParent(parent, false);
            camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.targetTexture = texture;
            camera.cullingMask = 1 << Layer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 20f;
        }

        /// <summary>Shows a speaker; returns false when it has no renderers to draw.</summary>
        public bool Show(Transform speaker, Color background)
        {
            camera.backgroundColor = background;
            if (speaker == subject) return subject != null;
            Hide();
            Renderer[] found = speaker.GetComponentsInChildren<Renderer>();
            if (found.Length == 0) return false;

            subject = speaker;
            animator = speaker.GetComponentInChildren<Animator>();
            renderers = found;
            // Both the cast's chibi rig and the Chibi Witch have a separate eye mesh; it pins the face exactly,
            // whereas the model's overall top includes hats and hair.
            eyes = Array.Find(found, r => r.name.IndexOf("eye", StringComparison.OrdinalIgnoreCase) >= 0);
            originalLayers = new int[found.Length];
            for (int i = 0; i < found.Length; i++)
            {
                originalLayers[i] = found[i].gameObject.layer;
                found[i].gameObject.layer = Layer;
            }

            camera.enabled = true;
            Frame();
            return true;
        }

        public void Hide()
        {
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].gameObject.layer = originalLayers[i];
            renderers = Array.Empty<Renderer>();
            originalLayers = Array.Empty<int>();
            subject = null;
            animator = null;
            eyes = null;
            camera.enabled = false;
        }

        /// <summary>Keeps the camera on the speaker's face as it idles and turns.</summary>
        public void Frame()
        {
            if (subject == null) return;
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer r in renderers) if (r != null) bounds.Encapsulate(r.bounds);

            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Vector3 headBase = head != null ? head.position : new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.35f, bounds.center.z);
            Vector3 centre;
            float headHeight;
            if (eyes != null)
            {
                // Size the shot from the span of the eyes, which scales with the face on every rig; head bones sit
                // at different heights on the chibi and witch rigs.
                float eyeSpan = Mathf.Max(eyes.bounds.size.x, eyes.bounds.size.z);
                headHeight = eyeSpan * 2.6f;
                centre = eyes.bounds.center + Vector3.up * eyeSpan * 0.3f;
            }
            else
            {
                headHeight = Mathf.Max(0.3f, bounds.max.y - headBase.y);
                centre = new Vector3(headBase.x, headBase.y + headHeight * 0.42f, headBase.z);
            }

            Vector3 forward = (animator != null ? animator.transform : subject).forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            forward.Normalize();
            // Slightly off-centre for a three-quarter view.
            Vector3 view = Quaternion.Euler(0f, -18f, 0f) * forward;

            float distance = headHeight * 0.85f / Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            camera.transform.position = centre + view * distance + Vector3.up * headHeight * 0.12f;
            camera.transform.LookAt(centre);
        }

        public void Dispose()
        {
            Hide();
            if (camera != null) UnityEngine.Object.Destroy(camera.gameObject);
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }
    }
}
