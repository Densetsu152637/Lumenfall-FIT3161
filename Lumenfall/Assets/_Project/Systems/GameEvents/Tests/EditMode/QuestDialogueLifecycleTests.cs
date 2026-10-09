using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Lumenfall.GameEvents;
using Lumenfall.DataDriven;
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

        [SetUp]
        public void SetUp()
        {
            ResetSharedState();
        }

        [TearDown]
        public void TearDown()
        {
            ResetSharedState();
        }

        private static void ResetSharedState()
        {
            GameEventManager.Instance.Runtime.ResetState();
            typeof(InventoryUiState).GetProperty(nameof(InventoryUiState.IsOpen)).SetValue(null, false);
        }

        // Ordinary MonoBehaviours do not run their play-mode lifecycle in EditMode.
        // Normalize subscriptions before explicitly wiring the production event handlers.
        private static void EnableForTest(Component component)
        {
            InvokeLifecycle(component, "OnDisable");
            InvokeLifecycle(component, "OnEnable");
        }

        private static void InvokeLifecycle(Component component, string method)
        {
            component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, null);
        }

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

                if (questId == "orio_patch_notes") runtime.SetFlag("orio.patch_notes.mechanism_aligned", true);
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
        public void StellaFollowUpDialogueAlwaysWinsOverCompletedFirstQuest()
        {
            GameEventManager manager = GameEventManager.Instance;
            GameEventRuntime runtime = manager.Runtime;
            runtime.ResetState();
            GameObject host = new GameObject("stella-follow-up-selection-test");
            try
            {
                StartAndComplete(runtime, "stella_first_light");
                DynamicNpc stella = host.AddComponent<DynamicNpc>();
                stella.Configure(new NpcDefinition
                {
                    id = "stella",
                    dialogue = new[]
                    {
                        new NpcDialogueDefinition
                        {
                            dialogueId = "stella_first_light_dialogue",
                            questId = "stella_first_light",
                            requiredQuestStatus = "Completed",
                            weight = 5
                        },
                        new NpcDialogueDefinition
                        {
                            dialogueId = "stella_string_them_together_dialogue",
                            questId = "stella_string_them_together",
                            requiredQuestStatus = "NotStarted",
                            prerequisiteQuestId = "stella_first_light",
                            prerequisiteQuestStatus = "Completed",
                            weight = 5
                        },
                        new NpcDialogueDefinition
                        {
                            dialogueId = "stella_string_them_together_dialogue",
                            questId = "stella_string_them_together",
                            requiredQuestStatus = "Active",
                            prerequisiteQuestId = "stella_first_light",
                            prerequisiteQuestStatus = "Completed",
                            weight = 5
                        },
                        new NpcDialogueDefinition
                        {
                            dialogueId = "stella_string_them_together_dialogue",
                            questId = "stella_string_them_together",
                            requiredQuestStatus = "Completed",
                            prerequisiteQuestId = "stella_first_light",
                            prerequisiteQuestStatus = "Completed",
                            weight = 5
                        }
                    }
                }, null);

                DialogueSession session = stella.Interact();
                Assert.That(session.Definition.id, Is.EqualTo("stella_string_them_together_dialogue"));
                Assert.That(ChoiceIds(session), Does.Contain("accept"));
                Assert.That(session.SelectChoice("accept"), Is.True);
                Assert.That(runtime.GetFlag("spell.unlocked.Weave"), Is.True);
                session.End();

                session = stella.Interact();
                Assert.That(session.Definition.id, Is.EqualTo("stella_string_them_together_dialogue"));
                Assert.That(ChoiceIds(session), Does.Contain("active"));
                session.End();
                Assert.That(runtime.TriggerEvent("complete_stella_string_them_together").Succeeded, Is.True);

                session = stella.Interact();
                Assert.That(session.Definition.id, Is.EqualTo("stella_string_them_together_dialogue"));
                Assert.That(ChoiceIds(session), Does.Contain("done"));
            }
            finally
            {
                if (runtime.ActiveDialogue != null) runtime.ActiveDialogue.End();
                runtime.ResetState();
                foreach (SpellCaster component in host.GetComponents<SpellCaster>()) EditModeLifecycle.Call(component, "OnDisable");
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SpellRewardsAreQuestScopedAndDialogueHasNoSpellReward()
        {
            GameEventRuntime runtime = GameEventManager.Instance.Runtime;
            runtime.ResetState();
            GameObject host = new GameObject("dialogue-spell-reward-test");
            SpellCaster caster = host.AddComponent<SpellCaster>();
            EditModeLifecycle.Call(caster, "OnEnable");
            try
            {
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
            finally
            {
                EditModeLifecycle.Call(caster, "OnDisable");
                foreach (SpellCaster component in host.GetComponents<SpellCaster>()) EditModeLifecycle.Call(component, "OnDisable");
                Object.DestroyImmediate(host);
                runtime.ResetState();
            }
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
                EditModeLifecycle.Call(caster, "OnEnable");
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
                foreach (SpellCaster component in host.GetComponents<SpellCaster>()) EditModeLifecycle.Call(component, "OnDisable");
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void GravitateArrivesThroughAuthoredLaterQuestCompletion()
        {
            GameEventManager manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            GameObject host = new GameObject("later-spell-progression-test");
            try
            {
                SpellCaster caster = host.AddComponent<SpellCaster>();
                EditModeLifecycle.Call(caster, "OnEnable");
                DialogueSession dialogue = manager.Runtime.StartDialogue("orio_patch_notes_dialogue");
                Assert.That(dialogue.SelectChoice("accept"), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.False);
                Assert.That(manager.Runtime.TriggerEvent("complete_orio_patch_notes").Succeeded, Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Gravitate), Is.True);
                Assert.That(caster.IsSpellUnlocked(SpellId.Rotara), Is.True);
            }
            finally
            {
                foreach (SpellCaster component in host.GetComponents<SpellCaster>()) EditModeLifecycle.Call(component, "OnDisable");
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void AuthoredQuestCompletionGrantsKeepsakeAndPersistsDiscovery()
        {
            GameEventManager manager = GameEventManager.Instance;
            manager.Runtime.ResetState();
            GameObject uiObject = new GameObject("inventory-test-ui");
            InventoryEncyclopediaUI ui = uiObject.AddComponent<InventoryEncyclopediaUI>();
            EditModeLifecycle.Call(ui, "OnEnable");
            try
            {
                EnableForTest(inventory);
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
                EditModeLifecycle.Call(ui, "OnDisable");
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
            GameEventRuntime runtime = new GameEventRuntime(catalog);
            // Catalogue-only traversal; the real map/object/angle condition has separate behavior coverage.
            runtime.RegisterConditionHandler("OrioMechanismAligned", (condition, current) => current.GetFlag("orio.patch_notes.mechanism_aligned"));
            return runtime;
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
