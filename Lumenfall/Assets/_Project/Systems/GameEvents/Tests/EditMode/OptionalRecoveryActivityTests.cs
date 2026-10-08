using System.Collections.Generic;
using System.Reflection;
using Lumenfall.Inventory;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class OptionalRecoveryActivityTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private WorldStateService world;
        private WorldStateService previousWorld;
        private GridWorld grid;
        private GameEventRuntime runtime;
        private OptionalRecoveryRules rules;
        private string priorRuntime;
        private readonly Vector2Int first = new Vector2Int(30, 40);
        private readonly Vector2Int second = new Vector2Int(34, 40);
        [SetUp]
        public void SetUp()
        {
            runtime = GameEventManager.Instance.Runtime; priorRuntime = runtime.ExportStateJson(false); runtime.ResetState();
            grid = Create("grid").AddComponent<GridWorld>(); grid.ConfigureBounds(64, 64);
            previousWorld = WorldStateService.Instance;
            world = Create("world").AddComponent<WorldStateService>();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, world);
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, false);
            world.BeginMapRebuild("campus"); RegisterTargets(); world.EndMapRebuild();
            rules = new OptionalRecoveryRules(runtime, world, "campus", first, second);
        }
        [TearDown]
        public void TearDown()
        {
            foreach (GameObject host in created) if (host != null)
            {
                foreach (SpellCaster caster in host.GetComponents<SpellCaster>()) Call(caster, "OnDisable");
                foreach (OptionalRecoveryActivity activity in host.GetComponents<OptionalRecoveryActivity>()) Call(activity, "OnDisable");
            }
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null) Object.DestroyImmediate(created[i]);
            created.Clear();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, previousWorld);
            runtime.ImportStateJson(priorRuntime);
        }
        [Test]
        public void DisabledAndLockedSpellCannotStartOrReward()
        {
            Assert.That(rules.TryBegin(true), Is.False);
            Assert.That(rules.Status(true), Does.Contain("Learn Lost Echoes"));
            Unlock(); Assert.That(rules.TryBegin(false), Is.False);
            world.SetProperty(OptionalRecoveryRules.TargetOne, value => value.revealed = true);
            world.SetProperty(OptionalRecoveryRules.TargetTwo, value => value.revealed = true);
            Assert.That(rules.Observe(false), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.RewardFlag), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.InventoryFlag), Is.False);
        }
        [Test]
        public void LocalRevealDoesNotRevealOrCompleteKatrina()
        {
            Unlock(); Assert.That(runtime.StartQuest("katrina_something_borrowed"), Is.True);
            Register("keepsake.katrina.hidden", first + Vector2Int.up, new[] { "hidden", "lost-echoes" });
            Assert.That(rules.TryBegin(true), Is.True);
            Assert.That(world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2), Is.True);
            Assert.That(world.TryGetObject("keepsake.katrina.hidden", out WorldObjectSnapshot keepsake) && keepsake.revealed, Is.False);
            Assert.That(runtime.GetQuestStatus("katrina_something_borrowed"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.HasEventTriggered("complete_katrina_something_borrowed"), Is.False);
            Assert.That(rules.Observe(true), Is.False);
        }
        [Test]
        public void RevealRequiresExactTagsMapCompatibilityAndRange()
        {
            Register("local", new Vector2Int(2, 2), new[] { "hidden", "lost-echoes" });
            Register("wrong-tags", new Vector2Int(2, 2), new[] { "hidden" });
            Assert.That(world.TryRevealObject("wrong-tags", Vector2Int.zero, 9), Is.False);
            Assert.That(world.TryRevealObject("local", Vector2Int.zero, -1), Is.False);
            Assert.That(world.TryRevealObject("local", new Vector2Int(2, 5), 2), Is.False);
            world.SetProperty("local", value => value.mapId = "other-map");
            Assert.That(world.TryRevealObject("local", new Vector2Int(2, 2), 2), Is.False);
            world.SetProperty("local", value => { value.mapId = "campus"; value.spellCompatible = false; });
            Assert.That(world.TryRevealObject("local", new Vector2Int(2, 2), 2), Is.False);
            world.SetProperty("local", value => value.spellCompatible = true);
            Assert.That(world.TryRevealObject("local", new Vector2Int(2, 4), 2), Is.True, "Exact Manhattan boundary is eligible.");
            Assert.That(world.TryRevealObject("local", new Vector2Int(2, 2), 2), Is.False, "Revealing again is idempotent.");
        }
        [Test]
        public void UnrelatedOptionalIdsAndRelocatedTargetsCannotSatisfyRecovery()
        {
            Unlock(); Register("unrelated", first, new[] { "hidden", "lost-echoes", "optional-recovery" });
            Assert.That(rules.TryBegin(true), Is.True);
            Assert.That(world.TryRevealObject("unrelated", first, 2), Is.False);
            world.SetProperty(OptionalRecoveryRules.TargetOne, value => { value.cell += Vector2Int.up; value.revealed = true; });
            world.SetProperty(OptionalRecoveryRules.TargetTwo, value => value.revealed = true);
            Assert.That(rules.Observe(true), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.RewardFlag), Is.False);
        }
        [Test]
        public void PartialResetPreservesUnrelatedWorldAndAllowsRetry()
        {
            Unlock(); Register("unrelated", first + Vector2Int.up, new[] { "hidden", "lost-echoes" });
            world.SetProperty("unrelated", value => { value.revealed = true; value.powered = true; value.rotation = 90; });
            Assert.That(rules.TryBegin(true), Is.True);
            world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2); rules.Observe(true);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.ProgressFlag(OptionalRecoveryRules.TargetOne)), Is.True);
            Assert.That(rules.TryResetSearch(true), Is.True);
            Assert.That(world.TryGetObject(OptionalRecoveryRules.TargetOne, out WorldObjectSnapshot target) && target.revealed, Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.ProgressFlag(OptionalRecoveryRules.TargetOne)), Is.False);
            world.TryGetObject("unrelated", out WorldObjectSnapshot unrelated);
            Assert.That(unrelated.revealed && unrelated.powered && unrelated.rotation == 90, Is.True);
            Assert.That(world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2), Is.True);
        }
        [Test]
        public void SavedPartialSearchRestoresAfterMapReentryAndCompletesOnce()
        {
            Unlock(); rules.TryBegin(true); world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2); rules.Observe(true);
            string eventSave = runtime.ExportStateJson(false); string worldSave = world.CaptureStateJson();
            runtime.ResetState(); world.ResetAllState();
            Assert.That(runtime.ImportStateJson(eventSave), Is.True);
            Assert.That(world.RestoreStateJson(worldSave), Is.True);
            world.BeginMapRebuild("campus"); RegisterTargets(); world.EndMapRebuild(); rules.RestoreSearch();
            world.TryGetObject(OptionalRecoveryRules.TargetOne, out WorldObjectSnapshot restored);
            Assert.That(restored.revealed, Is.True);
            world.TryGetObject(OptionalRecoveryRules.TargetTwo, out WorldObjectSnapshot remaining);
            Assert.That(remaining.revealed, Is.False);
            int receipts = 0;
            System.Action<string, bool> count = (key, value) => { if (key == OptionalRecoveryRules.RewardFlag && value) receipts++; };
            runtime.FlagChanged += count;
            try
            {
                world.TryRevealObject(OptionalRecoveryRules.TargetTwo, second, 2);
                Assert.That(rules.Observe(true), Is.True);
                Assert.That(rules.Observe(true), Is.False);
                Assert.That(rules.TryBegin(true), Is.False);
                Assert.That(rules.TryResetSearch(true), Is.False);
                Assert.That(receipts, Is.EqualTo(1));
                Assert.That(runtime.GetFlag(OptionalRecoveryRules.InventoryFlag), Is.True);
                Assert.That(runtime.GetFlag("inventory.keyitem.katrina-keepsake"), Is.False);
            }
            finally { runtime.FlagChanged -= count; }
        }
        [Test]
        public void CompletedReceiptSurvivesSaveAndRejectsResetReplay()
        {
            Unlock(); rules.TryBegin(true);
            world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2);
            world.TryRevealObject(OptionalRecoveryRules.TargetTwo, second, 2); rules.Observe(true);
            string save = runtime.ExportStateJson(false); runtime.ResetState(); runtime.ImportStateJson(save);
            Assert.That(rules.Completed, Is.True);
            Assert.That(rules.TryResetSearch(true), Is.False);
            Assert.That(rules.TryBegin(true), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.InventoryFlag), Is.True);
        }
        [Test]
        public void DisabledComponentRemovesTargetsAndRestoreCannotReviveThem()
        {
            // The bridge defaults to disabled, then creates its own checked visual markers on Begin.
            world.UnregisterRecoveryTarget(OptionalRecoveryRules.TargetOne);
            world.UnregisterRecoveryTarget(OptionalRecoveryRules.TargetTwo);
            OptionalRecoveryActivity activity = Create("activity").AddComponent<OptionalRecoveryActivity>();
            Assert.That(activity.ActivityEnabled, Is.False); Assert.That(activity.BeginSearch(), Is.False);
            Unlock(); activity.SetActivityEnabled(true); Assert.That(activity.BeginSearch(), Is.True);
            string saved = runtime.ExportStateJson(false); string worldSaved = world.CaptureStateJson();
            activity.SetActivityEnabled(false);
            Assert.That(world.TryGetObject(OptionalRecoveryRules.TargetOne, out _), Is.False);
            Assert.That(runtime.ImportStateJson(saved), Is.True); Assert.That(world.RestoreStateJson(worldSaved), Is.True);
            Assert.That(world.TryGetObject(OptionalRecoveryRules.TargetOne, out _), Is.False);
            Assert.That(world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2), Is.False);
            Assert.That(activity.BeginSearch(), Is.False);
        }
        [Test]
        public void SpellCasterDirectCastHonorsLocalRangeAndRejectsOtherSpellsOnOptionalTargets()
        {
            Unlock(); runtime.SetFlag("spell.unlocked.Ampere", true); rules.TryBegin(true);
            GameObject player = Create("player"); player.transform.position = grid.CellToWorld(first);
            GridMover mover = player.AddComponent<GridMover>();
            Assert.That(mover.TryTeleportTo(grid, first), Is.True);
            SpellCaster caster = player.AddComponent<SpellCaster>();
            Call(caster, "OnEnable");
            Assert.That(caster.Cast(SpellId.Ampere, OptionalRecoveryRules.TargetOne, Vector2Int.up), Is.False);
            Assert.That(caster.Cast(SpellId.LostEchoes, OptionalRecoveryRules.TargetTwo, Vector2Int.up), Is.False);
            Assert.That(caster.Cast(SpellId.LostEchoes, OptionalRecoveryRules.TargetOne, Vector2Int.up), Is.True);
        }
        [Test]
        public void WrongMapMissingTargetsAndSuspensionCannotComplete()
        {
            Unlock(); world.UnregisterRecoveryTarget(OptionalRecoveryRules.TargetTwo);
            Assert.That(rules.TryBegin(true), Is.False); RegisterTargets(); Assert.That(rules.TryBegin(true), Is.True);
            rules.Suspend(); Assert.That(world.TryRevealObject(OptionalRecoveryRules.TargetOne, first, 2), Is.False);
            Assert.That(rules.TryBegin(true), Is.True);
            world.BeginMapRebuild("other-map"); RegisterTargets(); world.EndMapRebuild();
            Assert.That(rules.TryBegin(true), Is.False); Assert.That(rules.Observe(true), Is.False);
            Assert.That(runtime.GetFlag(OptionalRecoveryRules.RewardFlag), Is.False);
        }
        private static void Call(object component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(component, null);
        }

        private void Unlock() => runtime.SetFlag("spell.unlocked.LostEchoes", true);
        private GameObject Create(string name) { GameObject item = new GameObject(name); created.Add(item); return item; }
        private void RegisterTargets()
        {
            Register(OptionalRecoveryRules.TargetOne, first, new[] { "hidden", "lost-echoes", "optional-recovery" });
            Register(OptionalRecoveryRules.TargetTwo, second, new[] { "hidden", "lost-echoes", "optional-recovery" });
        }
        private void Register(string id, Vector2Int cell, string[] tags)
        { GameObject target = Create(id); target.transform.position = grid.CellToWorld(cell); world.RegisterObject(id, target, cell, Vector2Int.one, true, tags); }
    }
}
