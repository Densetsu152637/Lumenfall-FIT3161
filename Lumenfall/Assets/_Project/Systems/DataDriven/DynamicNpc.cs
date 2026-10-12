using UnityEngine;
using System;
using System.Collections.Generic;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.Quests;

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
        private static Texture2D interactionKeyTexture;
        private void Awake() { ApplyDefinition(); }
        private void OnEnable() { ApplyDefinition(); }
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
            // OverlayInputGate covers the pause menu, journal, spell wheel and dialogue choices.
            if (Time.timeScale <= 0f || OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || InventoryUiState.IsOpen ||
                DialogueOverlay.IsNpcInteractionBlocked || BuildingSignInteraction.IsEInteractionBlocked ||
                !(KeyBindings.WasPressed(GameAction.Talk) || Gamepad.current?.buttonSouth.wasPressedThisFrame == true) ||
                GameEventManager.Instance == null || GameEventManager.Instance.Runtime.ActiveDialogue != null) return;

            GridMover player = FindAnyObjectByType<GridMover>();
            if (player == null) return;
            DynamicNpc nearest = null;
            float best = 2.01f;
            foreach (DynamicNpc npc in FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                float distance = Vector3.Distance(player.transform.position, npc.transform.position);
                if (distance <= best) { best = distance; nearest = npc; }
            }
            if (nearest == this) Interact();
        }

        private void OnGUI()
        {
            if (OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen ||
                DialogueOverlay.IsNpcInteractionBlocked || GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
            GridMover player = FindAnyObjectByType<GridMover>();
            if (player == null || Vector3.Distance(player.transform.position, transform.position) > 2.01f) return;
            DynamicNpc nearest = null;
            float best = 2.01f;
            foreach (DynamicNpc npc in FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude))
            {
                float distance = Vector3.Distance(player.transform.position, npc.transform.position);
                if (distance <= best) { best = distance; nearest = npc; }
            }
            if (nearest != this || Camera.main == null) return;
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.8f);
            if (screen.z <= 0f) return;
            GUIStyle box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            box.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
            GUIStyle key = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            key.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
            key.normal.background = InteractionKeyTexture();
            Rect rect = new Rect(screen.x - 52f, Screen.height - screen.y - 17f, 104f, 34f);
            GUI.Box(rect, GUIContent.none, box);
            GUI.Label(new Rect(rect.x + 8f, rect.y + 5f, 24f, 24f), "E", key);
            GUI.Label(new Rect(rect.x + 37f, rect.y + 5f, 58f, 24f), "Talk", box);
        }

        private static Texture2D InteractionKeyTexture()
        {
            if (interactionKeyTexture != null) return interactionKeyTexture;
            interactionKeyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            interactionKeyTexture.SetPixel(0, 0, new Color(0.93f, 0.78f, 0.36f, 1f));
            interactionKeyTexture.Apply();
            return interactionKeyTexture;
        }
        public DialogueSession Interact()
        { if (OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || GameEventManager.Instance == null || InventoryUiState.IsOpen) return null; DialogueId = SelectDialogue(); if (string.IsNullOrWhiteSpace(DialogueId)) return null; InventoryEncyclopediaUI.DiscoverCharacter(Id); return GameEventManager.Instance.StartDialogue(DialogueId); }
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

        private bool StatusMatches(string questId, string expectedStatus)
        {
            if (!Enum.TryParse(expectedStatus, true, out QuestStatus expected)) return false;
            return GameEventManager.Instance.Runtime.GetQuestStatus(questId) == expected;
        }
    }
}
