using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.Controls;
using Lumenfall.DataDriven;
using Lumenfall.GameEvents;
using Lumenfall.Inventory;
using Lumenfall.Quests;
using Lumenfall.Startup;
using UnityEngine.Serialization;
using Lumenfall.Cutscenes;

namespace Lumenfall.GridMovement
{
    /// <summary>
    /// A non-blocking, rectangular set of tiles that transfers a mover after it finishes entering.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class GridPortal : MonoBehaviour, ISerializationCallbackReceiver
    {
        [Header("Entrance")]
        [SerializeField] private GridWorld world;
        [Tooltip("Portal cells extending from the centre towards local grid +X. Zero includes only the centre cell.")]
        [SerializeField, Min(0)] private int extendPositiveX;
        [Tooltip("Portal cells extending from the centre towards local grid -X.")]
        [SerializeField, Min(0)] private int extendNegativeX;
        [Tooltip("Portal cells extending from the centre towards local grid +Z (grid Y).")]
        [SerializeField, Min(0)] private int extendPositiveZ;
        [Tooltip("Portal cells extending from the centre towards local grid -Z (grid Y).")]
        [SerializeField, Min(0)] private int extendNegativeZ;

        [FormerlySerializedAs("footprintSize")]
        [SerializeField, HideInInspector] private Vector2Int legacyFootprintSize;

        [Header("Connection")]
        [SerializeField] private PortalContentAsset content;
        [Tooltip("When enabled, preserve the player's relative position inside this portal footprint at the destination.")]
        [SerializeField] private bool preserveEntryOffset = true;

        [Header("Editor Preview")]
        [SerializeField] private Color gizmoColour = new Color(0.65f, 0.15f, 0.95f, 0.55f);
        [SerializeField, Range(0.05f, 1f)] private float gizmoFillOpacity = 0.22f;
        private static Texture2D interactionKeyTexture;

        public GridWorld World => world;
        public Vector2Int CentreCell => world == null ? Vector2Int.zero : world.WorldToCell(transform.position);
        public Vector2Int AnchorCell => CentreCell;
        public Vector2Int FirstCell => CentreCell - new Vector2Int(extendNegativeX, extendNegativeZ);
        public Vector2Int Cell => CentreCell;
        public Vector2Int FootprintSize => new Vector2Int(
            extendNegativeX + 1 + extendPositiveX,
            extendNegativeZ + 1 + extendPositiveZ);
        public int ExtendPositiveX => extendPositiveX;
        public int ExtendNegativeX => extendNegativeX;
        public int ExtendPositiveZ => extendPositiveZ;
        public int ExtendNegativeZ => extendNegativeZ;
        public string PortalId => content == null ? string.Empty : content.id;
        public PortalContentAsset Content => content;
        public bool IsBlocked => content != null && content.blocked;
        public static bool IsPlayerAtInteractivePortal
        {
            get
            {
                GridMover player = GridMover.FindActive();
                if (player == null || player.World == null || player.IsMoving || player.IsMovementLocked) return false;
                GridPortal portal = player.World.GetPortalAt(player.CurrentCell);
                return portal != null && portal.CanOfferInteraction(player);
            }
        }

        public void Configure(PortalContentAsset portalContent, GridWorld targetWorld)
        {
            content = portalContent;
            world = targetWorld;
            if (isActiveAndEnabled && world != null) world.RegisterPortal(this, EnumeratePortalCells());
        }

        private void Reset()
        {
            world = GetComponentInParent<GridWorld>();
        }

        private void OnEnable()
        {
            ResolveWorld();
            if (world == null)
            {
                Debug.LogError($"{name} needs a GridWorld to register its portal tile.", this);
                return;
            }

            world.RegisterPortal(this, EnumeratePortalCells());
        }

        private void OnDisable()
        {
            if (world != null)
            {
                world.UnregisterPortal(this);
            }
        }

        public bool TryEnter(GridMover mover)
        {
            if (mover == null || mover.IsMoving || mover.IsMovementLocked || mover.World != world ||
                !ContainsCell(mover.CurrentCell))
            {
                return false;
            }

            if (content != null && content.disabled)
            {
                return false;
            }

            if (content != null && content.blocked)
            {
                PortalBlockedPopup.Show("The door is locked");
                return false;
            }

            if (content == null || content.destination == null || DataDrivenWorldLoader.Instance == null)
            {
                Debug.LogError($"{name} needs a valid Portal Content asset with a destination.", this);
                return false;
            }

            Vector2Int entryOffset = preserveEntryOffset ? mover.CurrentCell - CentreCell : Vector2Int.zero;
            return DataDrivenWorldLoader.Instance.SwitchArea(content, entryOffset);
        }

        public bool ContainsCell(Vector2Int candidate)
        {
            Vector2Int first = FirstCell;
            Vector2Int size = FootprintSize;
            return candidate.x >= first.x && candidate.y >= first.y &&
                   candidate.x < first.x + size.x &&
                   candidate.y < first.y + size.y;
        }

        public IEnumerable<Vector2Int> EnumeratePortalCells()
        {
            for (int x = -extendNegativeX; x <= extendPositiveX; x++)
            {
                for (int z = -extendNegativeZ; z <= extendPositiveZ; z++)
                {
                    yield return CentreCell + new Vector2Int(x, z);
                }
            }
        }

        public Vector2Int GetArrivalCell(Vector2Int sourceEntryOffset)
        {
            Vector2Int mappedOffset = new Vector2Int(
                Mathf.Clamp(sourceEntryOffset.x, -extendNegativeX, extendPositiveX),
                Mathf.Clamp(sourceEntryOffset.y, -extendNegativeZ, extendPositiveZ));
            return CentreCell + mappedOffset;
        }

        public void RefreshFootprint()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            ResolveWorld();
            if (world != null)
            {
                world.RegisterPortal(this, EnumeratePortalCells());
            }
        }


