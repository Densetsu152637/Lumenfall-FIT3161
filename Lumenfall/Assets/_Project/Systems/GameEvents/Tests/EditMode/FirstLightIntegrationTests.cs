using System.Collections.Generic;
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
        private QuestPuzzleMonitor monitor;
        private SpellCaster caster;

        [SetUp]
        public void SetUp()
        {
            manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            state = WorldStateService.Instance;
            if (state == null) state = Create("first-light-world").AddComponent<WorldStateService>();
            state.ResetAllState();
            monitor = Object.FindAnyObjectByType<QuestPuzzleMonitor>();
            if (monitor == null) monitor = Create("first-light-monitor").AddComponent<QuestPuzzleMonitor>();
            caster = Create("first-light-caster").AddComponent<SpellCaster>();
            BuildRelic("B02.Ground");
        }

        [TearDown]
        public void TearDown()
        {
            state.ResetAllState();
            manager.Runtime.ResetState();
            for (int i = created.Count - 1; i >= 0; i--)
            {
                if (created[i] == null) continue;
                if (created[i].GetComponent<WorldStateService>() != null)
                    GameSaveManager.Instance.UnregisterProvider(state.SaveKey);
                Object.DestroyImmediate(created[i]);
            }
            created.Clear();
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
        public void LearnedAmpereCompletesPoweredRelicAndShowsSuccessOnce()
        {
            TeachAmpere();
            Assert.That(caster.Cast(SpellId.Ampere, "relic.stella.power", Vector2Int.up), Is.True);
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
            state.SetProperty("relic.stella.power", x => x.powered = true);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.NotStarted));
            manager.Runtime.StartQuest("stella_first_light");
            BuildRelic("campus");
            state.SetProperty("relic.stella.power", x => x.powered = true);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            BuildRelic("B02.Ground");
            state.SetProperty("relic.stella.power", x => x.powered = false);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
        }

        [Test]
        public void MonitorWaitsForDialogueAndDisabledMonitorCannotComplete()
        {
            manager.Runtime.StartQuest("stella_first_light");
            state.SetProperty("relic.stella.power", x => x.powered = true);
            manager.Runtime.StartDialogue("stella_first_light_dialogue");
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            manager.Runtime.ActiveDialogue.End();
            monitor.enabled = false;
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Active));
            monitor.enabled = true;
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
            Assert.That(oldRuntime.GetFlag("spell.unlocked.Ampere"), Is.False);
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);
            TeachAmpere();
            caster.Cast(SpellId.Ampere, "relic.stella.power", Vector2Int.up);
            monitor.EvaluatePuzzles();
            Assert.That(manager.Runtime.GetQuestStatus("stella_first_light"), Is.EqualTo(QuestStatus.Completed));
        }

        [Test]
        public void CompletedSaveDoesNotReplaySuccessAndResetCanCompleteAgain()
        {
            TeachAmpere();
            caster.Cast(SpellId.Ampere, "relic.stella.power", Vector2Int.up);
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
        public void DisabledCasterUnsubscribesAndReenabledCasterRecoversTeaching()
        {
            caster.enabled = false;
            TeachAmpere();
            Assert.That(manager.Runtime.GetFlag("spell.unlocked.Ampere"), Is.False);
            caster.enabled = true;
            DialogueSession hint = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            hint.SelectChoice("active");
            hint.Continue();
            Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.True);
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
            state.RegisterObject("relic.stella.power", null, new Vector2Int(18, 14), Vector2Int.one, true, new[] { "ampere" });
            state.EndMapRebuild();
        }

        private void TeachAmpere()
        {
            DialogueSession lesson = manager.Runtime.StartDialogue("stella_first_light_dialogue");
            Assert.That(lesson.SelectChoice("accept"), Is.True);
            Assert.That(lesson.Continue(), Is.True);
        }
    }
}
