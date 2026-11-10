using System.Collections.Generic;
using Lumenfall.Controls;
using Lumenfall.Cutscenes;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Spells;
using Lumenfall.Startup;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Lumenfall.Story
{
    /// <summary>
    /// Something in a secret room worth a closer look: in reach, the Talk key shows a "Look" prompt and plays a short
    /// narrated dialogue (no speaker) about it. It yields to anyone in reach to talk to, to a sign and to an item, so
    /// one key press never starts two things, and it is not a character, so the encyclopedia never lists it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoomInspectable : MonoBehaviour
    {
        private const float Reach = 1.5f;
        private static readonly List<RoomInspectable> active = new List<RoomInspectable>();

        [SerializeField] private string dialogueId;

        public string DialogueId => dialogueId;

        public void Configure(string dialogue) => dialogueId = dialogue;

        private void Awake() => useGUILayout = false;
        private void OnEnable() { if (!active.Contains(this)) active.Add(this); }
        private void OnDisable() => active.Remove(this);

        private void Update()
        {
            if (!Available(out GridMover _)) return;
            if (!(KeyBindings.WasPressed(GameAction.Talk) || Gamepad.current?.buttonSouth.wasPressedThisFrame == true)) return;
            GameEventManager.Instance.StartDialogue(dialogueId);
        }

        private void OnGUI()
        {
            using (UiScale.Begin())
                if (Available(out GridMover _)) HudSkin.PromptAbove(transform.position + Vector3.up * 1.2f, KeyBindings.GetKeyLabel(GameAction.Talk), "Look");
        }

        // In reach, the nearest thing to look at, nothing else in reach to use, and nothing else on screen.
        private bool Available(out GridMover player)
        {
            player = null;
            if (string.IsNullOrEmpty(dialogueId) || GameEventManager.Instance?.Runtime == null || GameEventManager.Instance.Runtime.ActiveDialogue != null ||
                OverlayInputGate.IsBlocked || QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || CutscenePlayer.BlocksInput ||
                CinnaTrialDeparture.InProgress || DialogueOverlay.IsNpcInteractionBlocked || BuildingSignInteraction.IsEInteractionBlocked ||
                GridPortal.IsPlayerAtInteractivePortal || WorldPickupInteraction.HasTargetInReach || StartupMenu.IsOpen) return false;
            player = GridMover.FindActive();
            if (player == null) return false;
            Vector3 from = player.transform.position;
            float distance = Flat(from, transform.position);
            if (distance > Reach) return false;
            foreach (RoomInspectable other in active)
                if (other != this && Flat(from, other.transform.position) < distance) return false;
            // Anyone close enough to talk to takes the key instead.
            foreach (DynamicNpc npc in DynamicNpc.Active)
                if (Vector3.Distance(from, npc.transform.position) <= 2.01f) return false;
            return true;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}