        private void ResolveWorld()
        {
            if (world == null)
            {
                world = GetComponentInParent<GridWorld>();
            }

            if (world == null)
            {
                world = FindAnyObjectByType<GridWorld>();
            }
        }

        private void OnValidate()
        {
            MigrateLegacyFootprint();
            extendPositiveX = Mathf.Max(0, extendPositiveX);
            extendNegativeX = Mathf.Max(0, extendNegativeX);
            extendPositiveZ = Mathf.Max(0, extendPositiveZ);
            extendNegativeZ = Mathf.Max(0, extendNegativeZ);

            if (Application.isPlaying && isActiveAndEnabled)
            {
                RefreshFootprint();
            }
        }

        public void OnBeforeSerialize()
        {
        }

        public void OnAfterDeserialize()
        {
            MigrateLegacyFootprint();
        }

        private void MigrateLegacyFootprint()
        {
            if (legacyFootprintSize.x <= 0 && legacyFootprintSize.y <= 0)
            {
                return;
            }

            // The old footprint expanded from its first cell towards +X/+Z. Keeping that cell as
            // the new centre preserves all existing authored portal cells and offset mappings.
            extendPositiveX = legacyFootprintSize.x > 1 ? legacyFootprintSize.x - 1 : 0;
            extendPositiveZ = legacyFootprintSize.y > 1 ? legacyFootprintSize.y - 1 : 0;
            legacyFootprintSize = Vector2Int.zero;
        }

        private void OnDrawGizmos()
        {
            ResolveWorld();
            if (world == null)
            {
                return;
            }

            Vector2Int first = FirstCell;
            Vector2Int size = FootprintSize;
            Vector2Int last = first + size - Vector2Int.one;
            Vector3 firstWorld = world.CellToWorld(first, 0.06f);
            Vector3 lastWorld = world.CellToWorld(last, 0.06f);
            Vector3 centreWorld = world.CellToWorld(CentreCell, 0.06f);
            float cellSize = Mathf.Max(0.01f, world.CellSize);
            Vector3 footprintCentre = (firstWorld + lastWorld) * 0.5f;
            Vector3 dimensions = new Vector3(size.x * cellSize, 0.08f, size.y * cellSize);

            Color fill = gizmoColour;
            fill.a = Mathf.Clamp01(gizmoFillOpacity);
            Gizmos.color = fill;
            Gizmos.DrawCube(footprintCentre, dimensions);

            Color outline = gizmoColour;
            outline.a = Mathf.Max(outline.a, 0.9f);
            Gizmos.color = outline;
            Gizmos.DrawWireCube(footprintCentre, dimensions);

            // The purple sphere is the portal transmission centre and zero-offset arrival point.
            Gizmos.DrawSphere(centreWorld + Vector3.up * 0.12f, cellSize * 0.12f);

        }

        private void Update()
        {
            GridMover player = GridMover.FindActive();
            if (!CanOfferInteraction(player) || InputBlocked()) return;
            if (KeyBindings.WasPressed(GameAction.Talk)) TryEnter(player);
        }

