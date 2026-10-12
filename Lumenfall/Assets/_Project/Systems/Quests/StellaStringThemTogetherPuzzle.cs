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
    /// Runtime presentation and interaction for Stella's five-relic board. The authored B02 prefab transforms
    /// are authoritative: Ampere changes the five large relics through resonance, then Weave carries their power
    /// through the small relay stones to the magical gyroscope.
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
        private const int RelicCount = 5;

        // Each switch toggles itself and the other indexed relics in its mask.
        // Switches 1, 2, 3, 4 (one-based) leave all five powered from an all-off board.
        private const int RelayCount = 10;
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
        private GameObject magicItem;
        private bool magicItemRunning;
        private bool magicItemStateApplied;
        private Vector3[] relicPositions;
        private Quaternion[] relicRotations;
        private Vector3[] relayPositions;
        private Quaternion[] relayRotations;
        private Vector3 magicItemPosition;
        private Quaternion magicItemRotation;
        private int appliedStateVersion = -1;
        private int appliedQuestSignature = -2;

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
            for (int i = 0; i < RelicCount; i++)
            {
                if (!worldState.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic)) return false;
                if (relic.powered) board |= 1 << i;
            }
            if (board == (1 << RelicCount) - 1)
            {
                instruction = "All five relics are lit. Return to Stella.";
                return true;
            }

            int nextSwitch = FindNextSwitch(board);
            if (nextSwitch < 0 || !worldState.TryGetObject(ObjectId(nextSwitch), out WorldObjectSnapshot target)) return false;
            instruction = $"Next: cast Ampere on relic {nextSwitch + 1}.";
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
                !string.Equals(worldState.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return false;
            if (CircuitNetworkComplete(worldState)) { instruction = "The whole circuit is live. Stella's magical item is moving again."; return true; }

            // Breadth-first search over every pair close enough to tie, starting from everything already lit.
            List<WorldObjectSnapshot> nodes = new List<WorldObjectSnapshot>();
            foreach (string id in CircuitNodeIds()) if (worldState.TryGetObject(id, out WorldObjectSnapshot node)) nodes.Add(node);
            Dictionary<string, string> previous = new Dictionary<string, string>(StringComparer.Ordinal);
            Queue<WorldObjectSnapshot> pending = new Queue<WorldObjectSnapshot>();
            foreach (WorldObjectSnapshot node in nodes) if (node.powered) { previous[node.id] = null; pending.Enqueue(node); }
            while (pending.Count > 0)
            {
                WorldObjectSnapshot current = pending.Dequeue();
                if (!current.powered)
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
            yield return WorkbenchLinkId;
            for (int i = 0; i < RelayCount; i++) yield return RelayId(i);
            for (int i = 0; i < RelicCount; i++) yield return ObjectId(i);
        }

        private static string NodeName(string id)
        {
            if (id == SourceId) return "Stella's power relic";
            if (id == WorkbenchLinkId) return "the magical item";
            if (id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal)) return "relic " + id.Substring(IdPrefix.Length);
            return "the next relay";
        }

        private static int CountLitRelics(WorldStateService worldState)
        {
            int lit = 0;
            for (int i = 0; i < RelicCount; i++) if (worldState.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) && relic.powered) lit++;
            return lit;
        }

        private static bool CircuitNetworkComplete(WorldStateService worldState)
        {
            if (worldState == null || !worldState.TryGetObject(WorkbenchLinkId, out WorldObjectSnapshot item) || !item.powered) return false;
            for (int i = 0; i < RelicCount; i++)
                if (!worldState.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) || !relic.powered) return false;
            for (int i = 0; i < RelayCount; i++)
                if (!worldState.TryGetObject(RelayId(i), out WorldObjectSnapshot relay) || !relay.powered) return false;
            List<string> required = new List<string> { WorkbenchLinkId };
            for (int i = 0; i < RelicCount; i++) required.Add(ObjectId(i));
            for (int i = 0; i < RelayCount; i++) required.Add(RelayId(i));
            HashSet<string> requiredSet = new HashSet<string>(required, StringComparer.Ordinal);
            HashSet<string> reached = new HashSet<string>(StringComparer.Ordinal) { required[0] };
            Queue<string> pending = new Queue<string>();
            pending.Enqueue(required[0]);
            string[] connections = worldState.SnapshotConnections();
            while (pending.Count > 0)
            {
                string current = pending.Dequeue();
                foreach (string connection in connections)
                {
                    string[] endpoints = connection.Split('|');
                    if (endpoints.Length != 2) continue;
                    string other = endpoints[0] == current ? endpoints[1] : endpoints[1] == current ? endpoints[0] : null;
                    if (other != null && requiredSet.Contains(other) && reached.Add(other)) pending.Enqueue(other);
                }
            }
            return reached.Count == required.Count;
        }

        private static bool CircuitMode()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime != null && runtime.GetQuestStatus(CircuitQuestId) != QuestStatus.NotStarted;
        }

        private static string[] RelicTags(int index, bool circuit) => circuit
            ? new[] { "stella-sequence", "switch:" + (index + 1), "ampere", "weave", "circuit" }
            : new[] { "stella-sequence", "switch:" + (index + 1), "ampere" };

        private static readonly string[] RelayTags = { "stella-relay", "weave", "circuit", WorldStateService.ConductorTag };

        private static int FindNextSwitch(int board)
        {
            int[] firstSwitch = new int[1 << RelicCount];
            for (int i = 0; i < firstSwitch.Length; i++) firstSwitch[i] = -1;
            firstSwitch[board] = -2;
            Queue<int> pending = new Queue<int>();
            pending.Enqueue(board);
            int goal = (1 << RelicCount) - 1;
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
            magicItem = FindInactiveObject("magical_gyroscope");
            if (magicItem != null)
            {
                if (state.TryGetSceneObject(WorkbenchLinkId, out GameObject placeholder) && placeholder != magicItem)
                    placeholder.SetActive(false);
                magicItemPosition = magicItem.transform.position;
                magicItemRotation = magicItem.transform.rotation;
                state.RegisterObject(WorkbenchLinkId, magicItem, grid.WorldToCell(magicItem.transform.position), Vector2Int.one, true, RelayTags);
                magicItemStateApplied = false;
            }
            if (ViewsMissing()) CreateViews();
            relicPositions = new Vector3[RelicCount];
            relicRotations = new Quaternion[RelicCount];
            relayPositions = new Vector3[RelayCount];
            relayRotations = new Quaternion[RelayCount];
            bool circuit = CircuitMode();
            for (int i = 0; i < RelicCount; i++)
            {
                string id = ObjectId(i);
                relicPositions[i] = relicViews[i].transform.position;
                relicRotations[i] = relicViews[i].transform.rotation;
                state.RegisterObject(id, relicViews[i], grid.WorldToCell(relicPositions[i]), Vector2Int.one, true, RelicTags(i, circuit));
            }
            for (int i = 0; i < RelayCount; i++)
            {
                relayPositions[i] = relayViews[i].transform.position;
                relayRotations[i] = relayViews[i].transform.rotation;
                state.RegisterObject(RelayId(i), relayViews[i], grid.WorldToCell(relayPositions[i]), Vector2Int.one, circuit, RelayTags);
            }
            if (relayRoot != null) relayRoot.gameObject.SetActive(true);
            for (int i = 0; i < relayViews.Length; i++)
                if (relayViews[i] != null) relayViews[i].SetActive(true);
            RefreshViews();
        }

        private bool ViewsMissing()
        {
            if (relicViews == null || relicViews.Length != RelicCount || relayViews == null || relayViews.Length != RelayCount)
                return true;
            for (int i = 0; i < relicViews.Length; i++) if (relicViews[i] == null) return true;
            for (int i = 0; i < relayViews.Length; i++) if (relayViews[i] == null) return true;
            return false;
        }

        private void CreateViews()
        {
            relicViews = new GameObject[RelicCount];
            Transform previousRoot = transform.Find("Relics");
            if (previousRoot != null) Destroy(previousRoot.gameObject);
            GameObject root = new GameObject("Relics");
            root.transform.SetParent(transform, false);
            presentationRoot = root.transform;
            for (int i = 0; i < RelicCount; i++)
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
            relayViews = new GameObject[RelayCount];
            for (int i = 0; i < RelayCount; i++)
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
            // The board logic snapshots and searches the circuit, so it only reruns when world state or either
            // Stella quest has changed. The version is read first so changes made by this pass get one more look.
            int version = state.Version;
            int questSignature = QuestSignature();
            if (version != appliedStateVersion || questSignature != appliedQuestSignature)
            {
                appliedStateVersion = version;
                appliedQuestSignature = questSignature;
                SyncCircuitMode();
                RefreshViews();
                RefreshMagicItem();
                TryCompleteIfSolved();
            }
            KeepAuthoredPresentation();
        }

        private static int QuestSignature()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            return runtime == null ? -1 : (int)runtime.GetQuestStatus(QuestId) * 64 + (int)runtime.GetQuestStatus(CircuitQuestId);
        }

        /// Cheap every-frame upkeep: relay stones stay visible, authored transforms and the gyroscope stay pinned
        /// against state transform syncs, and the number labels face the camera.
        private void KeepAuthoredPresentation()
        {
            // Relay stones are part of the authored room and stay visible before the Weave stage.
            // Circuit mode only changes whether they can be targeted and powered.
            if (relayRoot != null && !relayRoot.gameObject.activeSelf) relayRoot.gameObject.SetActive(true);
            if (relayViews != null)
                for (int i = 0; i < relayViews.Length; i++)
                    if (relayViews[i] != null && !relayViews[i].activeSelf) relayViews[i].SetActive(true);
            RestoreAuthoredTransforms();
            if (magicItem != null) magicItem.transform.SetPositionAndRotation(magicItemPosition, magicItemRotation);
            FaceLabelsToCamera();
        }

        private void FaceLabelsToCamera()
        {
            if (viewCamera == null) viewCamera = Camera.main;
            if (viewCamera == null) return;
            for (int i = 0; i < relicViews.Length; i++)
            {
                if (relicViews[i] == null) continue;
                Transform label = relicViews[i].transform.Find("Number");
                if (label != null) label.rotation = viewCamera.transform.rotation;
            }
        }

        /// The quest can start while the player is in the room, so convert the board as soon as it does.
        private void SyncCircuitMode()
        {
            bool circuit = CircuitMode();
            for (int i = 0; i < RelicCount; i++)
            {
                int index = i;
                if (state.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) &&
                    (!relic.spellCompatible || WorldStateService.IsConductor(relic) ||
                     Array.IndexOf(relic.tags ?? Array.Empty<string>(), circuit ? "weave" : "ampere") < 0))
                    state.SetProperty(ObjectId(i), item => { item.spellCompatible = true; item.tags = RelicTags(index, circuit); });
            }
            for (int i = 0; i < RelayCount; i++)
                if (state.TryGetObject(RelayId(i), out WorldObjectSnapshot relay) && relay.spellCompatible != circuit)
                    state.SetProperty(RelayId(i), item => item.spellCompatible = circuit);
        }

        public static bool TryApplyAmpereResonance(string targetId)
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            WorldStateService world = WorldStateService.Instance;
            if (runtime == null || world == null || runtime.GetQuestStatus(QuestId) != QuestStatus.Active ||
                string.IsNullOrEmpty(targetId) || !targetId.StartsWith(IdPrefix, StringComparison.Ordinal) ||
                !int.TryParse(targetId.Substring(IdPrefix.Length), out int number) || number < 1 || number > RelicCount) return false;
            int mask = ToggleMasks[number - 1];
            for (int i = 0; i < RelicCount; i++)
            {
                if ((mask & (1 << i)) == 0 || !world.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot)) continue;
                bool next = !snapshot.powered;
                world.SetProperty(ObjectId(i), item => item.powered = next);
                world.QueueEffect("stella-relic:" + (i + 1) + ":" + (next ? "on" : "off"));
            }
            instance?.RestoreAuthoredTransforms();
            return true;
        }

        private void TryCompleteIfSolved()
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null || runtime.GetQuestStatus(QuestId) != QuestStatus.Active) return;
            for (int i = 0; i < RelicCount; i++)
                if (!state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot) || !snapshot.powered) return;
            runtime.TriggerEvent("complete_stella_first_light");
        }

        private void RefreshViews()
        {
            if (state == null || relicViews == null) return;
            RestoreAuthoredTransforms();
            for (int i = 0; i < relicViews.Length; i++)
            {
                if (relicViews[i] == null || !state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot)) continue;
                if (!SetPoweredVisual(relicViews[i], snapshot.powered))
                    SetColor(relicViews[i], snapshot.powered ? OnColor : OffColor);
            }
            FaceLabelsToCamera();
            for (int i = 0; relayViews != null && i < relayViews.Length; i++)
                if (relayViews[i] != null && state.TryGetObject(RelayId(i), out WorldObjectSnapshot relay))
                    if (!SetPoweredVisual(relayViews[i], relay.powered))
                        SetColor(relayViews[i], relay.powered ? OnColor : OffColor * 0.8f);
        }

        private void SetPresentationVisible(bool visible)
        {
            if (presentationRoot != null && presentationRoot.gameObject.activeSelf != visible)
                presentationRoot.gameObject.SetActive(visible);
        }

        private void RefreshMagicItem()
        {
            if (magicItem == null) magicItem = FindInactiveObject("magical_gyroscope");
            if (magicItem == null) return;
            magicItem.transform.SetPositionAndRotation(magicItemPosition, magicItemRotation);
            bool completed = GameEventManager.Instance?.Runtime?.GetQuestStatus(CircuitQuestId) == QuestStatus.Completed;
            bool shouldRun = completed || (CircuitMode() && CircuitNetworkComplete(state));
            if (magicItemStateApplied && magicItemRunning == shouldRun) return;
            magicItemStateApplied = true;
            magicItemRunning = shouldRun;
            foreach (Animation animation in magicItem.GetComponentsInChildren<Animation>(true))
            {
                animation.enabled = true;
                if (shouldRun)
                {
                    bool played = animation.Play();
                    if (!played)
                        foreach (AnimationState clip in animation) { animation.Play(clip.name); break; }
                }
                else { animation.Stop(); animation.Rewind(); animation.Sample(); }
            }
            foreach (Animator animator in magicItem.GetComponentsInChildren<Animator>(true))
            {
                animator.speed = shouldRun ? 1f : 0f;
                if (!shouldRun && animator.runtimeAnimatorController != null) { animator.Play(0, 0, 0f); animator.Update(0f); }
            }
        }

        private void RestoreAuthoredTransforms()
        {
            if (relicPositions != null)
                for (int i = 0; i < relicViews.Length && i < relicPositions.Length; i++)
                    if (relicViews[i] != null) relicViews[i].transform.SetPositionAndRotation(relicPositions[i], relicRotations[i]);
            if (relayPositions != null)
                for (int i = 0; i < relayViews.Length && i < relayPositions.Length; i++)
                    if (relayViews[i] != null) relayViews[i].transform.SetPositionAndRotation(relayPositions[i], relayRotations[i]);
        }

        private void OnGUI()
        {
            if (state == null || !state.IsReady || !string.Equals(state.CurrentMapId, MapId, StringComparison.OrdinalIgnoreCase)) return;
            GameEventRuntime progress = GameEventManager.Instance?.Runtime;
            QuestStatus circuitStatus = progress == null ? QuestStatus.NotStarted : progress.GetQuestStatus(CircuitQuestId);
            if (circuitStatus == QuestStatus.Completed) return;
            if (circuitStatus == QuestStatus.Active) { DrawCircuitPanel(); return; }
            bool[] powered = new bool[RelicCount];
            int count = 0;
            for (int i = 0; i < powered.Length; i++)
                if (state.TryGetObject(ObjectId(i), out WorldObjectSnapshot snapshot) && (powered[i] = snapshot.powered)) count++;
            bool showGuidance = QuestHintOverlay.HintsEnabled && !QuestHintOverlay.IsSettingsOpen;
            GUI.Box(new Rect(18f, 18f, 420f, showGuidance ? 98f : 70f), "Stella — First Light");
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            bool complete = runtime != null && runtime.GetQuestStatus(QuestId) == QuestStatus.Completed;
            string instruction = complete ? "All five relics are glowing. Return to Stella." : "Cast Ampere on the large relics until all five glow together.";
            GridMover player = FindAnyObjectByType<GridMover>();
            if (!complete && player != null)
            {
                int nearest = -1;
                int best = 3;
            for (int i = 0; i < RelicCount; i++)
                {
                    if (!state.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic)) continue;
                    int distance = Mathf.Abs(player.CurrentCell.x - relic.cell.x) + Mathf.Abs(player.CurrentCell.y - relic.cell.y);
                    if (distance <= best) { best = distance; nearest = i; }
                }
                if (nearest >= 0) instruction = "Nearby relic " + (nearest + 1) + "  •  Select Ampere and cast to trigger its resonance";
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
                string instruction = caster != null && caster.HeldThread != null
                        ? "Holding a thread from " + NodeName(caster.HeldThread) + ". Weave the next relic."
                        : "Cast Weave on a glowing relic, then link nearby relays until the magical item is powered.";
                GUI.Label(new Rect(32f, 42f, 396f, 22f), instruction);
            }
            string status = "Lit: ";
            for (int i = 0; i < RelicCount; i++)
                status += (i == 0 ? "" : "   ") + (i + 1) + (state.TryGetObject(ObjectId(i), out WorldObjectSnapshot relic) && relic.powered ? " ●" : " ○");
            GUI.Label(new Rect(32f, showGuidance ? 68f : 44f, 396f, 22f), status + "     " + CountLitRelics(state) + "/5");
        }

        private static string ObjectId(int index) => IdPrefix + (index + 1);
        private static string RelayId(int index) => RelayPrefix + (index + 1);
        private static bool SetPoweredVisual(GameObject target, bool powered)
        {
            if (target == null) return false;
            Transform stone = target.transform.Find("PF_Runestone_Stone") ?? target.transform.Find("PF_RunestoneSmall_Stone");
            Transform glowing = target.transform.Find("PF_Runestone_Glowing") ?? target.transform.Find("PF_RunestoneSmall_Glowing");
            if (stone == null || glowing == null) return false;
            if (stone.gameObject.activeSelf == powered) stone.gameObject.SetActive(!powered);
            if (glowing.gameObject.activeSelf != powered) glowing.gameObject.SetActive(powered);
            return true;
        }

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
