using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Lumenfall.Startup
{
    /// <summary>Applies device graphics preferences for the entire gameplay session.</summary>
    [DisallowMultipleComponent]
    public sealed class GraphicsOptions : MonoBehaviour
    {
        private readonly Dictionary<ScreenSpaceAmbientOcclusion, bool> ambientOcclusionStates =
            new Dictionary<ScreenSpaceAmbientOcclusion, bool>();
        private readonly Dictionary<UniversalAdditionalCameraData, CameraState> cameraStates =
            new Dictionary<UniversalAdditionalCameraData, CameraState>();
        private RenderPipelineAsset originalQualityPipeline;
        private UniversalRenderPipelineAsset sourcePipeline;
        private UniversalRenderPipelineAsset runtimePipeline;
        private int originalVSync;
        private float originalLodBias;

        private readonly struct CameraState
        {
            public readonly bool PostProcessing;
            public readonly AntialiasingMode Antialiasing;
            public readonly AntialiasingQuality Quality;

            public CameraState(UniversalAdditionalCameraData data)
            {
                PostProcessing = data.renderPostProcessing;
                Antialiasing = data.antialiasing;
                Quality = data.antialiasingQuality;
            }
        }

        public static GraphicsOptions Instance { get; private set; }
        public GraphicsPreferences Preferences { get; private set; }
        public bool SupportsAmbientOcclusion => ambientOcclusionStates.Count > 0;
        public bool SupportsUrpOptions => runtimePipeline != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance == null) new GameObject("[GraphicsOptions]").AddComponent<GraphicsOptions>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            originalVSync = QualitySettings.vSyncCount;
            originalLodBias = QualitySettings.lodBias;
            originalQualityPipeline = QualitySettings.renderPipeline;
            sourcePipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (sourcePipeline != null)
            {
                // Shade a transient pipeline copy; never persist player choices into authored assets.
                runtimePipeline = Instantiate(sourcePipeline);
                runtimePipeline.name = sourcePipeline.name + " (Player Graphics)";
                runtimePipeline.hideFlags = HideFlags.DontSave;
                foreach (ScriptableRendererData renderer in sourcePipeline.rendererDataList)
                {
                    if (renderer == null) continue;
                    foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
                    {
                        if (feature is ScreenSpaceAmbientOcclusion ao && !ambientOcclusionStates.ContainsKey(ao))
                            ambientOcclusionStates.Add(ao, ao.isActive);
                    }
                }
                QualitySettings.renderPipeline = runtimePipeline;
            }

            Preferences = GraphicsPreferences.Load();
            Apply();
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        public void SetPreferences(GraphicsPreferences preferences)
        {
            Preferences = preferences;
            Apply();
            Preferences.Save();
        }

        private void Apply()
        {
            QualitySettings.vSyncCount = Preferences.VSync ? 1 : 0;
            if (runtimePipeline == null) return;

            bool low = Preferences.Shading == ShadingQuality.Low;
            bool high = Preferences.Shading == ShadingQuality.High;
            runtimePipeline.shadowDistance = Mathf.Min(sourcePipeline.shadowDistance, high ? float.MaxValue : low ? 20f : 35f);
            runtimePipeline.mainLightShadowmapResolution = Mathf.Min(sourcePipeline.mainLightShadowmapResolution, high ? int.MaxValue : low ? 512 : 1024);
            runtimePipeline.shadowCascadeCount = Mathf.Min(sourcePipeline.shadowCascadeCount, high ? 4 : low ? 1 : 2);
            QualitySettings.lodBias = Mathf.Min(originalLodBias, high ? float.MaxValue : low ? 0.75f : 1f);

            // URP shares renderer features between pipeline copies; restore their state on shutdown.
            foreach (ScreenSpaceAmbientOcclusion ao in ambientOcclusionStates.Keys)
                if (ao != null) ao.SetActive(Preferences.AmbientOcclusion);
            foreach (Camera camera in Camera.allCameras) ApplyToCamera(camera);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // Also handles cameras introduced by later scene loads, without a per-frame scene search.
            if (runtimePipeline != null) ApplyToCamera(camera);
        }

        private void ApplyToCamera(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game) return;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            if (!cameraStates.TryGetValue(data, out CameraState original))
            {
                original = new CameraState(data);
                cameraStates.Add(data, original);
            }
            data.antialiasing = Preferences.Antialiasing == GraphicsAntialiasing.FXAA
                ? AntialiasingMode.FastApproximateAntialiasing
                : Preferences.Antialiasing == GraphicsAntialiasing.SMAA
                    ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            data.antialiasingQuality = AntialiasingQuality.High;
            data.renderPostProcessing = original.PostProcessing || Preferences.Antialiasing != GraphicsAntialiasing.Off;
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            foreach (KeyValuePair<UniversalAdditionalCameraData, CameraState> entry in cameraStates)
            {
                if (entry.Key == null) continue;
                entry.Key.renderPostProcessing = entry.Value.PostProcessing;
                entry.Key.antialiasing = entry.Value.Antialiasing;
                entry.Key.antialiasingQuality = entry.Value.Quality;
            }
            foreach (KeyValuePair<ScreenSpaceAmbientOcclusion, bool> entry in ambientOcclusionStates)
                if (entry.Key != null) entry.Key.SetActive(entry.Value);
            if (QualitySettings.renderPipeline == runtimePipeline)
                QualitySettings.renderPipeline = originalQualityPipeline;
            QualitySettings.vSyncCount = originalVSync;
            QualitySettings.lodBias = originalLodBias;
            if (runtimePipeline != null) Destroy(runtimePipeline);
            Instance = null;
        }
    }
}
