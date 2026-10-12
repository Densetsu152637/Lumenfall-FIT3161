using System;
using System.Linq;
using Lumenfall.GameEvents;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class OrioPatchNotesTests
    {
        private GameEventRuntime runtime;
        private WorldStateService state;
        private WorldStateService previousState;
        private GameObject stateHost;
        private GameObject casterHost;
        private SpellCaster caster;
        private WorldStateService previousState;
        private const string Quest = "orio_patch_notes";
        private const string Event = "complete_orio_patch_notes";

        [SetUp]
        public void SetUp()
        {
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, false);
            previousState = WorldStateService.Instance;
            stateHost = new GameObject("orio-world-test");
            state = stateHost.AddComponent<WorldStateService>();
            EditModeLifecycle.SetWorldInstance(state);
            casterHost = new GameObject("orio-caster-test");
            caster = casterHost.AddComponent<SpellCaster>();
            EditModeLifecycle.Call(caster, "OnEnable");
            RotaraGearMechanism.RegisterQuestConditions(runtime);
            RegisterForestGear(180);
        }

        [TearDown]
        public void TearDown()
        {
            runtime.ActiveDialogue?.End();
            runtime.ResetState();
            state.ResetAllState();
            EditModeLifecycle.Call(caster, "OnDisable");
            UnityEngine.Object.DestroyImmediate(casterHost);
            UnityEngine.Object.DestroyImmediate(stateHost);
            EditModeLifecycle.SetWorldInstance(previousState);
        }

        [Test]
        public void OpeningLeavingAcceptanceAndAbandonmentDoNotTeach()
        {
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
            DialogueSession session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
            session.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
            session = runtime.StartDialogue(Quest + "_dialogue");
            session.SelectChoice("accept");
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
            session.End();
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
            state.SetProperty(RotaraGearMechanism.PuzzleObjectId, item => item.rotation = 90);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False, "Active but unfinished lesson cannot complete.");
        }

        [Test]
        public void TerminalReminderRecoversAbandonedLessonWithoutGrantingGravitate()
        {
            DialogueSession session = runtime.StartDialogue(Quest + "_dialogue");
            session.SelectChoice("accept"); session.End();
            session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(session.SelectChoice("active"), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
            Assert.That(session.Continue(), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.False);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void ThreeClockwiseCastsReachTargetThenConfirmationCompletesOnce()
        {
            Teach();
            foreach (float expected in new[] { 270f, 0f, 90f })
            {
                Assert.That(caster.Cast(SpellId.Rotara, RotaraGearMechanism.PuzzleObjectId, Vector2Int.up), Is.True);
                state.TryGetObject(RotaraGearMechanism.PuzzleObjectId, out WorldObjectSnapshot snapshot);
                Assert.That(snapshot.rotation, Is.EqualTo(expected));
                Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
                if (expected != 90f) Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
            }
            DialogueSession report = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(report.AvailableChoices.Any(choice => choice.id == "report"), Is.True);
            Assert.That(report.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.TriggerEvent(Event).Status, Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
        }

        [TestCase(0f)] [TestCase(180f)] [TestCase(270f)] [TestCase(89.6f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(float.NegativeInfinity)]
        public void SpoofedFlagAndWrongOrientationCannotComplete(float rotation)
        {
            Teach(); state.SetProperty(RotaraGearMechanism.PuzzleObjectId, item => item.rotation = rotation);
            runtime.SetFlag("orio.patch_notes.mechanism_aligned", true);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
            DialogueSession dialogue = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(dialogue.AvailableChoices.Any(choice => choice.id == "report"), Is.False);
        }

        [Test]
        public void CampusDuplicateAndOtherForestObjectCannotComplete()
        {
            Teach(); runtime.SetFlag("orio.patch_notes.mechanism_aligned", true);
            RegisterGear("campus", RotaraGearMechanism.PuzzleObjectId, 90);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
            RegisterGear("westForest", "other.gear", 90);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
        }

        [TestCase(false)] [TestCase(true)]
        public void OvershootWraparoundAndRestorePermitDeterministicRetry(bool legacyMap)
        {
            Teach();
            state.SetProperty(RotaraGearMechanism.PuzzleObjectId, item => item.rotation = 90);
            string worldSave = state.CaptureStateJson(); string questSave = runtime.ExportStateJson(false);
            if (legacyMap) worldSave = worldSave.Replace("westForest", "eastForest");
            caster.Cast(SpellId.Rotara, RotaraGearMechanism.PuzzleObjectId, Vector2Int.up);
            Assert.That(RotaraGearMechanism.IsQuestReady(state, runtime), Is.False);
            foreach (int unused in new[] { 1, 2, 3 }) caster.Cast(SpellId.Rotara, RotaraGearMechanism.PuzzleObjectId, Vector2Int.up);
            Assert.That(RotaraGearMechanism.IsQuestReady(state, runtime), Is.True);
            state.ResetAllState(); RegisterForestGear(180);
            Assert.That(state.RestoreStateJson(worldSave), Is.True);
            runtime.ResetState(); Assert.That(runtime.ImportStateJson(questSave), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
            Assert.That(RotaraGearMechanism.IsQuestReady(state, runtime), Is.True);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.True);
            questSave = runtime.ExportStateJson(false); runtime.ResetState(); runtime.ImportStateJson(questSave);
            Assert.That(runtime.TriggerEvent(Event).Status, Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
        }

        [Test]
        public void ResolverDoesNotRetainOldMapOrRuntime()
        {
            Teach(); state.SetProperty(RotaraGearMechanism.PuzzleObjectId, item => item.rotation = 90);
            GameEventRuntime replacement = new GameEventRuntime(runtime.Catalog);
            RotaraGearMechanism.RegisterQuestConditions(replacement);
            Assert.That(replacement.TriggerEvent(Event).Succeeded, Is.False);
            replacement.StartQuest(Quest); replacement.SetFlag("spell.unlocked.Rotara", true);
            Assert.That(replacement.TriggerEvent(Event).Succeeded, Is.True);
            RegisterGear("campus", RotaraGearMechanism.PuzzleObjectId, 90);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
        }

        [TestCase(-90f, 0f)] [TestCase(270f, 0f)] [TestCase(360f, 90f)] [TestCase(450f, 180f)]
        public void QuarterTurnsNormalizeSavedAngles(float angle, float next)
        { Assert.That(RotaraGearMechanism.NextQuarterTurn(angle), Is.EqualTo(next)); }

        [Test]
        public void RotaraRejectsIncompatibleTargets()
        {
            Teach(); state.RegisterObject("ordinary", null, Vector2Int.zero, Vector2Int.one, true, Array.Empty<string>());
            Assert.That(caster.Cast(SpellId.Rotara, "ordinary", Vector2Int.up), Is.False);
        }

        private void Teach()
        {
            DialogueSession session = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(session.SelectChoice("accept"), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
            Assert.That(session.Continue(), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
        }

        private void RegisterForestGear(float rotation) => RegisterGear("westForest", RotaraGearMechanism.PuzzleObjectId, rotation);
        private void RegisterGear(string map, string id, float rotation)
        {
            state.BeginMapRebuild(map);
            state.RegisterObject(id, null, new Vector2Int(8, 21), Vector2Int.one, true, new[] { "rotara" });
            state.EndMapRebuild();
            state.SetProperty(id, item => item.rotation = rotation);
        }
    }
}
