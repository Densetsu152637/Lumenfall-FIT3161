using System;
using System.Linq;
using System.Reflection;
using Lumenfall.Inventory;
using Lumenfall.GameEvents;
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
        private const string Quest = "orio_patch_notes";
        private const string Event = "complete_orio_patch_notes";
        private const string North = "mechanism.orio.socket.north";
        private const string West = "mechanism.orio.socket.west";
        private const string East = "mechanism.orio.socket.east";
        private const string Centre = OrioRuneDiscPuzzle.AnchorId;
        private const string DaisMap = OrioRuneDiscPuzzle.PuzzleMapId;
        private static readonly Vector2Int[] DiscCells = { new Vector2Int(5, 12), new Vector2Int(9, 12), new Vector2Int(7, 9) };
        private static readonly Vector2Int[] SocketCells = { new Vector2Int(7, 12), new Vector2Int(5, 10), new Vector2Int(9, 10), new Vector2Int(7, 11) };

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
            state.ResetAllState();
            casterHost = new GameObject("orio-caster-test");
            caster = casterHost.AddComponent<SpellCaster>();
            EditModeLifecycle.Call(caster, "OnEnable");
            OrioRuneDiscPuzzle.RegisterQuestConditions(runtime);
            RegisterDais(DaisMap);
        }

        [TearDown]
        public void TearDown()
        {
            runtime.ActiveDialogue?.End();
            runtime.ResetState();
            state.ResetAllState();
            EditModeLifecycle.Call(caster, "OnDisable");
            UnityEngine.Object.DestroyImmediate(casterHost);
            if (stateHost != null)
            {
                EditModeLifecycle.SetWorldInstance(previousState);
                UnityEngine.Object.DestroyImmediate(stateHost);
            }
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
            SetDiscs(0f, 0f, 0f);
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
            EditModeLifecycle.FinishDialogue(session);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.False);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void IntendedSocketPressesAlignDiscsAndReportingToOrioCompletes()
        {
            Teach();
            string[] presses = { North, North, North, Centre, Centre, West, West, East };
            foreach (string socket in presses)
            {
                Assert.That(OrioRuneDiscPuzzle.IsQuestReady(state, runtime), Is.False, "Solved before the last press.");
                Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
                Assert.That(caster.Cast(SpellId.Rotara, socket, Vector2Int.up), Is.True);
            }
            Assert.That(DiscRotations(), Is.EqualTo(new[] { 0f, 0f, 0f }));
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active), "Solving the Rune dais is not the hand-in.");
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False, "The completion event requires Orio's report dialogue.");
            DialogueSession report = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(report.AvailableChoices.Any(choice => choice.id == "report"), Is.True);
            Assert.That(report.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.TriggerEvent(Event).Status, Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
        }

        [TestCase(North, true, true, false)]
        [TestCase(West, true, false, true)]
        [TestCase(East, false, true, true)]
        [TestCase(Centre, true, true, true)]
        public void EachSocketTurnsOnlyItsLinkedDiscs(string socket, bool northwest, bool northeast, bool south)
        {
            Teach();
            float[] before = DiscRotations();
            Assert.That(caster.Cast(SpellId.Rotara, socket, Vector2Int.up), Is.True);
            float[] after = DiscRotations();
            bool[] expected = { northwest, northeast, south };
            for (int i = 0; i < expected.Length; i++)
                Assert.That(after[i], Is.EqualTo(expected[i] ? OrioRuneDiscPuzzle.NextQuarterTurn(before[i]) : before[i]), OrioRuneDiscPuzzle.DiscIds[i]);
        }

        [Test]
        public void DiscsCannotBeTurnedDirectly()
        {
            Teach();
            float[] before = DiscRotations();
            foreach (string disc in OrioRuneDiscPuzzle.DiscIds)
                Assert.That(caster.Cast(SpellId.Rotara, disc, Vector2Int.up), Is.False, disc);
            Assert.That(DiscRotations(), Is.EqualTo(before));
        }

        [TestCase(90f)] [TestCase(180f)] [TestCase(270f)] [TestCase(0.4f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(float.NegativeInfinity)]
        public void SpoofedFlagAndOneWrongDiscCannotComplete(float rotation)
        {
            Teach(); SetDiscs(0f, rotation, 0f);
            runtime.SetFlag("orio.patch_notes.mechanism_aligned", true);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
            DialogueSession dialogue = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(dialogue.AvailableChoices.Any(choice => choice.id == "report"), Is.False);
        }

        [Test]
        public void DiscsOutsideTheDeepForestCannotComplete()
        {
            Teach(); runtime.SetFlag("orio.patch_notes.mechanism_aligned", true);
            RegisterDais("campus"); SetDiscs(0f, 0f, 0f);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False, "The deep forest dais is still at its authored start.");
            Assert.That(OrioRuneDiscPuzzle.TryGetLinkedDiscs("campus", Centre, out _), Is.False, "Sockets only work in the deep forest.");
            Assert.That(OrioRuneDiscPuzzle.TryGetLinkedDiscs("westForest", Centre, out _), Is.False, "Sockets only work in the deep forest.");
        }

        [Test]
        public void CompletionReadsTheArchivedDeepForestLeftBehind()
        {
            Teach();
            EnterForest();
            Assert.That(OrioRuneDiscPuzzle.IsQuestReady(state, runtime), Is.False, "The deep forest dais was left unsolved.");
            RegisterDais(DaisMap); SetDiscs(0f, 0f, 0f);
            EnterForest();
            Assert.That(state.CurrentMapId, Is.EqualTo("westForest"));
            DialogueSession report = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(report.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void FourCentreTurnsWrapAroundAndComplete()
        {
            Teach();
            SetDiscs(0f, 0f, 0f);
            caster.Cast(SpellId.Rotara, Centre, Vector2Int.up);
            Assert.That(OrioRuneDiscPuzzle.IsQuestReady(state, runtime), Is.False);
            foreach (int unused in new[] { 1, 2, 3 }) caster.Cast(SpellId.Rotara, Centre, Vector2Int.up);
            Assert.That(DiscRotations(), Is.EqualTo(new[] { 0f, 0f, 0f }));
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Active));
            DialogueSession report = runtime.StartDialogue(Quest + "_dialogue");
            Assert.That(report.SelectChoice("report"), Is.True);
            Assert.That(runtime.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.TriggerEvent(Event).Status, Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered));
        }

        [Test]
        public void NewGameRestartsDiscsAtAuthoredAngles()
        {
            SetDiscs(0f, 0f, 0f);
            state.ResetAllState(); RegisterDais(DaisMap);
            Assert.That(DiscRotations(), Is.EqualTo(OrioRuneDiscPuzzle.DiscStartRotations.Select(angle => (float)angle).ToArray()));
            Assert.That(OrioRuneDiscPuzzle.AreDiscsAligned(DaisMap, Snapshots()), Is.False, "The authored start is unsolved.");
        }

        [Test]
        public void ResolverDoesNotRetainOldMapOrRuntime()
        {
            Teach(); SetDiscs(0f, 0f, 0f);
            GameEventRuntime replacement = new GameEventRuntime(runtime.Catalog);
            OrioRuneDiscPuzzle.RegisterQuestConditions(replacement);
            Assert.That(replacement.TriggerEvent(Event).Succeeded, Is.False);
            replacement.StartQuest(Quest); replacement.SetFlag("spell.unlocked.Rotara", true);
            DialogueSession report = replacement.StartDialogue(Quest + "_dialogue");
            Assert.That(report.SelectChoice("report"), Is.True);
            Assert.That(replacement.GetQuestStatus(Quest), Is.EqualTo(QuestStatus.Completed));
            state.ResetAllState(); RegisterDais(DaisMap);
            Assert.That(runtime.TriggerEvent(Event).Succeeded, Is.False);
        }

        [TestCase(-90f, 0f)] [TestCase(270f, 0f)] [TestCase(360f, 90f)] [TestCase(450f, 180f)]
        public void QuarterTurnsNormalizeSavedAngles(float angle, float next)
        { Assert.That(OrioRuneDiscPuzzle.NextQuarterTurn(angle), Is.EqualTo(next)); }

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
            EditModeLifecycle.FinishDialogue(session);
            Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
        }

        private void RegisterDais(string map)
        {
            state.BeginMapRebuild(map);
            for (int i = 0; i < OrioRuneDiscPuzzle.DiscIds.Length; i++)
            {
                state.RegisterObject(OrioRuneDiscPuzzle.DiscIds[i], null, DiscCells[i], Vector2Int.one, false,
                    new[] { OrioRuneDiscPuzzle.DiscTag });
                int start = OrioRuneDiscPuzzle.DiscStartRotations[i];
                state.SetProperty(OrioRuneDiscPuzzle.DiscIds[i], item => item.rotation = start);
            }
            for (int i = 0; i < OrioRuneDiscPuzzle.SocketIds.Length; i++)
                state.RegisterObject(OrioRuneDiscPuzzle.SocketIds[i], null, SocketCells[i], Vector2Int.one, true,
                    new[] { "rotara" });
            state.EndMapRebuild();
        }

        private void EnterForest()
        {
            state.BeginMapRebuild("westForest");
            state.EndMapRebuild();
        }

        private void SetDiscs(params float[] rotations)
        {
            for (int i = 0; i < rotations.Length; i++)
            {
                float rotation = rotations[i];
                state.SetProperty(OrioRuneDiscPuzzle.DiscIds[i], item => item.rotation = rotation);
            }
        }

        private WorldObjectSnapshot[] Snapshots() =>
            OrioRuneDiscPuzzle.DiscIds.Select(id => state.TryGetObject(id, out WorldObjectSnapshot snapshot) ? snapshot : null).ToArray();

        private float[] DiscRotations() => Snapshots().Select(snapshot => snapshot.rotation).ToArray();
    }
}
