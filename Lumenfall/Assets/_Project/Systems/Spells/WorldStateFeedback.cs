using System;
using System.Collections.Generic;
using UnityEngine;
using Lumenfall.DataDriven;
using Lumenfall.GridMovement;

namespace Lumenfall.Spells
{
    [Serializable] public sealed class PuzzleProgressSnapshot { public string puzzleId; public int value; }
    public sealed partial class WorldStateService
    {
        // Keep authored copies across cached prefab re-enables. Save payloads never replace them.
        private readonly Dictionary<string, WorldObjectSnapshot> authoredBaselines = new Dictionary<string, WorldObjectSnapshot>(StringComparer.Ordinal);
        private readonly Dictionary<string, string[]> puzzleEffectPrefixes = new Dictionary<string, string[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> puzzleProgress = new Dictionary<string, int>(StringComparer.Ordinal);
        public event Action<WorldObjectFeedback> ObjectFeedbackChanged;
        public event Action<string> PuzzleResetCompleted;
        public void RegisterAdditionalPuzzleTarget(PuzzleTargetDefinition target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.id)) return;
            var targets = new List<PuzzleTargetDefinition>(puzzleTargets);
            targets.RemoveAll(item => item != null && item.id == target.id);
            targets.Add(target); puzzleTargets = targets.ToArray();
        }
        public void RegisterPuzzleEffectPrefixes(string puzzleId, string[] prefixes)
        { if (FindPuzzle(puzzleId) != null) puzzleEffectPrefixes[BaselineKey(puzzleId)] = (string[])(prefixes ?? Array.Empty<string>()).Clone(); }
        public int GetPuzzleProgress(string puzzleId) => puzzleProgress.TryGetValue(puzzleId ?? string.Empty, out int value) ? value : 0;
        public bool SetPuzzleProgress(string puzzleId, int value)
        {
            if (!IsReady || FindPuzzle(puzzleId) == null || value < 0 || GetPuzzleProgress(puzzleId) == value) return false;
            puzzleProgress[puzzleId] = value; NotifyChanged(); return true;
        }
        public WorldObjectFeedback GetObjectFeedback(string id)
        {
            if (!IsReady || !entries.TryGetValue(id ?? string.Empty, out WorldEntry entry)) return null;
            bool solved = false;
            foreach (PuzzleTargetDefinition target in puzzleTargets)
                if (target != null && Array.IndexOf(target.objectIds ?? Array.Empty<string>(), id) >= 0 && EvaluatePuzzleTarget(target)) solved = true;
            return new WorldObjectFeedback(id, entry.restored ? ObjectFeedbackState.Restored : solved ? ObjectFeedbackState.Solved : ObjectFeedbackState.Idle, solved);
        }
        public WorldObjectFeedback GetTargetFeedback(string id, bool valid)
        {
            WorldObjectFeedback feedback = GetObjectFeedback(id);
            return feedback == null ? null : new WorldObjectFeedback(id, !valid ? ObjectFeedbackState.Invalid : feedback.IsSolved ? ObjectFeedbackState.Solved : ObjectFeedbackState.Valid, feedback.IsSolved);
        }
        internal void ReportCastFeedback(SpellCastFeedback result)
        {
            WorldObjectFeedback feedback = GetTargetFeedback(result.TargetId, result.Outcome == SpellCastOutcome.Applied || result.Outcome == SpellCastOutcome.NoChange);
            if (feedback != null) ObjectFeedbackChanged?.Invoke(feedback);
        }
        /// Restores only this puzzle, incident cables, object effects and local progress.
        /// Quest completion, rewards and unlocks belong to GameEventRuntime and remain durable.
        public bool ResetPuzzle(string puzzleId)
        {
            PuzzleTargetDefinition target = FindPuzzle(puzzleId);
            if (!IsReady || target == null || target.objectIds == null || target.objectIds.Length == 0) return false;
            var ids = new HashSet<string>(target.objectIds, StringComparer.Ordinal);
            foreach (string connection in target.requiredConnections ?? Array.Empty<string>())
            {
                string[] ends = (connection ?? string.Empty).Split('|');
                if (ends.Length != 2) return false;
                if (string.IsNullOrWhiteSpace(ends[0]) || string.IsNullOrWhiteSpace(ends[1])) return false;
            }
            foreach (string id in ids)
                if (string.IsNullOrWhiteSpace(id) || !entries.ContainsKey(id) || !authoredBaselines.ContainsKey(BaselineKey(id))) return false;
            IsReady = false; rebuilding = true;
            try
            {
                if (!CanRestoreFootprints(ids)) return false;
                foreach (string id in ids)
                {
                    WorldEntry entry = entries[id]; entry.state = Clone(authoredBaselines[BaselineKey(id)]);
                    entry.restored = true; SyncTransform(entry);
                }
                var remove = new List<string>();
                foreach (string connection in connections)
                {
                    string[] ends = connection.Split('|');
                    if (ends.Length == 2 && (ids.Contains(ends[0]) || ids.Contains(ends[1]))) remove.Add(connection);
                }
                foreach (string connection in remove) { connections.Remove(connection); connectionStrengths.Remove(connection); }
                puzzleEffectPrefixes.TryGetValue(BaselineKey(puzzleId), out string[] prefixes);
                pendingEffects.RemoveAll(effect => EffectBelongsTo(effect, ids) || Array.Exists(prefixes ?? Array.Empty<string>(), prefix => !string.IsNullOrWhiteSpace(prefix) && effect.StartsWith(prefix, StringComparison.Ordinal)));
                puzzleProgress.Remove(puzzleId);
            }
            finally { rebuilding = false; IsReady = true; }
            NotifyChanged();
            PuzzleResetCompleted?.Invoke(puzzleId); return true;
        }
        private bool CanRestoreFootprints(HashSet<string> ids)
        {
            GridWorld world = FindAnyObjectByType<GridWorld>();
            if (world == null) { foreach (string id in ids) if (entries[id].sceneObject != null) return false; return true; }
            var obstacles = new List<GridObstacle>();
            foreach (string id in ids)
            {
                GridObstacle obstacle = entries[id].sceneObject == null ? null : entries[id].sceneObject.GetComponent<GridObstacle>();
                if (obstacle != null && obstacle.isActiveAndEnabled && obstacle.World != null) obstacles.Add(obstacle);
            }
            // Exclude only the selected objects' current footprints during the complete preflight.
            foreach (GridObstacle obstacle in obstacles) obstacle.World.UnregisterObstacle(obstacle);
            try
            {
                var claimed = new HashSet<Vector2Int>();
                foreach (string id in ids)
                {
                    WorldObjectSnapshot baseline = authoredBaselines[BaselineKey(id)];
                    GridObstacle obstacle = entries[id].sceneObject == null ? null : entries[id].sceneObject.GetComponent<GridObstacle>();
                    Vector2Int offset = obstacle == null ? Vector2Int.zero : obstacle.FootprintOffset;
                    Vector2Int size = obstacle == null ? baseline.size : obstacle.FootprintSize;
                    for (int x = 0; x < Mathf.Max(1, size.x); x++)
                        for (int y = 0; y < Mathf.Max(1, size.y); y++)
                        {
                            Vector2Int cell = baseline.cell + offset + new Vector2Int(x, y);
                            if (!world.IsCellAvailable(cell) || !claimed.Add(cell)) return false;
                        }
                }
                return true;
            }
            finally { foreach (GridObstacle obstacle in obstacles) obstacle.RefreshFootprint(); }
        }
        private static bool EffectBelongsTo(string effect, HashSet<string> ids)
        {
            foreach (string id in ids)
                if (effect == id || effect.StartsWith(id + ":", StringComparison.Ordinal) || effect.Contains(":" + id + ":") || effect.EndsWith(":" + id, StringComparison.Ordinal)) return true;
            return false;
        }
        private PuzzleTargetDefinition FindPuzzle(string id) => Array.Find(puzzleTargets, item => item != null && string.Equals(item.id, id, StringComparison.Ordinal));
        private string BaselineKey(string id) => CurrentMapId + "|" + id;
        private PuzzleProgressSnapshot[] CapturePuzzleProgress()
        {
            var result = new List<PuzzleProgressSnapshot>();
            foreach (var pair in puzzleProgress) result.Add(new PuzzleProgressSnapshot { puzzleId = pair.Key, value = pair.Value });
            return result.ToArray();
        }
        private void RestorePuzzleProgress(PuzzleProgressSnapshot[] progress)
        {
            puzzleProgress.Clear();
            foreach (PuzzleProgressSnapshot item in progress ?? Array.Empty<PuzzleProgressSnapshot>())
                if (item != null && !string.IsNullOrWhiteSpace(item.puzzleId) && item.value >= 0) puzzleProgress[item.puzzleId] = item.value;
        }
        private void NotifyRestoredFeedback() { foreach (WorldObjectSnapshot item in SnapshotObjects()) ObjectFeedbackChanged?.Invoke(GetObjectFeedback(item.id)); }
        private static bool MutableEquals(WorldObjectSnapshot a, WorldObjectSnapshot b) => a.cell == b.cell && a.elevation == b.elevation && a.rotation == b.rotation && a.powered == b.powered && a.frozen == b.frozen && a.revealed == b.revealed && a.aligned == b.aligned;
        private static WorldObjectSnapshot RestoreMutable(WorldObjectSnapshot authored, WorldObjectSnapshot saved)
        {
            WorldObjectSnapshot restored = Clone(authored);
            restored.cell = saved.cell; restored.elevation = saved.elevation; restored.rotation = saved.rotation;
            restored.powered = saved.powered; restored.frozen = saved.frozen; restored.revealed = saved.revealed; restored.aligned = saved.aligned;
            return restored;
        }
    }
}
