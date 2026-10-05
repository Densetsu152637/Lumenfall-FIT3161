using UnityEngine;
using UnityEngine.Audio;

namespace Lumenfall.Audio
{
    [CreateAssetMenu(menuName = "Lumenfall/Area Audio Profile", fileName = "AreaAudioProfile")]
    public sealed class AreaAudioProfile : ScriptableObject
    {
        public AudioClip music;
        [Range(0f, 1f)] public float musicVolume = 0.5f;
        public AudioMixerGroup musicMixerGroup;
        public AudioClip ambience;
        [Range(0f, 1f)] public float ambienceVolume = 0.4f;
        public AudioMixerGroup ambienceMixerGroup;
        [Min(0f)] public float fadeDuration = 1.5f;
    }
}
