using System;
using System.Globalization;

namespace Lumenfall.Accessibility
{
    public enum AccessibilityOption { TextScale, CharactersPerSecond, InstantReveal, ReducedMotion, HighContrast, HoldToSprint }

    /// <summary>Immutable validated preferences. Default reveal speed preserves authored dialogue timing.</summary>
    public sealed class AccessibilitySnapshot : IEquatable<AccessibilitySnapshot>
    {
        public float TextScale { get; }
        public float CharactersPerSecond { get; }
        public bool InstantReveal { get; }
        public bool ReducedMotion { get; }
        public bool HighContrast { get; }
        public bool HoldToSprint { get; }
        public static AccessibilitySnapshot Defaults => new AccessibilitySnapshot();

        public AccessibilitySnapshot(float textScale = 1f, float charactersPerSecond = 42f,
            bool instantReveal = false, bool reducedMotion = false, bool highContrast = false, bool holdToSprint = true)
        {
            TextScale = SafeClamp(textScale, 1f, 1.75f, 1f);
            CharactersPerSecond = SafeClamp(charactersPerSecond, 10f, 100f, 42f);
            InstantReveal = instantReveal; ReducedMotion = reducedMotion; HighContrast = highContrast; HoldToSprint = holdToSprint;
        }

        public AccessibilitySnapshot With(AccessibilityOption option, float value) => new AccessibilitySnapshot(
            option == AccessibilityOption.TextScale ? value : TextScale,
            option == AccessibilityOption.CharactersPerSecond ? value : CharactersPerSecond,
            option == AccessibilityOption.InstantReveal ? value > 0f : InstantReveal,
            option == AccessibilityOption.ReducedMotion ? value > 0f : ReducedMotion,
            option == AccessibilityOption.HighContrast ? value > 0f : HighContrast,
            option == AccessibilityOption.HoldToSprint ? value > 0f : HoldToSprint);

        public bool Equals(AccessibilitySnapshot other) => other != null && TextScale == other.TextScale &&
            CharactersPerSecond == other.CharactersPerSecond && InstantReveal == other.InstantReveal &&
            ReducedMotion == other.ReducedMotion && HighContrast == other.HighContrast && HoldToSprint == other.HoldToSprint;
        public override bool Equals(object other) => Equals(other as AccessibilitySnapshot);
        public override int GetHashCode() => TextScale.GetHashCode() ^ CharactersPerSecond.GetHashCode() ^
            InstantReveal.GetHashCode() ^ ReducedMotion.GetHashCode() ^ HighContrast.GetHashCode() ^ HoldToSprint.GetHashCode();
        private static float SafeClamp(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
    }

    public interface IAccessibilityPreferenceStore
    {
        string Read(string key);
        void Write(string key, string value);
        void Flush();
    }

    /// <summary>Settings stay outside story save slots; malformed individual values retain safe defaults.</summary>
    public sealed class AccessibilityPreferences
    {
        private const string Prefix = "lumenfall.accessibility.";
        private readonly IAccessibilityPreferenceStore store;
        public AccessibilitySnapshot Current { get; private set; }
        public event Action<AccessibilitySnapshot> Changed;

        public AccessibilityPreferences(IAccessibilityPreferenceStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            Current = new AccessibilitySnapshot(ReadFloat("text-scale", 1f), ReadFloat("read-speed", 42f),
                ReadBool("instant-reveal", false), ReadBool("reduced-motion", false),
                ReadBool("high-contrast", false), ReadBool("hold-to-sprint", true));
        }

        public void Update(AccessibilitySnapshot value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (Current.Equals(value)) return;
            Persist(value);
            Current = value;
            Changed?.Invoke(Current);
        }

        // Always repairs malformed stored values, even when the effective settings already equal defaults.
        public void ResetDefaults()
        {
            AccessibilitySnapshot defaults = AccessibilitySnapshot.Defaults;
            bool changed = !Current.Equals(defaults);
            Persist(defaults);
            Current = defaults;
            if (changed) Changed?.Invoke(Current);
        }

        private void Persist(AccessibilitySnapshot value)
        {
            store.Write(Prefix + "text-scale", value.TextScale.ToString("R", CultureInfo.InvariantCulture));
            store.Write(Prefix + "read-speed", value.CharactersPerSecond.ToString("R", CultureInfo.InvariantCulture));
            store.Write(Prefix + "instant-reveal", value.InstantReveal.ToString());
            store.Write(Prefix + "reduced-motion", value.ReducedMotion.ToString());
            store.Write(Prefix + "high-contrast", value.HighContrast.ToString());
            store.Write(Prefix + "hold-to-sprint", value.HoldToSprint.ToString());
            store.Flush();
        }
        private float ReadFloat(string key, float fallback) => float.TryParse(store.Read(Prefix + key),
            NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
        private bool ReadBool(string key, bool fallback) => bool.TryParse(store.Read(Prefix + key), out bool value) ? value : fallback;
    }

    /// <summary>A sprint toggle is cleared when input is suspended or the preference changes.</summary>
    public sealed class SprintPreferenceState
    {
        private bool toggled;
        public bool Resolve(bool holdToSprint, bool pressed, bool pressedThisFrame, bool inputBlocked)
        {
            if (inputBlocked) { Reset(); return false; }
            if (holdToSprint) { Reset(); return pressed; }
            if (pressedThisFrame) toggled = !toggled;
            return toggled;
        }
        public void Reset() => toggled = false;
    }
}