        private void OnGUI()
        {
            if (InputBlocked() || Camera.main == null) return;
            GridMover player = GridMover.FindActive();
            if (!CanOfferInteraction(player)) return;

            Vector3 screen = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.up * 1.6f);
            if (screen.z <= 0f) return;
            GUIStyle box = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 14 };
            box.normal.textColor = new Color(0.95f, 0.92f, 0.84f);
            GUIStyle key = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontSize = 13 };
            key.normal.textColor = new Color(0.10f, 0.11f, 0.13f);
            key.normal.background = InteractionKeyTexture();
            Rect rect = new Rect(screen.x - 58f, Screen.height - screen.y - 17f, 116f, 34f);
            GUI.Box(rect, GUIContent.none, box);
            GUI.Label(new Rect(rect.x + 7f, rect.y + 5f, 24f, 24f), KeyBindings.GetKeyLabel(GameAction.Talk), key);
            GUI.Label(new Rect(rect.x + 35f, rect.y + 5f, 74f, 24f), "Enter", box);
        }

        private bool CanOfferInteraction(GridMover player)
        {
            return isActiveAndEnabled && player != null && !player.IsMoving && !player.IsMovementLocked &&
                player.World == world && world != null && world.GetPortalAt(player.CurrentCell) == this &&
                ContainsCell(player.CurrentCell) && content != null && !content.disabled;
        }

        private static bool InputBlocked()
        {
            return QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen ||
                DialogueOverlay.IsNpcInteractionBlocked || BuildingSignInteraction.IsEInteractionBlocked ||
                GameEventManager.Instance?.Runtime?.ActiveDialogue != null || PortalBlockedPopup.IsVisible ||
                CutscenePlayer.BlocksInput;
        }

        private static Texture2D InteractionKeyTexture()
        {
            if (interactionKeyTexture != null) return interactionKeyTexture;
            interactionKeyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            interactionKeyTexture.SetPixel(0, 0, new Color(0.93f, 0.78f, 0.36f, 1f));
            interactionKeyTexture.Apply();
            return interactionKeyTexture;
        }
    }

    /// <summary>A brief portal-entry notice using the same generated skin as the menu and options UI.</summary>
    [DisallowMultipleComponent]
    public sealed class PortalBlockedPopup : MonoBehaviour
    {
        private const float VisibleSeconds = 2.4f;
        private const float FadeSeconds = 0.2f;
        private static PortalBlockedPopup instance;

        private readonly MenuSkin skin = new MenuSkin();
        private string message = string.Empty;
        private float shownAt;
        private float hideAt;

        public static event Action<string> Shown;
        public static bool IsVisible => instance != null && Time.unscaledTime < instance.hideAt;

        public static void Show(string value)
        {
            string text = string.IsNullOrWhiteSpace(value) ? "The door is locked" : value;
            Shown?.Invoke(text);
            if (!Application.isPlaying) return;

            if (instance == null)
            {
                GameObject host = new GameObject("Portal Blocked Popup");
                DontDestroyOnLoad(host);
                instance = host.AddComponent<PortalBlockedPopup>();
            }

            instance.message = text;
            instance.shownAt = Time.unscaledTime;
            instance.hideAt = instance.shownAt + VisibleSeconds;
        }

        private void Update()
        {
            if (!IsVisible) return;
            skin.Tick(Time.unscaledDeltaTime);
        }

        private void OnGUI()
        {
            if (!IsVisible) return;
            skin.Ensure();

            float now = Time.unscaledTime;
            float fadeIn = Mathf.Clamp01((now - shownAt) / FadeSeconds);
            float fadeOut = Mathf.Clamp01((hideAt - now) / FadeSeconds);
            skin.Fade = Mathf.Min(fadeIn, fadeOut);

            float width = Mathf.Min(Screen.width - 32f, 460f);
            float height = 170f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            int previousDepth = GUI.depth;
            GUI.depth = -1000;
            skin.PanelFrame(panel, now);
            skin.DrawTitle(new Rect(panel.x + 28f, panel.y + 22f, panel.width - 56f, 48f), "Locked", now);
            skin.Divider(panel.center.x, panel.y + 82f, panel.width - 80f, now);
            GUIStyle messageStyle = new GUIStyle(skin.Body)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 19
            };
            Color previousColour = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, skin.Fade);
            GUI.Label(new Rect(panel.x + 30f, panel.y + 94f, panel.width - 60f, 44f), message, messageStyle);
            GUI.color = previousColour;
            skin.DrawSparks(skin.Fade);
            GUI.depth = previousDepth;
        }

        private void OnDestroy()
        {
            skin.Dispose();
            if (instance == this) instance = null;
        }
    }
}
