using Lumenfall.DataDriven;
using UnityEngine;
using UnityEngine.Audio;

namespace Lumenfall.Audio
{
    /// <summary>Plays the active world's music and ambience independently of map prefab lifetime.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(DataDrivenWorldLoader))]
    public sealed class AreaAudioController : MonoBehaviour
    {
        private DataDrivenWorldLoader loader;
        private LoopChannel music;
        private LoopChannel ambience;
        public AreaAudioProfile CurrentProfile { get; private set; }

        private void OnEnable()
        {
            loader = GetComponent<DataDrivenWorldLoader>();
            if (music == null)
            {
                music = new LoopChannel(gameObject);
                ambience = new LoopChannel(gameObject);
            }
            loader.AreaChanged += ApplyArea;
            ApplyArea(loader.CurrentArea);
        }

        private void OnDisable()
        {
            if (loader != null) loader.AreaChanged -= ApplyArea;
            music?.Stop();
            ambience?.Stop();
            CurrentProfile = null;
        }

        private void Update()
        {
            // The startup menu pauses gameplay; its restored backdrop should still finish its mix.
            music.Tick(Time.unscaledDeltaTime);
            ambience.Tick(Time.unscaledDeltaTime);
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
        }

        // Two sources per channel bound voice count even during rapid portal changes.
        private sealed class LoopChannel
        {
            private readonly AudioSource[] sources = new AudioSource[2];
            private readonly float[] initialVolumes = new float[2];
            private readonly float[] targetVolumes = new float[2];
            private AudioClip requestedClip;
            private AudioMixerGroup requestedGroup;
            private float requestedVolume;
            private float elapsed;
            private float duration;
            private bool transitioning;

            public LoopChannel(GameObject owner)
            {
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
                        sources[incoming].volume = 0f;
                        sources[incoming].Play();
                    }
                }
                for (int i = 0; i < sources.Length; i++)
                {
                    initialVolumes[i] = sources[i].volume;
                    targetVolumes[i] = i == incoming ? volume : 0f;
                }
                elapsed = 0f;
                duration = fadeDuration;
                transitioning = true;
                if (duration <= 0f) Tick(0f);
            }

            public void Tick(float deltaTime)
            {
                if (!transitioning) return;
                elapsed += deltaTime;
                float t = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
                for (int i = 0; i < sources.Length; i++)
                {
                    sources[i].volume = Mathf.Lerp(initialVolumes[i], targetVolumes[i], t);
                    if (t >= 1f && targetVolumes[i] == 0f)
                    {
                        sources[i].Stop();
                        sources[i].clip = null;
                    }
                }
                transitioning = t < 1f;
            }

            public void Stop()
            {
                foreach (AudioSource source in sources)
                {
                    source.Stop();
                    source.clip = null;
                    source.volume = 0f;
                }
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
