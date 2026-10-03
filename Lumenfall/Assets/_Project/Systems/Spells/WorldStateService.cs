using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;
using Lumenfall.DataDriven;

namespace Lumenfall.Spells
{
    public enum SpellId { Ampere, Weave, Gravitate, Rotara, Glaciate, Vis, LostEchoes }

    [Serializable] public sealed class WorldObjectSnapshot
    {
        public string id; public string mapId; public Vector2Int cell; public Vector2Int size = Vector2Int.one;
        public float elevation; public float rotation; public bool powered; public bool frozen; public bool revealed;
        public bool aligned; public bool spellCompatible = true; public string[] tags = Array.Empty<string>();
    }
    [Serializable] public sealed class MapStateSnapshot { public string mapId; public WorldObjectSnapshot[] objects = Array.Empty<WorldObjectSnapshot>(); public string[] connections = Array.Empty<string>(); public float[] connectionStrengths = Array.Empty<float>(); }
    public sealed class MapRebuildEvent { public readonly string MapId; public MapRebuildEvent(string mapId) { MapId = mapId; } }

    /// Authoritative mutable state for puzzle objects. Quest monitors can subscribe without holding scene references.
    [DefaultExecutionOrder(-300)]
    public sealed class WorldStateService : MonoBehaviour, ISaveStateProvider
    {
        public static WorldStateService Instance { get; private set; }
        private readonly Dictionary<string, WorldEntry> entries = new Dictionary<string, WorldEntry>(StringComparer.Ordinal);
        private readonly HashSet<string> connections = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> connectionStrengths = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly List<string> pendingEffects = new List<string>();
        private readonly Dictionary<string, MapStateSnapshot> archivedMaps = new Dictionary<string, MapStateSnapshot>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<string>> archivedEffects = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private PuzzleTargetDefinition[] puzzleTargets = Array.Empty<PuzzleTargetDefinition>();
        private MapStateSnapshot pendingRestore;
        private bool rebuilding;
        public string CurrentMapId { get; private set; }
        public bool IsReady { get; private set; }
        public string SaveKey => "spell-world-state";
        public event Action<MapStateSnapshot> StateChanged;
        public event Action<MapRebuildEvent> MapRebuilt;
        public event Action<string> EffectQueued;
        public PuzzleTargetDefinition[] PuzzleTargets => puzzleTargets;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void Bootstrap()
        { if (FindAnyObjectByType<WorldStateService>() == null) new GameObject("[WorldStateService]").AddComponent<WorldStateService>(); }
        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this; DontDestroyOnLoad(gameObject); GameSaveManager.Instance.RegisterProvider(this);
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        public void BeginMapRebuild(string mapId)
        {
            if (IsReady && !string.IsNullOrWhiteSpace(CurrentMapId)) { archivedMaps[CurrentMapId] = CaptureSnapshot(); archivedEffects[CurrentMapId] = new List<string>(pendingEffects); }
            rebuilding = true; IsReady = false; CurrentMapId = mapId ?? string.Empty; entries.Clear(); connections.Clear(); connectionStrengths.Clear(); pendingEffects.Clear(); pendingRestore = null; puzzleTargets = Array.Empty<PuzzleTargetDefinition>();
            if (archivedMaps.TryGetValue(CurrentMapId, out MapStateSnapshot saved)) pendingRestore = saved;
            if (archivedEffects.TryGetValue(CurrentMapId, out List<string> effects)) pendingEffects.AddRange(effects);
        }
        public void ResetAllState() { archivedMaps.Clear(); archivedEffects.Clear(); entries.Clear(); connections.Clear(); connectionStrengths.Clear(); pendingEffects.Clear(); pendingRestore = null; CurrentMapId = string.Empty; IsReady = false; }
        public void RegisterPuzzleTargets(PuzzleTargetDefinition[] definitions) { puzzleTargets = definitions ?? Array.Empty<PuzzleTargetDefinition>(); }
        public void RegisterObject(string id, GameObject sceneObject, Vector2Int cell, Vector2Int size, bool compatible, string[] tags)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            entries[id] = new WorldEntry { sceneObject = sceneObject, state = new WorldObjectSnapshot { id = id, mapId = CurrentMapId, cell = cell, size = size, elevation = sceneObject == null ? 0 : sceneObject.transform.position.y, rotation = sceneObject == null ? 0 : sceneObject.transform.eulerAngles.y, spellCompatible = compatible, tags = tags ?? Array.Empty<string>() } };
        }
        public void EndMapRebuild()
        {
            rebuilding = false; IsReady = true;
            if (pendingRestore != null && string.Equals(pendingRestore.mapId, CurrentMapId, StringComparison.OrdinalIgnoreCase)) { ApplySnapshot(pendingRestore); pendingRestore = null; }
            MapRebuilt?.Invoke(new MapRebuildEvent(CurrentMapId)); NotifyChanged();
        }
        public bool TryGetObject(string id, out WorldObjectSnapshot snapshot) { if (entries.TryGetValue(id ?? string.Empty, out WorldEntry item)) { snapshot = Clone(item.state); return true; } snapshot = null; return false; }
        public WorldObjectSnapshot[] SnapshotObjects() { var result = new WorldObjectSnapshot[entries.Count]; int i = 0; foreach (WorldEntry e in entries.Values) result[i++] = Clone(e.state); return result; }
        public bool IsConnected(string a, string b) => connections.Contains(ConnectionKey(a, b));
        public float GetConnectionStrength(string a, string b) { connectionStrengths.TryGetValue(ConnectionKey(a, b), out float value); return value; }
        public bool EvaluatePuzzleTarget(PuzzleTargetDefinition target)
        {
            if (target == null || target.objectIds == null || target.objectIds.Length == 0) return false;
            foreach (string requiredConnection in target.requiredConnections ?? Array.Empty<string>())
            {
                string[] endpoints = (requiredConnection ?? string.Empty).Split('|');
                if (endpoints.Length != 2 || !IsConnected(endpoints[0], endpoints[1])) return false;
            }
            bool any = false, all = true;
            foreach (string id in target.objectIds) { if (!TryGetObject(id, out WorldObjectSnapshot s)) { all = false; continue; } bool ok = (!target.requiredPowered || s.powered) && (!target.requiredConnected || IsConnectedToAny(id)) && (!target.requiredRevealed || s.revealed) && (!target.requiredFrozen || s.frozen) && (!target.requiredAligned || s.aligned) && (target.requiredRotation < 0 || Mathf.RoundToInt(s.rotation) % 360 == target.requiredRotation % 360) && (target.requiredPositionX == int.MinValue || s.cell.x == target.requiredPositionX) && (target.requiredPositionY == int.MinValue || s.cell.y == target.requiredPositionY); any |= ok; all &= ok; }
            return string.Equals(target.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) ? any : all;
        }
        public bool SetProperty(string id, Action<WorldObjectSnapshot> mutate)
        { if (!entries.TryGetValue(id ?? string.Empty, out WorldEntry entry) || mutate == null) return false; mutate(entry.state); SyncTransform(entry); NotifyChanged(); return true; }
        public bool AddConnection(string a, string b) { if (!entries.ContainsKey(a) || !entries.ContainsKey(b) || a == b) return false; string key = ConnectionKey(a, b); if (connections.Contains(key)) return false; float strength = 1f / (1f + Vector2Int.Distance(entries[a].state.cell, entries[b].state.cell)); if (strength < 0.2f) return false; connections.Add(key); connectionStrengths[key] = strength; NotifyChanged(); return true; }
        /// Removes only the named cable; unrelated connections and object state remain intact.
        public bool RemoveConnection(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || a == b) return false;
            string key = ConnectionKey(a, b);
            if (!connections.Remove(key)) return false;
            connectionStrengths.Remove(key);
            NotifyChanged();
            return true;
        }
        public void QueueEffect(string key) { if (string.IsNullOrWhiteSpace(key) || pendingEffects.Contains(key)) return; pendingEffects.Add(key); EffectQueued?.Invoke(key); }
        public bool AcknowledgeEffect(string key) => pendingEffects.Remove(key);
        public bool TryMoveObject(string id, Vector2Int direction)
        {
            if (!entries.TryGetValue(id ?? string.Empty, out WorldEntry entry) || entry.sceneObject == null) return false;
            GridWorld world = FindAnyObjectByType<GridWorld>(); if (world == null) return false;
            Vector2Int cell = entry.state.cell + direction; Vector2Int size = entry.state.size; GridObstacle obstacle = entry.sceneObject.GetComponent<GridObstacle>(); bool restoreObstacle = obstacle != null && obstacle.enabled; if (restoreObstacle) obstacle.enabled = false;
            bool available = true; for (int x = 0; x < Mathf.Max(1, size.x); x++) for (int y = 0; y < Mathf.Max(1, size.y); y++) if (!world.IsCellAvailable(cell + new Vector2Int(x, y))) available = false;
            if (!available) { if (restoreObstacle) obstacle.enabled = true; return false; }
            entry.state.cell = cell; entry.sceneObject.transform.position = world.CellToWorld(cell, entry.state.elevation); if (restoreObstacle) { obstacle.enabled = true; obstacle.RefreshFootprint(); } NotifyChanged(); return true;
        }
        public string CaptureStateJson()
        { if (IsReady && !string.IsNullOrWhiteSpace(CurrentMapId)) { archivedMaps[CurrentMapId] = CaptureSnapshot(); archivedEffects[CurrentMapId] = new List<string>(pendingEffects); } List<MapArchive> maps = new List<MapArchive>(); foreach (KeyValuePair<string, MapStateSnapshot> pair in archivedMaps) maps.Add(new MapArchive { mapId = pair.Key, snapshot = pair.Value, pendingEffects = archivedEffects.TryGetValue(pair.Key, out List<string> effects) ? effects.ToArray() : Array.Empty<string>() }); return JsonUtility.ToJson(new SaveData { mapId = CurrentMapId, objects = SnapshotObjects(), connections = new List<string>(connections).ToArray(), connectionStrengths = CaptureConnectionStrengths(), pendingEffects = pendingEffects.ToArray(), maps = maps.ToArray() }); }
        public bool RestoreStateJson(string json) { SaveData data; try { data = JsonUtility.FromJson<SaveData>(json); } catch { return false; } if (data == null) return false; archivedMaps.Clear(); archivedEffects.Clear(); foreach (MapArchive map in data.maps ?? Array.Empty<MapArchive>()) { if (map == null || string.IsNullOrWhiteSpace(map.mapId)) continue; archivedMaps[map.mapId] = map.snapshot; archivedEffects[map.mapId] = new List<string>(map.pendingEffects ?? Array.Empty<string>()); } pendingRestore = new MapStateSnapshot { mapId = data.mapId, objects = data.objects ?? Array.Empty<WorldObjectSnapshot>(), connections = data.connections ?? Array.Empty<string>(), connectionStrengths = data.connectionStrengths ?? Array.Empty<float>() }; pendingEffects.Clear(); pendingEffects.AddRange(data.pendingEffects ?? Array.Empty<string>()); if (IsReady && string.Equals(CurrentMapId, data.mapId, StringComparison.OrdinalIgnoreCase)) { ApplySnapshot(pendingRestore); pendingRestore = null; NotifyChanged(); } return true; }
        private void ApplySnapshot(MapStateSnapshot snapshot) { connections.Clear(); connectionStrengths.Clear(); string[] savedConnections = snapshot.connections ?? Array.Empty<string>(); float[] savedStrengths = snapshot.connectionStrengths ?? Array.Empty<float>(); connections.UnionWith(savedConnections); for (int i = 0; i < savedConnections.Length; i++) connectionStrengths[savedConnections[i]] = i < savedStrengths.Length ? savedStrengths[i] : 1f; foreach (WorldObjectSnapshot saved in snapshot.objects ?? Array.Empty<WorldObjectSnapshot>()) if (entries.TryGetValue(saved.id, out WorldEntry entry)) { entry.state = Clone(saved); SyncTransform(entry); } }
        private void SyncTransform(WorldEntry entry) { if (entry.sceneObject == null) return; GridWorld world = FindAnyObjectByType<GridWorld>(); if (world != null) entry.sceneObject.transform.position = world.CellToWorld(entry.state.cell, entry.state.elevation); entry.sceneObject.transform.rotation = Quaternion.Euler(0, entry.state.rotation, 0); entry.sceneObject.GetComponent<GridObstacle>()?.RefreshFootprint(); }
        private bool IsConnectedToAny(string id) { foreach (string key in connections) if (key.StartsWith(id + "|", StringComparison.Ordinal) || key.EndsWith("|" + id, StringComparison.Ordinal)) return true; return false; }
        private void NotifyChanged() { if (!rebuilding) StateChanged?.Invoke(new MapStateSnapshot { mapId = CurrentMapId, objects = SnapshotObjects(), connections = new List<string>(connections).ToArray() }); }
        private MapStateSnapshot CaptureSnapshot() { return new MapStateSnapshot { mapId = CurrentMapId, objects = SnapshotObjects(), connections = new List<string>(connections).ToArray(), connectionStrengths = CaptureConnectionStrengths() }; }
        private float[] CaptureConnectionStrengths() { float[] values = new float[connections.Count]; int i = 0; foreach (string key in connections) values[i++] = connectionStrengths.TryGetValue(key, out float value) ? value : 1f; return values; }
        private static string ConnectionKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        private static WorldObjectSnapshot Clone(WorldObjectSnapshot s) => s == null ? null : new WorldObjectSnapshot { id = s.id, mapId = s.mapId, cell = s.cell, size = s.size, elevation = s.elevation, rotation = s.rotation, powered = s.powered, frozen = s.frozen, revealed = s.revealed, aligned = s.aligned, spellCompatible = s.spellCompatible, tags = s.tags ?? Array.Empty<string>() };
        private sealed class WorldEntry { public GameObject sceneObject; public WorldObjectSnapshot state; }
        [Serializable] private sealed class SaveData { public string mapId; public WorldObjectSnapshot[] objects = Array.Empty<WorldObjectSnapshot>(); public string[] connections = Array.Empty<string>(); public float[] connectionStrengths = Array.Empty<float>(); public string[] pendingEffects = Array.Empty<string>(); public MapArchive[] maps = Array.Empty<MapArchive>(); }
        [Serializable] private sealed class MapArchive { public string mapId; public MapStateSnapshot snapshot; public string[] pendingEffects = Array.Empty<string>(); }
    }
}
