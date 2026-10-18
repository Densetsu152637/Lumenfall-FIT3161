using System;
using System.Collections.Generic;
using Lumenfall.Audio;
using Lumenfall.GameEvents;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class AudioFeedbackTests
    {
        [Test] public void MixClampsAndRejectsNonFiniteValues()
        {
            AudioMixLevels mix = new AudioMixLevels();
            mix.SetVolume(AudioBus.Master, -1f); Assert.That(mix.GetVolume(AudioBus.Master), Is.Zero);
            mix.SetVolume(AudioBus.Sfx, 4f); Assert.That(mix.GetVolume(AudioBus.Sfx), Is.EqualTo(1f));
            mix.SetVolume(AudioBus.Music, float.NaN); Assert.That(mix.GetVolume(AudioBus.Music), Is.EqualTo(0.7f));
            mix.SetVolume(AudioBus.Ambience, float.PositiveInfinity); Assert.That(mix.GetVolume(AudioBus.Ambience), Is.EqualTo(0.65f));
            Assert.Throws<ArgumentOutOfRangeException>(() => mix.SetVolume((AudioBus)19, 1f));
        }
        [Test] public void MixBusesAreIndependentAndRepeatedSettingIsNoOp()
        {
            AudioMixLevels mix = new AudioMixLevels();
            Assert.That(mix.SetVolume(AudioBus.Master, 0.5f), Is.True);
            Assert.That(mix.SetVolume(AudioBus.Master, 0.5f), Is.False);
            Assert.That(mix.GetVolume(AudioBus.Music), Is.EqualTo(0.7f));
        }
        [Test] public void ContextMapsAndPriorityResolveDeterministically()
        {
            Assert.That(AudioContextPolicy.Resolve("westForest", false, false, false), Is.EqualTo(AudioContext.Forest));
            Assert.That(AudioContextPolicy.Resolve("eastForest", false, false, false), Is.EqualTo(AudioContext.Forest));
            Assert.That(AudioContextPolicy.Resolve("campus", false, false, false), Is.EqualTo(AudioContext.Campus));
            Assert.That(AudioContextPolicy.Resolve("B04.Ground", false, false, false), Is.EqualTo(AudioContext.Interior));
            Assert.That(AudioContextPolicy.Resolve(null, false, false, false), Is.EqualTo(AudioContext.Silent));
            Assert.That(AudioContextPolicy.Resolve("campus", true, true, true), Is.EqualTo(AudioContext.Sleep));
            Assert.That(AudioContextPolicy.Resolve("campus", true, true, false), Is.EqualTo(AudioContext.Dialogue));
            Assert.That(AudioContextPolicy.Resolve("campus", false, true, false), Is.EqualTo(AudioContext.Mystery));
            Assert.That(AudioContextPolicy.Resolve("deepForest", false, false, false), Is.EqualTo(AudioContext.Forest));
        }
        [Test] public void MapsShareForestCampusAndDormitoryTracks()
        {
            Assert.That(AudioContextPolicy.MapMusicCue("westForest"), Is.EqualTo("music.forest"));
            Assert.That(AudioContextPolicy.MapMusicCue("deepForest"), Is.EqualTo("music.forest"));
            Assert.That(AudioContextPolicy.MapMusicCue("orioDaisRoom"), Is.EqualTo("music.forest"));
            Assert.That(AudioContextPolicy.MapMusicCue("B01.Ground"), Is.EqualTo("music.dormitory"));
            Assert.That(AudioContextPolicy.MapMusicCue("campus"), Is.EqualTo("music.campus"));
            Assert.That(AudioContextPolicy.MapMusicCue("B04.Ground"), Is.EqualTo("music.campus"));
            Assert.That(AudioContextPolicy.MapMusicCue("C03.IceTrial"), Is.EqualTo("music.campus"));
            Assert.That(AudioContextPolicy.MapMusicCue(null), Is.Null);
        }
        [Test] public void PausedEmptyVoicesRemainAvailableForStartupUi()
        {
            Assert.That(AudioContextPolicy.ShouldPauseVoice(false, false, true, false), Is.False);
            Assert.That(AudioContextPolicy.ShouldPauseVoice(true, true, true, false), Is.False);
            Assert.That(AudioContextPolicy.ShouldPauseVoice(true, false, true, false), Is.True);
            Assert.That(AudioContextPolicy.ShouldPauseVoice(true, true, false, true), Is.True);
            Assert.That(AudioContextPolicy.CanPlayGameplay(true, false), Is.False);
            Assert.That(AudioContextPolicy.CanPlayGameplay(false, false), Is.True);
        }
        [Test] public void DialogueAndSleepDuckWithoutMultiplyingMasterTwice()
        {
            Assert.That(AudioContextPolicy.MusicGain(true, false), Is.EqualTo(0.4f));
            Assert.That(AudioContextPolicy.AmbienceGain(true, false), Is.EqualTo(0.6f));
            Assert.That(AudioContextPolicy.MusicGain(true, true), Is.EqualTo(0.3f));
            Assert.That(AudioContextPolicy.MusicGain(false, false), Is.EqualTo(1f));
        }
        [Test] public void ChapterStoryFlagsDriveMysterySleepWakeAndClearMystery()
        {
            GameEventRuntime runtime = CreateRuntime(); using (GameAudioSignals signals = new GameAudioSignals())
            {
                List<string> cues = new List<string>(); signals.CueRequested += cues.Add;
                signals.SetMap("B04.Ground"); signals.Bind(runtime);
                runtime.SetFlag("chapter1.key_handover", true); Assert.That(signals.Context, Is.EqualTo(AudioContext.Mystery));
                runtime.SetFlag("chapter1.sleep", true); Assert.That(signals.Context, Is.EqualTo(AudioContext.Sleep));
                runtime.SetFlag("chapter1.complete", true); Assert.That(signals.Context, Is.EqualTo(AudioContext.Interior));
                runtime.SetFlag(GameAudioSignals.WokeFlag, true); runtime.SetFlag("chapter1.complete", true);
                Assert.That(cues, Is.EqualTo(new[] { "story.sleep", "story.wake" }));
            }
        }
        [Test] public void CompletedStoryDoesNotEnterSleepFromLateSleepFlag()
        {
            GameEventRuntime runtime = CreateRuntime(); using (GameAudioSignals signals = new GameAudioSignals())
            {
                signals.SetMap("campus"); runtime.SetFlag("chapter1.complete", true); signals.Bind(runtime);
                List<string> cues = new List<string>(); signals.CueRequested += cues.Add;
                runtime.SetFlag("chapter1.sleep", true);
                Assert.That(signals.Context, Is.EqualTo(AudioContext.Campus)); Assert.That(cues, Is.Empty);
            }
        }
        [Test] public void RuntimeReplacementDetachesOldEventsAndDoesNotReplayHistoricalCues()
        {
            GameEventRuntime old = CreateRuntime(); GameEventRuntime next = CreateRuntime();
            next.SetFlag("chapter1.sleep", true); next.StartQuest("q"); next.CompleteQuest("q");
            using (GameAudioSignals signals = new GameAudioSignals())
            {
                List<string> cues = new List<string>(); signals.CueRequested += cues.Add;
                signals.SetMap("campus"); signals.Bind(old); signals.Bind(next); signals.Bind(next);
                old.SetFlag(GameAudioSignals.WokeFlag, true); old.StartQuest("q"); old.CompleteQuest("q");
                Assert.That(signals.Context, Is.EqualTo(AudioContext.Sleep)); Assert.That(cues, Is.Empty);
                next.SetFlag("chapter1.complete", true); Assert.That(cues, Is.EqualTo(new[] { "story.wake" }));
            }
        }
        [Test] public void RepeatedBindingAndDisposeRebindHaveBalancedSubscriptions()
        {
            GameEventRuntime runtime = CreateRuntime(); GameAudioSignals signals = new GameAudioSignals();
            List<string> cues = new List<string>(); signals.CueRequested += cues.Add;
            signals.Bind(runtime); signals.Bind(runtime); runtime.StartQuest("q"); runtime.CompleteQuest("q");
            Assert.That(cues, Is.EqualTo(new[] { "quest.complete" }));
            signals.Dispose(); runtime.SetFlag("chapter1.complete", true); Assert.That(cues.Count, Is.EqualTo(1));
            signals.Bind(runtime); Assert.That(cues.Count, Is.EqualTo(1)); signals.Dispose();
        }
        [Test] public void MapChangeDuringDialogueRestoresLatestUnderlyingContext()
        {
            GameEventRuntime runtime = CreateRuntime(); using (GameAudioSignals signals = new GameAudioSignals())
            {
                signals.SetMap("westForest"); signals.Bind(runtime);
                DialogueSession session = runtime.StartDialogue("d"); Assert.That(signals.Context, Is.EqualTo(AudioContext.Dialogue));
                signals.SetMap("campus"); session.End(); Assert.That(signals.Context, Is.EqualTo(AudioContext.Campus));
                runtime.SetFlag(GameAudioSignals.MysteryFlag, true); session = runtime.StartDialogue("d");
                session.End(); Assert.That(signals.Context, Is.EqualTo(AudioContext.Mystery));
            }
        }
        [Test] public void ResetRestoreNotificationChangesContextWithoutOneShotReplay()
        {
            GameEventRuntime runtime = CreateRuntime(); using (GameAudioSignals signals = new GameAudioSignals())
            {
                signals.SetMap("campus"); signals.Bind(runtime); runtime.SetFlag("chapter1.sleep", true);
                List<string> cues = new List<string>(); signals.CueRequested += cues.Add;
                runtime.ResetState(); Assert.That(signals.Context, Is.EqualTo(AudioContext.Campus)); Assert.That(cues, Is.Empty);
                runtime.SetFlag("chapter1.sleep", true); Assert.That(cues, Is.EqualTo(new[] { "story.sleep" }));
            }
        }
        [Test] public void MetadataCueIsForwardedOnlyOnRealTriggeredEvent()
        {
            GameEventRuntime runtime = CreateRuntime(); using (GameAudioSignals signals = new GameAudioSignals())
            {
                List<string> cues = new List<string>(); signals.CueRequested += cues.Add; signals.Bind(runtime);
                runtime.TriggerEvent("e"); runtime.TriggerEvent("e");
                Assert.That(cues, Is.EqualTo(new[] { "ui.confirm" }));
            }
        }
        [Test] public void MissingMediaAndInvalidConfigurationAreExplicit()
        {
            int loads = 0;
            AudioClipCatalog catalog = new AudioClipCatalog(new AudioManifest { cues = new[] {
                new AudioCueDefinition { id = "missing", approvalStatus = "missing_approved_media" },
                new AudioCueDefinition { id = "bad", bus = "Master" },
                new AudioCueDefinition { id = "resource", resourcePath = "Audio/Absent", approvalStatus = "approved" }
            } }, _ => { loads++; return null; });
            Assert.That(loads, Is.EqualTo(1));
            Assert.That(catalog.GetStatus("missing"), Is.EqualTo(AudioClipStatus.MissingApprovedMedia));
            Assert.That(catalog.GetStatus("bad"), Is.EqualTo(AudioClipStatus.InvalidConfiguration));
            Assert.That(catalog.GetStatus("resource"), Is.EqualTo(AudioClipStatus.MissingResource));
            Assert.That(catalog.GetStatus("unknown"), Is.EqualTo(AudioClipStatus.UnknownCue));
            Assert.That(catalog.TryGetReady("missing", out _, out _), Is.False);
        }
        [Test] public void AnyReadyReportsOnlyBusesWithAPlayableClip()
        {
            // The settings menus show the Ambience slider only when an ambience clip is ready.
            AudioClip clip = AudioClip.Create("ready", 1, 1, 44100, false);
            try
            {
                AudioClipCatalog catalog = new AudioClipCatalog(new AudioManifest { cues = new[] {
                    new AudioCueDefinition { id = "sfx", bus = "Sfx", gain = 1f, resourcePath = "Audio/Ready", approvalStatus = "approved" },
                    new AudioCueDefinition { id = "ambience", bus = "Ambience", loop = true, gain = 1f, approvalStatus = "missing_approved_media" }
                } }, _ => clip);
                Assert.That(catalog.AnyReady(AudioBus.Sfx), Is.True);
                Assert.That(catalog.AnyReady(AudioBus.Ambience), Is.False, "Ambience with no approved media has nothing to adjust.");
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); }
        }
        [Test] public void ShippedManifestLoadsSpellWheelAndGlaciateSounds()
        {
            AudioClipCatalog catalog = new AudioClipCatalog(LoadShippedManifest(), path => Resources.Load<AudioClip>(path));
            foreach (string cue in new[] { "ui.spellwheelopen", "ui.spellwheelclose", "spell.glaciate" })
                Assert.That(catalog.GetStatus(cue), Is.EqualTo(AudioClipStatus.Ready), cue + " should load its sound effect.");
        }
        [Test] public void EveryUiCueHasAShippedManifestEntry()
        {
            AudioClipCatalog catalog = new AudioClipCatalog(LoadShippedManifest(), _ => null);
            // AudioFeedbackService.PlayUi plays "ui." + the lower-case enum name.
            foreach (AudioUiCue cue in Enum.GetValues(typeof(AudioUiCue)))
            {
                string id = "ui." + cue.ToString().ToLowerInvariant();
                Assert.That(catalog.GetStatus(id), Is.Not.EqualTo(AudioClipStatus.UnknownCue), id + " is missing from the audio manifest.");
            }
        }
        private static AudioManifest LoadShippedManifest()
        {
            TextAsset asset = Resources.Load<TextAsset>("Audio/audio-manifest");
            Assert.That(asset, Is.Not.Null, "Resources/Audio/audio-manifest.json is missing.");
            return JsonUtility.FromJson<AudioManifest>(asset.text);
        }
        [Test] public void UnityJsonRestoreDoesNotReplaySleepWakeQuestOrDialogueCues()
        {
            GameEventRuntime saved = CreateRuntime(); saved.SetFlag("chapter1.complete", true); saved.StartQuest("q"); saved.CompleteQuest("q");
            GameEventRuntime runtime = CreateRuntime(); using (GameAudioSignals signals = new GameAudioSignals())
            {
                signals.SetMap("campus"); signals.Bind(runtime); runtime.StartDialogue("d");
                List<string> cues = new List<string>(); signals.CueRequested += cues.Add;
                Assert.That(runtime.ImportStateJson(saved.ExportStateJson()), Is.True);
                Assert.That(signals.Context, Is.EqualTo(AudioContext.Campus)); Assert.That(cues, Is.Empty);
            }
        }
        [Test] public void UnitySettingsPersistLegacyMasterAndNotifyOnce()
        {
            float previous = AudioMixSettings.Master; int changes = 0; Action changed = () => changes++;
            AudioMixSettings.Changed += changed;
            try
            {
                AudioMixSettings.SetVolume(AudioBus.Master, -1f);
                int once = changes; AudioMixSettings.SetVolume(AudioBus.Master, -1f);
                Assert.That(changes, Is.EqualTo(once)); Assert.That(PlayerPrefs.GetFloat("lumenfall.master-volume"), Is.Zero);
                Assert.That(AudioMixSettings.Master, Is.Zero);
            }
            finally { AudioMixSettings.Changed -= changed; AudioMixSettings.SetVolume(AudioBus.Master, previous); }
        }
        private static GameEventRuntime CreateRuntime() => new GameEventRuntime(new GameEventCatalog(new[] {
            new GameEventDatabaseDefinition {
                quests = new[] { new QuestDefinition { id = "q" } },
                events = new[] { new GameEventDefinition { id = "e", metadata = new[] { new GameMetadataEntry { key = "audioCue", value = "ui.confirm" } } } },
                dialogues = new[] { new DialogueDefinition { id = "d", startNodeId = "n", nodes = new[] { new DialogueNodeDefinition { id = "n", text = "Hello" } } } }
            }
        }));
    }
}
