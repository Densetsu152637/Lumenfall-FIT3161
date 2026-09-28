using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Lumenfall.GameEvents;
using Lumenfall.Spells;

namespace Lumenfall.Quests
{
    /// Optional contract for scene puzzle objects that want a strongly typed state surface.
    public interface IQuestPuzzleState
    {
        bool IsPowered { get; }
        bool IsConnected { get; }
        bool IsRevealed { get; }
        bool IsFrozen { get; }
        bool IsAligned { get; }
        float Rotation { get; }
        int PositionX { get; }
        int PositionY { get; }
    }

    [Serializable]
    public sealed class QuestPuzzleDatabase
    {
        public int schemaVersion = 1;
        public QuestPuzzleDefinition[] puzzles = Array.Empty<QuestPuzzleDefinition>();
    }

    [Serializable]
    public sealed class QuestPuzzleDefinition
    {
        public string id;
        public string questId;
        public string completionEventId;
        public string mapId;
        public QuestPuzzleTarget[] targets = Array.Empty<QuestPuzzleTarget>();
    }

    [Serializable]
    public sealed class QuestPuzzleTarget
    {
        public string id;
        public string[] objectIds = Array.Empty<string>();
        public string conditionMode = "All";
        public bool requiredPowered;
        public bool requiredConnected;
        public bool requiredRevealed;
        public bool requiredFrozen;
        public bool requiredAligned;
        public int requiredRotation = -1;
        public int requiredPositionX = int.MinValue;
        public int requiredPositionY = int.MinValue;
        public string[] requiredConnections = Array.Empty<string>();
    }

    /// <summary>
    /// Polls stable puzzle-object state and triggers authored completion events. It intentionally
    /// resolves authoritative world state every check so scene transitions, loader rebuilds, and
    /// prefab replacement cannot leave stale references behind.
    /// </summary>
    public sealed class QuestPuzzleMonitor : MonoBehaviour
    {
        private const string ResourcePath = "Quests/quest_puzzle_definitions";
        private static QuestPuzzleMonitor instance;
        private QuestPuzzleDatabase database;
        private readonly HashSet<string> fired = new HashSet<string>(StringComparer.Ordinal);
        private float nextCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null) return;
            GameObject host = new GameObject("[QuestPuzzleMonitor]");
            instance = host.AddComponent<QuestPuzzleMonitor>();
            DontDestroyOnLoad(host);
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) { Debug.LogWarning($"Quest puzzle definitions missing at Resources/{ResourcePath}.json", this); return; }
            try { database = JsonUtility.FromJson<QuestPuzzleDatabase>(asset.text); }
            catch (Exception exception) { Debug.LogError($"Could not parse quest puzzle definitions: {exception.Message}", this); }
            GameEventManager eventManager = GameEventManager.Instance;
            GameEventRuntime runtime = eventManager == null ? null : eventManager.Runtime;
            if (runtime != null)
                runtime.StateRestored += OnStateRestored;
        }

        private void OnDestroy()
        {
            GameEventManager eventManager = GameEventManager.Instance;
            GameEventRuntime runtime = eventManager == null ? null : eventManager.Runtime;
            if (runtime != null) runtime.StateRestored -= OnStateRestored;
            if (instance == this) instance = null;
        }

        private void OnStateRestored() { fired.Clear(); }

        private void Update()
        {
            if (database == null || Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.1f;
            GameEventManager manager = GameEventManager.Instance;
            if (manager == null || manager.Runtime == null || manager.Runtime.ActiveDialogue != null) return;
            foreach (QuestPuzzleDefinition puzzle in database.puzzles ?? Array.Empty<QuestPuzzleDefinition>())
            {
                if (puzzle == null || string.IsNullOrWhiteSpace(puzzle.questId) || string.IsNullOrWhiteSpace(puzzle.completionEventId)) continue;
                string key = string.IsNullOrWhiteSpace(puzzle.id) ? puzzle.questId : puzzle.id;
                QuestStatus status = manager.Runtime.GetQuestStatus(puzzle.questId);
                if (status == QuestStatus.NotStarted) fired.Remove(key);
                if (status != QuestStatus.Active) continue;
                if (manager.Runtime.HasEventTriggered(puzzle.completionEventId)) { fired.Add(key); continue; }
                if (fired.Contains(key)) continue;
                if (!SceneMatches(puzzle.mapId) || !TargetsReady(puzzle.targets)) continue;
                GameEventTriggerResult result = manager.Runtime.TriggerEvent(puzzle.completionEventId);
                if (result.Succeeded) fired.Add(key);
            }
        }

        private static bool SceneMatches(string mapId)
        {
            WorldStateService state = WorldStateService.Instance;
            return string.IsNullOrWhiteSpace(mapId) || (state != null && string.Equals(state.CurrentMapId, mapId, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TargetsReady(QuestPuzzleTarget[] targets)
        {
            if (targets == null || targets.Length == 0) return false;
            WorldStateService state = WorldStateService.Instance;
            if (state == null || !state.IsReady) return false;
            foreach (QuestPuzzleTarget target in targets)
            {
                if (target == null || target.objectIds == null || target.objectIds.Length == 0) return false;
                foreach (string requiredConnection in target.requiredConnections ?? Array.Empty<string>())
                {
                    string[] endpoints = (requiredConnection ?? string.Empty).Split('|');
                    if (endpoints.Length != 2 || string.IsNullOrWhiteSpace(endpoints[0]) || string.IsNullOrWhiteSpace(endpoints[1]) ||
                        !state.IsConnected(endpoints[0], endpoints[1])) return false;
                }
                bool any = string.Equals(target.conditionMode, "Any", StringComparison.OrdinalIgnoreCase);
                bool targetReady = any ? false : true;
                foreach (string objectId in target.objectIds)
                {
                    bool ready = state.TryGetObject(objectId, out WorldObjectSnapshot snapshot) && StateMatches(state, snapshot, target);
                    if (any) targetReady |= ready;
                    else targetReady &= ready;
                }
                if (!targetReady) return false;
            }
            return true;
        }

        private static bool StateMatches(WorldStateService state, WorldObjectSnapshot snapshot, QuestPuzzleTarget target)
        {
            if (target.requiredPowered && !snapshot.powered) return false;
            if (target.requiredConnected && !IsConnectedToAny(state, snapshot.id)) return false;
            if (target.requiredRevealed && !snapshot.revealed) return false;
            if (target.requiredFrozen && !snapshot.frozen) return false;
            if (target.requiredAligned && !snapshot.aligned) return false;
            if (target.requiredRotation >= 0 && !Mathf.Approximately(NormalizeAngle(snapshot.rotation), NormalizeAngle(target.requiredRotation))) return false;
            if (target.requiredPositionX != int.MinValue && snapshot.cell.x != target.requiredPositionX) return false;
            if (target.requiredPositionY != int.MinValue && snapshot.cell.y != target.requiredPositionY) return false;
            return true;
        }

        private static bool IsConnectedToAny(WorldStateService state, string id)
        {
            foreach (WorldObjectSnapshot other in state.SnapshotObjects())
            {
                if (!string.Equals(other.id, id, StringComparison.Ordinal) && state.IsConnected(id, other.id)) return true;
            }
            return false;
        }

        private static float NormalizeAngle(float angle) { angle %= 360f; return angle < 0f ? angle + 360f : angle; }
    }
}
