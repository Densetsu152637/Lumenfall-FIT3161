using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.CameraEffects
{
    /// <summary>Camera-scoped circular reveal for vegetation and the tall blockers in the campus garden.</summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerOcclusionReveal : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 0.8f, 0f);
        [Tooltip("Additional camera-right / camera-up offset in world units (one unit is one cell in SampleScene).")]
        [SerializeField] private Vector2 viewOffset;
        [Tooltip("Outer radius as a fraction of camera pixel height.")]
        [SerializeField, Range(0.01f, 0.5f)] private float radius = 0.16f;
        [SerializeField, Range(0.001f, 0.5f)] private float feather = 0.07f;
        [SerializeField, Range(0f, 1f)] private float centerOpacity = 0f;
        [Tooltip("Only vegetation this far in front of the focus plane is revealed.")]
        [SerializeField, Min(0f)] private float depthPadding = 0.15f;
        private const float GardenScanInterval = 0.5f;
        private Camera view;
        private float nextGardenScanTime;
        private Shader gardenRevealShader;
        private bool warnedMissingGardenShader;
        private readonly HashSet<string> warnedUnsupportedShaders = new HashSet<string>();
        private readonly Dictionary<Renderer, Material[]> originalGardenMaterials = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> gardenMaterialCopies = new Dictionary<Material, Material>();
        private static readonly int Enabled = Shader.PropertyToID("_LFRevealEnabled");
        private static readonly int Matrix = Shader.PropertyToID("_LFRevealVP");
        private static readonly int Focus = Shader.PropertyToID("_LFRevealFocus");
        private static readonly int Forward = Shader.PropertyToID("_LFRevealForward");
        private static readonly int Settings = Shader.PropertyToID("_LFRevealSettings");
        private static readonly int Pixels = Shader.PropertyToID("_LFRevealPixels");

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < nextGardenScanTime) return;
            nextGardenScanTime = Time.unscaledTime + GardenScanInterval;
            BindGardenOccluders();
        }

        private void BindGardenOccluders()
        {
            Transform[] transforms = FindObjectsByType<Transform>();
            bool foundGarden = false;
            foreach (Transform transform in transforms)
            {
                if (transform == null || transform.name != "Garden Area") continue;
                foundGarden = true;
                foreach (Transform item in transform.GetComponentsInChildren<Transform>(true))
                {
                    if (item == transform) continue;
                    if (!IsGardenOccluder(item.name)) continue;
                    foreach (Renderer renderer in item.GetComponentsInChildren<Renderer>(true))
                        BindGardenRenderer(renderer);
                }
            }

            if (!foundGarden) RestoreGardenMaterials();
            else RemoveDestroyedGardenRenderers();
        }

        private static bool IsGardenOccluder(string objectName)
        {
            return objectName.StartsWith("Pavilion", StringComparison.OrdinalIgnoreCase) ||
                objectName.StartsWith("sakura+tree", StringComparison.OrdinalIgnoreCase) ||
                objectName.StartsWith("Old+Tree", StringComparison.OrdinalIgnoreCase);
        }

        private void BindGardenRenderer(Renderer renderer)
        {
            if (renderer == null || originalGardenMaterials.ContainsKey(renderer)) return;
            Material[] sourceMaterials = renderer.sharedMaterials;
            Material[] revealMaterials = null;
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                Material source = sourceMaterials[i];
                if (!CanUseGardenReveal(source)) continue;
                if (gardenRevealShader == null)
                    gardenRevealShader = Resources.Load<Shader>("PlayerOcclusionLit");
                if (gardenRevealShader == null)
                {
                    if (!warnedMissingGardenShader)
                    {
                        Debug.LogWarning("Could not load the garden occlusion shader from Resources/PlayerOcclusionLit.", this);
                        warnedMissingGardenShader = true;
                    }
                    return;
                }

                if (!gardenMaterialCopies.TryGetValue(source, out Material copy))
                {
                    copy = new Material(gardenRevealShader)
                    {
                        name = source.name + " (Garden Reveal)",
                        renderQueue = source.renderQueue,
                        enableInstancing = source.enableInstancing,
                        doubleSidedGI = source.doubleSidedGI
                    };
                    copy.CopyPropertiesFromMaterial(source);
                    CopyLegacyLitProperties(source, copy);
                    gardenMaterialCopies.Add(source, copy);
                }

                if (revealMaterials == null) revealMaterials = (Material[])sourceMaterials.Clone();
                revealMaterials[i] = copy;
            }

            if (revealMaterials == null) return;
            originalGardenMaterials.Add(renderer, sourceMaterials);
            renderer.sharedMaterials = revealMaterials;
        }

        private bool CanUseGardenReveal(Material material)
        {
            if (material == null || material.shader == null) return false;
            string shaderName = material.shader.name;
            if (shaderName.IndexOf("PT_Vegetation", StringComparison.OrdinalIgnoreCase) >= 0 ||
                shaderName.IndexOf("Player Occlusion Lit", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            bool supportedLitShader = shaderName == "Universal Render Pipeline/Lit" ||
                shaderName == "Universal Render Pipeline/Simple Lit" || shaderName == "Standard";
            if (!supportedLitShader)
            {
                if (warnedUnsupportedShaders.Add(shaderName))
                    Debug.LogWarning($"Garden occlusion reveal does not support shader '{shaderName}'. The corresponding garden model will keep its original opaque material.");
                return false;
            }
            return !material.HasProperty("_Surface") || material.GetFloat("_Surface") < 0.5f;
        }

        private static void CopyLegacyLitProperties(Material source, Material destination)
        {
            if (source.HasProperty("_MainTex") && destination.HasProperty("_BaseMap"))
            {
                destination.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                destination.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                destination.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            }
            if (source.HasProperty("_Color") && destination.HasProperty("_BaseColor"))
                destination.SetColor("_BaseColor", source.GetColor("_Color"));
            if (source.HasProperty("_Glossiness") && destination.HasProperty("_Smoothness"))
                destination.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
        }

        private void RemoveDestroyedGardenRenderers()
        {
            List<Renderer> destroyed = null;
            foreach (KeyValuePair<Renderer, Material[]> entry in originalGardenMaterials)
            {
                if (entry.Key != null) continue;
                if (destroyed == null) destroyed = new List<Renderer>();
                destroyed.Add(entry.Key);
            }
            if (destroyed == null) return;
            foreach (Renderer renderer in destroyed) originalGardenMaterials.Remove(renderer);
        }

        private void RestoreGardenMaterials()
        {
            foreach (KeyValuePair<Renderer, Material[]> entry in originalGardenMaterials)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
            originalGardenMaterials.Clear();
            foreach (Material copy in gardenMaterialCopies.Values)
                if (copy != null) Destroy(copy);
            gardenMaterialCopies.Clear();
        }

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera != view) return;
            Shader.SetGlobalFloat(Enabled, 0f);
            if (target == null) return;
            Vector3 focus = target.position + targetOffset
                + view.transform.right * viewOffset.x + view.transform.up * viewOffset.y;
            if (view.WorldToViewportPoint(focus).z <= view.nearClipPlane) return;
            Shader.SetGlobalMatrix(Matrix, view.projectionMatrix * view.worldToCameraMatrix);
            Shader.SetGlobalVector(Focus, new Vector4(focus.x, focus.y, focus.z, 1f));
            Vector3 forward = view.transform.forward;
            Shader.SetGlobalVector(Forward, new Vector4(forward.x, forward.y, forward.z, depthPadding));
            Shader.SetGlobalVector(Settings, new Vector4(radius, Mathf.Min(feather, radius), centerOpacity, view.aspect));
            Shader.SetGlobalVector(Pixels, new Vector4(view.pixelWidth, view.pixelHeight, 0f, 0f));
            Shader.SetGlobalFloat(Enabled, 1f);
        }

        private void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == view) Shader.SetGlobalFloat(Enabled, 0f);
        }

        private void OnDisable()
        {
            RestoreGardenMaterials();
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Shader.SetGlobalFloat(Enabled, 0f);
        }
    }
}
