using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Cutscenes;
using Lumenfall.Startup;

namespace Lumenfall.DataDriven
{
    /// <summary>Nearby campus building sign that opens a small, dismissible information card.</summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class BuildingSignInteraction : MonoBehaviour
    {
        private const float InteractionRange = 2.01f;
        private static BuildingSignInteraction activeSign;
        private static int inputBlockedUntilFrame = -1;
        // Signs on active GameObjects, so the nearest-sign check does not search the scene every frame.
        private static readonly List<BuildingSignInteraction> active = new List<BuildingSignInteraction>();

        [SerializeField] private string buildingName;
        private GridMover lockedMover;
        private bool moverWasLocked;

        public static bool IsEInteractionBlocked => activeSign != null || Time.frameCount <= inputBlockedUntilFrame;

        private void Awake()
        {
            if (string.IsNullOrWhiteSpace(buildingName))
                buildingName = gameObject.name.Replace("Sign-", "Building ");
        }

        private void Update()
        {
            if (activeSign != null && activeSign != this) return;
            if (QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || CutscenePlayer.BlocksInput ||
                DialogueOverlay.IsNpcInteractionBlocked || GameEventManager.Instance?.Runtime?.ActiveDialogue != null ||
                GridPortal.IsPlayerAtInteractivePortal || Keyboard.current == null) return;

            GridMover player = GridMover.FindActive();
            if (player == null) return;

            if (activeSign == this)
            {
                if (KeyBindings.WasPressed(GameAction.Talk)) Close();
                return;
            }

            if (!KeyBindings.WasPressed(GameAction.Talk) || !IsNearestInteractionTarget(player)) return;
            Open(player);
        }

        private void OnGUI()
        {
            if (activeSign == this)
            {
                DrawPopup();
                return;
            }

            if (activeSign != null || QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || CutscenePlayer.BlocksInput ||
                InventoryUiState.IsOpen || DialogueOverlay.IsNpcInteractionBlocked ||
                GridPortal.IsPlayerAtInteractivePortal || GameEventManager.Instance?.Runtime?.ActiveDialogue != null || Camera.main == null) return;

            GridMover player = GridMover.FindActive();
            if (player == null || !IsNearestInteractionTarget(player)) return;
            HudSkin.PromptAbove(transform.position + Vector3.up * 1.6f, KeyBindings.GetKeyLabel(GameAction.Talk), "Read");
        }

        private bool IsNearestInteractionTarget(GridMover player)
        {
            float signDistance = Vector3.Distance(player.transform.position, transform.position);
            if (signDistance > InteractionRange) return false;

            BuildingSignInteraction nearestSign = null;
            float nearestSignDistance = InteractionRange;
            foreach (BuildingSignInteraction sign in active)
            {
                float distance = Vector3.Distance(player.transform.position, sign.transform.position);
                if (distance <= nearestSignDistance) { nearestSignDistance = distance; nearestSign = sign; }
            }
            if (nearestSign != this) return false;

            DynamicNpc nearestNpc = null;
            float nearestNpcDistance = InteractionRange;
            foreach (DynamicNpc npc in DynamicNpc.Active)
            {
                float distance = Vector3.Distance(player.transform.position, npc.transform.position);
                if (distance <= nearestNpcDistance) { nearestNpcDistance = distance; nearestNpc = npc; }
            }
            return nearestNpc == null || signDistance < nearestNpcDistance;
        }

        private void Open(GridMover player)
        {
            activeSign = this;
            inputBlockedUntilFrame = Time.frameCount + 1;
            lockedMover = player;
            moverWasLocked = lockedMover.IsMovementLocked;
            lockedMover.SetMovementLocked(true);
        }

        private void Close()
        {
            activeSign = null;
            inputBlockedUntilFrame = Time.frameCount + 1;
            if (lockedMover != null) lockedMover.SetMovementLocked(moverWasLocked);
            lockedMover = null;
        }

        private void OnEnable()
        {
            if (!active.Contains(this)) active.Add(this);
        }

        private void OnDisable()
        {
            active.Remove(this);
            if (activeSign == this) Close();
        }

        private void DrawPopup()
        {
            float width = Mathf.Min(Screen.width - 40f, 560f);
            float height = Mathf.Min(Screen.height - 40f, 260f);
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            HudSkin.SignCard(panel, "Campus Building", buildingName);
            HudSkin.Prompt(new Vector2(panel.center.x, panel.yMax - 46f), KeyBindings.GetKeyLabel(GameAction.Talk), "Exit");
        }
    }
}
