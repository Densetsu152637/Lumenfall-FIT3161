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

        // A stuck-open inventory blocks every cast, so clear it alongside quest state.
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
                EditModeLifecycle.Call(monitor, "OnEnable");
            }
            try
            {
                Assert.That(monitor, Is.Not.Null);
                state.BeginMapRebuild("B02.Ground");
                RunPuzzleMonitor(monitor);
                string[] conductor = { "weave", "circuit", WorldStateService.ConductorTag };
                var network = new List<string>();
                for (int i = 0; i < 5; i++)
                {
                    string id = "stella.string-together.relic." + (i + 1);
                    Register(id, new Vector2Int(i, 4), Vector2Int.one, new[] { "ampere", "weave", "circuit" });
                    network.Add(id);
                }
                for (int i = 0; i < 10; i++)
                {
                    string id = "stella.circuit.relay." + (i + 1);
                    Register(id, new Vector2Int(5 + i, 4), Vector2Int.one, conductor);
                    network.Add(id);
                }
                Register("circuit.stella.link", new Vector2Int(15, 4), Vector2Int.one, conductor);
                network.Add("circuit.stella.link");
                state.EndMapRebuild();
                SpellCaster caster = created[1].AddComponent<SpellCaster>();
                EditModeLifecycle.Call(caster, "OnEnable");

                DialogueSession firstLight = runtime.StartDialogue("stella_first_light_dialogue");
                Assert.That(firstLight.SelectChoice("accept"), Is.True);
                while (!firstLight.IsCompleted) Assert.That(firstLight.Continue(), Is.True);
                Assert.That(runtime.TriggerEvent("complete_stella_first_light").Succeeded, Is.True);
                firstLight = runtime.StartDialogue("stella_first_light_dialogue");
                Assert.That(firstLight.SelectChoice("report"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);

                DialogueSession circuitQuest = runtime.StartDialogue("stella_string_them_together_dialogue");
                Assert.That(circuitQuest.SelectChoice("accept"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);
                while (!circuitQuest.IsCompleted)
                    Assert.That(circuitQuest.Continue(), Is.True,
                        "Finish Stella's teaching dialogue before casting puzzle spells.");
                Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);

                RunPuzzleMonitor(monitor);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
                for (int i = 0; i < 5; i++) state.SetProperty(network[i], item => item.powered = true);
                for (int i = 1; i < network.Count - 1; i++) Assert.That(state.AddConnection(network[i - 1], network[i]), Is.True);
                RunPuzzleMonitor(monitor);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active),
                    "The quest must remain active until the magical item joins the same network.");

                Assert.That(state.AddConnection(network[network.Count - 2], network[network.Count - 1]), Is.True);
                RunPuzzleMonitor(monitor);
                Assert.That(state.TryGetObject("circuit.stella.link", out WorldObjectSnapshot item) && item.powered, Is.True);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
                circuitQuest = runtime.StartDialogue("stella_string_them_together_dialogue");
                Assert.That(circuitQuest.SelectChoice("report"), Is.True);
                Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed));
            }
            finally
            {
                if (runtime.ActiveDialogue != null) runtime.ActiveDialogue.End();
                runtime.ResetState();
                if (monitorHost != null)
                {
                    EditModeLifecycle.Call(monitor, "OnDisable");
                    Object.DestroyImmediate(monitorHost);
                }
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

        [Test]
        public void InputCastExplainsUnavailableSpellWithoutChangingRelic()
        {
            Register("nearby-relic", Vector2Int.right, Vector2Int.one, null);
            SpellCaster caster = CreateInputCaster();
            state.EndMapRebuild();
            Assert.That(CastFromInput(caster), Is.False);
            Assert.That(caster.LastCastFeedback, Does.Contain("not available"));
            Assert.That(state.TryGetObject("nearby-relic", out WorldObjectSnapshot relic) && relic.powered, Is.False);
        }

        [Test]
        public void InputCastExplainsOutOfReachWithoutExtendingTargetRadius()
        {
            Register("distant-relic", new Vector2Int(3, 0), Vector2Int.one, null);
            UnlockForTest(SpellId.Ampere);
            SpellCaster caster = CreateInputCaster();
            state.EndMapRebuild();
            Assert.That(CastFromInput(caster), Is.False);
            Assert.That(caster.LastCastFeedback, Does.Contain("within reach"));
            Assert.That(state.TryGetObject("distant-relic", out WorldObjectSnapshot relic) && relic.powered, Is.False);
        }

        [Test]
        public void InputCastExplainsInapplicableSpellWithoutChangingTarget()
        {
            Register("plain-relic", Vector2Int.right, Vector2Int.one, null);
            UnlockForTest(SpellId.Rotara);
            SpellCaster caster = CreateInputCaster();
            typeof(SpellCaster).GetField("selectedSpell", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(caster, SpellId.Rotara);
            state.EndMapRebuild();
            Assert.That(CastFromInput(caster), Is.False);
            Assert.That(caster.LastCastFeedback, Does.Contain("cannot change"));
            Assert.That(state.TryGetObject("plain-relic", out WorldObjectSnapshot relic), Is.True);
            Assert.That(relic.rotation, Is.Zero);
        }

        [Test]
        public void InputCastPreservesSpecificFeedbackOnRepeatedWeaveAttempts()
        {
            Register("circuit-relic", Vector2Int.right, Vector2Int.one, new[] { "circuit" });
            UnlockForTest(SpellId.Weave);
            SpellCaster caster = CreateInputCaster();
            typeof(SpellCaster).GetField("selectedSpell", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(caster, SpellId.Weave);
            state.EndMapRebuild();
            Assert.That(CastFromInput(caster), Is.False);
            Assert.That(caster.HeldThread, Is.EqualTo("circuit-relic"));
            Assert.That(caster.LastCastFeedback, Does.StartWith("Thread held"));
            Assert.That(CastFromInput(caster), Is.False);
            Assert.That(caster.HeldThread, Is.Null);
            Assert.That(caster.LastCastFeedback, Is.EqualTo("Thread released."));
            Assert.That(CastFromInput(caster), Is.False);
            Assert.That(caster.LastCastFeedback, Does.StartWith("Thread held"));
        }

        private SpellCaster CreateInputCaster()
        {
            GameObject player = new GameObject("feedback-player");
            created.Add(player);
            player.AddComponent<GridMover>();
            return player.AddComponent<SpellCaster>();
        }

        // Exercise the common F/right-click entry after input eligibility, without changing keyboard preferences.
        private static bool CastFromInput(SpellCaster caster) =>
            (bool)typeof(SpellCaster).GetMethod("CastSelectedFromInput", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(caster, null);

        private GameObject Register(string id, Vector2Int cell, Vector2Int size, string[] tags)
        { GameObject go = new GameObject(id); created.Add(go); go.transform.position = grid.CellToWorld(cell); state.RegisterObject(id, go, cell, size, true, tags); return go; }

        private static void UnlockForTest(SpellId spell)
        { GameEventManager.Instance.Runtime.SetFlag("spell.unlocked." + spell, true); }
    }
}
