using System;
using System.Collections.Generic;
using Lumenfall.Accessibility;
using NUnit.Framework;

namespace Lumenfall.FeatureAcceptance
{
    [Category("PR11")]
    public sealed class Pr11AccessibilityIsolationTests
    {
        private sealed class Store : IAccessibilityPreferenceStore
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public string Read(string key) => Values.TryGetValue(key, out string value) ? value : null;
            public void Write(string key, string value) => Values[key] = value;
            public void Flush() { }
        }
        [Test]
        public void ChangingOneOptionPreservesOtherSettingsAndOriginalSnapshot()
        {
            var original = new AccessibilitySnapshot(1.5f, 60f, true, true, true, false);
            var changed = original.With(AccessibilityOption.CharactersPerSecond, 500f);
            Assert.That(changed.CharactersPerSecond, Is.EqualTo(100f));
            Assert.That(original.CharactersPerSecond, Is.EqualTo(60f));
            Assert.That(changed.TextScale, Is.EqualTo(original.TextScale));
            Assert.That(changed.InstantReveal && changed.ReducedMotion && changed.HighContrast, Is.True);
            Assert.That(changed.HoldToSprint, Is.False);
        }
        [Test]
        public void InvalidSingleStoredPreferenceDoesNotDiscardValidSiblings()
        {
            var store = new Store();
            store.Values["lumenfall.accessibility.text-scale"] = "NaN";
            store.Values["lumenfall.accessibility.read-speed"] = "80";
            store.Values["lumenfall.accessibility.reduced-motion"] = "True";
            store.Values["lumenfall.accessibility.hold-to-sprint"] = "False";
            var preferences = new AccessibilityPreferences(store);
            Assert.That(preferences.Current.TextScale, Is.EqualTo(1f));
            Assert.That(preferences.Current.CharactersPerSecond, Is.EqualTo(80f));
            Assert.That(preferences.Current.ReducedMotion, Is.True);
            Assert.That(preferences.Current.HoldToSprint, Is.False);
            Assert.That(store.Values["lumenfall.accessibility.text-scale"], Is.EqualTo("NaN"));
        }
        [Test]
        public void LocalizationRecoversAfterProviderFailureWithoutCallingBlankIds()
        {
            Func<string, string> previous = Localization.Resolver;
            try
            {
                int calls = 0;
                Localization.Resolver = key => { calls++; if (calls == 1) throw new InvalidOperationException(); return "Translated " + key; };
                Assert.That(Localization.Resolve("story", "English"), Is.EqualTo("English"));
                Assert.That(Localization.Resolve(" ", "Blank"), Is.EqualTo("Blank"));
                Assert.That(Localization.Resolve("story", "English"), Is.EqualTo("Translated story"));
                Assert.That(calls, Is.EqualTo(2));
            }
            finally { Localization.Resolver = previous; }
        }
    }
}
