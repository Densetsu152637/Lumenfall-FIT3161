using System;
using UnityEngine;

namespace Lumenfall.Accessibility
{
    public static class AccessibilitySettings
    {
        private static AccessibilityPreferences preferences;
        private static AccessibilityPreferences Preferences => preferences ?? (preferences = new AccessibilityPreferences(new PlayerPreferenceStore()));
        public static AccessibilitySnapshot Current => Preferences.Current;
        public static event Action<AccessibilitySnapshot> Changed
        {
            add { Preferences.Changed += value; }
            remove { if (preferences != null) preferences.Changed -= value; }
        }
        public static void Update(AccessibilitySnapshot value) => Preferences.Update(value);
        public static void ResetDefaults() => Preferences.ResetDefaults();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime() { preferences = null; Localization.Resolver = null; }

        private sealed class PlayerPreferenceStore : IAccessibilityPreferenceStore
        {
            public string Read(string key) => PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
            public void Write(string key, string value) => PlayerPrefs.SetString(key, value);
            public void Flush() => PlayerPrefs.Save();
        }
    }
}
