using System.Collections;
using System.Linq;
using System.Reflection;
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
        private float previousMusicVolume;
        private float previousAmbienceVolume;
        private AudioFeedbackService audioService;
        private GameObject audioServiceOwner;
        private bool previousMenuMusic;
        private bool previousOpeningSilence;
        private bool ownsAudioService;
        private GameSaveManager saveManager;
        private bool ownsSaveManager;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            previousTimeScale = Time.timeScale;
            previousMasterVolume = AudioListener.volume;
            previousMusicVolume = AudioMixSettings.Music;
            previousAmbienceVolume = AudioMixSettings.Ambience;
            AudioMixSettings.SetVolume(AudioBus.Music, 0.4f);
            AudioMixSettings.SetVolume(AudioBus.Ambience, 0.25f);
            audioService = AudioFeedbackService.Instance;
            ownsAudioService = audioService == null;
            if (ownsAudioService)
            {
                audioServiceOwner = new GameObject("Audio test feedback service");
                audioService = audioServiceOwner.AddComponent<AudioFeedbackService>();
            }
            previousMenuMusic = GetAudioServiceFlag("menuMusic");
            previousOpeningSilence = GetAudioServiceFlag("openingSilence");
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
            AudioMixSettings.SetVolume(AudioBus.Music, previousMusicVolume);
            AudioMixSettings.SetVolume(AudioBus.Ambience, previousAmbienceVolume);
            SetAudioServiceFlag("menuMusic", previousMenuMusic);
            SetAudioServiceFlag("openingSilence", previousOpeningSilence);
            NotifyAudioServicePolicyChanged();
            loader.Clear();
            saveManager.UnregisterProvider(loader.SaveKey);
            Object.DestroyImmediate(owner);
            if (ownsSaveManager) Object.DestroyImmediate(saveManager.gameObject);
            if (ownsAudioService && audioServiceOwner != null) Object.DestroyImmediate(audioServiceOwner);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(firstArea);
            Object.DestroyImmediate(secondArea);
            Object.DestroyImmediate(firstProfile);
            Object.DestroyImmediate(secondProfile);
            Object.DestroyImmediate(firstClip);
            Object.DestroyImmediate(secondClip);
        }

        private bool GetAudioServiceFlag(string name) =>
            (bool)typeof(AudioFeedbackService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(audioService);

        private void SetAudioServiceFlag(string name, bool value) =>
            typeof(AudioFeedbackService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(audioService, value);

        private void NotifyAudioServicePolicyChanged()
        {
            FieldInfo field = typeof(AudioFeedbackService).GetField("AreaAmbiencePolicyChanged", BindingFlags.Instance | BindingFlags.NonPublic);
            (field?.GetValue(audioService) as System.Action)?.Invoke();
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
                Is.EqualTo(firstProfile.ambienceVolume * AudioMixSettings.Ambience));
            Assert.That(Sources.Single(source => source.clip == secondClip).volume,
                Is.EqualTo(0.7f * AudioMixSettings.Music));
            loader.Load(secondArea, null);
            Assert.That(controller.CurrentProfile, Is.SameAs(secondProfile));
            Assert.That(Sources.Count(source => source.clip != null), Is.EqualTo(1));
            Assert.That(Sources.Single(source => source.clip != null).clip, Is.SameAs(secondClip));
            Assert.That(AudioListener.volume, Is.EqualTo(0.25f), "Area audio must preserve the master setting.");
        }

        [Test]
        public void BusChangesUpdatePlayingLoopsWithoutMultiplyingMaster()
        {
            loader.Load(firstArea, null);
            AudioSource ambience = Sources.Single(source => source.clip == firstClip);
            AudioListener.volume = 0.3f;
            AudioMixSettings.SetVolume(AudioBus.Ambience, 0f);
            Assert.That(ambience.volume, Is.Zero, "Muting the ambience bus should mute the active profile loop.");
            AudioMixSettings.SetVolume(AudioBus.Ambience, 0.8f);
            Assert.That(ambience.volume, Is.EqualTo(firstProfile.ambienceVolume * 0.8f).Within(0.001f));
            Assert.That(AudioListener.volume, Is.EqualTo(0.3f), "Area profile gains must not apply master volume twice.");
        }

        [Test]
        public void MenuAndOpeningPolicyMuteAreaAmbienceAndRestoreIt()
        {
            loader.Load(firstArea, null);
            AudioSource ambience = Sources.Single(source => source.clip == firstClip);
            SetAudioServiceFlag("menuMusic", true);
            NotifyAudioServicePolicyChanged();
            Assert.That(ambience.volume, Is.Zero, "Area ambience should be silent on the title menu.");

            SetAudioServiceFlag("menuMusic", false);
            SetAudioServiceFlag("openingSilence", true);
            NotifyAudioServicePolicyChanged();
            Assert.That(ambience.volume, Is.Zero, "Area ambience should remain silent through the opening sequence.");

            SetAudioServiceFlag("openingSilence", false);
            NotifyAudioServicePolicyChanged();
            Assert.That(ambience.volume, Is.EqualTo(firstProfile.ambienceVolume * AudioMixSettings.Ambience).Within(0.001f));
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
                Is.EqualTo(secondProfile.ambienceVolume * AudioMixSettings.Ambience).Within(0.001f));
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
            Assert.That(Sources.Single(source => source.clip == firstClip).volume,
                Is.EqualTo(AudioMixSettings.Ambience).Within(0.001f));
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
