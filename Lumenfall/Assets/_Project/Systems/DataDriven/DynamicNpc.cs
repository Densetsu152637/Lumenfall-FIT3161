using UnityEngine;
using System;
using System.Collections.Generic;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.Quests;
using Lumenfall.Cutscenes;
using Lumenfall.Startup;
using Lumenfall.Story;

namespace Lumenfall.DataDriven
{
    /// Scene-facing component for an NPC loaded from data. It exposes the selected dialogue id so
    /// any existing interaction/UI layer can start the same GameEvent dialogue session.
    public sealed class DynamicNpc : MonoBehaviour
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string DialogueId { get; private set; }
        public NpcDefinition Definition => definition;
        [SerializeField] private NpcDefinition definition;
        [SerializeField] private string initialDialogueId;
        private static readonly List<DynamicNpc> active = new List<DynamicNpc>();
        /// NPCs on active GameObjects; per-frame callers read this instead of searching the scene.
        public static IReadOnlyList<DynamicNpc> Active => active;
        private void Awake()
        {
            // Every NPC idles with some variety and turns to face the player; see NpcAmbientBehaviour.
            if (GetComponent<NpcAmbientBehaviour>() == null) gameObject.AddComponent<NpcAmbientBehaviour>();
            ApplyDefinition();
        }
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); ApplyDefinition(); }
        private void OnDisable() { active.Remove(this); }
        private void ApplyDefinition()
        {
            if (definition == null) return;
            Id = definition.id; DisplayName = definition.displayName;
            if (string.IsNullOrEmpty(DialogueId)) DialogueId = initialDialogueId;
            if (GetComponent<Collider>() == null) gameObject.AddComponent<SphereCollider>().isTrigger = true;
        }
        public void Configure(NpcDefinition definition, string dialogueId)
        { this.definition = definition; initialDialogueId = dialogueId; DialogueId = dialogueId; ApplyDefinition(); }
        public void RefreshVisibility()
        {
            if (definition == null || GameEventManager.Instance == null) return;
            bool visible = string.IsNullOrWhiteSpace(definition.showWhenQuestCompleted) ||
                GameEventManager.Instance.Runtime.GetQuestStatus(definition.showWhenQuestCompleted) == QuestStatus.Completed;
            visible &= string.IsNullOrWhiteSpace(definition.hideWhenQuestCompleted) ||
                GameEventManager.Instance.Runtime.GetQuestStatus(definition.hideWhenQuestCompleted) != QuestStatus.Completed;
            if (gameObject.activeSelf != visible) gameObject.SetActive(visible);
        }
        private void Update()
        {
            // The departure fade runs right after a dialogue closes; a quick Talk press must not reopen it behind the fade.
            if (OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || CutscenePlayer.BlocksInput ||
                CinnaTrialDeparture.InProgress || DialogueOverlay.IsNpcInteractionBlocked || BuildingSignInteraction.IsEInteractionBlocked ||
                GridPortal.IsPlayerAtInteractivePortal ||
                !(KeyBindings.WasPressed(GameAction.Talk) || Gamepad.current?.buttonSouth.wasPressedThisFrame == true) || GameEventManager.Instance == null ||
                GameEventManager.Instance.Runtime.ActiveDialogue != null) return;

            GridMover player = GridMover.FindActive();
            if (player == null) return;
            DynamicNpc nearest = null;
            float best = 2.01f;
            foreach (DynamicNpc npc in Active)
            {
                float distance = Vector3.Distance(player.transform.position, npc.transform.position);
                if (distance <= best) { best = distance; nearest = npc; }
            }
            if (nearest == this) Interact();
        }

        private void OnGUI()
        {
            if (OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || DialogueOverlay.IsNpcInteractionBlocked ||
                CutscenePlayer.BlocksInput || CinnaTrialDeparture.InProgress || GridPortal.IsPlayerAtInteractivePortal || GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
            GridMover player = GridMover.FindActive();
            if (player == null || Vector3.Distance(player.transform.position, transform.position) > 2.01f) return;
            DynamicNpc nearest = null;
            float best = 2.01f;
            foreach (DynamicNpc npc in Active)
            {
                float distance = Vector3.Distance(player.transform.position, npc.transform.position);
                if (distance <= best) { best = distance; nearest = npc; }
            }
            if (nearest != this) return;
            HudSkin.PromptAbove(transform.position + Vector3.up * 1.8f, KeyBindings.GetKeyLabel(GameAction.Talk), "Talk");
        }
        public DialogueSession Interact()
        {
            if (OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || GameEventManager.Instance == null || InventoryUiState.IsOpen) return null;
            DialogueId = SelectDialogue();
            if (string.IsNullOrWhiteSpace(DialogueId)) return null;
            InventoryEncyclopediaUI.DiscoverCharacter(Id);
            QuestHintOverlay.RecordQuestConversation(GameEventManager.Instance.Runtime, Id);
            return NpcIntroductions.StartConversation(Id, DialogueId, HasQuestBusiness());
        }
        private string SelectDialogue()
        {
            if (definition == null) return DialogueId;
            if (GameEventManager.Instance == null || GameEventManager.Instance.Runtime == null)
                return DialogueId;

            List<string> choices = new List<string>(definition.dialogueIds ?? Array.Empty<string>());
            List<string> prerequisiteChoices = new List<string>();
            foreach (NpcDialogueDefinition item in definition.dialogue ?? Array.Empty<NpcDialogueDefinition>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.dialogueId)) continue;
                bool questMatches = string.IsNullOrWhiteSpace(item.questId) ||
                    StatusMatches(item.questId, item.requiredQuestStatus);
                bool prerequisiteMatches = string.IsNullOrWhiteSpace(item.prerequisiteQuestId) ||
                    StatusMatches(item.prerequisiteQuestId, item.prerequisiteQuestStatus);
                if (!questMatches || !prerequisiteMatches) continue;
                List<string> eligible = string.IsNullOrWhiteSpace(item.prerequisiteQuestId) ? choices : prerequisiteChoices;
                for (int i = 0; i < Mathf.Max(1, item.weight); i++) eligible.Add(item.dialogueId);
            }
            // A newly available prerequisite-gated dialogue is the NPC's follow-up beat. Keep
            // completed earlier quests from competing with it in the weighted random selection.
            if (prerequisiteChoices.Count > 0) choices = prerequisiteChoices;
            return choices.Count == 0 ? null : choices[UnityEngine.Random.Range(0, choices.Count)];
        }

        /// True while this NPC's quest dialogue is what they need to say rather than general conversation: a quest to
        /// offer (its prerequisite met), one ready to report back on, or one finished whose thanks the player has not
        /// heard yet. While a quest is simply under way, their general conversation offers it as a topic instead.
        private bool HasQuestBusiness()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (definition == null || runtime == null) return false;
            foreach (NpcDialogueDefinition item in definition.dialogue ?? Array.Empty<NpcDialogueDefinition>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.questId)) continue;
                if (!string.IsNullOrWhiteSpace(item.prerequisiteQuestId) && !StatusMatches(item.prerequisiteQuestId, item.prerequisiteQuestStatus)) continue;
                QuestStatus status = runtime.GetQuestStatus(item.questId);
                if (status == QuestStatus.NotStarted) return true;
                if (status == QuestStatus.Active && CanReport(runtime, item.dialogueId)) return true;
                if (status == QuestStatus.Completed && !runtime.GetFlag(NpcIntroductions.QuestThankedFlag(item.questId))) return true;
            }
            return false;
        }

        // A quest dialogue offers a "report" choice once its task is done but still has to be handed in.
        private static bool CanReport(GameEventRuntime runtime, string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId) || !runtime.Catalog.TryGetDialogue(dialogueId, out DialogueDefinition dialogue) ||
                !runtime.Catalog.TryGetDialogueNode(dialogueId, dialogue.startNodeId, out DialogueNodeDefinition start)) return false;
            foreach (DialogueChoiceDefinition choice in start.choices ?? Array.Empty<DialogueChoiceDefinition>())
                if (choice != null && choice.id == "report" && runtime.EvaluateConditions(choice.conditions, choice.conditionMode)) return true;
            return false;
        }

        private bool StatusMatches(string questId, string expectedStatus)
        {
            if (!Enum.TryParse(expectedStatus, true, out QuestStatus expected)) return false;
            return GameEventManager.Instance.Runtime.GetQuestStatus(questId) == expected;
        }
    }
}
