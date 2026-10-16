using System;
using UnityEngine;

namespace Lumenfall.Audio
{
    /// <summary>Single volume preference authority, independent of save slots and UI lifetime.</summary>
    public static class AudioMixSettings
    {
        private static readonly string[] Keys = { "lumenfall.master-volume", "lumenfall.music-volume",
            "lumenfall.sfx-volume", "lumenfall.ambience-volume" };
        private static AudioMixLevels levels;
        public static event Action Changed;
        public static float Master => GetVolume(AudioBus.Master);
        public static float Music => GetVolume(AudioBus.Music);
        public static float Sfx => GetVolume(AudioBus.Sfx);
        public static float Ambience => GetVolume(AudioBus.Ambience);
        public static float GetVolume(AudioBus bus) { EnsureLoaded(); return levels.GetVolume(bus); }
        public static void SetVolume(AudioBus bus, float value)
        {
            EnsureLoaded();
            if (!levels.SetVolume(bus, value)) return;
            PlayerPrefs.SetFloat(Keys[(int)bus], levels.GetVolume(bus)); PlayerPrefs.Save(); Changed?.Invoke();
        }
        public static void ResetDefaults()
        {
            EnsureLoaded(); bool changed = false;
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
            { changed |= levels.SetVolume(bus, AudioMixLevels.DefaultVolume(bus)); PlayerPrefs.SetFloat(Keys[(int)bus], levels.GetVolume(bus)); }
            PlayerPrefs.Save(); if (changed) Changed?.Invoke();
        }
        private static void EnsureLoaded()
        {
            if (levels != null) return;
            levels = new AudioMixLevels(); bool repaired = false;
            foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
            {
                float raw = PlayerPrefs.GetFloat(Keys[(int)bus], AudioMixLevels.DefaultVolume(bus));
                levels.SetVolume(bus, raw); float normalized = levels.GetVolume(bus);
                if (raw != normalized) { PlayerPrefs.SetFloat(Keys[(int)bus], normalized); repaired = true; }
            }
            if (repaired) PlayerPrefs.Save();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession() { levels = null; Changed = null; }
    }
}
