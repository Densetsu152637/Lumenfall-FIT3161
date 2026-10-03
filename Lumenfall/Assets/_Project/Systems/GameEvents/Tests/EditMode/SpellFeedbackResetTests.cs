using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SpellFeedbackResetTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private WorldStateService state;
        private GridWorld grid;
        private SpellCaster caster;
        [SetUp] public void SetUp()
        {
            GameEventManager.Instance.Runtime.ResetState();
            grid = NewObject("grid").AddComponent<GridWorld>(); grid.ConfigureBounds(20, 20);
            state = NewObject("state").AddComponent<WorldStateService>(); EditModeLifecycle.SetWorldInstance(state);
            state.BeginMapRebuild("feedback-map");
            GameObject player = NewObject("caster");
            Assert.That(player.AddComponent<GridMover>().TryTeleportTo(grid, new Vector2Int(2, 1)), Is.True);
            caster = player.AddComponent<SpellCaster>();
        }
        [TearDown] public void TearDown()
        {
            GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = created.Count - 1; i >= 0; i--)
            {
                foreach (SpellCaster item in created[i].GetComponents<SpellCaster>()) EditModeLifecycle.Call(item, "OnDisable");
                foreach (GridMover item in created[i].GetComponents<GridMover>()) EditModeLifecycle.Call(item, "OnDisable");
                UnityEngine.Object.DestroyImmediate(created[i]);
            }
            created.Clear(); GameEventManager.Instance.Runtime.ResetState();
        }
        [Test] public void EveryRejectedDirectCastReportsOneTypedAndLegacyEventWithoutMutation()
        {
            Register("relic", new Vector2Int(2, 2), "ampere");
            Register("wrong", new Vector2Int(3, 1), "rotara");
            Register("far", new Vector2Int(10, 10), "ampere"); state.EndMapRebuild();
            var outcomes = new List<SpellCastOutcome>(); int completed = 0;
            caster.CastFeedback += result => outcomes.Add(result.Outcome);
            caster.CastCompleted += (spell, target, changed) => { completed++; Assert.That(changed, Is.False); };
            Assert.That(caster.TryCast(SpellId.Ampere, "relic", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.LockedSpell));
            Unlock(SpellId.Ampere);
            Assert.That(caster.TryCast(SpellId.Ampere, null, Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.NoTarget));
            Assert.That(caster.TryCast(SpellId.Ampere, "missing", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.UnknownTarget));
            Assert.That(caster.TryCast(SpellId.Ampere, "far", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.OutOfRange));
            Assert.That(caster.TryCast(SpellId.Ampere, "wrong", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.IncompatibleTarget));
            float timeScale = Time.timeScale;
            try { Time.timeScale = 0; Assert.That(caster.TryCast(SpellId.Ampere, "relic", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.InputBlocked)); }
            finally { Time.timeScale = timeScale; }
            state.BeginMapRebuild("loading");
            Assert.That(caster.TryCast(SpellId.Ampere, "relic", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.WorldUnavailable));
            Assert.That(outcomes.Count, Is.EqualTo(7)); Assert.That(completed, Is.EqualTo(7));
        }
        [Test] public void MissingCasterCannotBypassDirectRangeChecks()
        {
            Register("relic", new Vector2Int(2, 2), "ampere"); state.EndMapRebuild(); Unlock(SpellId.Ampere);
            SpellCaster invalidCaster = NewObject("no-grid-position").AddComponent<SpellCaster>();
            Assert.That(invalidCaster.TryCast(SpellId.Ampere, "relic", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.CasterUnavailable));
            Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot snapshot) && snapshot.powered, Is.False);
        }
        [Test] public void BackgroundAndIncompatibleCloserObjectsDoNotShadowValidTarget()
        {
            state.RegisterObject("building", null, Vector2Int.zero, new Vector2Int(10, 10), false, Array.Empty<string>());
            Register("mechanism", new Vector2Int(2, 1), "rotara");
            Register("relic", new Vector2Int(2, 2), "ampere"); state.EndMapRebuild(); Unlock(SpellId.Ampere);
            string selected = (string)typeof(SpellCaster).GetMethod("FindNearestTarget", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(caster, null);
            Assert.That(selected, Is.EqualTo("relic"));
            Assert.That(caster.Cast(SpellId.Ampere, selected, Vector2Int.up), Is.True);
        }
        [Test] public void RepeatedVisAndIceAtGoalAreMeaningfulNoOps()
        {
            Register("alignment", new Vector2Int(2, 2), "vis");
            Register("ice", new Vector2Int(3, 1), "glaciate", "slide-goal:3,1"); state.EndMapRebuild(); Unlock(SpellId.Vis); Unlock(SpellId.Glaciate);
            Assert.That(caster.Cast(SpellId.Vis, "alignment", Vector2Int.up), Is.True);
            Assert.That(caster.TryCast(SpellId.Vis, "alignment", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.NoChange));
            Assert.That(caster.Cast(SpellId.Glaciate, "ice", Vector2Int.right), Is.True);
            Assert.That(caster.TryCast(SpellId.Glaciate, "ice", Vector2Int.right).Outcome, Is.EqualTo(SpellCastOutcome.NoChange));
        }
        [Test] public void LostEchoesRevealsOnlyTheSelectedLocalObject()
        {
            Register("near", new Vector2Int(2, 2), "hidden", "lost-echoes");
            Register("far", new Vector2Int(15, 15), "hidden", "lost-echoes"); state.EndMapRebuild(); Unlock(SpellId.LostEchoes);
            Assert.That(caster.Cast(SpellId.LostEchoes, "near", Vector2Int.up), Is.True);
            Assert.That(state.TryGetObject("far", out WorldObjectSnapshot hidden) && hidden.revealed, Is.False);
            Assert.That(caster.TryCast(SpellId.LostEchoes, "near", Vector2Int.up).Outcome, Is.EqualTo(SpellCastOutcome.NoChange));
        }
        [Test] public void FeedbackDerivesIdleValidInvalidSolvedAndRestoredFromLiveAuthority()
        {
            Register("relic", new Vector2Int(2, 2), "ampere"); Target("power", "relic"); state.EndMapRebuild();
            Assert.That(state.GetObjectFeedback("relic").State, Is.EqualTo(ObjectFeedbackState.Idle));
            Assert.That(state.GetTargetFeedback("relic", true).State, Is.EqualTo(ObjectFeedbackState.Valid));
            Assert.That(state.GetTargetFeedback("relic", false).State, Is.EqualTo(ObjectFeedbackState.Invalid));
            state.SetProperty("relic", item => item.powered = true);
            Assert.That(state.GetObjectFeedback("relic").State, Is.EqualTo(ObjectFeedbackState.Solved));
            Assert.That(state.GetTargetFeedback("relic", false).State, Is.EqualTo(ObjectFeedbackState.Invalid));
            string saved = state.CaptureStateJson(); state.SetProperty("relic", item => item.powered = false);
            Assert.That(state.RestoreStateJson(saved), Is.True);
            Assert.That(state.GetObjectFeedback("relic").State, Is.EqualTo(ObjectFeedbackState.Restored));
            Assert.That(state.GetObjectFeedback("relic").IsSolved, Is.True);
        }
        [Test] public void ResetIsAtomicAndPreservesOtherObjectsProgressEffectsAndCompletedQuests()
        {
            GameObject relic = Register("relic", new Vector2Int(2, 2), "ampere");
            relic.transform.rotation = Quaternion.Euler(0, 270, 0);
            state.RegisterObject("relic", relic, new Vector2Int(2, 2), Vector2Int.one, true, new[] { "ampere" });
            Register("other", new Vector2Int(4, 2), "ampere"); Register("source", new Vector2Int(3, 2), "ampere");
            Target("power", "relic"); Target("other-power", "other"); state.EndMapRebuild();
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            runtime.StartQuest("stella_first_light"); runtime.CompleteQuest("stella_first_light");
            runtime.SetFlag("spell.unlocked.Ampere", true);
            state.SetProperty("relic", item => { item.powered = true; item.rotation = 180; item.cell = new Vector2Int(8, 8); });
            state.SetProperty("other", item => item.powered = true);
            state.SetProperty("source", item => item.powered = true);
            state.AddConnection("source", "other");
            state.SetProperty("relic", item => item.cell = new Vector2Int(2, 2)); state.AddConnection("relic", "source");
            state.SetPuzzleProgress("power", 3); state.SetPuzzleProgress("other-power", 4);
            state.QueueEffect("lightning:relic:on"); state.QueueEffect("lightning:other:on");
            int changed = 0;
            state.StateChanged += snapshot =>
            {
                changed++; Assert.That(state.IsReady, Is.True);
                Assert.That(state.EvaluatePuzzleTarget(state.PuzzleTargets[0]), Is.False);
                Assert.That(state.GetPuzzleProgress("power"), Is.Zero);
                Assert.That(state.IsConnected("relic", "source"), Is.False);
            };
            Assert.That(state.ResetPuzzle("power"), Is.True); Assert.That(changed, Is.EqualTo(1));
            Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot restored), Is.True);
            Assert.That(restored.cell, Is.EqualTo(new Vector2Int(2, 2))); Assert.That(restored.rotation, Is.Zero);
            Assert.That(state.GetObjectFeedback("relic").State, Is.EqualTo(ObjectFeedbackState.Restored));
            Assert.That(state.TryGetObject("source", out WorldObjectSnapshot source) && source.powered, Is.True);
            Assert.That(state.IsConnected("source", "other"), Is.True);
            Assert.That(state.GetPuzzleProgress("other-power"), Is.EqualTo(4));
            Assert.That(state.AcknowledgeEffect("lightning:relic:on"), Is.False);
            Assert.That(state.AcknowledgeEffect("lightning:other:on"), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.True);
        }
        [Test] public void MissingResetObjectRejectsTheWholeOperationAndRequiredCableDoesNotResetExternalSource()
        {
            Register("relic", new Vector2Int(2, 2), "ampere"); Register("source", new Vector2Int(3, 2), "ampere");
            state.RegisterAdditionalPuzzleTarget(new PuzzleTargetDefinition { id = "bad", objectIds = new[] { "relic", "missing" }, requiredPowered = true });
            state.RegisterAdditionalPuzzleTarget(new PuzzleTargetDefinition { id = "good", objectIds = new[] { "relic" }, requiredConnections = new[] { "relic|source" }, requiredPowered = true }); state.EndMapRebuild();
            state.SetProperty("relic", x => x.powered = true); state.SetProperty("source", x => x.powered = true); state.AddConnection("relic", "source");
            Assert.That(state.ResetPuzzle("bad"), Is.False); Assert.That(state.IsConnected("relic", "source"), Is.True);
            Assert.That(state.ResetPuzzle("good"), Is.True);
            Assert.That(state.TryGetObject("source", out WorldObjectSnapshot source) && source.powered, Is.True);
        }
        [Test] public void SavedStateCannotReplaceAuthoredMetadataOrResetBaselineAndProgressSurvivesMapTravel()
        {
            Register("relic", new Vector2Int(2, 2), "ampere"); Target("power", "relic"); state.EndMapRebuild();
            state.SetProperty("relic", x => { x.powered = true; x.rotation = 90; }); state.SetPuzzleProgress("power", 2);
            string saved = state.CaptureStateJson().Replace("ampere", "rotara");
            Assert.That(state.RestoreStateJson(saved), Is.True);
            Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot restored) && SpellTargetRules.Accepts(restored, SpellId.Ampere), Is.True);
            state.BeginMapRebuild("other-map"); Register("relic", new Vector2Int(6, 6), "rotara"); Target("elsewhere", "relic"); state.EndMapRebuild();
            state.SetProperty("relic", x => x.powered = true); state.SetPuzzleProgress("elsewhere", 7);
            state.BeginMapRebuild("feedback-map"); Register("relic", new Vector2Int(2, 2), "ampere"); Target("power", "relic"); state.EndMapRebuild();
            Assert.That(state.GetPuzzleProgress("power"), Is.EqualTo(2)); Assert.That(state.ResetPuzzle("power"), Is.True);
            Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot baseline) && baseline.rotation == 0 && !baseline.powered, Is.True);
            state.BeginMapRebuild("other-map"); Register("relic", new Vector2Int(6, 6), "rotara"); Target("elsewhere", "relic"); state.EndMapRebuild();
            Assert.That(state.GetPuzzleProgress("elsewhere"), Is.EqualTo(7));
            Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot other) && other.powered, Is.True);
        }
        [Test] public void ResetClearsOnlyRegisteredLegacyEffectPrefixes()
        {
            Register("board", new Vector2Int(2, 2), "ampere"); Target("board-puzzle", "board");
            state.RegisterPuzzleEffectPrefixes("board-puzzle", new[] { "stella-relic:1:" }); state.EndMapRebuild();
            state.QueueEffect("stella-relic:1:on"); state.QueueEffect("stella-relic:10:on");
            Assert.That(state.ResetPuzzle("board-puzzle"), Is.True);
            Assert.That(state.AcknowledgeEffect("stella-relic:1:on"), Is.False);
            Assert.That(state.AcknowledgeEffect("stella-relic:10:on"), Is.True);
        }
        [Test] public void FootprintRangeAndExplicitSpellTagsHaveClearBoundaries()
        {
            var target = new WorldObjectSnapshot { cell = new Vector2Int(4, 2), size = new Vector2Int(3, 1), tags = new[] { "rotara", "hidden" } };
            Assert.That(SpellTargetRules.Distance(new Vector2Int(8, 2), target), Is.EqualTo(2));
            Assert.That(SpellTargetRules.Accepts(target, SpellId.Rotara), Is.True);
            Assert.That(SpellTargetRules.Accepts(target, SpellId.LostEchoes), Is.False, "Explicit spell authoring overrides broad categories.");
            target.tags = new[] { "hidden" }; Assert.That(SpellTargetRules.Accepts(target, SpellId.LostEchoes), Is.True);
        }
        [Test] public void OccupiedOrReservedAuthoredCellsRejectResetWithoutStateOrFeedbackEvents()
        {
            Register("relic", new Vector2Int(2, 2), "ampere"); Target("power", "relic"); state.EndMapRebuild();
            state.SetProperty("relic", item => { item.powered = true; item.cell = new Vector2Int(8, 8); }); state.SetPuzzleProgress("power", 2);
            GridMover player = caster.GetComponent<GridMover>(); Assert.That(player.TryTeleportTo(grid, new Vector2Int(2, 2)), Is.True);
            int changes = 0, feedback = 0;
            state.StateChanged += snapshot => changes++; state.ObjectFeedbackChanged += value => feedback++;
            Assert.That(state.ResetPuzzle("power"), Is.False);
            Assert.That(state.IsReady, Is.True); Assert.That(state.GetPuzzleProgress("power"), Is.EqualTo(2));
            Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot unchanged) && unchanged.powered && unchanged.cell == new Vector2Int(8, 8), Is.True);
            Assert.That(changes, Is.Zero); Assert.That(feedback, Is.Zero);
            Assert.That(player.TryTeleportTo(grid, new Vector2Int(2, 3)), Is.True);
            Assert.That((bool)typeof(GridWorld).GetMethod("TryReserveStep", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, new object[] { player, Vector2Int.down, default(Vector2Int) }), Is.True);
            Assert.That(state.ResetPuzzle("power"), Is.False); Assert.That(changes, Is.Zero);
            typeof(GridWorld).GetMethod("CancelMove", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(grid, new object[] { player });
            Assert.That(state.ResetPuzzle("power"), Is.True); Assert.That(changes, Is.EqualTo(1));
        }
        [Test] public void ResetAndNewGameRegistrationRetainTheAuthoredNonzeroRotation()
        {
            GameObject mechanism = NewObject("authored-mechanism"); mechanism.transform.position = grid.CellToWorld(new Vector2Int(2, 2));
            mechanism.transform.rotation = Quaternion.Euler(0, 270, 0);
            state.RegisterObject("mechanism", mechanism, new Vector2Int(2, 2), Vector2Int.one, true, new[] { "rotara" });
            Target("rotation", "mechanism"); state.EndMapRebuild();
            state.SetProperty("mechanism", item => { item.rotation = 90; item.cell = new Vector2Int(8, 8); });
            Assert.That(state.ResetPuzzle("rotation"), Is.True);
            Assert.That(state.TryGetObject("mechanism", out WorldObjectSnapshot restored) && Mathf.Approximately(restored.rotation, 270), Is.True);
            state.SetProperty("mechanism", item => item.rotation = 90);
            state.ResetAllState(); state.BeginMapRebuild("feedback-map");
            state.RegisterObject("mechanism", mechanism, new Vector2Int(2, 2), Vector2Int.one, true, new[] { "rotara" });
            state.EndMapRebuild();
            Assert.That(state.TryGetObject("mechanism", out WorldObjectSnapshot newGame) && Mathf.Approximately(newGame.rotation, 270), Is.True);
        }
        [Test] public void GravitateReportsMovementBlockedAndResetFindsTheDeclaredNonspellBoard()
        {
            Register("movable", new Vector2Int(2, 2), "gravitate");
            state.RegisterObject("board", null, new Vector2Int(3, 1), Vector2Int.one, false, Array.Empty<string>()); Target("board-puzzle", "board");
            state.RegisterObject("background", null, Vector2Int.zero, new Vector2Int(10, 10), false, Array.Empty<string>());
            GameObject obstacleObject = NewObject("obstacle"); obstacleObject.transform.position = grid.CellToWorld(new Vector2Int(3, 2));
            obstacleObject.AddComponent<GridObstacle>().Configure(grid, Vector2Int.one); state.EndMapRebuild(); Unlock(SpellId.Gravitate);
            Assert.That(caster.TryCast(SpellId.Gravitate, "movable", Vector2Int.right).Outcome, Is.EqualTo(SpellCastOutcome.MovementBlocked));
            Assert.That(state.TryGetObject("movable", out WorldObjectSnapshot original) && original.cell == new Vector2Int(2, 2), Is.True);
            state.SetProperty("board", item => item.powered = true);
            Assert.That(caster.ResetNearbyPuzzle(), Is.True);
            Assert.That(state.TryGetObject("board", out WorldObjectSnapshot restored) && !restored.powered, Is.True);
        }
        private void Target(string id, params string[] objectIds) => state.RegisterAdditionalPuzzleTarget(new PuzzleTargetDefinition { id = id, objectIds = objectIds, requiredPowered = true });
        private GameObject NewObject(string name) { var go = new GameObject(name); created.Add(go); return go; }
        private GameObject Register(string id, Vector2Int cell, params string[] tags)
        { GameObject go = NewObject(id); go.transform.position = grid.CellToWorld(cell); state.RegisterObject(id, go, cell, Vector2Int.one, true, tags); return go; }
        private static void Unlock(SpellId spell) => GameEventManager.Instance.Runtime.SetFlag("spell.unlocked." + spell, true);
    }
}
