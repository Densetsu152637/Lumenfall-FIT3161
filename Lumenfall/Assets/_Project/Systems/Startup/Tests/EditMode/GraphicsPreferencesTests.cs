using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Startup.Tests
{
    public sealed class GraphicsPreferencesTests
    {
        private readonly Dictionary<string, int?> previous = new Dictionary<string, int?>();
        private static readonly string[] Keys =
        {
            GraphicsPreferences.VSyncKey, GraphicsPreferences.AntialiasingKey,
            GraphicsPreferences.AmbientOcclusionKey, GraphicsPreferences.ShadingKey
        };

        [SetUp]
        public void PreservePreferences()
        {
            foreach (string key in Keys)
            {
                previous[key] = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetInt(key) : (int?)null;
                PlayerPrefs.DeleteKey(key);
            }
        }

        [TearDown]
        public void RestorePreferences()
        {
            foreach (KeyValuePair<string, int?> entry in previous)
            {
                if (entry.Value.HasValue) PlayerPrefs.SetInt(entry.Key, entry.Value.Value);
                else PlayerPrefs.DeleteKey(entry.Key);
            }
            previous.Clear();
            PlayerPrefs.Save();
        }

        [Test]
        public void FirstLaunchUsesDefaultsWithoutWritingPreferences()
        {
            AssertPreferences(GraphicsPreferences.Load(), GraphicsPreferences.Defaults);
            foreach (string key in Keys) Assert.That(PlayerPrefs.HasKey(key), Is.False);
        }

        [TestCase(false, GraphicsAntialiasing.Off, false, ShadingQuality.Low)]
        [TestCase(true, GraphicsAntialiasing.FXAA, true, ShadingQuality.Medium)]
        [TestCase(true, GraphicsAntialiasing.SMAA, false, ShadingQuality.High)]
        public void AllChoicesSurviveReload(bool vSync, GraphicsAntialiasing aa, bool ao, ShadingQuality shading)
        {
            GraphicsPreferences expected = new GraphicsPreferences(vSync, aa, ao, shading);
            expected.Save();
            AssertPreferences(GraphicsPreferences.Load(), expected);
        }

        [TestCase(-1)]
        [TestCase(3)]
        [TestCase(int.MaxValue)]
        public void InvalidStoredEnumsUseSafeDefaults(int invalid)
        {
            PlayerPrefs.SetInt(GraphicsPreferences.AntialiasingKey, invalid);
            PlayerPrefs.SetInt(GraphicsPreferences.ShadingKey, invalid);
            GraphicsPreferences actual = GraphicsPreferences.Load();
            Assert.That(actual.Antialiasing, Is.EqualTo(GraphicsAntialiasing.Off));
            Assert.That(actual.Shading, Is.EqualTo(ShadingQuality.High));
        }

        [Test]
        public void PartialPreferencesUseDefaultsForMissingChoices()
        {
            PlayerPrefs.SetInt(GraphicsPreferences.VSyncKey, 1);
            GraphicsPreferences actual = GraphicsPreferences.Load();
            Assert.That(actual.VSync, Is.True);
            Assert.That(actual.Antialiasing, Is.EqualTo(GraphicsPreferences.Defaults.Antialiasing));
            Assert.That(actual.AmbientOcclusion, Is.EqualTo(GraphicsPreferences.Defaults.AmbientOcclusion));
            Assert.That(actual.Shading, Is.EqualTo(GraphicsPreferences.Defaults.Shading));
        }

        [Test]
        public void ResetReplacesEveryGraphicsChoice()
        {
            new GraphicsPreferences(true, GraphicsAntialiasing.SMAA, false, ShadingQuality.Low).Save();
            GraphicsPreferences.Defaults.Save();
            AssertPreferences(GraphicsPreferences.Load(), GraphicsPreferences.Defaults);
        }

        [Test]
        public void FreshInstallPersistsRecommendationBeforeOpeningSettings()
        {
            GraphicsPreferences recommended = GraphicsRecommendations.Recommend(7500, 4, 2000, 45, false, true);
            AssertPreferences(GraphicsPreferences.LoadOrCreate(recommended), recommended);
            foreach (string key in Keys) Assert.That(PlayerPrefs.HasKey(key), Is.True);
            AssertPreferences(GraphicsPreferences.Load(), recommended);
        }

        [Test]
        public void LaterLaunchWithDifferentHardwareKeepsInitialRecommendation()
        {
            GraphicsPreferences initial = GraphicsRecommendations.Recommend(7500, 4, 2000, 45, false, true);
            GraphicsPreferences upgraded = GraphicsRecommendations.Recommend(15000, 8, 4000, 45, false, true);
            GraphicsPreferences.LoadOrCreate(initial);
            AssertPreferences(GraphicsPreferences.LoadOrCreate(upgraded), initial);
            AssertPreferences(GraphicsPreferences.Load(), initial);
        }

        [Test]
        public void ExistingCustomChoicesAreNeverReplacedByRecommendation()
        {
            GraphicsPreferences custom = new GraphicsPreferences(false, GraphicsAntialiasing.SMAA, true, ShadingQuality.Low);
            custom.Save();
            GraphicsPreferences recommended = GraphicsRecommendations.Recommend(15000, 8, 4000, 45, false, true);
            AssertPreferences(GraphicsPreferences.LoadOrCreate(recommended), custom);
            AssertPreferences(GraphicsPreferences.Load(), custom);
        }

        [Test]
        public void PartialPreferencesFillMissingKeysAndPreserveSavedChoice()
        {
            PlayerPrefs.SetInt(GraphicsPreferences.VSyncKey, 0);
            PlayerPrefs.SetInt(GraphicsPreferences.AntialiasingKey, (int)GraphicsAntialiasing.SMAA);
            GraphicsPreferences recommended = GraphicsRecommendations.Recommend(7500, 4, 2000, 45, false, true);
            GraphicsPreferences expected = new GraphicsPreferences(false, GraphicsAntialiasing.SMAA,
                recommended.AmbientOcclusion, recommended.Shading);
            AssertPreferences(GraphicsPreferences.LoadOrCreate(recommended), expected);
            foreach (string key in Keys) Assert.That(PlayerPrefs.HasKey(key), Is.True);
            AssertPreferences(GraphicsPreferences.Load(), expected);
        }

        private static void AssertPreferences(GraphicsPreferences actual, GraphicsPreferences expected)
        {
            Assert.That(actual.VSync, Is.EqualTo(expected.VSync));
            Assert.That(actual.Antialiasing, Is.EqualTo(expected.Antialiasing));
            Assert.That(actual.AmbientOcclusion, Is.EqualTo(expected.AmbientOcclusion));
            Assert.That(actual.Shading, Is.EqualTo(expected.Shading));
        }
    }
}
