using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Cutscenes;

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
        private static Texture2D keyTexture;
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
                if (Keyboard.current.eKey.wasPressedThisFrame) Close();
                return;
            }

            if (!Keyboard.current.eKey.wasPressedThisFrame || !IsNearestInteractionTarget(player)) return;
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
            Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.6f);
            if (screen.z <= 0f) return;

            EnsureKeyTexture();
            GUIStyle box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            box.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
            GUIStyle key = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            key.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
            key.normal.background = keyTexture;
            Rect rect = new Rect(screen.x - 58f, Screen.height - screen.y - 17f, 116f, 34f);
            GUI.Box(rect, GUIContent.none, box);
            GUI.Label(new Rect(rect.x + 7f, rect.y + 5f, 24f, 24f), "E", key);
            GUI.Label(new Rect(rect.x + 35f, rect.y + 5f, 74f, 24f), "Read", box);
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
            GUIStyle panelStyle = new GUIStyle(GUI.skin.box);
            panelStyle.normal.background = PanelTexture();
            GUI.Box(panel, GUIContent.none, panelStyle);

            GUIStyle title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 19, fontStyle = FontStyle.Bold };
            title.normal.textColor = new Color(0.94f, 0.78f, 0.40f);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 30f, panel.width - 48f, 38f), "Campus Building", title);

            GUIStyle building = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 30, fontStyle = FontStyle.Bold };
            building.normal.textColor = new Color(0.95f, 0.93f, 0.87f);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 82f, panel.width - 48f, 58f), buildingName, building);

            EnsureKeyTexture();
            GUIStyle key = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 16, fontStyle = FontStyle.Bold };
            key.normal.background = keyTexture;
            key.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
            GUIStyle exit = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontSize = 17 };
            exit.normal.textColor = new Color(0.88f, 0.87f, 0.83f);
            float promptWidth = 105f;
            float promptX = panel.x + (panel.width - promptWidth) * 0.5f;
            GUI.Label(new Rect(promptX, panel.yMax - 62f, 30f, 30f), "E", key);
            GUI.Label(new Rect(promptX + 38f, panel.yMax - 62f, 72f, 30f), "Exit", exit);
        }

        private static void EnsureKeyTexture()
        {
            if (keyTexture != null) return;
            keyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            keyTexture.SetPixel(0, 0, new Color(0.93f, 0.78f, 0.36f, 1f));
            keyTexture.Apply();
        }

        private static Texture2D PanelTexture()
        {
            // A single shared background makes this overlay independent of scene UI setup.
            if (panelTexture != null) return panelTexture;
            panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            panelTexture.SetPixel(0, 0, new Color(0.055f, 0.07f, 0.09f, 0.96f));
            panelTexture.Apply();
            return panelTexture;
        }
        private static Texture2D panelTexture;
    }
}
