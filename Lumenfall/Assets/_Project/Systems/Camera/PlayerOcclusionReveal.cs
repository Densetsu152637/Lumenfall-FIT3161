using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;

namespace Lumenfall.CameraEffects
{
    /// <summary>Camera-scoped circular reveal for vegetation and tall authored environment blockers.</summary>
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
        private const float OccluderScanInterval = 0.5f;
        private Camera view;
        private float nextGardenScanTime;
        private bool hasScannedOccluders;
        private GameObject scannedAreaRoot;
        private int scannedRootCount;
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
            nextGardenScanTime = Time.unscaledTime + OccluderScanInterval;
            if (!OccluderLayoutChanged()) return;
            BindAuthoredOccluders();
        }

        // The full scan walks every Transform in the scene and allocates each name, which hitched every scan
        // interval. Occluders only arrive with a loaded area or new scene content, so rescan only when those change.
        private bool OccluderLayoutChanged()
        {
            DataDrivenWorldLoader loader = DataDrivenWorldLoader.Instance;
            GameObject areaRoot = loader == null ? null : loader.CurrentAreaRoot;
            int rootCount = 0;
            for (int i = 0; i < SceneManager.sceneCount; i++) rootCount += SceneManager.GetSceneAt(i).rootCount;
            if (hasScannedOccluders && areaRoot == scannedAreaRoot && rootCount == scannedRootCount) return false;
            hasScannedOccluders = true;
            scannedAreaRoot = areaRoot;
            scannedRootCount = rootCount;
            return true;
        }

        private void BindAuthoredOccluders()
        {
            Transform[] transforms = FindObjectsByType<Transform>();
            bool foundOccluderRoot = false;
            foreach (Transform transform in transforms)
            {
                if (transform == null) continue;
                if (transform.name == "Garden Area")
                {
                    foundOccluderRoot = true;
                    foreach (Transform item in transform.GetComponentsInChildren<Transform>(true))
                    {
                        if (item == transform || !IsGardenOccluder(item.name)) continue;
                        BindRenderers(item);
                    }
                }
                else if (IsLibraryOccluder(transform.name))
                {
                    foundOccluderRoot = true;
                    BindLibraryOccluders(transform);
                }
                else if (IsWorkshopWallGroup(transform.name))
                {
                    // The Arcane Workshop's tall walls hide the player near them, like the library's shelves did.
                    foundOccluderRoot = true;
                    BindRenderers(transform);
                }
            }

            if (!foundOccluderRoot) RestoreGardenMaterials();
            else RemoveDestroyedGardenRenderers();
        }

        private void BindRenderers(Transform root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
                BindGardenRenderer(renderer);
        }

        private void BindLibraryOccluders(Transform libraryRoot)
        {
            foreach (Transform item in libraryRoot.GetComponentsInChildren<Transform>(true))
            {
                if (!IsSelectedLibraryOccluder(item.name)) continue;
                foreach (Renderer renderer in item.GetComponents<Renderer>())
                    BindGardenRenderer(renderer);
            }
        }

        private static bool IsGardenOccluder(string objectName)
        {
            return objectName.StartsWith("Pavilion", StringComparison.OrdinalIgnoreCase) ||
                objectName.StartsWith("sakura+tree", StringComparison.OrdinalIgnoreCase) ||
                objectName.StartsWith("Old+Tree", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLibraryOccluder(string objectName)
        {
            return objectName.StartsWith("old_fantasy-style_library", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The Arcane Workshop's wall groups ("Wall Group", "Wall Group (1)" and so on), which only it uses.</summary>
        private static bool IsWorkshopWallGroup(string objectName)
        {
            return objectName == "Wall Group" || objectName.StartsWith("Wall Group (", StringComparison.Ordinal);
        }

        private static bool IsSelectedLibraryOccluder(string objectName)
        {
            if (!objectName.StartsWith("Object_", StringComparison.Ordinal)) return false;
            if (!int.TryParse(objectName.Substring("Object_".Length), out int objectNumber)) return false;
            return objectNumber == 4 || objectNumber == 5 ||
                (objectNumber >= 12 && objectNumber <= 26) ||
                objectNumber == 33 || objectNumber == 39;
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
                    CopyGltfLitProperties(source, copy);
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
                shaderName == "Universal Render Pipeline/Simple Lit" || shaderName == "Standard" ||
                shaderName.IndexOf("glTF-pbrMetallicRoughness", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!supportedLitShader)
            {
                if (warnedUnsupportedShaders.Add(shaderName))
                    Debug.LogWarning($"Garden occlusion reveal does not support shader '{shaderName}'. The corresponding garden model will keep its original opaque material.");
                return false;
            }
            if (material.renderQueue >= (int)RenderQueue.Transparent) return false;
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

        private static void CopyGltfLitProperties(Material source, Material destination)
        {
            if (!source.HasProperty("baseColorTexture")) return;

            Texture baseMap = source.GetTexture("baseColorTexture");
            destination.SetTexture("_BaseMap", baseMap);
            destination.SetTextureScale("_BaseMap", source.GetTextureScale("baseColorTexture"));
            destination.SetTextureOffset("_BaseMap", source.GetTextureOffset("baseColorTexture"));
            if (source.HasProperty("baseColorFactor"))
                destination.SetColor("_BaseColor", source.GetColor("baseColorFactor"));
            if (source.HasProperty("metallicFactor"))
                destination.SetFloat("_Metallic", source.GetFloat("metallicFactor"));
            if (source.HasProperty("roughnessFactor"))
                destination.SetFloat("_Smoothness", 1f - source.GetFloat("roughnessFactor"));

            CopyGltfTexture(source, destination, "normalTexture", "_BumpMap", "_NORMALMAP");
            if (source.HasProperty("normalTexture_scale"))
                destination.SetFloat("_BumpScale", source.GetFloat("normalTexture_scale"));
            CopyGltfTexture(source, destination, "occlusionTexture", "_OcclusionMap", "_OCCLUSIONMAP");
            if (source.HasProperty("occlusionTexture_strength"))
                destination.SetFloat("_OcclusionStrength", source.GetFloat("occlusionTexture_strength"));
            CopyGltfTexture(source, destination, "emissiveTexture", "_EmissionMap", "_EMISSION");
            if (source.HasProperty("emissiveFactor"))
                destination.SetColor("_EmissionColor", source.GetColor("emissiveFactor"));
        }

        private static void CopyGltfTexture(
            Material source, Material destination, string sourceProperty, string destinationProperty, string keyword)
        {
            if (!source.HasProperty(sourceProperty)) return;
            Texture texture = source.GetTexture(sourceProperty);
            if (texture == null) return;
            destination.SetTexture(destinationProperty, texture);
            destination.SetTextureScale(destinationProperty, source.GetTextureScale(sourceProperty));
            destination.SetTextureOffset(destinationProperty, source.GetTextureOffset(sourceProperty));
            destination.EnableKeyword(keyword);
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
            // A see-through circle in the trees would give away a hidden player's spot behind the startup menu.
            if (target == null || PlayerVisibility.IsHidden) return;
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
            hasScannedOccluders = false;
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            Shader.SetGlobalFloat(Enabled, 0f);
        }
    }
}
