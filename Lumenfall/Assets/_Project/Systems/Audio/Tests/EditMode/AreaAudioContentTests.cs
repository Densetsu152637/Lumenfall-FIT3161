using Lumenfall.Audio;
using Lumenfall.DataDriven;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.Tests.Audio
{
    public sealed class AreaAudioContentTests
    {
        [TestCase("eastForest", "Forest")]
        [TestCase("campus", "Campus")]
        [TestCase("B01.Ground", "Interior")]
        [TestCase("B02.Ground", "Interior")]
        [TestCase("B03.Ground", "Interior")]
        [TestCase("B04.Ground", "Interior")]
        public void ShippedAreaHasAnImportedAmbienceProfile(string areaName, string profileName)
        {
            WorldAreaAsset area = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(
                "Assets/_Project/World/Areas/" + areaName + ".asset");
            AreaAudioProfile profile = AssetDatabase.LoadAssetAtPath<AreaAudioProfile>(
                "Assets/_Project/Systems/Audio/Profiles/" + profileName + ".asset");
            Assert.That(area, Is.Not.Null);
            Assert.That(profile, Is.Not.Null);
            Assert.That(area.audioProfile, Is.SameAs(profile));
            Assert.That(profile.ambience, Is.Not.Null, "The assigned loop must import as an AudioClip.");
            AudioImporter importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(profile.ambience));
            if (profileName == "Forest")
            {
                Assert.That(AssetDatabase.GetAssetPath(profile.ambience),
                    Is.EqualTo("Assets/_Project/Art/Audio/lumenForest.wav"));
                Assert.That(profile.ambience.length, Is.EqualTo(74.76925f).Within(0.1f));
                Assert.That(importer.defaultSampleSettings.loadType, Is.EqualTo(AudioClipLoadType.Streaming));
                Assert.That(importer.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.Vorbis));
                Assert.That(importer.defaultSampleSettings.preloadAudioData, Is.False);
            }
            else
            {
                Assert.That(profile.ambience.length, Is.EqualTo(12f).Within(0.01f));
                Assert.That(importer.defaultSampleSettings.compressionFormat, Is.EqualTo(AudioCompressionFormat.PCM),
                    "Short seamless starter loops should avoid lossy codec padding.");
            }
            Assert.That(profile.ambienceVolume, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
        }
    }
}
