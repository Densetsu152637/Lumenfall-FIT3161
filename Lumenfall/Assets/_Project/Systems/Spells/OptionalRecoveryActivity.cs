using System;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.Inventory;
using UnityEngine;

namespace Lumenfall.Spells
{
    /// Deterministic optional rules. Mutable progress lives in existing event flags and world snapshots.
    public sealed class OptionalRecoveryRules
    {
        public const string SearchActiveFlag = "optional.recovery.search-active";
        public const string StartedFlag = "optional.recovery.started";
        public const string RewardFlag = "optional.recovery.reward-claimed";
        public const string InventoryFlag = "inventory.keyitem.optional-recovered-item";
        public const string TargetOne = "optional.recovery.item.1";
        public const string TargetTwo = "optional.recovery.item.2";
        private static readonly string[] Targets = { TargetOne, TargetTwo };
        private readonly GameEventRuntime runtime;
        private readonly WorldStateService world;
        private readonly string mapId;
        private readonly Vector2Int firstCell, secondCell;
        public OptionalRecoveryRules(GameEventRuntime runtime, WorldStateService world, string mapId, Vector2Int firstCell, Vector2Int secondCell)
        { this.runtime = runtime; this.world = world; this.mapId = mapId; this.firstCell = firstCell; this.secondCell = secondCell; }
        private bool OnMap => world != null && world.IsReady && !string.IsNullOrWhiteSpace(mapId) && string.Equals(world.CurrentMapId, mapId, StringComparison.OrdinalIgnoreCase);
        private bool HasSpell => runtime != null && runtime.GetFlag("spell.unlocked.LostEchoes");
        public bool Completed => runtime != null && runtime.GetFlag(RewardFlag);
        public string Status(bool enabled)
        {
            if (!enabled) return "Optional recovery is disabled.";
            if (Completed) return "Recovery complete. Recovered item saved; no replay reward.";
            if (!HasSpell) return "Learn Lost Echoes through its lesson before starting this optional search.";
            if (!OnMap) return "Visit " + mapId + " to start the optional search.";
            if (!TargetsReady()) return "Search locations are unavailable. Check the configured cells.";
            if (!runtime.GetFlag(SearchActiveFlag)) return "Begin an optional search for a neutral recovered item.";
            int count = 0;
            foreach (string id in Targets) if (world.TryGetObject(id, out WorldObjectSnapshot target) && target.revealed) count++;
            return "Cast Lost Echoes within two grid cells of each search location: " + count + "/2 found.";
        }
        public bool TryBegin(bool enabled)
        {
            if (!enabled || Completed || !HasSpell || !OnMap || !TargetsReady()) return false;
            runtime.SetFlag(StartedFlag, true);
            runtime.SetFlag(SearchActiveFlag, true);
            return true;
        }
        public bool Observe(bool enabled)
        {
            if (!enabled || Completed || !HasSpell || !OnMap || !TargetsReady() || !runtime.GetFlag(StartedFlag) || !runtime.GetFlag(SearchActiveFlag)) return false;
            bool all = true;
            foreach (string id in Targets)
            {
                world.TryGetObject(id, out WorldObjectSnapshot target);
                runtime.SetFlag(ProgressFlag(id), target.revealed);
                all &= target.revealed;
            }
            if (!all) return false;
            // Set the durable receipt first. Repeated observations cannot issue a second reward.
            runtime.SetFlag(RewardFlag, true);
            runtime.SetFlag(InventoryFlag, true);
            runtime.SetFlag(SearchActiveFlag, false);
            return true;
        }
        public bool TryResetSearch(bool enabled)
        {
            if (!enabled || Completed || !OnMap || !TargetsReady() || !runtime.GetFlag(StartedFlag)) return false;
            foreach (string id in Targets)
            {
                runtime.SetFlag(ProgressFlag(id), false);
                world.SetProperty(id, target => target.revealed = false);
            }
            return true;
        }
        public void RestoreSearch()
        {
            if (!OnMap || !TargetsReady()) return;
            foreach (string id in Targets)
            {
                bool revealed = runtime.GetFlag(ProgressFlag(id));
                if (world.TryGetObject(id, out WorldObjectSnapshot target) && target.revealed != revealed)
                    world.SetProperty(id, value => value.revealed = revealed);
            }
        }
        public void Suspend() { runtime?.SetFlag(SearchActiveFlag, false); }
        public static bool IsTargetId(string id) => id == TargetOne || id == TargetTwo;
        public static string ProgressFlag(string id) => "optional.recovery.found." + id;
        private bool TargetsReady()
        {
            foreach (string id in Targets)
                if (!world.TryGetObject(id, out WorldObjectSnapshot target) || !target.spellCompatible ||
                    !string.Equals(target.mapId, mapId, StringComparison.OrdinalIgnoreCase) || target.cell != (id == TargetOne ? firstCell : secondCell) ||
                    !WorldStateService.HasTag(target, "optional-recovery") || !WorldStateService.HasTag(target, "hidden") || !WorldStateService.HasTag(target, "lost-echoes")) return false;
            return true;
        }
    }

