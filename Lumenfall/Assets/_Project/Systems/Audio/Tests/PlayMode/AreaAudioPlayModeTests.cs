using System.Collections;
using System.Linq;
using Lumenfall.Audio;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Lumenfall.Tests.Audio
{
    public sealed class AreaAudioPlayModeTests
    {
        private GameObject owner;
        private GameObject prefab;
        private DataDrivenWorldLoader loader;
        private AreaAudioController controller;
        private WorldAreaAsset firstArea;
        private WorldAreaAsset secondArea;
        private AreaAudioProfile firstProfile;
        private AreaAudioProfile secondProfile;
        private AudioClip firstClip;
        private AudioClip secondClip;
        private float previousTimeScale;
        private float previousMasterVolume;
        private GameSaveManager saveManager;
        private bool ownsSaveManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            previousMasterVolume = AudioListener.volume;
            saveManager = Object.FindAnyObjectByType<GameSaveManager>();
            ownsSaveManager = saveManager == null;
            if (ownsSaveManager) saveManager = new GameObject("Audio test saves").AddComponent<GameSaveManager>();
            owner = new GameObject("Audio test world");
            loader = owner.AddComponent<DataDrivenWorldLoader>();
            loader.enabled = false; // Tests explicitly load their own areas instead of running Start.
            prefab = new GameObject("Audio test area prefab");
            firstClip = AudioClip.Create("First loop", 44100, 1, 44100, false);
            secondClip = AudioClip.Create("Second loop", 44100, 1, 44100, false);
            firstProfile = ScriptableObject.CreateInstance<AreaAudioProfile>();
            firstProfile.ambience = firstClip;
            firstProfile.fadeDuration = 0f;
            secondProfile = ScriptableObject.CreateInstance<AreaAudioProfile>();
            secondProfile.ambience = secondClip;
            secondProfile.fadeDuration = 0f;
            firstArea = CreateArea("first", firstProfile);
            secondArea = CreateArea("second", secondProfile);
            controller = owner.AddComponent<AreaAudioController>();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = previousTimeScale;
            AudioListener.volume = previousMasterVolume;
            loader.Clear();
            saveManager.UnregisterProvider(loader.SaveKey);
            Object.DestroyImmediate(owner);
            if (ownsSaveManager) Object.DestroyImmediate(saveManager.gameObject);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(firstArea);
            Object.DestroyImmediate(secondArea);
            Object.DestroyImmediate(firstProfile);
            Object.DestroyImmediate(secondProfile);
            Object.DestroyImmediate(firstClip);
            Object.DestroyImmediate(secondClip);
        }

        private WorldAreaAsset CreateArea(string id, AreaAudioProfile profile)
        {
            WorldAreaAsset area = ScriptableObject.CreateInstance<WorldAreaAsset>();
            area.definition.id = id;
            area.areaPrefab = prefab;
            area.audioProfile = profile;
            return area;
        }

        private AudioSource[] Sources => owner.GetComponents<AudioSource>();

        [Test]
        public void LoadsSwitchesAndKeepsMusicAndAmbienceIndependent()
        {
            firstProfile.music = secondClip;
            firstProfile.musicVolume = 0.7f;
            AudioListener.volume = 0.25f;
            loader.Load(firstArea, null);
            Assert.That(controller.CurrentProfile, Is.SameAs(firstProfile));
            Assert.That(Sources.Count(source => source.isPlaying), Is.EqualTo(2),
                "Assigned music and ambience must actually start playback.");
            Assert.That(Sources.Single(source => source.clip == firstClip).volume,
                Is.EqualTo(firstProfile.ambienceVolume));
            Assert.That(Sources.Single(source => source.clip == secondClip).volume, Is.EqualTo(0.7f));
            loader.Load(secondArea, null);
            Assert.That(controller.CurrentProfile, Is.SameAs(secondProfile));
            Assert.That(Sources.Count(source => source.clip != null), Is.EqualTo(1));
            Assert.That(Sources.Single(source => source.clip != null).clip, Is.SameAs(secondClip));
            Assert.That(AudioListener.volume, Is.EqualTo(0.25f), "Area audio must preserve the master setting.");
        }

        [UnityTest]
        public IEnumerator SharedClipsAndReloadsPreservePlaybackPosition()
        {
            loader.Load(firstArea, null);
            yield return null;
            AudioSource source = Sources.Single(item => item.clip == firstClip);
            source.timeSamples = 22050;
            secondProfile.ambience = firstClip;
            loader.Load(secondArea, null);
            loader.Load(secondArea, null);
            Assert.That(Sources.Single(item => item.clip == firstClip), Is.SameAs(source));
            Assert.That(source.timeSamples, Is.GreaterThanOrEqualTo(22050));
        }

        [UnityTest]
        public IEnumerator CrossfadeCompletesWhileGameplayIsPaused()
        {
            loader.Load(firstArea, null);
            secondProfile.fadeDuration = 0.1f;
            Time.timeScale = 0f;
            loader.Load(secondArea, null);
            Assert.That(Sources.Count(source => source.clip != null), Is.EqualTo(2));
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(Sources.Count(source => source.clip != null), Is.EqualTo(1));
            Assert.That(Sources.Single(source => source.clip != null).clip, Is.SameAs(secondClip));
            Assert.That(Sources.Single(source => source.clip != null).volume,
                Is.EqualTo(secondProfile.ambienceVolume).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator RapidChangesReturnToTheFinalAreaWithoutExtraSources()
        {
            firstProfile.fadeDuration = secondProfile.fadeDuration = 0.1f;
            loader.Load(firstArea, null);
            yield return null;
            for (int i = 0; i < 10; i++)
            {
                loader.Load(secondArea, null);
                loader.Load(firstArea, null);
            }
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(Sources.Length, Is.EqualTo(4));
            Assert.That(Sources.Count(source => source.clip != null), Is.EqualTo(1));
            Assert.That(Sources.Single(source => source.clip != null).clip, Is.SameAs(firstClip));
        }

        [UnityTest]
        public IEnumerator MissingProfileAndClearFadeToSilence()
        {
            loader.Load(firstArea, null);
            secondArea.audioProfile = null;
            loader.Load(secondArea, null);
            Assert.That(controller.CurrentProfile, Is.Null);
            yield return new WaitForSecondsRealtime(1.6f);
            Assert.That(Sources.All(source => source.clip == null && source.volume == 0f), Is.True);
            loader.Load(firstArea, null);
            loader.Clear();
            yield return new WaitForSecondsRealtime(1.6f);
            Assert.That(Sources.All(source => source.clip == null && source.volume == 0f), Is.True);
        }

        [Test]
        public void EnableResynchronizesAndDisableStopsAndUnsubscribes()
        {
            loader.Load(firstArea, null);
            controller.enabled = false;
            loader.Load(secondArea, null);
            Assert.That(Sources.All(source => source.clip == null), Is.True);
            controller.enabled = true;
            Assert.That(controller.CurrentProfile, Is.SameAs(secondProfile));
            Assert.That(Sources.Single(source => source.clip != null).clip, Is.SameAs(secondClip));
            Assert.That(Sources.Length, Is.EqualTo(4));
        }

        [Test]
        public void MissingClipStopsOnlyItsChannelAndClampsGain()
        {
            firstProfile.music = secondClip;
            firstProfile.ambienceVolume = 2f;
            loader.Load(firstArea, null);
            Assert.That(Sources.Single(source => source.clip == firstClip).volume, Is.EqualTo(1f));
            secondProfile.music = secondClip;
            secondProfile.ambience = null;
            loader.Load(secondArea, null);
            Assert.That(Sources.Single(source => source.clip != null).clip, Is.SameAs(secondClip));
        }

        [UnityTest]
        public IEnumerator DestroyingTheControllerReleasesItsOwnedSources()
        {
            loader.Load(firstArea, null);
            Object.Destroy(controller);
            yield return null;
            yield return null;
            Assert.That(Sources, Is.Empty);
            controller = owner.AddComponent<AreaAudioController>();
            Assert.That(Sources.Length, Is.EqualTo(4));
            Assert.That(controller.CurrentProfile, Is.SameAs(firstProfile));
        }
    }
}
