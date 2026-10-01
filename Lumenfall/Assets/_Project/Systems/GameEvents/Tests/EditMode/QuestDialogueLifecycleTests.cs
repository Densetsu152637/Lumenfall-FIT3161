using System.Collections.Generic;
using System.Linq;
using Lumenfall.GameEvents;
using Lumenfall.Spells;
using Lumenfall.Inventory;
using NUnit.Framework;
using UnityEngine;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class QuestDialogueLifecycleTests
    {
        private static readonly string[] QuestIds =
        {
            "stella_first_light", "stella_string_them_together", "cinna_out_of_alignment",
            "cinna_cold_feet_warm_idea", "orio_patch_notes", "katrina_something_borrowed"
        };

        [Test]
        public void EveryQuestDialogueTraversesNotStartedActiveAndCompletedBranches()
        {
            GameEventRuntime runtime = CreateRuntime();
            foreach (string questId in QuestIds)
            {
                DialogueSession initial = runtime.StartDialogue(questId + "_dialogue");
                Assert.That(ChoiceIds(initial), Does.Contain("accept"), questId);
                Assert.That(ChoiceIds(initial), Does.Not.Contain("active"), questId);
                Assert.That(ChoiceIds(initial), Does.Not.Contain("done"), questId);

                Assert.That(initial.SelectChoice("accept"), Is.True, questId);
                Assert.That(runtime.GetQuestStatus(questId), Is.EqualTo(QuestStatus.Active), questId);

                DialogueSession active = runtime.StartDialogue(questId + "_dialogue");
                Assert.That(ChoiceIds(active), Does.Contain("active"), questId);
                Assert.That(ChoiceIds(active), Does.Not.Contain("accept"), questId);

                Assert.That(runtime.TriggerEvent("complete_" + questId).Succeeded, Is.True, questId);
                DialogueSession completed = runtime.StartDialogue(questId + "_dialogue");
                Assert.That(ChoiceIds(completed), Does.Contain("done"), questId);
                Assert.That(ChoiceIds(completed), Does.Not.Contain("accept"), questId);
                Assert.That(runtime.TriggerEvent("complete_" + questId).Status,
                    Is.EqualTo(GameEventTriggerStatus.AlreadyTriggered), questId);
            }
        }

        [Test]
        public void DependentQuestAcceptChoiceRemainsUnavailableUntilPrerequisiteCompletes()
        {
            GameEventRuntime runtime = CreateRuntime();
            DialogueSession stellaSecond = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(ChoiceIds(stellaSecond), Does.Not.Contain("accept"));
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.NotStarted));

            StartAndComplete(runtime, "stella_first_light");
            stellaSecond = runtime.StartDialogue("stella_string_them_together_dialogue");
            Assert.That(stellaSecond.SelectChoice("accept"), Is.True);
            Assert.That(runtime.GetQuestStatus("stella_string_them_together"), Is.EqualTo(QuestStatus.Active));

            GameEventRuntime cinnaRuntime = CreateRuntime();
            DialogueSession cinnaSecond = cinnaRuntime.StartDialogue("cinna_cold_feet_warm_idea_dialogue");
            Assert.That(ChoiceIds(cinnaSecond), Does.Not.Contain("accept"));
            StartAndComplete(cinnaRuntime, "cinna_out_of_alignment");
            cinnaSecond = cinnaRuntime.StartDialogue("cinna_cold_feet_warm_idea_dialogue");
            Assert.That(cinnaSecond.SelectChoice("accept"), Is.True);
        }

        [Test]
        public void SpellRewardsAreQuestScopedAndDialogueHasNoSpellReward()
        {
            GameEventRuntime runtime = CreateRuntime();
            DialogueSession session = runtime.StartDialogue("stella_first_light_dialogue");
            Assert.That(session.Definition.metadata.Any(item => item.key == "spellReward" && item.value == "Ampere"), Is.True);
            Assert.That(session.IsCompleted, Is.False);
            session.SelectChoice("leave");
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.False, "Leaving before accepting the quest must not award Ampere.");

            session = runtime.StartDialogue("stella_first_light_dialogue");
            Assert.That(session.SelectChoice("accept"), Is.True);
            Assert.That(session.IsCompleted, Is.False);
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.False, "Accepting the quest alone must not award Ampere.");
            Assert.That(session.Continue(), Is.True);
            Assert.That(session.IsCompleted, Is.True);
            Assert.That(runtime.GetFlag("spell.unlocked.Ampere"), Is.True, "Accepting the first quest teaches Ampere before its puzzle.");
        }

        [Test]
        public void SpellRewardFlagsSurviveRuntimeSaveRoundTrip()
        {
            GameEventRuntime runtime = CreateRuntime();
            runtime.SetFlag("spell.unlocked.Weave", true);
            string saved = runtime.ExportStateJson(false);
            GameEventRuntime restored = CreateRuntime();
            Assert.That(restored.ImportStateJson(saved), Is.True);
            Assert.That(restored.GetFlag("spell.unlocked.Weave"), Is.True);
        }

        [Test]
        public void FreshSpellCasterHasNoSpellsAndFirstQuestTeachesAmpere()
        {
            GameEventManager manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            GameObject host = new GameObject("spell-progression-test");
            try
            {
                SpellCaster caster = host.AddComponent<SpellCaster>();
                Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);
                Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.False);
                Assert.That(caster.IsSpellUnlocked(SpellId.Weave), Is.False);

                DialogueSession abandoned = manager.Runtime.StartDialogue("stella_first_light_dialogue");
                Assert.That(abandoned.SelectChoice("leave"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);

                DialogueSession finished = manager.Runtime.StartDialogue("stella_first_light_dialogue");
                Assert.That(finished.SelectChoice("accept"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.False);
                Assert.That(finished.Continue(), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Ampere), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OrioQuestCompletionTeachesOnlyRotara()
        {
            GameEventManager manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            GameObject host = new GameObject("later-spell-progression-test");
            try
            {
                SpellCaster caster = host.AddComponent<SpellCaster>();
                DialogueSession dialogue = manager.Runtime.StartDialogue("orio_patch_notes_dialogue");
                Assert.That(dialogue.SelectChoice("accept"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.False);
                Assert.That(manager.Runtime.TriggerEvent("complete_orio_patch_notes").Succeeded, Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OrioConfirmsPatchOnlyOnceGearIsAlignedAndTeachesRotara()
        {
            GameEventManager manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            GameObject host = new GameObject("orio-report-test");
            try
            {
                SpellCaster caster = host.AddComponent<SpellCaster>();
                Assert.That(manager.Runtime.StartDialogue("orio_patch_notes_dialogue").SelectChoice("accept"), Is.True);
                manager.Runtime.ActiveDialogue?.End();

                DialogueSession misaligned = manager.Runtime.StartDialogue("orio_patch_notes_dialogue");
                Assert.That(ChoiceIds(misaligned), Does.Not.Contain("report"));
                Assert.That(ChoiceIds(misaligned), Does.Contain("active"));
                misaligned.End();

                manager.Runtime.SetFlag("orio.patch_notes.mechanism_aligned", true);
                DialogueSession aligned = manager.Runtime.StartDialogue("orio_patch_notes_dialogue");
                Assert.That(ChoiceIds(aligned), Does.Not.Contain("active"));
                Assert.That(aligned.SelectChoice("report"), Is.True);
                Assert.That(manager.Runtime.GetQuestStatus("orio_patch_notes"), Is.EqualTo(QuestStatus.Completed));
                Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
            }
            finally
            {
                manager.Runtime.ActiveDialogue?.End();
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AuthoredQuestCompletionGrantsKeepsakeAndPersistsDiscovery()
        {
            GameEventManager manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            GameObject uiObject = new GameObject("inventory-test-ui");
            uiObject.AddComponent<InventoryEncyclopediaUI>();
            try
            {
                Assert.That(InventoryEncyclopediaUI.IsKeyItemOwned("katrina-keepsake"), Is.False);

                DialogueSession dialogue = manager.Runtime.StartDialogue("katrina_something_borrowed_dialogue");
                Assert.That(dialogue.SelectChoice("accept"), Is.True);
                Assert.That(InventoryEncyclopediaUI.IsKeyItemOwned("katrina-keepsake"), Is.False);
                Assert.That(manager.Runtime.TriggerEvent("complete_katrina_something_borrowed").Succeeded, Is.True);
                Assert.That(InventoryEncyclopediaUI.IsKeyItemOwned("katrina-keepsake"), Is.True);

                string saved = manager.Runtime.ExportStateJson(false);
                manager.Runtime.ResetState();
                Assert.That(manager.Runtime.ImportStateJson(saved), Is.True);
                Assert.That(InventoryEncyclopediaUI.IsKeyItemOwned("katrina-keepsake"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(uiObject);
            }
        }

        [Test]
        public void EveryAuthoredNpcCanBeDiscoveredAndPersistsAsACharacterFlag()
        {
            GameEventManager.Instance.Runtime.ResetState();
            string[] ids = { "stella", "cinna_cirra_orientation", "cinna_cirra_trial", "orio", "campus_student", "forest_student", "katrina" };
            foreach (string id in ids)
            {
                Assert.That(InventoryEncyclopediaUI.IsCharacterDiscovered(id), Is.False, id);
                InventoryEncyclopediaUI.DiscoverCharacter(id);
                Assert.That(InventoryEncyclopediaUI.IsCharacterDiscovered(id), Is.True, id);
            }
            Assert.That(InventoryEncyclopediaUI.IsCharacterDiscovered("unknown"), Is.False);
        }

        private static GameEventRuntime CreateRuntime()
        {
            TextAsset asset = Resources.Load<TextAsset>("GameEvents/quest_npc_dialogue");
            Assert.That(asset, Is.Not.Null);
            Assert.That(GameEventCatalog.TryParseJson(asset.text, asset.name,
                out GameEventDatabaseDefinition database, out string error), Is.True, error);
            GameEventCatalog catalog = new GameEventCatalog(new[] { database });
            Assert.That(catalog.IsValid, Is.True, string.Join("\n", catalog.ValidationMessages));
            return new GameEventRuntime(catalog);
        }

        private static void StartAndComplete(GameEventRuntime runtime, string questId)
        {
            DialogueSession dialogue = runtime.StartDialogue(questId + "_dialogue");
            Assert.That(dialogue.SelectChoice("accept"), Is.True, questId);
            Assert.That(runtime.TriggerEvent("complete_" + questId).Succeeded, Is.True, questId);
        }

        private static List<string> ChoiceIds(DialogueSession session)
        {
            List<string> result = new List<string>();
            foreach (DialogueChoiceDefinition choice in session.AvailableChoices) result.Add(choice.id);
            return result;
        }
    }
}