    /// Disabled by default, bounded preview bridge. Does not author or replace main WorldArea content.
    [DisallowMultipleComponent]
    public sealed class OptionalRecoveryActivity : MonoBehaviour
    {
        [SerializeField] private bool activityEnabled;
        [SerializeField] private string activityMapId = "campus";
        [SerializeField] private Vector2Int firstSearchCell = new Vector2Int(30, 40);
        [SerializeField] private Vector2Int secondSearchCell = new Vector2Int(34, 40);
        private GameEventRuntime runtime;
        private WorldStateService world;
        private OptionalRecoveryRules rules;
        private readonly GameObject[] markers = new GameObject[2];
        private readonly Renderer[] markerRenderers = new Renderer[2];
        private string registeredMap;
        private bool synchronizing;
        private float nextCheck;
        public bool ActivityEnabled => activityEnabled;
        public string Status => rules == null ? (activityEnabled ? "Waiting for world state." : "Optional recovery is disabled.") : rules.Status(activityEnabled);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (FindAnyObjectByType<OptionalRecoveryActivity>() != null) return;
            GameObject host = new GameObject("[OptionalRecoveryActivity]");
            host.AddComponent<OptionalRecoveryActivity>();
            DontDestroyOnLoad(host);
        }
        public void SetActivityEnabled(bool enabled)
        {
            activityEnabled = enabled;
            if (!enabled) { rules?.Suspend(); RetireMarkers(); }
            else Bind();
        }
        public bool BeginSearch()
        {
            if (!activityEnabled || Time.timeScale <= 0f || InventoryUiState.IsOpen || GameEventManager.Instance.Runtime.ActiveDialogue != null) return false;
            Bind(); EnsureTargets();
            bool begun = rules != null && rules.TryBegin(activityEnabled);
            RefreshPresentation(); return begun;
        }
        public bool ResetPartialSearch()
        {
            if (!activityEnabled || Time.timeScale <= 0f || InventoryUiState.IsOpen || runtime?.ActiveDialogue != null || rules == null) return false;
            synchronizing = true;
            bool reset = rules.TryResetSearch(activityEnabled);
            synchronizing = false; RefreshPresentation(); return reset;
        }
        private void Update()
        {
            if (!activityEnabled || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.2f;
            Bind();
            if (world == null || !world.IsReady) return;
            if (!string.Equals(world.CurrentMapId, activityMapId, StringComparison.OrdinalIgnoreCase)) { RetireMarkers(); return; }
            if (runtime.GetFlag(OptionalRecoveryRules.StartedFlag)) EnsureTargets();
            rules?.Observe(activityEnabled); RefreshPresentation();
        }
        private void Bind()
        {
            WorldStateService currentWorld = WorldStateService.Instance;
            GameEventRuntime currentRuntime = GameEventManager.Instance.Runtime;
            if (world == currentWorld && runtime == currentRuntime && rules != null) return;
            RetireMarkers(); Unbind(); world = currentWorld; runtime = currentRuntime;
            rules = new OptionalRecoveryRules(runtime, world, activityMapId, firstSearchCell, secondSearchCell);
            if (world != null) { world.StateChanged += OnWorldChanged; world.MapRebuilt += OnMapRebuilt; }
            runtime.StateRestored += OnRestored;
        }
        private void Unbind()
        {
            if (world != null) { world.StateChanged -= OnWorldChanged; world.MapRebuilt -= OnMapRebuilt; }
            if (runtime != null) runtime.StateRestored -= OnRestored;
        }
        private void OnWorldChanged(MapStateSnapshot snapshot)
        { if (!synchronizing && activityEnabled) { rules?.Observe(true); RefreshPresentation(); } }
        private void OnMapRebuilt(MapRebuildEvent rebuilt)
        {
            registeredMap = null; RetireMarkers();
            if (activityEnabled && runtime.GetFlag(OptionalRecoveryRules.StartedFlag)) EnsureTargets();
        }
        private void OnRestored()
        {
            // Event state is restored before world providers. Recreate only after their map is ready.
            if (!activityEnabled) return;
            synchronizing = true; rules?.RestoreSearch(); synchronizing = false;
            RefreshPresentation();
        }
        private void EnsureTargets()
        {
            if (!activityEnabled || world == null || !world.IsReady || !string.Equals(world.CurrentMapId, activityMapId, StringComparison.OrdinalIgnoreCase) ||
                !runtime.GetFlag("spell.unlocked.LostEchoes")) return;
            if (registeredMap == world.CurrentMapId && markers[0] != null && markers[1] != null) return;
            GridWorld grid = FindAnyObjectByType<GridWorld>();
            if (grid == null || firstSearchCell == secondSearchCell || !grid.IsCellAvailable(firstSearchCell) || !grid.IsCellAvailable(secondSearchCell) ||
                grid.GetPortalAt(firstSearchCell) != null || grid.GetPortalAt(secondSearchCell) != null) return;
            RetireMarkers(); synchronizing = true;
            string[] ids = { OptionalRecoveryRules.TargetOne, OptionalRecoveryRules.TargetTwo };
            Vector2Int[] cells = { firstSearchCell, secondSearchCell };
            for (int i = 0; i < ids.Length; i++)
            {
                markers[i] = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                markers[i].name = "Optional recovery search " + (i + 1);
                markers[i].transform.SetParent(transform);
                markers[i].transform.position = grid.CellToWorld(cells[i], 0.25f);
                markers[i].transform.localScale = Vector3.one * 0.35f;
                // Pure visual placeholders: never add a blocker, quest trigger or pickup collider.
                Collider collider = markers[i].GetComponent<Collider>(); if (collider != null) collider.enabled = false;
                markerRenderers[i] = markers[i].GetComponent<Renderer>();
                world.RegisterObject(ids[i], markers[i], cells[i], Vector2Int.one, true, new[] { "hidden", "lost-echoes", "optional-recovery" });
            }
            registeredMap = world.CurrentMapId; rules.RestoreSearch(); synchronizing = false;
            RefreshPresentation();
        }
        private void RefreshPresentation()
        {
            if (world == null) return;
            string[] ids = { OptionalRecoveryRules.TargetOne, OptionalRecoveryRules.TargetTwo };
            for (int i = 0; i < ids.Length; i++)
                if (markerRenderers[i] != null && world.TryGetObject(ids[i], out WorldObjectSnapshot target)) markerRenderers[i].enabled = target.revealed;
        }
        private void RetireMarkers()
        {
            if (world != null && string.Equals(registeredMap, world.CurrentMapId, StringComparison.OrdinalIgnoreCase))
            {
                foreach (string id in new[] { OptionalRecoveryRules.TargetOne, OptionalRecoveryRules.TargetTwo })
                    if (world.TryGetObject(id, out WorldObjectSnapshot target) && WorldStateService.HasTag(target, "optional-recovery"))
                        world.UnregisterRecoveryTarget(id);
            }
            registeredMap = null;
            for (int i = 0; i < markers.Length; i++)
            { if (markers[i] != null) { markers[i].SetActive(false); DisposeMarker(markers[i]); } markers[i] = null; markerRenderers[i] = null; }
        }
        private static void DisposeMarker(GameObject marker)
        { if (Application.isPlaying) Destroy(marker); else DestroyImmediate(marker); }
        private void OnDisable() { rules?.Suspend(); Unbind(); RetireMarkers(); world = null; runtime = null; rules = null; }
        private void OnGUI()
        {
            if (!activityEnabled || Time.timeScale <= 0f || InventoryUiState.IsOpen || runtime?.ActiveDialogue != null) return;
            GUILayout.BeginArea(new Rect(16f, 100f, 360f, 200f), GUI.skin.box);
            GUILayout.Label("Optional item recovery (preview)");
            GUILayout.Label(Status);
            if (world != null && string.Equals(world.CurrentMapId, activityMapId, StringComparison.OrdinalIgnoreCase))
            {
                GUILayout.Label("Search locations: " + firstSearchCell + " and " + secondSearchCell);
                if (GUILayout.Button("Begin / resume search")) BeginSearch();
                if (GUILayout.Button("Reset partial search")) ResetPartialSearch();
            }
            GUILayout.EndArea();
        }
    }
}
