using UnityEngine;

namespace Lumenfall.Audio
{
    /// <summary>Native playback boundary; routing and cue state remain active during tests.</summary>
    public static class AudioPlayback
    {
#if UNITY_EDITOR
        public const string TestRunSessionKey = "Lumenfall.Audio.AutomatedTestRun";
#endif
        public static bool IsSuppressed
        {
            get
            {
#if UNITY_EDITOR
                return UnityEditor.SessionState.GetBool(TestRunSessionKey, false);
#else
                return false;
#endif
            }
        }

        public static void Play(AudioSource source)
        {
            if (!IsSuppressed) source.Play();
        }

        public static void PlayOneShot(AudioSource source, AudioClip clip, float volume = 1f)
        {
            if (!IsSuppressed) source.PlayOneShot(clip, volume);
        }

        public static void UnPause(AudioSource source)
        {
            if (!IsSuppressed) source.UnPause();
        }
    }
}
