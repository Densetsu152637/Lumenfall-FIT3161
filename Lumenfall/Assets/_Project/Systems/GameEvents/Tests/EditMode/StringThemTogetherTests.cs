using System.Collections.Generic;
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
        private GameEventRuntime runtime;
        private SpellCaster caster;

        [SetUp] public void SetUp()
        {
            runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            state = Host("world").AddComponent<WorldStateService>();
            state.BeginMapRebuild("B02.Ground");
            state.RegisterObject("relic.stella.power", null, new Vector2Int(18, 14), Vector2Int.one, true, new[] { "weave-with:circuit.stella.link" });
            state.RegisterObject("circuit.stella.link", null, new Vector2Int(20, 14), Vector2Int.one, true, new[] { "weave-with:relic.stella.power" });
            state.EndMapRebuild();
            caster = Host("caster").AddComponent<SpellCaster>();
        }
        [TearDown] public void TearDown()
        {
            runtime.ActiveDialogue?.End();
            GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
            for (int i = hosts.Count - 1; i >= 0; i--) Object.DestroyImmediate(hosts[i]);
            hosts.Clear(); runtime.ResetState();
        }

        [Test] public void LessonRequiresPrerequisiteAcceptanceAndTerminalContinuation()
        {
            DialogueSession blocked = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(blocked.SelectChoice("accept"), Is.False); blocked.End();
            runtime.StartQuest("stella_first_light"); runtime.CompleteQuest("stella_first_light");
            DialogueSession session = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);
            Assert.That(session.SelectChoice("accept"), Is.True);
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);
            session.End();
            session = runtime.StartDialogue("stella_string_them_together_dialogue"); session.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);
            session = runtime.StartDialogue("stella_string_them_together_dialogue"); session.SelectChoice("active"); session.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);
            string save = runtime.ExportStateJson(false); runtime.ResetState(); runtime.ImportStateJson(save);
            Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.True);
        }

        [Test] public void ExactCableNeedsLiveSourceAndDistanceAndSupportsRetry()
        {
            PuzzleTargetDefinition target = new PuzzleTargetDefinition { objectIds = new[] { "relic.stella.power", "circuit.stella.link" }, requiredConnections = new[] { "relic.stella.power|circuit.stella.link" }, minimumEffectivePower = 0.2f };
            state.RegisterObject("decoy", null, new Vector2Int(19, 14), Vector2Int.one, true, null);
            state.SetProperty("relic.stella.power", x => x.powered = true);
            state.AddConnection("relic.stella.power", "decoy"); state.AddConnection("circuit.stella.link", "decoy");
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False, "Two stray cables do not satisfy the marked pair.");
            runtime.SetFlag("spell.unlocked.Weave", true);
            Assert.That(caster.Cast(SpellId.Weave, "circuit.stella.link", Vector2Int.up), Is.True);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
            state.SetProperty("relic.stella.power", x => x.powered = false);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False);
            state.SetProperty("relic.stella.power", x => x.powered = true);
            state.SetProperty("circuit.stella.link", x => x.cell = new Vector2Int(22, 14));
            Assert.That(state.GetConnectionEffectivePower("relic.stella.power", "circuit.stella.link"), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
            state.SetProperty("circuit.stella.link", x => x.cell = new Vector2Int(23, 14));
            Assert.That(state.EvaluatePuzzleTarget(target), Is.False, "Existing cables must reevaluate moved endpoints.");
            Assert.That(caster.Cast(SpellId.Weave, "circuit.stella.link", Vector2Int.up), Is.True, "An invalid cable can be removed.");
            Assert.That(state.IsConnected("relic.stella.power", "circuit.stella.link"), Is.False);
            Assert.That(caster.Cast(SpellId.Weave, "circuit.stella.link", Vector2Int.up), Is.False);
            state.SetProperty("circuit.stella.link", x => x.cell = new Vector2Int(20, 14));
            Assert.That(caster.Cast(SpellId.Weave, "circuit.stella.link", Vector2Int.up), Is.True);
            string save = state.CaptureStateJson(); state.RemoveConnection("relic.stella.power", "circuit.stella.link"); state.RestoreStateJson(save);
            Assert.That(state.EvaluatePuzzleTarget(target), Is.True);
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

        [Test] public void MonitorRejectsStrayCablesAndCompletesThePoweredExactPair()
        {
            runtime.StartQuest("stella_first_light"); runtime.CompleteQuest("stella_first_light"); runtime.StartQuest("stella_string_them_together");
            state.SetProperty("relic.stella.power", x => x.powered = true);
            state.RegisterObject("decoy", null, new Vector2Int(19, 14), Vector2Int.one, true, null);
            state.AddConnection("relic.stella.power", "decoy"); state.AddConnection("circuit.stella.link", "decoy");
            QuestPuzzleMonitor monitor = Host("monitor").AddComponent<QuestPuzzleMonitor>(); monitor.SendMessage("Update");
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));
            Object.DestroyImmediate(monitor.gameObject); hosts.RemoveAt(hosts.Count - 1);
            state.AddConnection("relic.stella.power", "circuit.stella.link");
            monitor = Host("monitor-exact").AddComponent<QuestPuzzleMonitor>(); monitor.SendMessage("Update");
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Completed));
        }
        private GameObject Host(string name) { GameObject go = new GameObject(name); hosts.Add(go); return go; }
    }
}
