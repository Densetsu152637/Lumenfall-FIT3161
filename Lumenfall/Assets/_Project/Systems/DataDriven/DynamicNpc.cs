using UnityEngine;
using System;
using System.Collections.Generic;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using UnityEngine.InputSystem;

namespace Lumenfall.DataDriven
{
    /// Scene-facing component for an NPC loaded from data. It exposes the selected dialogue id so
    /// any existing interaction/UI layer can start the same GameEvent dialogue session.
    public sealed class DynamicNpc : MonoBehaviour
    {
        public string Id { get; private set; }
        public string DisplayName { get; private set; }
        public string DialogueId { get; private set; }
        private NpcDefinition definition;
        public void Configure(NpcDefinition definition, string dialogueId)
        { this.definition = definition; Id = definition == null ? null : definition.id; DisplayName = definition == null ? null : definition.displayName; DialogueId = dialogueId; if (GetComponent<Collider>() == null) gameObject.AddComponent<SphereCollider>().isTrigger = true; }
        private void Update()
        { if (Time.timeScale <= 0f || Keyboard.current == null || !Keyboard.current.eKey.wasPressedThisFrame || GameEventManager.Instance == null || GameEventManager.Instance.Runtime.ActiveDialogue != null) return; GridMover player = FindAnyObjectByType<GridMover>(); if (player == null) return; DynamicNpc nearest = null; float best = 2.01f; foreach (DynamicNpc npc in FindObjectsByType<DynamicNpc>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) { float d = Vector3.Distance(player.transform.position, npc.transform.position); if (d <= best) { best = d; nearest = npc; } } if (nearest == this) Interact(); }
        public DialogueSession Interact()
        { if (GameEventManager.Instance == null) return null; DialogueId = SelectDialogue(); return string.IsNullOrWhiteSpace(DialogueId) ? null : GameEventManager.Instance.StartDialogue(DialogueId); }
        private string SelectDialogue()
        {
            if (definition == null) return DialogueId;
            if (GameEventManager.Instance == null || GameEventManager.Instance.Runtime == null)
                return DialogueId;

            List<string> choices = new List<string>(definition.dialogueIds ?? Array.Empty<string>());
            foreach (NpcDialogueDefinition item in definition.dialogue ?? Array.Empty<NpcDialogueDefinition>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.dialogueId)) continue;
                bool questMatches = string.IsNullOrWhiteSpace(item.questId) ||
                    StatusMatches(item.questId, item.requiredQuestStatus);
                bool prerequisiteMatches = string.IsNullOrWhiteSpace(item.prerequisiteQuestId) ||
                    StatusMatches(item.prerequisiteQuestId, item.prerequisiteQuestStatus);
                if (!questMatches || !prerequisiteMatches) continue;
                for (int i = 0; i < Mathf.Max(1, item.weight); i++) choices.Add(item.dialogueId);
            }
            return choices.Count == 0 ? null : choices[UnityEngine.Random.Range(0, choices.Count)];
        }

        private bool StatusMatches(string questId, string expectedStatus)
        {
            if (!Enum.TryParse(expectedStatus, true, out QuestStatus expected)) return false;
            return GameEventManager.Instance.Runtime.GetQuestStatus(questId) == expected;
        }
    }
}
