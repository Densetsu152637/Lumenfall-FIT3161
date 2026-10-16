using Lumenfall.DataDriven;
using Lumenfall.Cutscenes;
using Lumenfall.Startup;
using UnityEngine;
using UnityEngine.Audio;

namespace Lumenfall.Audio
{
    /// <summary>Plays the active world's music and ambience independently of map prefab lifetime.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DataDrivenWorldLoader))]
    public sealed class AreaAudioController : MonoBehaviour
    {
        public static AreaAudioController Instance { get; private set; }
        private DataDrivenWorldLoader loader;
        private AudioFeedbackService audioService;
        private LoopChannel music;
        private LoopChannel ambience;
        private bool applicationPaused;
        public AreaAudioProfile CurrentProfile { get; private set; }

        private void OnEnable()
        {
            Instance = this;
            loader = GetComponent<DataDrivenWorldLoader>();
            if (music == null)
            {
                music = new LoopChannel(gameObject, AudioBus.Music);
                ambience = new LoopChannel(gameObject, AudioBus.Ambience);
            }
            AudioMixSettings.Changed += ApplyBusVolumes;
            loader.AreaChanged += ApplyArea;
            BindAudioService();
            ApplyArea(loader.CurrentArea);
            audioService?.RefreshAreaAmbienceOwnership();
        }

        private void OnDisable()
        {
            if (loader != null) loader.AreaChanged -= ApplyArea;
            AudioMixSettings.Changed -= ApplyBusVolumes;
            if (audioService != null)
            {
                audioService.AreaAmbiencePolicyChanged -= ApplyBusVolumes;
                audioService.RefreshAreaAmbienceOwnership();
                audioService = null;
            }
            music?.Stop();
            ambience?.Stop();
            CurrentProfile = null;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            BindAudioService();
            // The startup menu pauses gameplay; its restored backdrop should still finish its mix.
            music.Tick(Time.unscaledDeltaTime);
            GetAmbiencePolicy(out bool paused, out float contextGain);
            ambience.Tick(Time.unscaledDeltaTime, contextGain, paused);
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            ApplyBusVolumes();
        }

        private void BindAudioService()
        {
            AudioFeedbackService next = AudioFeedbackService.Instance;
            if (next != null && !next.isActiveAndEnabled) next = null;
            if (ReferenceEquals(audioService, next)) return;
            if (audioService != null) audioService.AreaAmbiencePolicyChanged -= ApplyBusVolumes;
            audioService = next;
            if (audioService != null)
            {
                audioService.AreaAmbiencePolicyChanged += ApplyBusVolumes;
                audioService.RefreshAreaAmbienceOwnership();
            }
            ApplyBusVolumes();
        }

        private void GetAmbiencePolicy(out bool paused, out float contextGain)
        {
            if (audioService != null && audioService.isActiveAndEnabled)
            {
                paused = audioService.AreaAmbiencePaused;
                contextGain = audioService.AreaAmbienceSuppressed || StartupMenu.IsMainMenu || CutscenePlayer.OpeningInProgress
                    ? 0f : audioService.AreaAmbienceContextGain;
                return;
            }
            paused = applicationPaused || Time.timeScale <= 0f;
            contextGain = StartupMenu.IsMainMenu || CutscenePlayer.OpeningInProgress ? 0f : 1f;
        }

        private void ApplyBusVolumes()
        {
            music?.ApplyBusVolume(1f, applicationPaused);
            GetAmbiencePolicy(out bool paused, out float contextGain);
            ambience?.ApplyBusVolume(contextGain, paused);
        }

        private void OnDestroy()
        {
            music?.Dispose();
            ambience?.Dispose();
        }

        private void ApplyArea(WorldAreaAsset area)
        {
            CurrentProfile = area == null ? null : area.audioProfile;
            float duration = CurrentProfile == null ? 1.5f : Mathf.Max(0f, CurrentProfile.fadeDuration);
            music.SetClip(CurrentProfile == null ? null : CurrentProfile.music,
                CurrentProfile == null ? 0f : CurrentProfile.musicVolume,
                CurrentProfile == null ? null : CurrentProfile.musicMixerGroup, duration);
            ambience.SetClip(CurrentProfile == null ? null : CurrentProfile.ambience,
                CurrentProfile == null ? 0f : CurrentProfile.ambienceVolume,
                CurrentProfile == null ? null : CurrentProfile.ambienceMixerGroup, duration);
            ApplyBusVolumes();
        }

