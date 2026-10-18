using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lumenfall.Cutscenes;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using Lumenfall.Startup;

namespace Lumenfall.Audio
{
    /// <summary>Persistent event-to-playback bridge. No mixer or scene edits are required.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioFeedbackService : MonoBehaviour
    {
        private sealed class LoopChannel
        {
            public AudioSource Source;
            public float Gain;
            public float Target;
        }
        private sealed class Voice
        {
            public AudioSource Source;
            public float Gain;
            public bool Ui;
            public bool Paused;
        }
        private static AudioFeedbackService instance;
        private readonly GameAudioSignals signals = new GameAudioSignals();
        private readonly LoopChannel[] music = new LoopChannel[2];
        private readonly LoopChannel[] ambience = new LoopChannel[2];
        private readonly Voice[] voices = new Voice[8];
        private GameEventManager manager;
        private WorldStateService world;
        private GridMover player;
        private SpellCaster caster;
        private AudioClipCatalog clips;
        private string musicCue;
        private string ambienceCue;
        private bool applicationPaused;
        private bool gameplayPaused;
        private bool menuMusic;
        private bool openingSilence;
        // The forest track waits this long after the opening's last line of dialogue closes.
        private const float OpeningMusicDelay = 1f;
        private float openingQuietUntil = float.NegativeInfinity;
        public static AudioFeedbackService Instance => instance;
        public AudioContext CurrentContext => signals.Context;
        /// <summary>True once an ambience clip ships; until then the settings menus hide the Ambience volume.</summary>
        public static bool HasAmbience => instance != null && instance.clips != null && instance.clips.AnyReady(AudioBus.Ambience);
        public AudioClipStatus GetClipStatus(string cueId) => clips == null ? AudioClipStatus.InvalidConfiguration : clips.GetStatus(cueId);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<AudioFeedbackService>() == null)
                new GameObject("[AudioFeedbackService]").AddComponent<AudioFeedbackService>();
        }
        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject);
            for (int i = 0; i < 2; i++)
            { music[i] = new LoopChannel { Source = CreateSource("Music " + i, true) }; ambience[i] = new LoopChannel { Source = CreateSource("Ambience " + i, true) }; }
            for (int i = 0; i < voices.Length; i++) voices[i] = new Voice { Source = CreateSource("SFX " + i, false) };
            AudioManifest manifest = null; TextAsset asset = Resources.Load<TextAsset>("Audio/audio-manifest");
            if (asset != null)
            {
                try { manifest = JsonUtility.FromJson<AudioManifest>(asset.text); }
                catch (Exception error) { Debug.LogWarning("Audio manifest could not be read: " + error.Message, this); }
            }
            clips = new AudioClipCatalog(manifest, path => Resources.Load<AudioClip>(path));
            if (clips.ConfigurationIssues.Count > 0)
                Debug.LogWarning("Audio configuration: " + string.Join("; ", clips.ConfigurationIssues) + ". Supply approved clips using the audio handoff manifest.", this);
        }
        private AudioSource CreateSource(string sourceName, bool loop)
        {
            GameObject owner = new GameObject(sourceName); owner.transform.SetParent(transform, false);
            AudioSource source = owner.AddComponent<AudioSource>(); source.playOnAwake = false;
            source.loop = loop; source.spatialBlend = 0f; source.volume = 0f; return source;
        }
        private void OnEnable()
        {
            if (instance != this) return;
            signals.ContextChanged += OnContextChanged; signals.CueRequested += OnCueRequested;
            AudioMixSettings.Changed += ApplyVolumes; SceneManager.sceneLoaded += OnSceneLoaded;
            RefreshDependencies(); ApplyVolumes(); RefreshPlayback();
        }
        private void OnDisable()
        {
            if (instance != this) return;
            AudioMixSettings.Changed -= ApplyVolumes; SceneManager.sceneLoaded -= OnSceneLoaded;
            signals.ContextChanged -= OnContextChanged; signals.CueRequested -= OnCueRequested;
            signals.Dispose(); UnbindWorld(); UnbindPlayer();
            if (manager != null) manager.RuntimeChanged -= OnRuntimeChanged;
            manager = null; musicCue = null; ambienceCue = null;
            foreach (LoopChannel channel in music) Stop(channel);
            foreach (LoopChannel channel in ambience) Stop(channel);
            foreach (Voice voice in voices) if (voice != null) { voice.Source.Stop(); voice.Source.clip = null; voice.Paused = false; }
        }
        private static void Stop(LoopChannel channel)
        { if (channel == null) return; channel.Source.Stop(); channel.Source.clip = null; channel.Gain = channel.Target = 0f; }
        private void OnDestroy() { if (instance == this) instance = null; }
        private void OnSceneLoaded(Scene _, LoadSceneMode __) { RefreshDependencies(); BindPlayer(); }
        private void RefreshDependencies()
        {
            GameEventManager next = GameEventManager.Instance;
            if (!ReferenceEquals(manager, next))
            {
                if (manager != null) manager.RuntimeChanged -= OnRuntimeChanged;
                manager = next;
                if (manager != null) manager.RuntimeChanged += OnRuntimeChanged;
                signals.Bind(manager == null ? null : manager.Runtime);
            }
            WorldStateService nextWorld = WorldStateService.Instance;
            if (!ReferenceEquals(world, nextWorld))
            {
                UnbindWorld(); world = nextWorld;
                if (world != null) world.MapRebuilt += OnMapRebuilt;
                signals.SetMap(world != null && world.IsReady ? world.CurrentMapId : null); BindPlayer();
            }
        }
        private void UnbindWorld() { if (world != null) world.MapRebuilt -= OnMapRebuilt; world = null; }
        private void OnRuntimeChanged(GameEventRuntime next) { signals.Bind(next); RefreshPlayback(); ApplyVolumes(); }
        private void OnMapRebuilt(MapRebuildEvent rebuilt) { signals.SetMap(rebuilt.MapId); BindPlayer(); RefreshPlayback(); }
        private void BindPlayer()
        {
            UnbindPlayer(); GridPlayerInput input = FindAnyObjectByType<GridPlayerInput>();
            player = input == null ? null : input.GetComponent<GridMover>();
            if (player != null) { player.MoveCompleted += OnStep; BindCaster(); }
        }
        private void BindCaster()
        {
            SpellCaster next = player == null ? null : player.GetComponent<SpellCaster>();
            if (ReferenceEquals(caster, next)) return;
            if (caster != null) caster.CastCompleted -= OnCast;
            caster = next; if (caster != null) caster.CastCompleted += OnCast;
        }
        private void UnbindPlayer()
        { if (player != null) player.MoveCompleted -= OnStep; if (caster != null) caster.CastCompleted -= OnCast; player = null; caster = null; }
        private void OnStep(GridMover mover, Vector2Int _)
        {
            AudioContext map = AudioContextPolicy.Resolve(world?.CurrentMapId, false, false, false);
            PlayCue(map == AudioContext.Forest ? "step.forest" : map == AudioContext.Campus ? "step.campus" : "step.interior", false, mover.transform.position);
        }
        private void OnCast(SpellId spell, string _, bool changed)
        { PlayCue(changed ? "spell." + spell.ToString().ToLowerInvariant() : "spell.no-change", false, player == null ? transform.position : player.transform.position); }
        private void OnContextChanged(AudioContext _) { RefreshPlayback(); ApplyVolumes(); }
        private void OnCueRequested(string cue) { PlayCue(cue, cue.StartsWith("ui.", StringComparison.Ordinal), transform.position); }
        public static void PlayUi(AudioUiCue cue)
        { if (instance != null && instance.isActiveAndEnabled) instance.PlayCue("ui." + cue.ToString().ToLowerInvariant(), true, instance.transform.position); }
        private void PlayCue(string cue, bool ui, Vector3 position)
        {
            if (applicationPaused || (!ui && !AudioContextPolicy.CanPlayGameplay(Time.timeScale <= 0f, applicationPaused))) return;
            if (!clips.TryGetReady(cue, out AudioCueDefinition definition, out AudioClip clip) || definition.loop) return;
            // A bounded pool drops excess simultaneous cues; paused voices retain ownership.
            foreach (Voice voice in voices)
            {
                if (voice.Paused || voice.Source.isPlaying) continue;
                voice.Ui = ui; voice.Gain = definition.gain; voice.Source.transform.position = position;
                voice.Source.spatialBlend = ui ? 0f : 0.2f; voice.Source.clip = clip;
                voice.Source.volume = AudioMixSettings.Sfx * voice.Gain; Lumenfall.Audio.AudioPlayback.Play(voice.Source); return;
            }
        }
        private void RefreshPlayback()
        {
            string key = signals.Context == AudioContext.Silent ? null : signals.Context.ToString().ToLowerInvariant();
            SetLoop(music, ref musicCue, SelectMusicCue(key));
            SetLoop(ambience, ref ambienceCue, key == null || openingSilence ? null : "ambience." + key);
        }
        private string SelectMusicCue(string key)
        {
            if (openingSilence) return null;
            if (menuMusic) return "music.menu";
            // Dialogue, mystery and sleep keep the map's track (ducked) until they have music of their own.
            if (signals.Context == AudioContext.Dialogue || signals.Context == AudioContext.Mystery || signals.Context == AudioContext.Sleep)
            { string mood = "music." + key; if (clips.GetStatus(mood) == AudioClipStatus.Ready) return mood; }
            return AudioContextPolicy.MapMusicCue(signals.MapId);
        }
        private void SetLoop(LoopChannel[] channels, ref string current, string cue)
        {
            if (current == cue) return; current = cue;
            AudioClip clip = null; AudioCueDefinition definition = null;
            bool ready = clips.TryGetReady(cue, out definition, out clip) && definition.loop;
            LoopChannel selected = channels[0].Gain <= channels[1].Gain ? channels[0] : channels[1];
            foreach (LoopChannel channel in channels) channel.Target = 0f;
            if (!ready) return;
            // Reuse an already playing clip when context returns during a fade.
            foreach (LoopChannel channel in channels) if (channel.Source.clip == clip) { selected = channel; break; }
            if (selected.Source.clip != clip)
            { selected.Source.Stop(); selected.Source.clip = clip; selected.Gain = 0f; selected.Source.volume = 0f; Lumenfall.Audio.AudioPlayback.Play(selected.Source); }
            selected.Target = definition.gain;
            if (applicationPaused || (ReferenceEquals(channels, ambience) && gameplayPaused)) selected.Source.Pause();
        }
        private void Update()
        {
            RefreshDependencies(); BindCaster();
            bool menu = StartupMenu.IsMainMenu;
            if (CutscenePlayer.OpeningInProgress) openingQuietUntil = Time.unscaledTime + OpeningMusicDelay;
            bool opening = Time.unscaledTime < openingQuietUntil;
            if (menu != menuMusic || opening != openingSilence) { menuMusic = menu; openingSilence = opening; RefreshPlayback(); }
            bool paused = Time.timeScale <= 0f;
            if (paused != gameplayPaused) { gameplayPaused = paused; ApplyPause(); }
            Advance(music, AudioMixSettings.Music * AudioContextPolicy.MusicGain(signals.DialogueActive, signals.Sleeping));
            Advance(ambience, AudioMixSettings.Ambience * AudioContextPolicy.AmbienceGain(signals.DialogueActive, signals.Sleeping));
        }
        private static void Advance(LoopChannel[] channels, float bus)
        {
            foreach (LoopChannel channel in channels)
            {
                channel.Gain = Mathf.MoveTowards(channel.Gain, channel.Target, Time.unscaledDeltaTime / 0.6f);
                channel.Source.volume = channel.Gain * bus;
                if (channel.Target == 0f && channel.Gain == 0f && channel.Source.clip != null)
                { channel.Source.Stop(); channel.Source.clip = null; }
            }
        }
        private void ApplyVolumes()
        {
            AudioListener.volume = AudioMixSettings.Master;
            foreach (LoopChannel channel in music) if (channel != null) channel.Source.volume = channel.Gain * AudioMixSettings.Music * AudioContextPolicy.MusicGain(signals.DialogueActive, signals.Sleeping);
            foreach (LoopChannel channel in ambience) if (channel != null) channel.Source.volume = channel.Gain * AudioMixSettings.Ambience * AudioContextPolicy.AmbienceGain(signals.DialogueActive, signals.Sleeping);
            foreach (Voice voice in voices) if (voice != null) voice.Source.volume = voice.Gain * AudioMixSettings.Sfx;
        }
        private void OnApplicationPause(bool paused) { applicationPaused = paused; ApplyPause(); }
        private void ApplyPause()
        {
            foreach (LoopChannel channel in music) { if (applicationPaused) channel.Source.Pause(); else Lumenfall.Audio.AudioPlayback.UnPause(channel.Source); }
            foreach (LoopChannel channel in ambience) { if (applicationPaused || gameplayPaused) channel.Source.Pause(); else Lumenfall.Audio.AudioPlayback.UnPause(channel.Source); }
            foreach (Voice voice in voices)
            {
                bool paused = AudioContextPolicy.ShouldPauseVoice(voice.Source.isPlaying || voice.Paused, voice.Ui, gameplayPaused, applicationPaused);
                if (!voice.Source.isPlaying && !voice.Paused) { voice.Source.clip = null; continue; }
                if (voice.Paused == paused) continue; voice.Paused = paused;
                if (paused) voice.Source.Pause(); else Lumenfall.Audio.AudioPlayback.UnPause(voice.Source);
            }
        }
    }
}
