using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace Lumenfall.Startup.Tests
{
    public sealed class GraphicsOptionsPlayModeTests
    {
        private GraphicsOptions options;
        private GraphicsPreferences previousPreferences;
        private readonly Dictionary<string, int?> previousKeys = new Dictionary<string, int?>();
        private GameObject cameraObject;
        private RenderTexture target;

        [SetUp]
        public void PreservePreferences()
        {
            options = GraphicsOptions.Instance;
            Assert.That(options, Is.Not.Null, "Graphics options must bootstrap before scene load.");
            previousPreferences = options.Preferences;
            foreach (string key in new[] { GraphicsPreferences.VSyncKey, GraphicsPreferences.AntialiasingKey,
                GraphicsPreferences.AmbientOcclusionKey, GraphicsPreferences.ShadingKey })
                previousKeys[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
        }

        [TearDown]
        public void RestorePreferences()
        {
            if (options != null) options.SetPreferences(previousPreferences);
            foreach (KeyValuePair<string, int?> entry in previousKeys)
            {
                if (entry.Value.HasValue) PlayerPrefs.SetInt(entry.Key, entry.Value.Value);
                else PlayerPrefs.DeleteKey(entry.Key);
            }
            previousKeys.Clear();
            PlayerPrefs.Save();
            if (cameraObject != null) Object.Destroy(cameraObject);
            if (target != null) { target.Release(); Object.Destroy(target); }
        }

        [Test]
        public void BootstrapPersistsInitialPreferencesWithoutOpeningSettings()
        {
            GraphicsPreferences saved = GraphicsPreferences.Load();
            foreach (string key in previousKeys.Keys) Assert.That(PlayerPrefs.HasKey(key), Is.True);
            Assert.That(saved.VSync, Is.EqualTo(options.Preferences.VSync));
            Assert.That(saved.Antialiasing, Is.EqualTo(options.Preferences.Antialiasing));
            Assert.That(saved.AmbientOcclusion, Is.EqualTo(options.Preferences.AmbientOcclusion));
            Assert.That(saved.Shading, Is.EqualTo(options.Preferences.Shading));
            Assert.That(options.RecommendedPreferences.Shading, Is.EqualTo(GraphicsPreferences.Defaults.Shading));
        }

        [Test]
        public void VSyncChangesImmediatelyAndPersists()
        {
            options.SetPreferences(new GraphicsPreferences(true, GraphicsAntialiasing.Off, true, ShadingQuality.High));
            Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
            Assert.That(GraphicsPreferences.Load().VSync, Is.True);
            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.Off, true, ShadingQuality.High));
            Assert.That(QualitySettings.vSyncCount, Is.Zero);
        }

        [Test]
        public void ShadingAndAmbientOcclusionCanBeChangedRepeatedly()
        {
            Assert.That(options.SupportsUrpOptions, Is.True);
            UniversalRenderPipelineAsset pipeline = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.Off, false, ShadingQuality.Low));
            Assert.That(pipeline.shadowDistance, Is.LessThanOrEqualTo(20f));
            Assert.That(pipeline.mainLightShadowmapResolution, Is.LessThanOrEqualTo(512));
            Assert.That(pipeline.shadowCascadeCount, Is.EqualTo(1));
            AssertAmbientOcclusion(pipeline, false);

            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.Off, true, ShadingQuality.High));
            AssertAmbientOcclusion(pipeline, true);
            float highDistance = pipeline.shadowDistance;
            int highResolution = pipeline.mainLightShadowmapResolution;
            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.Off, true, ShadingQuality.Medium));
            Assert.That(pipeline.shadowDistance, Is.LessThanOrEqualTo(Mathf.Min(highDistance, 35f)));
            Assert.That(pipeline.mainLightShadowmapResolution, Is.LessThanOrEqualTo(Mathf.Min(highResolution, 1024)));
            Assert.That(pipeline.shadowCascadeCount, Is.LessThanOrEqualTo(2));
        }

        [UnityTest]
        public IEnumerator LaterCamerasReceiveAntialiasingAndRetainAuthoredPostProcessing()
        {
            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.SMAA, true, ShadingQuality.High));
            cameraObject = new GameObject("Graphics options test camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            target = new RenderTexture(64, 64, 24);
            camera.targetTexture = target;
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            yield return null;
            yield return null;
            Assert.That(data.antialiasing, Is.EqualTo(AntialiasingMode.SubpixelMorphologicalAntiAliasing));
            Assert.That(data.renderPostProcessing, Is.True);

            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.FXAA, true, ShadingQuality.High));
            Assert.That(data.antialiasing, Is.EqualTo(AntialiasingMode.FastApproximateAntialiasing));
            options.SetPreferences(new GraphicsPreferences(false, GraphicsAntialiasing.Off, true, ShadingQuality.High));
            Assert.That(data.antialiasing, Is.EqualTo(AntialiasingMode.None));
            Assert.That(data.renderPostProcessing, Is.False);
        }

        private static void AssertAmbientOcclusion(UniversalRenderPipelineAsset pipeline, bool expected)
        {
            foreach (ScriptableRendererData renderer in pipeline.rendererDataList)
            {
                if (renderer == null) continue;
                foreach (ScriptableRendererFeature feature in renderer.rendererFeatures)
                    if (feature is ScreenSpaceAmbientOcclusion) Assert.That(feature.isActive, Is.EqualTo(expected));
            }
        }
    }
}
