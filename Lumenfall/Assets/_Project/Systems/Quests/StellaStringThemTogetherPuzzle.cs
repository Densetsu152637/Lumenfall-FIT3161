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
    /// <summary>
    /// Runtime presentation and interaction for Stella's five-relic board. During First Light the relics are
    /// switches toggled with the interact key. Once String Them Together starts they become Weave conductors:
    /// they go dark and light only while woven cables carry power to them from Stella's Ampere relic, through a
    /// ring of relay pylons that bridges the four-tile cable limit.
    /// </summary>
    public sealed class StellaStringThemTogetherPuzzle : MonoBehaviour
    {
        private const string MapId = "B02.Ground";
        private const string IdPrefix = "stella.string-together.relic.";
        private const string QuestId = "stella_first_light";
        private const string CircuitQuestId = "stella_string_them_together";
        private const string RelayPrefix = "stella.circuit.relay.";
        private const string SourceId = "relic.stella.power";
        private const string WorkbenchLinkId = "circuit.stella.link";
        private static readonly Vector2Int[] Cells =
        {
            // Five widely separated vertices of a pentagon around the B02 hall.
            new Vector2Int(19, 19), new Vector2Int(26, 14), new Vector2Int(23, 6),
            new Vector2Int(15, 6), new Vector2Int(12, 14)
        };

        // Each switch toggles itself and the other indexed relics in its mask.
        // Switches 1, 2, 3, 4 (one-based) leave all five powered from an all-off board.
        // Relays sit between neighbouring relics so every hop of the ring is under four tiles. The workbench link
        // carries power from Stella's relic to relic 4; from there the player chooses how to wire the ring.
        private static readonly Vector2Int[] RelayCells =
        {
            new Vector2Int(18, 7), new Vector2Int(21, 7), new Vector2Int(25, 9), new Vector2Int(26, 12), new Vector2Int(24, 17),
            new Vector2Int(21, 19), new Vector2Int(16, 18), new Vector2Int(13, 16), new Vector2Int(12, 11), new Vector2Int(12, 8)
        };
        private static readonly int[] ToggleMasks = { 0b11101, 0b11010, 0b10100, 0b01100, 0b10010 };
        private static readonly Color OffColor = new Color(0.23f, 0.27f, 0.38f);
        private static readonly Color OnColor = new Color(0.25f, 0.92f, 0.75f);

        private static StellaStringThemTogetherPuzzle instance;
        private GameObject[] relicViews;
        private GameObject[] relayViews;
        private Transform presentationRoot;
        private Transform relayRoot;
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

        /// <summary>Returns the next cable on a shortest woven route from the lit network to a dark relic.</summary>
        public static bool TryGetCircuitStep(out string instruction, out Vector2Int targetCell)
        {
            instruction = null;
            targetCell = default;
            WorldStateService worldState = WorldStateService.Instance;
            if (worldState == null || !worldState.IsReady ||
                !string.Equals(worldState.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase) ||
                !worldState.TryGetObject(SourceId, out WorldObjectSnapshot source)) return false;
            if (CountLitRelics(worldState) == Cells.Length) { instruction = "All five relics are lit. The circuit holds."; return true; }
            if (!source.powered)
            {
                instruction = "Power Stella's relic at the workbench with Ampere.";
                targetCell = source.cell;
                return true;
            }

            // Breadth-first search over every pair close enough to tie, starting from everything already lit.
            List<WorldObjectSnapshot> nodes = new List<WorldObjectSnapshot>();
            foreach (string id in CircuitNodeIds()) if (worldState.TryGetObject(id, out WorldObjectSnapshot node)) nodes.Add(node);
            Dictionary<string, string> previous = new Dictionary<string, string>(StringComparer.Ordinal);
            Queue<WorldObjectSnapshot> pending = new Queue<WorldObjectSnapshot>();
            foreach (WorldObjectSnapshot node in nodes) if (node.powered) { previous[node.id] = null; pending.Enqueue(node); }
            while (pending.Count > 0)
            {
                WorldObjectSnapshot current = pending.Dequeue();
                if (!current.powered && current.id.StartsWith(IdPrefix, StringComparison.Ordinal))
                {
                    // Walk back to the first dark node after the lit network: that is the next cable to tie.
                    string step = current.id;
                    while (previous[step] != null && worldState.TryGetObject(previous[step], out WorldObjectSnapshot before) && !before.powered) step = previous[step];
                    worldState.TryGetObject(step, out WorldObjectSnapshot next);
                    instruction = $"Weave from {NodeName(previous[step])} to {NodeName(step)} to carry the power on.";
                    targetCell = next.cell;
                    return true;
                }
                foreach (WorldObjectSnapshot other in nodes)
                    if (!previous.ContainsKey(other.id) && 1f / (1f + Vector2Int.Distance(current.cell, other.cell)) >= WorldStateService.MinimumCableStrength)
                    { previous[other.id] = current.id; pending.Enqueue(other); }
            }
            instruction = "Weave the relays into a ring that reaches every relic.";
            return true;
        }

        private static IEnumerable<string> CircuitNodeIds()
        {
            yield return SourceId;
            yield return WorkbenchLinkId;
            for (int i = 0; i < RelayCells.Length; i++) yield return RelayId(i);
            for (int i = 0; i < Cells.Length; i++) yield return ObjectId(i);
        }

        private static string NodeName(string id)
        {
            if (id == SourceId) return "Stella's power relic";
            if (id == WorkbenchLinkId) return "the workbench link";
            if (id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal)) return "relic " + id.Substring(IdPrefix.Length);
            return "the next relay";
        }

        private static int CountLitRelics(WorldStateService worldState)
        {
            int lit = 0;
            for (int i = 0; i < Cells.Length; i++) if (worldState.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) && relic.powered) lit++;
            return lit;
        }

        private static bool CircuitMode()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime != null && runtime.GetQuestStatus(CircuitQuestId) != QuestStatus.NotStarted;
        }

        private static string[] RelicTags(int index, bool circuit) => circuit
            ? new[] { "stella-sequence", "switch:" + (index + 1), "weave", "circuit", WorldStateService.ConductorTag }
            : new[] { "stella-sequence", "switch:" + (index + 1) };

        private static readonly string[] RelayTags = { "stella-relay", "weave", "circuit", WorldStateService.ConductorTag };

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
            if (ViewsMissing()) CreateViews();
            bool circuit = CircuitMode();
            for (int i = 0; i < Cells.Length; i++)
            {
                string id = ObjectId(i);
                relicViews[i].transform.position = grid.CellToWorld(Cells[i], 0.08f);
                state.RegisterObject(id, relicViews[i], Cells[i], Vector2Int.one, circuit, RelicTags(i, circuit));
            }
            for (int i = 0; i < RelayCells.Length; i++)
            {
                relayViews[i].transform.position = grid.CellToWorld(RelayCells[i], 0.08f);
                state.RegisterObject(RelayId(i), relayViews[i], RelayCells[i], Vector2Int.one, circuit, RelayTags);
            }
            if (relayRoot != null) relayRoot.gameObject.SetActive(circuit);
            RefreshViews();
        }

        private bool ViewsMissing()
        {
            if (relicViews == null || relicViews.Length != Cells.Length || relayViews == null || relayViews.Length != RelayCells.Length)
                return true;
            for (int i = 0; i < relicViews.Length; i++) if (relicViews[i] == null) return true;
            for (int i = 0; i < relayViews.Length; i++) if (relayViews[i] == null) return true;
            return false;
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
                GameObject authored = GameObject.Find(ObjectId(i));
                if (authored != null)
                {
                    TextMesh authoredLabel = authored.GetComponentInChildren<TextMesh>(true);
                    if (authoredLabel != null) authoredLabel.text = (i + 1).ToString();
                    relicViews[i] = authored;
                    continue;
                }
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
            GameObject relays = new GameObject("Relays");
            relays.transform.SetParent(root.transform, false);
            relayRoot = relays.transform;
            relayViews = new GameObject[RelayCells.Length];
            for (int i = 0; i < RelayCells.Length; i++)
            {
                GameObject authored = FindInactiveObject(RelayId(i));
                if (authored != null)
                {
                    relayViews[i] = authored;
                    continue;
                }
                GameObject relay = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                relay.name = "Circuit Relay " + (i + 1);
                relay.transform.SetParent(relays.transform, false);
                relay.transform.localScale = new Vector3(0.3f, 0.42f, 0.3f);
                Collider collider = relay.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                relayViews[i] = relay;
            }
        }

        private static GameObject FindInactiveObject(string objectName)
        {
            foreach (Transform candidate in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.name == objectName) return candidate.gameObject;
            return null;
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
            SyncCircuitMode();
            RefreshViews();
            TryCompleteIfSolved();
            if (QuestHintOverlay.IsSettingsOpen || Time.timeScale <= 0f || InventoryUiState.IsOpen || Keyboard.current == null ||
                GameEventManager.Instance?.Runtime?.ActiveDialogue != null || !KeyBindings.WasPressed(GameAction.Interact)) return;
            TryActivateNearest();
        }

        /// The quest can start while the player is in the room, so convert the board as soon as it does.
        private void SyncCircuitMode()
        {
            bool circuit = CircuitMode();
            if (relayRoot != null && relayRoot.gameObject.activeSelf != circuit) relayRoot.gameObject.SetActive(circuit);
            if (relayViews != null)
                for (int i = 0; i < relayViews.Length; i++)
                    if (relayViews[i] != null && relayViews[i].activeSelf != circuit) relayViews[i].SetActive(circuit);
            for (int i = 0; i < Cells.Length; i++)
            {
                int index = i;
                if (state.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) &&
                    (relic.spellCompatible != circuit || WorldStateService.IsConductor(relic) != circuit))
                    state.SetProperty(ObjectId(i), item => { item.spellCompatible = circuit; item.tags = RelicTags(index, circuit); });
            }
            for (int i = 0; i < RelayCells.Length; i++)
                if (state.TryGetObject(RelayId(i), out WorldObjectSnapshot relay) && relay.spellCompatible != circuit)
                    state.SetProperty(RelayId(i), item => item.spellCompatible = circuit);
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
            for (int i = 0; relayViews != null && i < relayViews.Length; i++)
                if (relayViews[i] != null && state.TryGetObject(RelayId(i), out WorldObjectSnapshot relay))
                    SetColor(relayViews[i], relay.powered ? OnColor : OffColor * 0.8f);
        }

        private void SetPresentationVisible(bool visible)
        {
            if (presentationRoot != null && presentationRoot.gameObject.activeSelf != visible)
                presentationRoot.gameObject.SetActive(visible);
        }

        private void OnGUI()
        {
            if (state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            GameEventRuntime progress = GameEventManager.Instance?.Runtime;
            QuestStatus circuitStatus = progress == null ? QuestStatus.NotStarted : progress.GetQuestStatus(CircuitQuestId);
            if (circuitStatus == QuestStatus.Completed) return;
            if (circuitStatus == QuestStatus.Active) { DrawCircuitPanel(); return; }
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

        private void DrawCircuitPanel()
        {
            bool showGuidance = QuestHintOverlay.HintsEnabled && !QuestHintOverlay.IsSettingsOpen;
            GUI.Box(new Rect(18f, 18f, 420f, showGuidance ? 98f : 70f), "Stella — String Them Together");
            if (showGuidance)
            {
                SpellCaster caster = FindAnyObjectByType<SpellCaster>();
                string instruction = state.TryGetObject(SourceId, out WorldObjectSnapshot source) && !source.powered
                    ? "Power Stella's relic at the workbench with Ampere."
                    : caster != null && caster.HeldThread != null
                        ? "Holding a thread from " + NodeName(caster.HeldThread) + ". Weave the next relic."
                        : "Cast Weave on a lit relic, then on the next one within four tiles.";
                GUI.Label(new Rect(32f, 42f, 396f, 22f), instruction);
            }
            string status = "Lit: ";
            for (int i = 0; i < Cells.Length; i++)
                status += (i == 0 ? "" : "   ") + (i + 1) + (state.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) && relic.powered ? " ●" : " ○");
            GUI.Label(new Rect(32f, showGuidance ? 68f : 44f, 396f, 22f), status + "     " + CountLitRelics(state) + "/5");
        }

        private static string ObjectId(int index) => IdPrefix + (index + 1);
        private static string RelayId(int index) => RelayPrefix + (index + 1);
        private static void SetColor(GameObject target, Color color)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null) renderer = target.GetComponentInChildren<Renderer>(true);
            if (renderer == null) return;
            renderer.material.color = color;
            renderer.material.SetColor("_EmissionColor", color * 0.18f);
            renderer.material.EnableKeyword("_EMISSION");
        }
    }
}
