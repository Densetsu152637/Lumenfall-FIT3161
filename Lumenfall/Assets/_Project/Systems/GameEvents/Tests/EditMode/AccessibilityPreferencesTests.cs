using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using Lumenfall.Accessibility;

namespace Lumenfall.GameEvents.Tests
{
    public sealed class AccessibilityPreferencesTests
    {
        private sealed class MemoryStore : IAccessibilityPreferenceStore
        {
            public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
            public int Writes;
            public int Flushes;
            public string Read(string key) => Values.TryGetValue(key, out string value) ? value : null;
            public void Write(string key, string value) { Values[key] = value; Writes++; }
            public void Flush() => Flushes++;
        }

        [Test]
        public void MissingSettingsPreserveAuthoredDefaults()
        {
            var value = new AccessibilityPreferences(new MemoryStore()).Current;
            Assert.That(value, Is.EqualTo(AccessibilitySnapshot.Defaults));
            Assert.That(value.CharactersPerSecond, Is.EqualTo(42f));
            Assert.That(value.HoldToSprint, Is.True);
        }
        [TestCase(float.NaN, float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity, float.NaN)]
        public void NonfiniteValuesUseSafeDefaults(float scale, float speed)
        {
            var value = new AccessibilitySnapshot(scale, speed);
            Assert.That(value.TextScale, Is.EqualTo(1f));
            Assert.That(value.CharactersPerSecond, Is.EqualTo(42f));
        }
        [TestCase(-5f, -1f, 1f, 10f)]
        [TestCase(8f, 999f, 1.75f, 100f)]
        public void FiniteValuesClampAtSupportedBoundaries(float scale, float speed, float expectedScale, float expectedSpeed)
        {
            var value = new AccessibilitySnapshot(scale, speed);
            Assert.That(value.TextScale, Is.EqualTo(expectedScale));
            Assert.That(value.CharactersPerSecond, Is.EqualTo(expectedSpeed));
        }
        [Test]
        public void InvalidStoredFieldsAreIsolatedAndDoNotLoseValidPreferences()
        {
            var store = new MemoryStore();
            store.Values["lumenfall.accessibility.text-scale"] = "NaN";
            store.Values["lumenfall.accessibility.read-speed"] = "broken";
            store.Values["lumenfall.accessibility.instant-reveal"] = "certainly";
            store.Values["lumenfall.accessibility.hold-to-sprint"] = "wrong";
            store.Values["lumenfall.accessibility.high-contrast"] = "True";
            var value = new AccessibilityPreferences(store).Current;
            Assert.That(value.TextScale, Is.EqualTo(1f));
            Assert.That(value.CharactersPerSecond, Is.EqualTo(42f));
            Assert.That(value.InstantReveal, Is.False);
            Assert.That(value.HoldToSprint, Is.True);
            Assert.That(value.HighContrast, Is.True);
            Assert.That(store.Flushes, Is.Zero, "Reading must not overwrite invalid preferences before an explicit edit/reset.");
        }
        [Test]
        public void OneEffectiveUpdatePersistsOnceAndNotifiesOnce()
        {
            var store = new MemoryStore();
            var preferences = new AccessibilityPreferences(store);
            int notifications = 0;
            AccessibilitySnapshot observed = null;
            preferences.Changed += value => { notifications++; observed = value; };
            var changed = new AccessibilitySnapshot(1.5f, 65f, true, true, true, false);
            preferences.Update(changed);
            preferences.Update(changed);
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(store.Flushes, Is.EqualTo(1));
            Assert.That(store.Writes, Is.EqualTo(6));
            Assert.That(observed, Is.SameAs(preferences.Current));
            Assert.That(new AccessibilityPreferences(store).Current, Is.EqualTo(changed));
        }
        [Test]
        public void ResetRestoresAllPreferencesAndLeavesUnrelatedDataIntact()
        {
            var store = new MemoryStore();
            store.Values["story.quest"] = "completed";
            store.Values["lumenfall.master-volume"] = "0.4";
            var preferences = new AccessibilityPreferences(store);
            preferences.Update(new AccessibilitySnapshot(1.75f, 10f, true, true, true, false));
            int notifications = 0;
            preferences.Changed += _ => notifications++;
            preferences.ResetDefaults();
            Assert.That(preferences.Current, Is.EqualTo(AccessibilitySnapshot.Defaults));
            Assert.That(new AccessibilityPreferences(store).Current, Is.EqualTo(AccessibilitySnapshot.Defaults));
            Assert.That(notifications, Is.EqualTo(1));
            Assert.That(store.Values["story.quest"], Is.EqualTo("completed"));
            Assert.That(store.Values["lumenfall.master-volume"], Is.EqualTo("0.4"), "Audio is owned separately.");
        }
        [Test]
        public void ResetRepairsInvalidStorageWhenEffectiveValuesAlreadyMatchDefaults()
        {
            var store = new MemoryStore();
            store.Values["lumenfall.accessibility.text-scale"] = "Infinity";
            var preferences = new AccessibilityPreferences(store);
            int notifications = 0;
            preferences.Changed += _ => notifications++;
            preferences.ResetDefaults();
            Assert.That(store.Values["lumenfall.accessibility.text-scale"], Is.EqualTo("1"));
            Assert.That(store.Flushes, Is.EqualTo(1));
            Assert.That(notifications, Is.Zero);
        }
        [Test]
        public void PersistedFloatsAreIndependentOfMachineCulture()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
                var store = new MemoryStore();
                var preferences = new AccessibilityPreferences(store);
                preferences.Update(new AccessibilitySnapshot(1.375f, 52f));
                Assert.That(store.Values["lumenfall.accessibility.text-scale"], Is.EqualTo("1.375"));
                Assert.That(new AccessibilityPreferences(store).Current.TextScale, Is.EqualTo(1.375f));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
        [Test]
        public void NullUpdateIsRejectedWithoutChangingSettings()
        {
            var store = new MemoryStore();
            var preferences = new AccessibilityPreferences(store);
            Assert.Throws<ArgumentNullException>(() => preferences.Update(null));
            Assert.That(preferences.Current, Is.EqualTo(AccessibilitySnapshot.Defaults));
            Assert.That(store.Writes, Is.Zero);
        }
        [Test]
        public void SprintToggleNeedsNewPressAndClearsWhenInputIsBlocked()
        {
            var state = new SprintPreferenceState();
            Assert.That(state.Resolve(false, true, true, false), Is.True);
            Assert.That(state.Resolve(false, false, false, false), Is.True);
            Assert.That(state.Resolve(false, true, true, false), Is.False);
            Assert.That(state.Resolve(false, true, true, false), Is.True);
            Assert.That(state.Resolve(false, true, false, true), Is.False);
            Assert.That(state.Resolve(false, false, false, false), Is.False);
        }
        [Test]
        public void HoldSprintAndModeChangesDoNotLeaveToggleLatched()
        {
            var state = new SprintPreferenceState();
            Assert.That(state.Resolve(false, true, true, false), Is.True);
            Assert.That(state.Resolve(true, false, false, false), Is.False);
            Assert.That(state.Resolve(true, true, true, false), Is.True);
            Assert.That(state.Resolve(false, false, false, false), Is.False);
            state.Reset();
            Assert.That(state.Resolve(false, false, false, false), Is.False);
        }
        [Test]
        public void LocalizationUsesFallbackForMissingBlankOrFailedProvider()
        {
            Func<string, string> previous = Localization.Resolver;
            try
            {
                Localization.Resolver = null;
                Assert.That(Localization.Resolve("id", "English"), Is.EqualTo("English"));
                Localization.Resolver = _ => " ";
                Assert.That(Localization.Resolve("id", "English"), Is.EqualTo("English"));
                Localization.Resolver = _ => throw new InvalidOperationException();
                Assert.That(Localization.Resolve("id", "English"), Is.EqualTo("English"));
                Assert.That(Localization.Resolve(null, null), Is.Empty);
                Assert.That(Localization.Resolve("id", null), Is.EqualTo("id"));
            }
            finally { Localization.Resolver = previous; }
        }
        [Test]
        public void LocalizationPreservesStableDialogueIdsAndAuthoredRichText()
        {
            Func<string, string> previous = Localization.Resolver;
            try
            {
                string id = Localization.DialogueTextId("stella_first_light_dialogue", "lesson");
                string requested = null;
                Localization.Resolver = key => { requested = key; return "<b>Resolved</b>"; };
                Assert.That(Localization.Resolve(id, "Fallback"), Is.EqualTo("<b>Resolved</b>"));
                Assert.That(requested, Is.EqualTo("dialogue.stella_first_light_dialogue.lesson.text"));
                Assert.That(Localization.DialogueChoiceId("dialogue", "node", "accept"), Is.EqualTo("dialogue.dialogue.node.choice.accept"));
            }
            finally { Localization.Resolver = previous; }
        }
        [Test]
        public void HighContrastRemovesOnlyColorTagsAndKeepsEmphasisAndVisibleText()
        {
            Assert.That(AccessibleDialogueText.RemoveColorTags("A <color=#111><b>dark</b></color> line"), Is.EqualTo("A <b>dark</b> line"));
            Assert.That(AccessibleDialogueText.RemoveColorTags("<COLOR = black>Text</COLOR>"), Is.EqualTo("Text"));
            Assert.That(AccessibleDialogueText.RemoveColorTags("<b>Plain</b> <color=broken"), Is.EqualTo("<b>Plain</b> <color=broken"));
            Assert.That(AccessibleDialogueText.RemoveColorTags(null), Is.Empty);
        }
    }
}
