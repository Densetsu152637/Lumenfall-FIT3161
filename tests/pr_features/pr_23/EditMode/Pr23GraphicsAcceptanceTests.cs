using Lumenfall.Startup;
using NUnit.Framework;
using UnityEngine;
namespace Lumenfall.FeatureAcceptance
{
    [Category("PR23"), NonParallelizable]
    public sealed class Pr23GraphicsAcceptanceTests
    {
        private readonly string[] keys = { GraphicsPreferences.VSyncKey, GraphicsPreferences.AntialiasingKey, GraphicsPreferences.AmbientOcclusionKey, GraphicsPreferences.ShadingKey };
        private readonly bool[] present = new bool[4]; private readonly int[] values = new int[4];
        [SetUp] public void SavePreferences() { for (int i = 0; i < 4; i++) { present[i] = PlayerPrefs.HasKey(keys[i]); values[i] = PlayerPrefs.GetInt(keys[i]); PlayerPrefs.DeleteKey(keys[i]); } }
        [TearDown] public void RestorePreferences() { for (int i = 0; i < 4; i++) { if (present[i]) PlayerPrefs.SetInt(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]); } PlayerPrefs.Save(); }
        [Test] public void EveryPartialPreferenceCombinationPreservesChoicesAndFillsOnlyMissingValues()
        {
            var defaults = new GraphicsPreferences(true, GraphicsAntialiasing.SMAA, true, ShadingQuality.High);
            for (int mask = 1; mask < 15; mask++)
            {
                for (int i = 0; i < 4; i++) { PlayerPrefs.DeleteKey(keys[i]); if ((mask & (1 << i)) != 0) PlayerPrefs.SetInt(keys[i], 0); }
                GraphicsPreferences.LoadOrCreate(defaults);
                int[] expected = { 1, 2, 1, 2 };
                for (int i = 0; i < 4; i++) Assert.That(PlayerPrefs.GetInt(keys[i], -99), Is.EqualTo((mask & (1 << i)) != 0 ? 0 : expected[i]), "mask=" + mask + " key=" + keys[i]);
            }
        }
        [Test] public void StrongerHardwareDoesNotOverwritePreviouslyCreatedLowPreferences()
        {
            var low = GraphicsRecommendations.Recommend(1024, 1, 512, 30, false, true);
            GraphicsPreferences.LoadOrCreate(low);
            var high = GraphicsRecommendations.Recommend(32000, 16, 8000, 50, false, true);
            var actual = GraphicsPreferences.LoadOrCreate(high);
            Assert.That(actual.Shading, Is.EqualTo(ShadingQuality.Low));
            Assert.That(actual.Antialiasing, Is.EqualTo(GraphicsAntialiasing.Off));
            Assert.That(actual.AmbientOcclusion, Is.False);
        }
        [Test] public void AskingForRecommendationDoesNotPersistPreferences()
        {
            GraphicsRecommendations.Recommend(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue, false, true);
            foreach (string key in keys) Assert.That(PlayerPrefs.HasKey(key), Is.False);
            var mobile = GraphicsRecommendations.Recommend(-1, -1, -1, -1, true, false);
            Assert.That(mobile.Shading, Is.EqualTo(ShadingQuality.High));
            Assert.That(mobile.VSync, Is.False);
        }
    }
}
