using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.Controls;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// <summary>Runtime presentation and interaction for Stella's five-relic toggle board.</summary>
    public sealed class StellaStringThemTogetherPuzzle : MonoBehaviour
    {
        private const string MapId = "B02.Ground";
        private const string IdPrefix = "stella.string-together.relic.";
        private const string QuestId = "stella_first_light";
        private static readonly Vector2Int[] Cells =
        {
            // Five widely separated vertices of a pentagon around the B02 hall.
            new Vector2Int(19, 19), new Vector2Int(26, 14), new Vector2Int(23, 6),
            new Vector2Int(15, 6), new Vector2Int(12, 14)
        };

        // Each switch toggles itself and the other indexed relics in its mask.
        // Switches 1, 2, 3, 4 (one-based) leave all five powered from an all-off board.
        private static readonly int[] ToggleMasks = { 0b11101, 0b11010, 0b10100, 0b01100, 0b10010 };
        private static readonly Color OffColor = new Color(0.23f, 0.27f, 0.38f);
        private static readonly Color OnColor = new Color(0.25f, 0.92f, 0.75f);

        private static StellaStringThemTogetherPuzzle instance;
        private GameObject[] relicViews;
        private Transform presentationRoot;
        private GridWorld grid;
        private WorldStateService state;
        private Camera viewCamera;

        /// <summary>Called by the area loader before EndMapRebuild so saved relic state is applied.</summary>
        public static void RegisterForMap(string mapId, GridWorld world)
        {
            if (!string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase))
            {
                if (instance != null) instance.SetPresentationVisible(false);
                return;
            }
            if (world == null) return;
            if (instance == null)
            {
                GameObject host = new GameObject("[Stella String Them Together]");
                instance = host.AddComponent<StellaStringThemTogetherPuzzle>();
                DontDestroyOnLoad(host);
            }
            instance.SetPresentationVisible(true);
            instance.RegisterObjects(world);
        }

        /// <summary>Returns the next board switch on a shortest route to lighting all five relics.</summary>
        public static bool TryGetFirstLightStep(out string instruction, out Vector2Int targetCell)
        {
            instruction = null;
            targetCell = default;
            WorldStateService worldState = WorldStateService.Instance;
            if (worldState == null || !worldState.IsReady ||
                !string.Equals(worldState.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return false;

            int board = 0;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (!worldState.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic)) return false;
                if (relic.powered) board |= 1 << i;
            }
            if (board == (1 << Cells.Length) - 1)
            {
                instruction = "All five relics are lit. Return to Stella.";
                return true;
            }

            int nextSwitch = FindNextSwitch(board);
            if (nextSwitch < 0 || !worldState.TryGetObject(ObjectId(nextSwitch), out WorldObjectSnapshot target)) return false;
            instruction = $"Next: activate relic {nextSwitch + 1}. Press F when nearby.";
            targetCell = target.cell;
            return true;
        }

        private static int FindNextSwitch(int board)
        {
            int[] firstSwitch = new int[1 << Cells.Length];
            for (int i = 0; i < firstSwitch.Length; i++) firstSwitch[i] = -1;
            firstSwitch[board] = -2;
            Queue<int> pending = new Queue<int>();
            pending.Enqueue(board);
            int goal = (1 << Cells.Length) - 1;
            while (pending.Count > 0)
            {
                int current = pending.Dequeue();
                for (int i = 0; i < ToggleMasks.Length; i++)
                {
                    int next = current ^ ToggleMasks[i];
                    if (firstSwitch[next] != -1) continue;
                    firstSwitch[next] = current == board ? i : firstSwitch[current];
                    if (next == goal) return firstSwitch[next];
                    pending.Enqueue(next);
                }
            }
            return -1;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void RegisterObjects(GridWorld world)
        {
            state = WorldStateService.Instance;
            if (state == null) return;
            grid = world;
            viewCamera = Camera.main;
            if (relicViews == null || relicViews.Length != Cells.Length) CreateViews();
            for (int i = 0; i < Cells.Length; i++)
            {
                string id = ObjectId(i);
                relicViews[i].transform.position = grid.CellToWorld(Cells[i], 0.08f);
                state.RegisterObject(id, relicViews[i], Cells[i], Vector2Int.one, false,
                    new[] { "stella-sequence", "switch:" + (i + 1) });
            }
            RefreshViews();
        }

        private void CreateViews()
        {
            relicViews = new GameObject[Cells.Length];
            Transform previousRoot = transform.Find("Relics");
            if (previousRoot != null) Destroy(previousRoot.gameObject);
            GameObject root = new GameObject("Relics");
            root.transform.SetParent(transform, false);
            presentationRoot = root.transform;
            for (int i = 0; i < Cells.Length; i++)
            {
                GameObject relic = GameObject.CreatePrimitive(PrimitiveType.Cube);
                relic.name = "Circuit Relic " + (i + 1);
                relic.transform.SetParent(root.transform, false);
                relic.transform.localScale = new Vector3(0.52f, 0.72f, 0.32f);
                Collider collider = relic.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                AddBase(relic.transform, i);
                AddLabel(relic.transform, i + 1);
                relicViews[i] = relic;
            }
        }

        private static void AddBase(Transform relic, int index)
        {
            GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseObject.name = "Socket";
            baseObject.transform.SetParent(relic, false);
            baseObject.transform.localPosition = new Vector3(0f, -0.53f, 0f);
            baseObject.transform.localScale = new Vector3(0.82f, 0.08f, 0.82f);
            Collider collider = baseObject.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            SetColor(baseObject, new Color(0.2f + index * 0.025f, 0.3f, 0.48f));
        }

        private static void AddLabel(Transform relic, int number)
        {
            GameObject label = new GameObject("Number");
            label.transform.SetParent(relic, false);
            label.transform.localPosition = new Vector3(0f, 0.58f, -0.18f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = number.ToString();
            text.fontSize = 64;
            text.characterSize = 0.12f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = Color.white;
        }

        private void Update()
        {
            state = WorldStateService.Instance;
            if (state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            if (relicViews == null) return;
            RefreshViews();
            TryCompleteIfSolved();
            if (QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || Keyboard.current == null ||
                GameEventManager.Instance?.Runtime?.ActiveDialogue != null || !KeyBindings.WasPressed(GameAction.Interact)) return;
            TryActivateNearest();
        }

        private void TryActivateNearest()
        {
            GridMover player = FindAnyObjectByType<GridMover>();
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (player == null || runtime == null || runtime.GetQuestStatus(QuestId) == QuestStatus.Completed) return;
            int nearest = -1;
            int bestDistance = 3;
            for (int i = 0; i < Cells.Length; i++)
            {
                int distance = Mathf.Abs(player.CurrentCell.x - Cells[i].x) + Mathf.Abs(player.CurrentCell.y - Cells[i].y);
                if (distance <= bestDistance) { bestDistance = distance; nearest = i; }
            }
            if (nearest < 0 || !state.TryGetObject(ObjectId(nearest), out _)) return;

            int mask = ToggleMasks[nearest];
            for (int i = 0; i < Cells.Length; i++)
            {
                if ((mask & (1 << i)) == 0 || !state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot)) continue;
                bool next = !snapshot.powered;
                state.SetProperty(ObjectId(i), item => item.powered = next);
                state.QueueEffect("stella-relic:" + (i + 1) + ":" + (next ? "on" : "off"));
            }
            RefreshViews();
            TryCompleteIfSolved();
        }

        private void TryCompleteIfSolved()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime.GetQuestStatus(QuestId) != QuestStatus.Active) return;
            for (int i = 0; i < Cells.Length; i++)
                if (!state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot) || !snapshot.powered) return;
            runtime.TriggerEvent("complete_stella_first_light");
        }

        private void RefreshViews()
        {
            if (state == null || relicViews == null) return;
            for (int i = 0; i < relicViews.Length; i++)
            {
                if (relicViews[i] == null || !state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot)) continue;
                SetColor(relicViews[i], snapshot.powered ? OnColor : OffColor);
                if (viewCamera == null) viewCamera = Camera.main;
                Transform label = relicViews[i].transform.Find("Number");
                if (label != null && viewCamera != null) label.rotation = viewCamera.transform.rotation;
            }
        }

        private void SetPresentationVisible(bool visible)
        {
            if (presentationRoot != null && presentationRoot.gameObject.activeSelf != visible)
                presentationRoot.gameObject.SetActive(visible);
        }

        private void OnGUI()
        {
            if (state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            bool[] powered = new bool[Cells.Length];
            int count = 0;
            for (int i = 0; i < powered.Length; i++)
                if (state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot) && (powered[i] = snapshot.powered)) count++;
            bool showGuidance = QuestHintOverlay.HintsEnabled && !QuestHintOverlay.IsSettingsOpen;
            GUI.Box(new Rect(18f, 18f, 420f, showGuidance ? 98f : 70f), "Stella — First Light");
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool complete = runtime != null && runtime.GetQuestStatus(QuestId) == QuestStatus.Completed;
            string instruction = complete ? "Circuit restored. Return to Stella." : "Stand near a relic and press " + KeyBindings.GetKeyLabel(GameAction.Interact) + ". Light all five.";
            GridMover player = FindAnyObjectByType<GridMover>();
            if (!complete && player != null)
            {
                int nearest = -1;
                int best = 3;
                for (int i = 0; i < Cells.Length; i++)
                {
                    int distance = Mathf.Abs(player.CurrentCell.x - Cells[i].x) + Mathf.Abs(player.CurrentCell.y - Cells[i].y);
                    if (distance <= best) { best = distance; nearest = i; }
                }
                if (nearest >= 0) instruction = "Nearby relic " + (nearest + 1) + "  •  Press " + KeyBindings.GetKeyLabel(GameAction.Interact) + " to activate";
            }
            if (showGuidance) GUI.Label(new Rect(32f, 42f, 396f, 22f), instruction);
            string status = "Power: ";
            for (int i = 0; i < powered.Length; i++) status += (i == 0 ? "" : "   ") + (i + 1) + (powered[i] ? " ●" : " ○");
            GUI.Label(new Rect(32f, showGuidance ? 68f : 44f, 396f, 22f), status + "     " + count + "/5");
        }

        private static string ObjectId(int index) => IdPrefix + (index + 1);
        private static void SetColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.material.color = color;
            renderer.material.SetColor("_EmissionColor", color * 0.18f);
            renderer.material.EnableKeyword("_EMISSION");
        }
    }
}
