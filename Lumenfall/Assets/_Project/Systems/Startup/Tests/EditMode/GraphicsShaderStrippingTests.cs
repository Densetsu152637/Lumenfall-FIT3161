using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Lumenfall.Startup.Tests
{
    public sealed class GraphicsShaderStrippingTests
    {
        private const string PipelinePath = "Assets/_Project/Settings/PC_RPAsset.asset";
        private const string PrimaryPath = "Assets/_Project/Settings/PC_Renderer.asset";
        private const string AlternatePath = "Assets/_Project/Settings/PC_Renderer_AOOffVariants.asset";
        private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        [Test]
        public void ShippingPipelineEnablesStrippingWithoutChangingTheDefaultRenderer()
        {
            Assert.That(GraphicsSettings.TryGetRenderPipelineSettings<URPShaderStrippingSetting>(out var settings), Is.True);
            Assert.That(settings.stripUnusedVariants, Is.True);
            Assert.That(settings.stripUnusedPostProcessingVariants, Is.True);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var serialized = new SerializedObject(pipeline);
            var renderers = serialized.FindProperty("m_RendererDataList");
            Assert.That(serialized.FindProperty("m_DefaultRendererIndex").intValue, Is.Zero);
            Assert.That(renderers.arraySize, Is.EqualTo(2));
            Assert.That(AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(0).objectReferenceValue), Is.EqualTo(PrimaryPath));
            Assert.That(AssetDatabase.GetAssetPath(renderers.GetArrayElementAtIndex(1).objectReferenceValue), Is.EqualTo(AlternatePath));
            var primary = (ScriptableRendererData)renderers.GetArrayElementAtIndex(0).objectReferenceValue;
            var alternate = (ScriptableRendererData)renderers.GetArrayElementAtIndex(1).objectReferenceValue;
            Assert.That(primary.rendererFeatures.Count(feature => feature is ScreenSpaceAmbientOcclusion && feature.isActive), Is.EqualTo(1));
            Assert.That(alternate.rendererFeatures, Is.Empty, "A shared/deactivated SSAO feature cannot represent the AO-off build state.");
            Assert.That(new SerializedObject(primary).FindProperty("m_RenderingMode").intValue, Is.EqualTo(2), "Keep the authored ForwardPlus rendering path.");
            Assert.That(new SerializedObject(alternate).FindProperty("m_RenderingMode").intValue, Is.EqualTo(2));
        }

        [Test]
        public void EngineCollectionRetainsBothRuntimeAmbientOcclusionStates()
        {
            CollectFeatures(removeAlternate: false, out long[] features, out string prefilter, out long aoBit);
            Assert.That(features.Length, Is.EqualTo(2));
            Assert.That(features.Count(value => (value & aoBit) != 0), Is.EqualTo(1));
            Assert.That(features.Count(value => (value & aoBit) == 0), Is.EqualTo(1));
            Assert.That(features[0] ^ features[1], Is.EqualTo(aoBit), "Renderer feature sets must differ only in SSAO.");
            Assert.That(prefilter, Is.EqualTo("Select"), "URP must select both the empty and _SCREEN_SPACE_OCCLUSION keywords.");
        }

        [Test]
        public void RemovingTheAlternateRendererWouldStripTheRuntimeOffState()
        {
            CollectFeatures(removeAlternate: true, out long[] features, out string prefilter, out long aoBit);
            Assert.That(features.Length, Is.EqualTo(1));
            Assert.That((features[0] & aoBit) != 0, Is.True);
            Assert.That(prefilter, Is.EqualTo("SelectOnly"), "Negative control: the authored AO renderer alone removes OFF.");
        }

        private static void CollectFeatures(bool removeAlternate, out long[] features, out string prefilter, out long aoBit)
        {
            // Exercise the installed URP collector with an explicit transient PC asset: no quality/platform selection
            // or persistent source-asset writes. Reflection deliberately fails on a package contract change.
            Type collector = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("UnityEditor.Rendering.Universal.ShaderBuildPreprocessor"))
                .FirstOrDefault(type => type != null);
            Assert.That(collector, Is.Not.Null, "URP build preprocessor was not loaded.");
            MethodInfo gather = collector.GetMethod("GetSupportedShaderFeaturesFromAssets", StaticMembers);
            FieldInfo enabled = collector.GetField("s_StripUnusedVariants", StaticMembers);
            FieldInfo strip2D = collector.GetField("s_Strip2DPasses", StaticMembers);
            Assert.That(gather, Is.Not.Null, "Review the pinned URP feature-collection contract after package changes.");
            Assert.That(enabled, Is.Not.Null);
            Assert.That(strip2D, Is.Not.Null);
            var pipeline = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath));
            bool previousEnabled = (bool)enabled.GetValue(null);
            bool previousStrip2D = (bool)strip2D.GetValue(null);
            try
            {
                if (removeAlternate)
                {
                    var serialized = new SerializedObject(pipeline);
                    serialized.FindProperty("m_RendererDataList").arraySize = 1;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                enabled.SetValue(null, true);
                Type featureListType = gather.GetParameters()[1].ParameterType.GetElementType();
                var collected = (IEnumerable)Activator.CreateInstance(featureListType);
                object[] arguments = { new List<UniversalRenderPipelineAsset> { pipeline }, collected, true };
                gather.Invoke(null, arguments);
                Type featureType = featureListType.GetGenericArguments()[0];
                aoBit = Convert.ToInt64(Enum.Parse(featureType, "ScreenSpaceOcclusion"));
                features = ((IEnumerable)arguments[1]).Cast<object>().Select(Convert.ToInt64).ToArray();
                prefilter = typeof(UniversalRenderPipelineAsset)
                    .GetField("m_PrefilteringModeScreenSpaceOcclusion", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(pipeline).ToString();
            }
            finally
            {
                enabled.SetValue(null, previousEnabled);
                strip2D.SetValue(null, previousStrip2D);
                UnityEngine.Object.DestroyImmediate(pipeline);
            }
        }
    }
}