        // Two sources per channel bound voice count even during rapid portal changes.
        private sealed class LoopChannel
        {
            private readonly AudioSource[] sources = new AudioSource[2];
            private readonly float[] initialVolumes = new float[2];
            private readonly float[] currentVolumes = new float[2];
            private readonly float[] targetVolumes = new float[2];
            private readonly AudioBus bus;
            private AudioClip requestedClip;
            private AudioMixerGroup requestedGroup;
            private float requestedVolume;
            private float elapsed;
            private float duration;
            private bool transitioning;

            public LoopChannel(GameObject owner, AudioBus bus)
            {
                this.bus = bus;
                for (int i = 0; i < sources.Length; i++)
                {
                    sources[i] = owner.AddComponent<AudioSource>();
                    sources[i].playOnAwake = false;
                    sources[i].loop = true;
                    sources[i].spatialBlend = 0f;
                    sources[i].volume = 0f;
                }
            }

            public void SetClip(AudioClip clip, float volume, AudioMixerGroup group, float fadeDuration)
            {
                volume = clip == null ? 0f : Mathf.Clamp01(volume);
                // Reloads and shared interior profiles must preserve playback position and any ongoing fade.
                if (requestedClip == clip && requestedVolume == volume && requestedGroup == group) return;
                requestedClip = clip;
                requestedVolume = volume;
                requestedGroup = group;
                int incoming = -1;
                if (clip != null)
                {
                    for (int i = 0; i < sources.Length; i++)
                        if (sources[i].clip == clip && sources[i].outputAudioMixerGroup == group) incoming = i;
                    if (incoming < 0)
                    {
                        incoming = sources[0].volume <= sources[1].volume ? 0 : 1;
                        sources[incoming].Stop();
                        sources[incoming].clip = clip;
                        sources[incoming].outputAudioMixerGroup = group;
                        currentVolumes[incoming] = 0f;
                        sources[incoming].volume = 0f;
                        sources[incoming].Play();
                    }
                }
                for (int i = 0; i < sources.Length; i++)
                {
                    initialVolumes[i] = currentVolumes[i];
                    targetVolumes[i] = i == incoming ? volume : 0f;
                }
                elapsed = 0f;
                duration = fadeDuration;
                transitioning = true;
                if (duration <= 0f) Tick(0f);
            }

            public void Tick(float deltaTime, float contextGain = 1f, bool paused = false)
            {
                if (transitioning)
                {
                    elapsed += deltaTime;
                    float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                    for (int i = 0; i < sources.Length; i++)
                    {
                        currentVolumes[i] = Mathf.Lerp(initialVolumes[i], targetVolumes[i], t);
                        if (t >= 1f && targetVolumes[i] == 0f)
                        {
                            sources[i].Stop();
                            sources[i].clip = null;
                            currentVolumes[i] = 0f;
                        }
                    }
                    transitioning = t < 1f;
                }
                ApplyBusVolume(contextGain, paused);
            }

            public void ApplyBusVolume(float contextGain, bool paused)
            {
                float busVolume = AudioMixSettings.GetVolume(bus);
                for (int i = 0; i < sources.Length; i++)
                {
                    if (sources[i] == null) continue;
                    sources[i].volume = currentVolumes[i] * busVolume * contextGain;
                    if (paused) sources[i].Pause(); else sources[i].UnPause();
                }
            }

            public void Stop()
            {
                foreach (AudioSource source in sources)
                {
                    source.Stop();
                    source.clip = null;
                    source.volume = 0f;
                }
                System.Array.Clear(currentVolumes, 0, currentVolumes.Length);
                requestedClip = null;
                requestedGroup = null;
                requestedVolume = 0f;
                transitioning = false;
            }

            public void Dispose()
            {
                foreach (AudioSource source in sources)
                    if (source != null) Object.Destroy(source);
            }
        }
    }
}
