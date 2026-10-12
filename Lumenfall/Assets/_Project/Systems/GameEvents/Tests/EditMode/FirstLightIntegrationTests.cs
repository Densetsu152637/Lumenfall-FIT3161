using System.Collections.Generic;
using System.Reflection;
using Lumenfall.Inventory;
using Lumenfall.GameEvents;
using Lumenfall.Quests;
using Lumenfall.Spells;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class FirstLightIntegrationTests
    {
        private readonly List<GameObject> created = new List<GameObject>();
        private GameEventManager manager;
        private WorldStateService state;
        private WorldStateService previousState;
        private QuestPuzzleMonitor monitor;
        private SpellCaster caster;

        [SetUp]
        public void SetUp()
        {
            manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            previousState = WorldStateService.Instance;
            state = Create("first-light-world").AddComponent<WorldStateService>();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, state);
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, false);
            state.ResetAllState();
            monitor = Create("first-light-monitor").AddComponent<QuestPuzzleMonitor>();
            Call(monitor, "Awake");
            Call(monitor, "OnEnable");
            caster = Create("first-light-caster").AddComponent<SpellCaster>();
            Call(caster, "OnEnable");
            BuildRelic("B02.Ground");
        }

        [TearDown]
        public void TearDown()
        {
            Call(caster, "OnDisable");
            Call(monitor, "OnDisable");
            state.ResetAllState();
            manager.Runtime.ResetState();
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] == null) continue;

                Object.DestroyImmediate(created[i]);
            }
            created.Clear();
            typeof(WorldStateService).GetProperty(nameof(WorldStateService.Instance)).SetValue(null, previousState);
        }

        [Test]
        public void AcceptedLessonCanBeAbandonedAndRecoveredThroughActiveHint()
        {
            DialogueSession lesson = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            lesson.SelectChoice("accept");
            lesson.End();
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);
            DialogueSession revisit = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            revisit.SelectChoice("leave");
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);
            revisit = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            revisit.SelectChoice("active");
            revisit.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.True);
        }

        [Test]
        public void PoweredStarRelicsCompleteFirstLightAndShowSuccessOnce()
        {
            TeachAmpere();
            SetRelicsPowered(true);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Completed));
            Assert.That(manager.Runtime.ActiveDialogue.Definition.id, Is.EqualTo("stella_first_light_success"));
            manager.Runtime.ActiveDialogue.Continue();
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.ActiveDialogue, Is.Null);
        }

        [Test]
        public void CompletionRequiresActiveQuestCorrectMapAndPoweredTarget()
        {
            SetRelicsPowered(true);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.NotStarted));
            manager.Runtime.StartQuest("stella_first_light");
            BuildRelic("campus");
            SetRelicsPowered(true);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            BuildRelic("B02.Ground");
            SetRelicsPowered(false);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void MonitorWaitsForDialogueAndDisabledMonitorCannotComplete()
        {
            manager.Runtime.StartQuest("stella_first_light");
            SetRelicsPowered(true);
            manager.Runtime.StartDialogue("stella_first_light_dialogue");
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            manager.Runtime.ActiveDialogue.End();
            Call(monitor, "OnDisable");
            monitor.enabled = false;
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            monitor.enabled = true;
            Call(monitor, "OnEnable");
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void ReloadDefinitionsRebindsCasterAndMonitorWithoutOldRuntimeRewards()
        {
            GameEventRuntime oldRuntime = manager.Runtime;
            DialogueSession oldLesson = oldRuntime.StartDialogue("stella_first_light_dialogue");
            oldLesson.SelectChoice("accept");
            manager.ReloadDefinitions();
            oldLesson.Continue();
            Assert.That(oldRuntime.GetFlag("spell.unlocked.Ampere"), Is.True, "The old runtime owns its lesson reward; it must not affect the replacement runtime.");
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);
            TeachAmpere();
            SetRelicsPowered(true);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void CompletedSaveDoesNotReplaySuccessAndResetCanCompleteAgain()
        {
            TeachAmpere();
            SetRelicsPowered(true);
            monitor.EvaluatePuzzles();
            manager.Runtime.ActiveDialogue.Continue();
            string saved = manager.Runtime.ExportStateJson(false);
            Assert.That(manager.Runtime.ImportStateJson(saved), Is.True);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.ActiveDialogue, Is.Null);
            manager.Runtime.ResetState();
            manager.Runtime.StartQuest("stella_first_light");
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.ActiveDialogue.Definition.id, Is.EqualTo("stella_first_light_success"));
        }

        [Test]
        public void DisabledCasterDoesNotBlockRuntimeTeachingAndReenablesWithReward()
        {
            Call(caster, "OnDisable");
            caster.enabled = false;
            TeachAmpere();
            Assert.That(manager.Runtime.GetFlag("spell.unlocked.Ampere"), Is.True, "Lesson rewards belong to the runtime even while the caster is disabled.");
            caster.enabled = true;
            Call(caster, "OnEnable");
            DialogueSession hint = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            hint.SelectChoice("active");
            hint.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.True);
        }

        private static void Call(MonoBehaviour component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(component, null);
        }

        private GameObject Create(string name)
        {
            GameObject host = new GameObject(name);
            created.Add(host);
            return host;
        }

        private void BuildRelic(string mapId)
        {
            state.BeginMapRebuild(mapId);
            for (int i = 1; i <= 5; i++)
                state.RegisterObject("stella.string-together.relic." + i, null, new Vector2Int(i, 14), Vector2Int.one, true, new[] { "ampere" });
            state.EndMapRebuild();
        }

        private void SetRelicsPowered(bool powered)
        {
            for (int i = 1; i <= 5; i++)
                state.SetProperty("stella.string-together.relic." + i, item => item.powered = powered);
        }

        private void TeachAmpere()
        {
            DialogueSession lesson = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            Assert.That(lesson.SelectChoice("accept"), Is.True);
            Assert.That(lesson.Continue(), Is.True);
        }
    }
}
