using System.Collections.Generic;
using System.Reflection;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class StringThemTogetherTests
    {
        private readonly List<GameObject> hosts = new List<GameObject>();
        private WorldStateService state;
        private WorldStateService previousState;
        private GameEventRuntime runtime;
        private SpellCaster caster;
        private const string Source = "relic.stella.power";
        private const string Link = "circuit.stella.link";
        private static readonly string[] ConductorTags = { "weave", "circuit", WorldStateService.ConductorTag };

        [SetUp] public void SetUp()
        {
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            previousState = WorldStateService.Instance;
            state = Host("world").AddComponent<WorldStateService>();
            EditModeLifecycle.SetWorldInstance(state);
            state.BeginMapRebuild("B02.Ground");
            state.RegisterObject(Source, null, new Vector2Int(11, 3), Vector2Int.one, true, new[] { "ampere", "weave", "circuit" });
            state.RegisterObject(Link, null, new Vector2Int(14, 3), Vector2Int.one, true, ConductorTags);
            state.EndMapRebuild();
            caster = Host("caster").AddComponent<SpellCaster>();
            EditModeLifecycle.Call(caster, "OnEnable");
        }
        [TearDown] public void TearDown()
        {
            runtime.ActiveDialogue?.End();
            EditModeLifecycle.Call(caster, "OnDisable");
            for (int i = hosts.Count - 1; i >= 0; i--) Object.DestroyImmediate(hosts[i]);
            hosts.Clear();
            EditModeLifecycle.SetWorldInstance(previousState);
            runtime.ResetState();
        }

        [Test] public void AcceptanceRequiresPrerequisiteAndTeachesWeaveWithoutCompletingQuest()
        {
            DialogueSession blocked = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(blocked.SelectChoice("accept"), Is.False); blocked.End();
            runtime.StartQuest("stella_first_light"); runtime.CompleteQuest("stella_first_light");
            DialogueSession session = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);
            Assert.That(session.SelectChoice("accept"), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True, "The authored acceptance teaches Weave immediately.");
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
            Assert.That(runtime.HasEventTriggered("complete_stella_string_them_together"), Is.False);
            session.End();
            session = runtime.StartDialogue("stella_string_them_together_dialogue"); session.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
            session = runtime.StartDialogue("stella_string_them_together_dialogue"); session.SelectChoice("active"); session.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);
            string save = runtime.ExportStateJson(false); runtime.ResetState(); runtime.ImportStateJson(save);
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);
        }

        [Test] public void BoardRelicsTakePowerOnlyThroughAWovenChainFromTheSource()
        {
            state.RegisterObject("relic.near", null, new Vector2Int(17, 3), Vector2Int.one, true, ConductorTags);
            state.RegisterObject("relic.far", null, new Vector2Int(22, 3), Vector2Int.one, true, ConductorTags);
            runtime.SetFlag("spell.unlocked.Weave", true); runtime.SetFlag("spell.unlocked.Ampere", true);

            Assert.That(caster.Cast(SpellId.Ampere, "relic.near", Vector2Int.up), Is.False, "Ampere cannot charge a conductor.");
            Assert.That(caster.LastCastFeedback, Does.Contain("Weave"));
            state.SetProperty("relic.near", x => x.powered = true);
            Assert.That(Powered("relic.near"), Is.False, "Conductor power is derived from cables, not stored.");

            Assert.That(caster.Cast(SpellId.Weave, Source, Vector2Int.up), Is.False, "Holding a thread changes nothing yet.");
            Assert.That(caster.HeldThread, Is.EqualTo(Source));
            Assert.That(caster.Cast(SpellId.Weave, Link, Vector2Int.up), Is.True);
            Assert.That(caster.HeldThread, Is.EqualTo(Link), "The thread carries on from the relic it was tied to.");
            Assert.That(caster.Cast(SpellId.Weave, "relic.near", Vector2Int.up), Is.True);
            Assert.That(Powered("relic.near"), Is.False, "A chain from a dark source stays dark.");
            Assert.That(caster.Cast(SpellId.Ampere, Source, Vector2Int.up), Is.True);
            Assert.That(Powered(Link) && Powered("relic.near"), Is.True, "Powering the source lights the whole chain.");

            Assert.That(caster.Cast(SpellId.Weave, "relic.far", Vector2Int.up), Is.False);
            Assert.That(caster.LastCastFeedback, Does.StartWith("Too far"));
            Assert.That(caster.HeldThread, Is.EqualTo("relic.near"), "A refused tie keeps the thread.");

            Assert.That(caster.Cast(SpellId.Weave, Link, Vector2Int.up), Is.True, "Weaving a linked pair cuts it.");
            Assert.That(caster.HeldThread, Is.Null);
            Assert.That(Powered("relic.near"), Is.False);
            Assert.That(Powered(Link), Is.True);

            caster.Cast(SpellId.Weave, Link, Vector2Int.up);
            Assert.That(caster.Cast(SpellId.Weave, "relic.near", Vector2Int.up), Is.True);
            string save = state.CaptureStateJson();
            state.RemoveConnection(Link, "relic.near");
            Assert.That(Powered("relic.near"), Is.False);
            state.RestoreStateJson(save);
            Assert.That(Powered("relic.near"), Is.True, "Restored cables carry power again.");

            Assert.That(caster.Cast(SpellId.Ampere, Source, Vector2Int.up), Is.True);
            Assert.That(Powered(Link) || Powered("relic.near"), Is.False, "Switching the source off darkens the chain.");
        }

        [Test] public void AuthoredRingCanBeWovenToEveryRelicByFollowingTheHint()
        {
            WorldAreaAsset area = UnityEditor.AssetDatabase.LoadAssetAtPath<WorldAreaAsset>("Assets/_Project/World/Areas/B02.Ground.asset");
            Assert.That(area, Is.Not.Null);
            PuzzleTargetDefinition target = null;
            foreach (PuzzleTargetDefinition authored in area.definition.puzzleTargets) if (authored.id == "quest.stella.string-them-together") target = authored;
            Assert.That(target, Is.Not.Null);
            Assert.That(target.requiredPowered, Is.True);
            Assert.That(target.objectIds, Is.EquivalentTo(BoardIds()));
            foreach (MapObjectDefinition item in area.definition.objects)
            {
                if (item.id == Source) Assert.That(new Vector2Int(item.x, item.y), Is.EqualTo(new Vector2Int(11, 3)));
                if (item.id == Link) { Assert.That(new Vector2Int(item.x, item.y), Is.EqualTo(new Vector2Int(14, 3))); Assert.That(item.tags, Does.Contain(WorldStateService.ConductorTag)); }
            }

            Vector2Int[] relics = PuzzleCells("Cells"), relays = PuzzleCells("RelayCells");
            for (int i = 0; i < relics.Length; i++) state.RegisterObject("stella.string-together.relic." + (i + 1), null, relics[i], Vector2Int.one, true, ConductorTags);
            for (int i = 0; i < relays.Length; i++) state.RegisterObject("stella.circuit.relay." + (i + 1), null, relays[i], Vector2Int.one, true, ConductorTags);
            Assert.That(StellaStringThemTogetherPuzzle.TryGetCircuitStep(out string first, out Vector2Int sourceCell), Is.True);
            Assert.That(first, Does.Contain("Ampere"));
            Assert.That(sourceCell, Is.EqualTo(new Vector2Int(11, 3)));
            state.SetProperty(Source, x => x.powered = true);

            int cables = 0;
            while (!state.EvaluatePuzzleTarget(target) && cables < 20)
            {
                Assert.That(StellaStringThemTogetherPuzzle.TryGetCircuitStep(out string instruction, out Vector2Int cell), Is.True);
                Assert.That(instruction, Does.StartWith("Weave from"));
                WorldObjectSnapshot next = null, lit = null;
                foreach (WorldObjectSnapshot node in state.SnapshotObjects())
                {
                    if (node.cell == cell) next = node;
                    else if (node.powered && (lit == null || Vector2Int.Distance(node.cell, cell) < Vector2Int.Distance(lit.cell, cell))) lit = node;
                }
                Assert.That(next, Is.Not.Null, instruction);
                Assert.That(next.powered, Is.False, "The hint points at a dark relic or relay.");
                Assert.That(state.AddConnection(lit.id, next.id), Is.True, lit.id + " -> " + next.id + " must be within reach.");
                cables++;
            }
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True, "Every board relic is reachable from Stella's relic.");
            Assert.That(StellaStringThemTogetherPuzzle.TryGetCircuitStep(out string done, out _), Is.True);
            Assert.That(done, Does.Contain("All five relics are lit"));
        }

        [Test] public void StellaDeterministicallyHandsOffToNextOrActiveAssignment()
        {
            NpcCatalogAsset catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<NpcCatalogAsset>("Assets/_Project/World/Areas/NpcCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            NpcDefinition stella = null; foreach (NpcDefinition definition in catalog.npcs) if (definition.id == "stella") stella = definition;
            Assert.That(stella, Is.Not.Null);
            DynamicNpc npc = Host("Stella").AddComponent<DynamicNpc>(); npc.Configure(stella, null);
            runtime.StartQuest("stella_first_light"); runtime.CompleteQuest("stella_first_light");
            for (int i = 0; i < 20; i++) { DialogueSession session = npc.Interact(); Assert.That(session.Definition.id, Is.EqualTo("stella_string_them_together_dialogue")); session.End(); }
            runtime.StartQuest("stella_string_them_together");
            Assert.That(npc.Interact().Definition.id, Is.EqualTo("stella_string_them_together_dialogue"));
        }

        [Test] public void MonitorCompletesOnlyOnceAllFiveBoardRelicsAreLit()
        {
            string[] board = BoardIds();
            for (int i = 0; i < board.Length; i++) state.RegisterObject(board[i], null, new Vector2Int(17 + 3 * i, 3), Vector2Int.one, true, ConductorTags);
            runtime.StartQuest("stella_first_light"); runtime.CompleteQuest("stella_first_light"); runtime.StartQuest("stella_string_them_together");
            state.SetProperty(Source, x => x.powered = true);
            state.AddConnection(Source, Link);
            string previous = Link;
            for (int i = 0; i < board.Length - 1; i++) { state.AddConnection(previous, board[i]); previous = board[i]; }
            QuestPuzzleMonitor monitor = Host("monitor").AddComponent<QuestPuzzleMonitor>(); EditModeLifecycle.Call(monitor, "Awake"); monitor.EvaluatePuzzles();
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active), "Four lit relics are not enough.");
            Object.DestroyImmediate(monitor.gameObject); hosts.RemoveAt(hosts.Count - 1);
            state.AddConnection(previous, board[board.Length - 1]);
            monitor = Host("monitor-lit").AddComponent<QuestPuzzleMonitor>(); EditModeLifecycle.Call(monitor, "Awake"); monitor.EvaluatePuzzles();
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed));
            state.SetProperty(Source, x => x.powered = false);
            Object.DestroyImmediate(monitor.gameObject); hosts.RemoveAt(hosts.Count - 1);
            monitor = Host("monitor-completed").AddComponent<QuestPuzzleMonitor>(); EditModeLifecycle.Call(monitor, "Awake"); monitor.EvaluatePuzzles();
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed), "Completion remains durable after the source goes dark.");
        }
        private bool Powered(string id) => state.TryGetObject(id, out WorldObjectSnapshot item) && item.powered;
        private static string[] BoardIds() => new[] { "stella.string-together.relic.1", "stella.string-together.relic.2", "stella.string-together.relic.3", "stella.string-together.relic.4", "stella.string-together.relic.5" };
        private static Vector2Int[] PuzzleCells(string field) => (Vector2Int[])typeof(StellaStringThemTogetherPuzzle).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        private GameObject Host(string name) { GameObject go = new GameObject(name); hosts.Add(go); return go; }
    }
}
