// Scratch validation fixture only. It belongs in an Editor-capable PlayMode test assembly;
// it is intentionally outside Assets so Unity cannot import it before the writer is authorized.
using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Lumenfall.Startup.Tests
{
    public sealed class ShaderAdditionalLightsValidation
    {
        private const int TestLayer = 31;
        private const int Width = 128;
        private const int Height = 128;
        private const string Rock = "Assets/_Project/Art/Environment/ThirdPartyExtracted/Outdoor Environment - Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Rocks_mat.mat";
        private const string Opaque = "Assets/_Project/Art/Environment/ThirdPartyExtracted/Outdoor Environment - Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Fruit_Tree_Opaque_mat.mat";
        private const string Flowers = "Assets/_Project/Art/Environment/ThirdPartyExtracted/Outdoor Environment - Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Poppy_mat.mat";
        private const string Foliage = "Assets/_Project/Art/Environment/ThirdPartyExtracted/Outdoor Environment - Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Fruit_Tree_Foliage_Mat.mat";
        private const string Plants = "Assets/_Project/Art/Environment/ThirdPartyExtracted/Outdoor Environment - Polytope Studio/Lowpoly_Environments/Sources/Materials/PT_Grass_Mat.mat";
        private const string FrostIce = "Assets/_Project/Art/Environment/Puzzle Elements/Ice Trial/MAT_IceTrial_IceSurface.mat";
        private const string FrostSnow = "Assets/_Project/Art/Environment/Puzzle Elements/Ice Trial/MAT_IceTrial_SnowSurface.mat";

        private static readonly (string path, string shader)[] PointLitMaterials =
        {
            (Rock, "Polytope Studio/PT_Rock_Shader"),
            (Opaque, "Polytope Studio/PT_Vegetation_Opaque_Shader"),
            (Flowers, "Polytope Studio/PT_Vegetation_Flowers_Shader"),
            (Foliage, "Polytope Studio/PT_Vegetation_Foliage_Shader"),
            (Plants, "Polytope Studio/PT_Vegetation_Plants_Shader"),
        };

        private static readonly (string path, string shader)[] FrostMaterials =
        {
            (FrostIce, "Lumenfall/Ice Trial Frost"),
            (FrostSnow, "Lumenfall/Ice Trial Frost"),
        };

        private RenderPipelineAsset previousDefaultPipeline;
        private RenderPipelineAsset previousQualityPipeline;
        private UniversalRenderPipelineAsset transientPipeline;
        private UniversalRendererData transientRenderer;
        private readonly List<ScriptableRendererFeature> transientFeatures = new List<ScriptableRendererFeature>();
        private Scene previousActiveScene;
        private Scene testScene;
        private bool testSceneCreated;
        private bool pipelineStateCaptured;
        private bool lightingStateCaptured;
        private bool previousRunInBackground;
        private bool runInBackgroundCaptured;
        private AsyncOperation sceneUnload;
        private Camera camera;
        private UniversalAdditionalCameraData cameraData;
        private RenderTexture target;
        private readonly List<LightState> sceneLights = new List<LightState>();
        private readonly List<ProbeState> sceneProbes = new List<ProbeState>();
        private AmbientMode previousAmbientMode;
        private Color previousAmbientLight;
        private float previousAmbientIntensity;
        private Material previousSkybox;
        private Light previousSun;
        private float previousReflectionIntensity;
        private bool previousFog;
        private Color previousFogColor;
        private float previousFogDensity;

        [UnityTest, Category("RequiresGraphics")]
        public IEnumerator ForwardPerObjectAdditionalLightsAffectFivePolytopeMaterialsAndFrostReceivesMainLight()
        {
            yield return ValidateMode(RenderingMode.Forward, expectDeferred: false);
        }

        [UnityTest, Category("RequiresGraphics")]
        public IEnumerator ForwardPlusClusterLightsAffectFivePolytopeMaterialsAndFrostReceivesMainLight()
        {
            yield return ValidateMode(RenderingMode.ForwardPlus, expectDeferred: false);
        }

        [UnityTest, Category("RequiresGraphics")]
        public IEnumerator DeferredGBufferLightingWorksForRockAndOpaqueMaterials()
        {
            yield return ValidateMode(RenderingMode.Deferred, expectDeferred: true);
        }

        private IEnumerator ValidateMode(RenderingMode mode, bool expectDeferred)
        {
            try
            {
                CreateTransientRenderState(mode);
                yield return null;
                AssertActiveRendererConfiguration(mode);

                if (expectDeferred)
                {
                    foreach (var entry in PointLitMaterials)
                    {
                        if (entry.path != Rock && entry.path != Opaque) continue;
                        yield return AssertPointLightResponse(entry.path, entry.shader, requireGBuffer: true);
                    }
                }
                else
                {
                    foreach (var entry in PointLitMaterials)
                        yield return AssertPointLightResponse(entry.path, entry.shader, requireGBuffer: false);

                    // Frost uses UniversalFragmentPBR for its supported main-light response. Keep this
                    // separate from the five Polytope clustered additional-light regression contract.
                    foreach (var entry in FrostMaterials)
                        yield return AssertMainLightResponse(entry.path, entry.shader);
                }
            }
            finally
            {
                RestoreAndDispose();
            }
        }

        private void CreateTransientRenderState(RenderingMode mode)
        {
            Assert.That(Application.isBatchMode, Is.False, "Shader pixel tests require a graphics device.");
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null));

            previousDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
            previousQualityPipeline = QualitySettings.renderPipeline;
            pipelineStateCaptured = true;
            previousRunInBackground = Application.runInBackground;
            runInBackgroundCaptured = true;
            Application.runInBackground = true;
            var sourcePipeline = (previousQualityPipeline as UniversalRenderPipelineAsset) ??
                                 (previousDefaultPipeline as UniversalRenderPipelineAsset);
            Assert.That(sourcePipeline, Is.Not.Null, "The project must have its URP pipeline loaded.");
            Assert.That(sourcePipeline.rendererDataList.Length, Is.GreaterThan(0));
            transientRenderer = UnityEngine.Object.Instantiate(sourcePipeline.rendererDataList[0] as UniversalRendererData);
            Assert.That(transientRenderer, Is.Not.Null);
            for (int i = 0; i < transientRenderer.rendererFeatures.Count; i++)
            {
                ScriptableRendererFeature feature = transientRenderer.rendererFeatures[i];
                if (feature == null) continue;
                ScriptableRendererFeature clone = UnityEngine.Object.Instantiate(feature);
                transientRenderer.rendererFeatures[i] = clone;
                transientFeatures.Add(clone);
            }
            transientRenderer.renderingMode = mode;
            transientPipeline = UniversalRenderPipelineAsset.Create(transientRenderer);
            Assert.That(transientPipeline.additionalLightsRenderingMode, Is.EqualTo(LightRenderingMode.PerPixel));
            transientPipeline.maxAdditionalLightsCount = 8;

            previousActiveScene = SceneManager.GetActiveScene();
            previousAmbientMode = RenderSettings.ambientMode;
            previousAmbientLight = RenderSettings.ambientLight;
            previousAmbientIntensity = RenderSettings.ambientIntensity;
            previousSkybox = RenderSettings.skybox;
            previousSun = RenderSettings.sun;
            previousReflectionIntensity = RenderSettings.reflectionIntensity;
            previousFog = RenderSettings.fog;
            previousFogColor = RenderSettings.fogColor;
            previousFogDensity = RenderSettings.fogDensity;
            lightingStateCaptured = true;

            testScene = SceneManager.CreateScene("Shader render validation " + mode);
            testSceneCreated = testScene.IsValid();
            Assert.That(testSceneCreated, Is.True);
            SceneManager.SetActiveScene(testScene);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.ambientIntensity = 0f;
            RenderSettings.skybox = null;
            RenderSettings.sun = null;
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.fog = false;

            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
            {
                sceneLights.Add(new LightState(light, light.enabled));
                light.enabled = false;
            }
            foreach (ReflectionProbe probe in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
            {
                sceneProbes.Add(new ProbeState(probe, probe.enabled));
                probe.enabled = false;
            }

            GraphicsSettings.defaultRenderPipeline = transientPipeline;
            QualitySettings.renderPipeline = transientPipeline;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                name = "Transient shader validation target"
            };
            target.Create();
            var cameraObject = new GameObject("Transient shader validation camera") { layer = TestLayer };
            camera = cameraObject.AddComponent<Camera>();
            cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.SetRenderer(0);
            camera.transform.SetPositionAndRotation(new Vector3(0f, 10f, 0f), Quaternion.LookRotation(Vector3.down));
            camera.orthographic = true;
            camera.orthographicSize = 2f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 25f;
            camera.cullingMask = 1 << TestLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.targetTexture = target;
        }

        private void AssertActiveRendererConfiguration(RenderingMode mode)
        {
            Assert.That(GraphicsSettings.currentRenderPipeline, Is.SameAs(transientPipeline),
                "The test camera must render through the transient pipeline under test.");
            Assert.That(transientRenderer.renderingMode, Is.EqualTo(mode));
            Assert.That(cameraData.scriptableRenderer, Is.TypeOf<UniversalRenderer>(),
                "The camera must resolve the configured Universal Renderer.");
            if (mode == RenderingMode.Deferred)
            {
                // URP 17.4 exposes renderingModeActual only as an internal property. Require the
                // public device capability needed by its GBuffer path and keep the material GBuffer
                // assertions below; do not use reflection to inspect the internal fallback state.
                Assert.That(GL.wireframe, Is.False, "Deferred mode is unsupported in wireframe rendering.");
                Assert.That(SystemInfo.supportedRenderTargetCount, Is.GreaterThanOrEqualTo(4),
                    "Deferred GBuffer requires at least four simultaneous render targets.");
            }
        }

        private IEnumerator AssertPointLightResponse(string path, string expectedShader, bool requireGBuffer)
        {
            Material source = LoadActualMaterial(path);
            Assert.That(source.shader, Is.Not.Null, path);
            Assert.That(source.shader.name, Is.EqualTo(expectedShader), path);
            Assert.That(source.shader.isSupported, Is.True, "Unsupported shader: " + expectedShader);
            Assert.That(source.FindPass("Forward"), Is.GreaterThanOrEqualTo(0), "Missing forward pass: " + expectedShader);
            if (requireGBuffer)
                Assert.That(source.FindPass("GBuffer"), Is.GreaterThanOrEqualTo(0), "Missing GBuffer pass: " + expectedShader);

            Material material = UnityEngine.Object.Instantiate(source);
            FreezeVegetationWind(material);
            var subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            subject.name = "Shader validation target " + source.name;
            subject.layer = TestLayer;
            subject.transform.position = Vector3.zero;
            subject.transform.localScale = Vector3.one;
            UnityEngine.Object.Destroy(subject.GetComponent<Collider>());
            subject.GetComponent<Renderer>().sharedMaterial = material;
            Light[] points = CreatePointLights();
            try
            {
                foreach (Light point in points) point.enabled = false;
                yield return CaptureFrame();
                Color[] unlit = ReadCenterRegion();
                foreach (Light point in points) point.enabled = true;
                yield return CaptureFrame();
                Color[] lit = ReadCenterRegion();
                AssertNoPinkErrorPixels(unlit, expectedShader);
                AssertNoPinkErrorPixels(lit, expectedShader);
                AssertVisible(lit, expectedShader);
                AssertLightingDelta(unlit, lit, expectedShader);
            }
            finally
            {
                foreach (Light point in points) if (point != null) UnityEngine.Object.DestroyImmediate(point.gameObject);
                if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private IEnumerator AssertMainLightResponse(string path, string expectedShader)
        {
            Material source = LoadActualMaterial(path);
            Assert.That(source.shader, Is.Not.Null, path);
            Assert.That(source.shader.name, Is.EqualTo(expectedShader), path);
            Assert.That(source.shader.isSupported, Is.True, "Unsupported shader: " + expectedShader);
            Assert.That(source.FindPass("ForwardLit"), Is.GreaterThanOrEqualTo(0), "Missing frost forward pass.");
            Assert.That(source.FindPass("GBuffer"), Is.LessThan(0), "Frost is forward-only; do not claim deferred coverage.");

            Material material = UnityEngine.Object.Instantiate(source);
            if (material.HasProperty("_RevealCenter")) material.SetVector("_RevealCenter", Vector4.zero);
            if (material.HasProperty("_RevealRadius")) material.SetFloat("_RevealRadius", 1000f);
            if (material.HasProperty("_RevealDirection")) material.SetFloat("_RevealDirection", 1f);
            if (material.HasProperty("_Coverage")) material.SetFloat("_Coverage", 1f);
            var subject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            subject.name = "Frost shader validation target " + source.name;
            subject.layer = TestLayer;
            UnityEngine.Object.Destroy(subject.GetComponent<Collider>());
            subject.GetComponent<Renderer>().sharedMaterial = material;
            Light sun = CreateMainLight();
            try
            {
                sun.enabled = false;
                yield return CaptureFrame();
                Color[] unlit = ReadCenterRegion();
                sun.enabled = true;
                yield return CaptureFrame();
                Color[] lit = ReadCenterRegion();
                AssertNoPinkErrorPixels(unlit, expectedShader);
                AssertNoPinkErrorPixels(lit, expectedShader);
                AssertVisible(lit, expectedShader);
                AssertLightingDelta(unlit, lit, expectedShader + " main light");
            }
            finally
            {
                if (sun != null) UnityEngine.Object.DestroyImmediate(sun.gameObject);
                if (subject != null) UnityEngine.Object.DestroyImmediate(subject);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
            }
        }

        private static Material LoadActualMaterial(string path)
        {
#if UNITY_EDITOR
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material, Is.Not.Null, "Missing referenced material " + path);
            return material;
#else
            Assert.Fail("Load the referenced materials through AssetDatabase in the Editor PlayMode assembly.");
            return null;
#endif
        }

        private static void FreezeVegetationWind(Material material)
        {
            if (material.HasProperty("_WindMovement")) material.SetFloat("_WindMovement", 0f);
            if (material.HasProperty("_WindStrength")) material.SetFloat("_WindStrength", 0f);
            if (material.HasProperty("_WindMovement1")) material.SetFloat("_WindMovement1", 0f);
            if (material.HasProperty("_WindStrength1")) material.SetFloat("_WindStrength1", 0f);
        }

        private Light[] CreatePointLights()
        {
            var lights = new Light[2];
            lights[0] = CreatePointLight(new Vector3(-0.25f, 1.5f, 0f), new Color(1f, 0.6f, 0.2f));
            lights[1] = CreatePointLight(new Vector3(0.25f, 1.5f, 0f), new Color(0.2f, 0.6f, 1f));
            return lights;
        }

        private Light CreatePointLight(Vector3 position, Color color)
        {
            var go = new GameObject("Transient additional point light") { layer = TestLayer };
            go.transform.position = position;
            Light light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = 12f;
            light.range = 5f;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << TestLayer;
            return light;
        }

        private Light CreateMainLight()
        {
            var go = new GameObject("Transient frost main light") { layer = TestLayer };
            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.LookRotation(Vector3.down);
            light.cullingMask = 1 << TestLayer;
            RenderSettings.sun = light;
            return light;
        }

        private IEnumerator CaptureFrame()
        {
            yield return null;
            yield return null;
        }

        private Color[] ReadCenterRegion()
        {
            var readback = new Texture2D(Width, Height, TextureFormat.RGBA32, false, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                readback.Apply(false, false);
                return readback.GetPixels(Width / 2 - 40, Height / 2 - 40, 80, 80);
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.Destroy(readback);
            }
        }

        private static void AssertNoPinkErrorPixels(Color[] pixels, string context)
        {
            foreach (Color pixel in pixels)
                Assert.That(pixel.r > 0.85f && pixel.g < 0.2f && pixel.b > 0.85f, Is.False,
                    "Pink shader-error output in " + context);
        }

        private static void AssertVisible(Color[] pixels, string context)
        {
            int visible = 0;
            foreach (Color pixel in pixels)
                if (Mathf.Max(pixel.r, pixel.g, pixel.b) > 0.04f) visible++;
            Assert.That(visible, Is.GreaterThan(50), "No material pixels rendered for " + context);
        }

        private static void AssertLightingDelta(Color[] unlit, Color[] lit, string context)
        {
            Assert.That(unlit.Length, Is.EqualTo(lit.Length));
            float totalDelta = 0f;
            int foreground = 0;
            for (int i = 0; i < unlit.Length; i++)
            {
                if (Mathf.Max(Mathf.Max(unlit[i].r, unlit[i].g), unlit[i].b) <= 0.04f &&
                    Mathf.Max(Mathf.Max(lit[i].r, lit[i].g), lit[i].b) <= 0.04f) continue;
                totalDelta += Mathf.Abs(Luminance(lit[i]) - Luminance(unlit[i]));
                foreground++;
            }
            Assert.That(foreground, Is.GreaterThan(50), "No foreground pixels to compare for " + context);
            Assert.That(totalDelta / foreground, Is.GreaterThan(0.01f),
                "Enabling the intended light did not change rendered output for " + context);
        }

        private static float Luminance(Color color) => color.r * 0.2126f + color.g * 0.7152f + color.b * 0.0722f;

        private void RestoreAndDispose()
        {
            if (pipelineStateCaptured)
            {
                GraphicsSettings.defaultRenderPipeline = previousDefaultPipeline;
                QualitySettings.renderPipeline = previousQualityPipeline;
                pipelineStateCaptured = false;
            }
            if (runInBackgroundCaptured)
            {
                Application.runInBackground = previousRunInBackground;
                runInBackgroundCaptured = false;
            }

            foreach (LightState state in sceneLights) if (state.light != null) state.light.enabled = state.enabled;
            foreach (ProbeState state in sceneProbes) if (state.probe != null) state.probe.enabled = state.enabled;
            sceneLights.Clear();
            sceneProbes.Clear();

            if (previousActiveScene.IsValid()) SceneManager.SetActiveScene(previousActiveScene);
            if (lightingStateCaptured)
            {
                RenderSettings.ambientMode = previousAmbientMode;
                RenderSettings.ambientLight = previousAmbientLight;
                RenderSettings.ambientIntensity = previousAmbientIntensity;
                RenderSettings.skybox = previousSkybox;
                RenderSettings.sun = previousSun;
                RenderSettings.reflectionIntensity = previousReflectionIntensity;
                RenderSettings.fog = previousFog;
                RenderSettings.fogColor = previousFogColor;
                RenderSettings.fogDensity = previousFogDensity;
                lightingStateCaptured = false;
            }

            if (camera != null) UnityEngine.Object.DestroyImmediate(camera.gameObject);
            camera = null;
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                target = null;
            }

            if (testSceneCreated && testScene.IsValid())
            {
                foreach (GameObject root in testScene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(root);
                sceneUnload = SceneManager.UnloadSceneAsync(testScene);
                testSceneCreated = false;
            }

            if (transientPipeline != null) UnityEngine.Object.DestroyImmediate(transientPipeline);
            if (transientRenderer != null) UnityEngine.Object.DestroyImmediate(transientRenderer);
            foreach (ScriptableRendererFeature feature in transientFeatures)
                if (feature != null) UnityEngine.Object.DestroyImmediate(feature);
            transientFeatures.Clear();
            transientPipeline = null;
            transientRenderer = null;

        }

        [UnityTearDown]
        public IEnumerator WaitForSceneCleanup()
        {
            if (sceneUnload != null)
            {
                yield return sceneUnload;
                sceneUnload = null;
            }
            yield return null;
        }

        private sealed class LightState
        {
            public readonly Light light;
            public readonly bool enabled;
            public LightState(Light light, bool enabled) { this.light = light; this.enabled = enabled; }
        }

        private sealed class ProbeState
        {
            public readonly ReflectionProbe probe;
            public readonly bool enabled;
            public ProbeState(ReflectionProbe probe, bool enabled) { this.probe = probe; this.enabled = enabled; }
        }
    }
}
