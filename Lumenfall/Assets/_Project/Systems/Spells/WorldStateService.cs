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
        public float elevation; public float rotation; public bool powered; public bool frozen; public bool revealed; public bool collected;
        public bool aligned; public bool spellCompatible = true; public string[] tags = Array.Empty<string>();
    }
    [Serializable] public sealed class MapStateSnapshot { public string mapId; public WorldObjectSnapshot[] objects = Array.Empty<WorldObjectSnapshot>(); public string[] connections = Array.Empty<string>(); public float[] connectionStrengths = Array.Empty<float>(); }
    public sealed class MapRebuildEvent { public readonly string MapId; public MapRebuildEvent(string mapId) { MapId = mapId; } }

    /// Authoritative mutable state for puzzle objects. Quest monitors can subscribe without holding scene references.
    [DefaultExecutionOrder(-300)]
    public sealed class WorldStateService : MonoBehaviour, ISaveStateProvider
    {
        public static WorldStateService Instance { get; private set; }
        /// Cables weaker than this are refused, and conductors cannot draw power through them.
        public const float MinimumCableStrength = 0.2f;
        /// Objects with this tag hold no charge of their own: they are powered only while a chain of live cables
        /// links them to a powered object without the tag, so Ampere and direct power changes do not stick.
        public const string ConductorTag = "conductor";
        private readonly Dictionary<string, WorldEntry> entries = new Dictionary<string, WorldEntry>(StringComparer.Ordinal);
        // Area prefabs are cached and reused by the loader; enabled flags survive entry rebuilds.
        private readonly Dictionary<Renderer, bool> authoredRendererStates = new Dictionary<Renderer, bool>();
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
        /// <summary>Raised once each time Lost Echoes reveals an object. Restoring saved state does not raise it.</summary>
        public event Action<WorldObjectSnapshot> ObjectRevealed;
        /// <summary>Tag prefix naming a GameEvent to trigger when the object is revealed, e.g. <c>on-reveal:open_hidden_door</c>.</summary>
        public const string OnRevealTagPrefix = "on-reveal:";
        /// <summary>Raised once when the player picks up an object. Restoring saved state does not raise it.</summary>
        public event Action<WorldObjectSnapshot> ObjectCollected;
        /// <summary>Tag marking an object the player can pick up with the talk key. Hidden pickups can be picked up too; Lost Echoes helps locate them.</summary>
        public const string PickupTag = "pickup";
        /// <summary>Tag prefix naming a GameEvent to trigger when the object is picked up, e.g. <c>on-pickup:found_old_letter</c>.</summary>
        public const string OnPickupTagPrefix = "on-pickup:";
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
            PruneDestroyedRenderers();
            rebuilding = true; IsReady = false; CurrentMapId = mapId ?? string.Empty; entries.Clear(); connections.Clear(); connectionStrengths.Clear(); pendingEffects.Clear(); pendingRestore = null; puzzleTargets = Array.Empty<PuzzleTargetDefinition>();
            if (archivedMaps.TryGetValue(CurrentMapId, out MapStateSnapshot saved)) pendingRestore = saved;
            if (archivedEffects.TryGetValue(CurrentMapId, out List<string> effects)) pendingEffects.AddRange(effects);
        }
        public void ResetAllState() { PruneDestroyedRenderers(); archivedMaps.Clear(); archivedEffects.Clear(); entries.Clear(); connections.Clear(); connectionStrengths.Clear(); pendingEffects.Clear(); pendingRestore = null; CurrentMapId = string.Empty; IsReady = false; }
        public void RegisterPuzzleTargets(PuzzleTargetDefinition[] definitions) { puzzleTargets = definitions ?? Array.Empty<PuzzleTargetDefinition>(); }
        public void RegisterObject(string id, GameObject sceneObject, Vector2Int cell, Vector2Int size, bool compatible, string[] tags)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            Renderer[] renderers = sceneObject == null ? Array.Empty<Renderer>() : sceneObject.GetComponentsInChildren<Renderer>(true);
            bool[] enabledStates = new bool[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!authoredRendererStates.TryGetValue(renderers[i], out bool authoredEnabled))
                    authoredRendererStates[renderers[i]] = authoredEnabled = renderers[i].enabled;
                enabledStates[i] = authoredEnabled;
            }
            WorldEntry entry = new WorldEntry { sceneObject = sceneObject, renderers = renderers, rendererEnabledStates = enabledStates, authoredHidden = Array.Exists(tags ?? Array.Empty<string>(), tag => string.Equals(tag, "hidden", StringComparison.OrdinalIgnoreCase)),
                state = new WorldObjectSnapshot { id = id, mapId = CurrentMapId, cell = cell, size = size, elevation = sceneObject == null ? 0 : sceneObject.transform.position.y, rotation = sceneObject == null ? 0 : sceneObject.transform.eulerAngles.y, spellCompatible = compatible, tags = tags ?? Array.Empty<string>() } };
            entries[id] = entry;
            SyncPresentation(entry);
        }
        public void EndMapRebuild()
        {
            rebuilding = false; IsReady = true;
            if (pendingRestore != null && string.Equals(pendingRestore.mapId, CurrentMapId, StringComparison.OrdinalIgnoreCase)) { ApplySnapshot(pendingRestore); pendingRestore = null; }
            MapRebuilt?.Invoke(new MapRebuildEvent(CurrentMapId)); NotifyChanged();
        }
        public bool TryGetObject(string id, out WorldObjectSnapshot snapshot) { if (entries.TryGetValue(id ?? string.Empty, out WorldEntry item)) { snapshot = Clone(item.state); return true; } snapshot = null; return false; }
        public WorldObjectSnapshot[] SnapshotObjects() { var result = new WorldObjectSnapshot[entries.Count]; int i = 0; foreach (WorldEntry e in entries.Values) result[i++] = Clone(e.state); return result; }
        public bool TryGetSceneObject(string id, out GameObject sceneObject) { sceneObject = entries.TryGetValue(id ?? string.Empty, out WorldEntry item) ? item.sceneObject : null; return sceneObject != null; }
        /// Connection keys use the "a|b" form that puzzle targets declare in requiredConnections.
        public string[] SnapshotConnections() => new List<string>(connections).ToArray();
        public bool IsConnected(string a, string b) => connections.Contains(ConnectionKey(a, b));
        /// Cable strength follows live endpoint positions, including after movement and save restore.
        public float GetConnectionStrength(string a, string b)
        {
            if (!IsConnected(a, b) || !entries.TryGetValue(a ?? string.Empty, out WorldEntry first) || !entries.TryGetValue(b ?? string.Empty, out WorldEntry second)) return 0f;
            return 1f / (1f + Vector2Int.Distance(first.state.cell, second.state.cell));
        }
        public float GetConnectionEffectivePower(string a, string b)
        {
            if (!TryGetObject(a, out WorldObjectSnapshot first) || !TryGetObject(b, out WorldObjectSnapshot second) || (!first.powered && !second.powered)) return 0f;
            return GetConnectionStrength(a, b);
        }
        public bool RemoveConnection(string a, string b)
        {
            string key = ConnectionKey(a, b);
            if (!connections.Remove(key)) return false;
            connectionStrengths.Remove(key); NotifyChanged(); return true;
        }
        public bool EvaluatePuzzleTarget(PuzzleTargetDefinition target)
        {
            if (target == null || target.objectIds == null || target.objectIds.Length == 0) return false;
            foreach (string requiredConnection in target.requiredConnections ?? Array.Empty<string>())
            {
                string[] endpoints = (requiredConnection ?? string.Empty).Split('|');
                if (endpoints.Length != 2 || !IsConnected(endpoints[0], endpoints[1]) || GetConnectionEffectivePower(endpoints[0], endpoints[1]) < target.minimumEffectivePower) return false;
            }
            bool any = false, all = true;
            foreach (string id in target.objectIds) { if (!TryGetObject(id, out WorldObjectSnapshot s)) { all = false; continue; } bool ok = (!target.requiredPowered || s.powered) && (!target.requiredConnected || IsConnectedToAny(id)) && (!target.requiredRevealed || s.revealed) && (!target.requiredFrozen || s.frozen) && (!target.requiredAligned || s.aligned) && (target.requiredRotation < 0 || Mathf.RoundToInt(s.rotation) % 360 == target.requiredRotation % 360) && (target.requiredPositionX == int.MinValue || s.cell.x == target.requiredPositionX) && (target.requiredPositionY == int.MinValue || s.cell.y == target.requiredPositionY); any |= ok; all &= ok; }
            return string.Equals(target.conditionMode, "Any", StringComparison.OrdinalIgnoreCase) ? any : all;
        }
        public bool SetProperty(string id, Action<WorldObjectSnapshot> mutate)
        { if (!entries.TryGetValue(id ?? string.Empty, out WorldEntry entry) || mutate == null) return false; mutate(entry.state); SyncTransform(entry); NotifyChanged(); return true; }
        /// <summary>Lost Echoes is general purpose: any spell-compatible object authored with the <c>hidden</c> tag.</summary>
        public static bool IsLostEchoesTarget(WorldObjectSnapshot target)
        {
            return target != null && target.spellCompatible &&
                Array.Exists(target.tags ?? Array.Empty<string>(), tag => string.Equals(tag, "hidden", StringComparison.OrdinalIgnoreCase));
        }
        /// <summary>Reveals every unrevealed Lost Echoes target on the current map within a Manhattan radius.</summary>
        public int RevealHiddenAround(Vector2Int searchCell, int radius, List<string> revealedIds = null)
        {
            if (!IsReady || radius < 0) return 0;
            List<string> found = new List<string>();
            foreach (WorldEntry entry in entries.Values)
            {
                WorldObjectSnapshot target = entry.state;
                if (target == null || target.revealed || !IsLostEchoesTarget(target) ||
                    !string.Equals(target.mapId, CurrentMapId, StringComparison.OrdinalIgnoreCase) ||
                    Mathf.Abs(target.cell.x - searchCell.x) + Mathf.Abs(target.cell.y - searchCell.y) > radius) continue;
                found.Add(target.id);
            }
            int count = 0;
            foreach (string id in found)
                if (Reveal(id)) { count++; revealedIds?.Add(id); }
            return count;
        }
        /// <summary>Finds the nearest unrevealed Lost Echoes target on the current map within a Manhattan distance, for sonar hints.</summary>
        public bool TryFindNearestConcealed(Vector2Int fromCell, int maxDistance, out WorldObjectSnapshot nearest, out int distance)
        {
            nearest = null; distance = int.MaxValue;
            if (!IsReady) return false;
            foreach (WorldEntry entry in entries.Values)
            {
                WorldObjectSnapshot target = entry.state;
                if (target == null || target.revealed || target.collected || !IsLostEchoesTarget(target) ||
                    !string.Equals(target.mapId, CurrentMapId, StringComparison.OrdinalIgnoreCase)) continue;
                int d = Mathf.Abs(target.cell.x - fromCell.x) + Mathf.Abs(target.cell.y - fromCell.y);
                if (d <= maxDistance && d < distance) { distance = d; nearest = Clone(target); }
            }
            return nearest != null;
        }
        public bool TryRevealObject(string id, Vector2Int searchCell, int radius)
        {
            if (!IsReady || radius < 0 || !TryGetObject(id, out WorldObjectSnapshot target) ||
                !string.Equals(target.mapId, CurrentMapId, StringComparison.OrdinalIgnoreCase) ||
                !IsLostEchoesTarget(target) || target.revealed ||
                Mathf.Abs(target.cell.x - searchCell.x) + Mathf.Abs(target.cell.y - searchCell.y) > radius) return false;
            return Reveal(id);
        }
        // Every Lost Echoes reveal goes through here: presentation listens to ObjectRevealed, and authored
        // "on-reveal:<eventId>" tags trigger GameEvents directly, so hidden passages, memories or collectibles need
        // only data. Each event's own conditions and repeatability still apply.
        private bool Reveal(string id)
        {
            if (!SetProperty(id, value => value.revealed = true) || !TryGetObject(id, out WorldObjectSnapshot revealed)) return false;
            ObjectRevealed?.Invoke(revealed);
            TriggerTaggedEvents(revealed, OnRevealTagPrefix);
            return true;
        }
        /// <summary>True when the object is tagged <c>pickup</c> and not yet collected, whether or not it is hidden.</summary>
        public static bool IsCollectable(WorldObjectSnapshot target)
        {
            return target != null && !target.collected && HasTag(target, PickupTag);
        }
        /// <summary>True when the object is tagged <c>hidden</c> and has not been revealed, so it should not be shown.</summary>
        public static bool IsConcealed(WorldObjectSnapshot target)
        {
            return target != null && !target.revealed && HasTag(target, "hidden");
        }
        /// <summary>Picks up a collectable object on the current map within a Manhattan radius of the player's cell.</summary>
        public bool TryCollectObject(string id, Vector2Int fromCell, int radius)
        {
            if (!IsReady || radius < 0 || !TryGetObject(id, out WorldObjectSnapshot target) ||
                !string.Equals(target.mapId, CurrentMapId, StringComparison.OrdinalIgnoreCase) || !IsCollectable(target) ||
                Mathf.Abs(target.cell.x - fromCell.x) + Mathf.Abs(target.cell.y - fromCell.y) > radius) return false;
            // Picking up an unrevealed hidden object finds it too, so reveal feedback and on-reveal hooks still run.
            if (IsConcealed(target)) Reveal(id);
            if (!SetProperty(id, value => value.collected = true) || !TryGetObject(id, out WorldObjectSnapshot collected)) return false;
            ObjectCollected?.Invoke(collected);
            TriggerTaggedEvents(collected, OnPickupTagPrefix);
            return true;
        }
        private static bool HasTag(WorldObjectSnapshot target, string tag) =>
            Array.Exists(target.tags ?? Array.Empty<string>(), value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
        private static void TriggerTaggedEvents(WorldObjectSnapshot target, string prefix)
        {
            GameEventRuntime runtime = GameEventManager.Instance?.Runtime;
            if (runtime == null) return;
            foreach (string tag in target.tags ?? Array.Empty<string>())
            {
                if (tag == null || !tag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string eventId = tag.Substring(prefix.Length).Trim();
                if (eventId.Length > 0) runtime.TriggerEvent(eventId);
            }
        }
        public bool AddConnection(string a, string b) { if (!entries.ContainsKey(a) || !entries.ContainsKey(b) || a == b) return false; string key = ConnectionKey(a, b); if (connections.Contains(key)) return false; float strength = 1f / (1f + Vector2Int.Distance(entries[a].state.cell, entries[b].state.cell)); if (strength < MinimumCableStrength) return false; connections.Add(key); connectionStrengths[key] = strength; NotifyChanged(); return true; }
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
        public bool RestoreStateJson(string json) { SaveData data; try { data = JsonUtility.FromJson<SaveData>(json); } catch { return false; } if (data == null) return false; data.mapId = NormalizeMapId(data.mapId); NormalizeObjectMapIds(data.objects); archivedMaps.Clear(); archivedEffects.Clear(); foreach (MapArchive map in data.maps ?? Array.Empty<MapArchive>()) { if (map == null || string.IsNullOrWhiteSpace(map.mapId)) continue; map.mapId = NormalizeMapId(map.mapId); if (map.snapshot != null) { map.snapshot.mapId = NormalizeMapId(map.snapshot.mapId); NormalizeObjectMapIds(map.snapshot.objects); } archivedMaps[map.mapId] = map.snapshot; archivedEffects[map.mapId] = new List<string>(map.pendingEffects ?? Array.Empty<string>()); } pendingRestore = new MapStateSnapshot { mapId = data.mapId, objects = data.objects ?? Array.Empty<WorldObjectSnapshot>(), connections = data.connections ?? Array.Empty<string>(), connectionStrengths = data.connectionStrengths ?? Array.Empty<float>() }; pendingEffects.Clear(); pendingEffects.AddRange(data.pendingEffects ?? Array.Empty<string>()); if (IsReady && string.Equals(CurrentMapId, data.mapId, StringComparison.OrdinalIgnoreCase)) { ApplySnapshot(pendingRestore); pendingRestore = null; NotifyChanged(); } return true; }
        private static string NormalizeMapId(string mapId) => string.Equals(mapId, "eastForest", StringComparison.OrdinalIgnoreCase) ? "westForest" : mapId;
        private static void NormalizeObjectMapIds(WorldObjectSnapshot[] objects) { foreach (WorldObjectSnapshot item in objects ?? Array.Empty<WorldObjectSnapshot>()) if (item != null) item.mapId = NormalizeMapId(item.mapId); }
        private void ApplySnapshot(MapStateSnapshot snapshot) { connections.Clear(); connectionStrengths.Clear(); string[] savedConnections = snapshot.connections ?? Array.Empty<string>(); float[] savedStrengths = snapshot.connectionStrengths ?? Array.Empty<float>(); connections.UnionWith(savedConnections); for (int i = 0; i < savedConnections.Length; i++) connectionStrengths[savedConnections[i]] = i < savedStrengths.Length ? savedStrengths[i] : 1f; foreach (WorldObjectSnapshot saved in snapshot.objects ?? Array.Empty<WorldObjectSnapshot>()) if (entries.TryGetValue(saved.id, out WorldEntry entry)) { string[] authoredTags = entry.state.tags; bool compatible = entry.state.spellCompatible; entry.state = Clone(saved); entry.state.tags = authoredTags; entry.state.spellCompatible = compatible; entry.state.mapId = CurrentMapId; SyncTransform(entry); } }
        private void SyncTransform(WorldEntry entry) { if (entry.sceneObject == null) return; GridWorld world = FindAnyObjectByType<GridWorld>(); if (world != null) entry.sceneObject.transform.position = world.CellToWorld(entry.state.cell, entry.state.elevation); entry.sceneObject.transform.rotation = Quaternion.Euler(0, entry.state.rotation, 0); entry.sceneObject.GetComponent<GridObstacle>()?.RefreshFootprint(); SyncPresentation(entry); }
        private void PruneDestroyedRenderers()
        {
            var destroyed = new List<Renderer>();
            foreach (Renderer renderer in authoredRendererStates.Keys) if (renderer == null) destroyed.Add(renderer);
            foreach (Renderer renderer in destroyed) authoredRendererStates.Remove(renderer);
        }
        private static void SyncPresentation(WorldEntry entry)
        {
            // A picked-up object leaves the world, so its spot returns to its unfound look.
            if (!entry.authoredHidden && !entry.state.collected) return;
            for (int i = 0; i < entry.renderers.Length; i++)
                if (entry.renderers[i] != null)
                    entry.renderers[i].enabled = entry.rendererEnabledStates[i] && !entry.state.collected &&
                        (!entry.authoredHidden || entry.state.revealed);
        }
        private bool IsConnectedToAny(string id) { foreach (string key in connections) if (key.StartsWith(id + "|", StringComparison.Ordinal) || key.EndsWith("|" + id, StringComparison.Ordinal)) return true; return false; }
        public static bool IsConductor(WorldObjectSnapshot item) => item != null && Array.Exists(item.tags ?? Array.Empty<string>(), tag => string.Equals(tag, ConductorTag, StringComparison.OrdinalIgnoreCase));
        private void PropagateConductorPower()
        {
            HashSet<string> reached = new HashSet<string>(StringComparer.Ordinal);
            Queue<string> frontier = new Queue<string>();
            foreach (KeyValuePair<string, WorldEntry> pair in entries)
                if (pair.Value.state.powered && !IsConductor(pair.Value.state)) { reached.Add(pair.Key); frontier.Enqueue(pair.Key); }
            while (frontier.Count > 0)
            {
                string current = frontier.Dequeue();
                foreach (string key in connections)
                {
                    int split = key.IndexOf('|');
                    string other = key.Substring(0, split) == current ? key.Substring(split + 1) : key.Substring(split + 1) == current ? key.Substring(0, split) : null;
                    // Power only continues through conductors; a dark ordinary relic breaks the chain.
                    if (other == null || reached.Contains(other) || !entries.TryGetValue(other, out WorldEntry next) || !IsConductor(next.state) ||
                        GetConnectionStrength(current, other) < MinimumCableStrength) continue;
                    reached.Add(other); frontier.Enqueue(other);
                }
            }
            foreach (KeyValuePair<string, WorldEntry> pair in entries)
                if (IsConductor(pair.Value.state)) pair.Value.state.powered = reached.Contains(pair.Key);
        }
        private void NotifyChanged() { PropagateConductorPower(); if (!rebuilding) StateChanged?.Invoke(new MapStateSnapshot { mapId = CurrentMapId, objects = SnapshotObjects(), connections = new List<string>(connections).ToArray() }); }
        private MapStateSnapshot CaptureSnapshot() { return new MapStateSnapshot { mapId = CurrentMapId, objects = SnapshotObjects(), connections = new List<string>(connections).ToArray(), connectionStrengths = CaptureConnectionStrengths() }; }
        private float[] CaptureConnectionStrengths() { float[] values = new float[connections.Count]; int i = 0; foreach (string key in connections) values[i++] = connectionStrengths.TryGetValue(key, out float value) ? value : 1f; return values; }
        private static string ConnectionKey(string a, string b) => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;
        private static WorldObjectSnapshot Clone(WorldObjectSnapshot s) => s == null ? null : new WorldObjectSnapshot { id = s.id, mapId = s.mapId, cell = s.cell, size = s.size, elevation = s.elevation, rotation = s.rotation, powered = s.powered, frozen = s.frozen, revealed = s.revealed, collected = s.collected, aligned = s.aligned, spellCompatible = s.spellCompatible, tags = s.tags ?? Array.Empty<string>() };
        private sealed class WorldEntry { public GameObject sceneObject; public WorldObjectSnapshot state; public Renderer[] renderers; public bool[] rendererEnabledStates; public bool authoredHidden; }
        [Serializable] private sealed class SaveData { public string mapId; public WorldObjectSnapshot[] objects = Array.Empty<WorldObjectSnapshot>(); public string[] connections = Array.Empty<string>(); public float[] connectionStrengths = Array.Empty<float>(); public string[] pendingEffects = Array.Empty<string>(); public MapArchive[] maps = Array.Empty<MapArchive>(); }
        [Serializable] private sealed class MapArchive { public string mapId; public MapStateSnapshot snapshot; public string[] pendingEffects = Array.Empty<string>(); }
    }
}
