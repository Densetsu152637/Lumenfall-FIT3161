using System.Collections.Generic;
using System.Reflection;
using Lumenfall.Inventory;
using NUnit.Framework;
using UnityEngine;
using Lumenfall.Spells;
using Lumenfall.GridMovement;
using Lumenfall.GameEvents;
using Lumenfall.Quests;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SpellWorldStateTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private WorldStateService state;
        private GridWorld grid;
        private WorldStateService previousState;

        [SetUp]
        public void SetUp()
        {
            ResetSharedState();
            previousState = WorldStateService.Instance;
            GameObject gridObject = new GameObject("test-grid"); created.Add(gridObject); grid = gridObject.AddComponent<GridWorld>(); grid.ConfigureBounds(12, 12);
            GameObject stateObject = new GameObject("test-spell-state"); created.Add(stateObject); state = stateObject.AddComponent<WorldStateService>();
            // Awake is not run in EditMode. Bind only the dependency Cast uses,
            // avoiding Awake's scene persistence and save-provider side effects.
            EditModeLifecycle.SetWorldInstance(state);
            state.BeginMapRebuild("test-map");
        }

        [TearDown]
        public void TearDown()
        {
            if (state != null) GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = created.Count - 1; i >= 0; i--) if (created[i] != null)
            {
                foreach (SpellCaster caster in created[i].GetComponents<SpellCaster>()) EditModeLifecycle.Call(caster, "OnDisable");
                Object.DestroyImmediate(created[i]);
            }
            created.Clear();
            EditModeLifecycle.SetWorldInstance(previousState);
            ResetSharedState();
        }

        private static void ResetSharedState()
        {
            GameEventManager.Instance.Runtime.ResetState();
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, false);
        }

        [Test]
        public void AmpereTogglesAndQueuesDurableEffectUntilAcknowledged()
        {
            GameObject target = Register("relic", new Vector2Int(2, 2), Vector2Int.one, null); UnlockForTest(SpellId.Ampere); SpellCaster caster = target.AddComponent<SpellCaster>(); state.EndMapRebuild();
            Assert.That(caster.Cast(SpellId.Ampere, "relic", Vector2Int.up), Is.True); Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot on) && on.powered, Is.True);
            Assert.That(caster.Cast(SpellId.Ampere, "relic", Vector2Int.up), Is.True); Assert.That(state.TryGetObject("relic", out WorldObjectSnapshot off) && !off.powered, Is.True);
            Assert.That(state.AcknowledgeEffect("lightning:relic:off"), Is.True);
        }

        [Test]
        public void WeaveUsesDeclaredEndpointAndPersistsAttenuation()
        {
            Register("power", new Vector2Int(2, 2), Vector2Int.one, new[] { "weave-with:link" }); Register("link", new Vector2Int(4, 2), Vector2Int.one, new[] { "weave-with:power" }); state.EndMapRebuild();
            state.SetProperty("power", x => x.powered = true); UnlockForTest(SpellId.Weave); SpellCaster caster = created[1].AddComponent<SpellCaster>(); Assert.That(caster.Cast(SpellId.Weave, "link", Vector2Int.right), Is.True);
            Assert.That(state.IsConnected("power", "link"), Is.True); Assert.That(state.GetConnectionStrength("power", "link"), Is.GreaterThan(0.2f).And.LessThan(1f));
            string save = state.CaptureStateJson(); state.ResetAllState(); state.BeginMapRebuild("test-map"); Register("power", new Vector2Int(2, 2), Vector2Int.one, new[] { "weave-with:link" }); Register("link", new Vector2Int(4, 2), Vector2Int.one, new[] { "weave-with:power" }); state.EndMapRebuild(); Assert.That(state.RestoreStateJson(save), Is.True); Assert.That(state.GetConnectionStrength("power", "link"), Is.GreaterThan(0.2f));
        }

        [Test]
        public void StellaCircuitMonitorRequiresAllBoardRelicsLitThroughWovenCablesAfterAcceptance()
        {
            GameEventManager manager = GameEventManager.Instance;
            GameEventRuntime runtime = manager.Runtime;
            runtime.ResetState();
            QuestPuzzleMonitor monitor = Object.FindAnyObjectByType<QuestPuzzleMonitor>();
            GameObject monitorHost = null;
            if (monitor == null)
            {
                monitorHost = new GameObject("stella-quest-puzzle-monitor-test");
                monitor = monitorHost.AddComponent<QuestPuzzleMonitor>();
                EditModeLifecycle.Call(monitor, "Awake");
            }
            try
            {
                Assert.That(monitor, Is.Not.Null);
                state.BeginMapRebuild("B02.Ground");
                RunPuzzleMonitor(monitor);
                string[] conductor = { "weave", "circuit", WorldStateService.ConductorTag };
                Register("relic.stella.power", new Vector2Int(1, 4), Vector2Int.one, new[] { "ampere", "weave", "circuit" });
                Register("circuit.stella.link", new Vector2Int(4, 4), Vector2Int.one, conductor);
                // Five board relics in a loop of three-tile hops from the workbench link.
                Vector2Int[] board = { new Vector2Int(7, 4), new Vector2Int(10, 4), new Vector2Int(10, 7), new Vector2Int(7, 7), new Vector2Int(4, 7) };
                for (int i = 0; i < board.Length; i++) Register("stella.string-together.relic." + (i + 1), board[i], Vector2Int.one, conductor);
                state.EndMapRebuild();
                SpellCaster caster = created[1].AddComponent<SpellCaster>();
                EditModeLifecycle.Call(caster, "OnEnable");

                DialogueSession firstLight = runtime.StartDialogue("stella_first_light_dialogue");
                Assert.That(firstLight.SelectChoice("accept"), Is.True);
                Assert.That(firstLight.Continue(), Is.True);
                Assert.That(runtime.TriggerEvent("complete_stella_first_light").Succeeded, Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);

                DialogueSession circuitQuest = runtime.StartDialogue("stella_string_them_together_dialogue");
                Assert.That(circuitQuest.SelectChoice("accept"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);
                Assert.That(circuitQuest.Continue(), Is.True,
                    "Finish Stella's teaching dialogue before casting puzzle spells.");

                RunPuzzleMonitor(monitor);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
                Assert.That(caster.Cast(SpellId.Ampere, "stella.string-together.relic.1", Vector2Int.right), Is.False,
                    "Board relics only take power through Weave cables.");
                caster.Cast(SpellId.Weave, "relic.stella.power", Vector2Int.right);
                Assert.That(caster.Cast(SpellId.Weave, "circuit.stella.link", Vector2Int.right), Is.True);
                for (int i = 0; i < board.Length; i++)
                    Assert.That(caster.Cast(SpellId.Weave, "stella.string-together.relic." + (i + 1), Vector2Int.right), Is.True, "relic " + (i + 1));
                RunPuzzleMonitor(monitor);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active),
                    "A woven board must not complete the quest while the source relic is dark.");

                Assert.That(caster.Cast(SpellId.Ampere, "relic.stella.power", Vector2Int.right), Is.True);
                RunPuzzleMonitor(monitor);
                Assert.That(state.TryGetObject("stella.string-together.relic.5", out WorldObjectSnapshot last) && last.powered, Is.True);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed));
            }
            finally
            {
                if (runtime.ActiveDialogue != null) runtime.ActiveDialogue.End();
                runtime.ResetState();
                if (monitorHost != null) Object.DestroyImmediate(monitorHost);
            }
        }

        private static void RunPuzzleMonitor(QuestPuzzleMonitor monitor)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(QuestPuzzleMonitor).GetField("nextCheck", flags).SetValue(monitor, 0f);
            typeof(QuestPuzzleMonitor).GetMethod("Update", flags).Invoke(monitor, null);
        }

        [Test]
        public void GlaciateStopsAtAuthoredGoalAndFootprintMovementRejectsBlocker()
        {
            GameObject ice = Register("ice", new Vector2Int(2, 4), Vector2Int.one, new[] { "slide-goal:5,4" }); UnlockForTest(SpellId.Glaciate); SpellCaster caster = ice.AddComponent<SpellCaster>(); state.EndMapRebuild(); Assert.That(caster.Cast(SpellId.Glaciate, "ice", Vector2Int.right), Is.True); Assert.That(state.TryGetObject("ice", out WorldObjectSnapshot moved) && moved.cell == new Vector2Int(5, 4), Is.True);
            state.BeginMapRebuild("footprint"); GameObject block = Register("wide", new Vector2Int(2, 2), new Vector2Int(2, 1), null); GridObstacle obstacle = block.AddComponent<GridObstacle>(); obstacle.Configure(grid, new Vector2Int(2, 1)); GameObject blocker = Register("blocker", new Vector2Int(4, 2), Vector2Int.one, null); GridObstacle blockerObstacle = blocker.AddComponent<GridObstacle>(); blockerObstacle.Configure(grid, Vector2Int.one); state.EndMapRebuild(); Assert.That(state.TryMoveObject("wide", Vector2Int.right), Is.False);
        }

        private GameObject Register(string id, Vector2Int cell, Vector2Int size, string[] tags)
        { GameObject go = new GameObject(id); created.Add(go); go.transform.position = grid.CellToWorld(cell); state.RegisterObject(id, go, cell, size, true, tags); return go; }

        private static void UnlockForTest(SpellId spell)
        { GameEventManager.Instance.Runtime.SetFlag("spell.unlocked." + spell, true); }
    }
}
