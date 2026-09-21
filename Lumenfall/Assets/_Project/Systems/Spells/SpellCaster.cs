using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Lumenfall.GameEvents;
using Lumenfall.GridMovement;

namespace Lumenfall.Spells
{
    /// Applies the seven documented spells to live world state. The keyboard adapter is deliberately
    /// small: number keys select a spell and F casts at the nearest compatible object.
    [DisallowMultipleComponent]
    public sealed class SpellCaster : MonoBehaviour
    {
        [SerializeField] private SpellId selectedSpell = SpellId.Ampere;
        [SerializeField, Min(1)] private int interactionRadius = 2;
        private Vector2Int lastDirection = Vector2Int.up;
        public SpellId SelectedSpell => selectedSpell;
        public event Action<SpellId, string, bool> CastCompleted;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Bootstrap()
        { GridMover player = FindAnyObjectByType<GridMover>(); if (player != null && player.GetComponent<SpellCaster>() == null) player.gameObject.AddComponent<SpellCaster>(); }
        private void Update()
        {
            Keyboard keyboard = Keyboard.current; if (keyboard == null || GameEventManager.Instance?.Runtime?.ActiveDialogue != null) return;
            Vector2Int inputDirection = new Vector2Int((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0), (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)); if (inputDirection != Vector2Int.zero) lastDirection = inputDirection;
            if (keyboard.digit1Key.wasPressedThisFrame) selectedSpell = SpellId.Ampere;
            if (keyboard.digit2Key.wasPressedThisFrame) selectedSpell = SpellId.Weave;
            if (keyboard.digit3Key.wasPressedThisFrame) selectedSpell = SpellId.Gravitate;
            if (keyboard.digit4Key.wasPressedThisFrame) selectedSpell = SpellId.Rotara;
            if (keyboard.digit5Key.wasPressedThisFrame) selectedSpell = SpellId.Glaciate;
            if (keyboard.digit6Key.wasPressedThisFrame) selectedSpell = SpellId.Vis;
            if (keyboard.digit7Key.wasPressedThisFrame) selectedSpell = SpellId.LostEchoes;
            if (keyboard.fKey.wasPressedThisFrame) { string target = FindNearestTarget(); if (!string.IsNullOrEmpty(target)) Cast(selectedSpell, target, DirectionFromPlayer()); }
        }
        public bool Cast(SpellId spell, string targetId, Vector2Int direction)
        {
            WorldStateService state = WorldStateService.Instance; if (state == null || !state.IsReady || !state.TryGetObject(targetId, out WorldObjectSnapshot target) || !target.spellCompatible) return false;
            bool changed = false;
            switch (spell)
            {
                case SpellId.Ampere:
                    bool nextPower = !target.powered;
                    changed = state.SetProperty(targetId, x => x.powered = nextPower);
                    if (changed) state.QueueEffect("lightning:" + targetId + ":" + (nextPower ? "on" : "off"));
                    break;
                case SpellId.Weave: changed = WeaveToPowered(state, targetId); break;
                case SpellId.Gravitate: changed = state.TryMoveObject(targetId, ClampDirection(direction)); break;
                case SpellId.Rotara: changed = state.SetProperty(targetId, x => x.rotation = (Mathf.Round(x.rotation / 90f) * 90f + 90f) % 360f); break;
                case SpellId.Glaciate: changed = FreezeSlide(state, targetId, ClampDirection(direction)); break;
                case SpellId.Vis: changed = state.SetProperty(targetId, x => { x.aligned = true; x.rotation = Mathf.Round(x.rotation / 90f) * 90f; }); break;
                case SpellId.LostEchoes: changed = Reveal(state, targetId); break;
            }
            CastCompleted?.Invoke(spell, targetId, changed); return changed;
        }
        private static bool WeaveToPowered(WorldStateService state, string targetId)
        {
            string intended = null;
            if (state.TryGetObject(targetId, out WorldObjectSnapshot target))
                foreach (string tag in target.tags ?? Array.Empty<string>())
                    if (tag.StartsWith("weave-with:", StringComparison.OrdinalIgnoreCase)) { intended = tag.Substring("weave-with:".Length); break; }
            foreach (WorldObjectSnapshot item in state.SnapshotObjects())
                if (item.id != targetId && item.powered && (string.IsNullOrWhiteSpace(intended) || string.Equals(item.id, intended, StringComparison.Ordinal)))
                    return state.AddConnection(targetId, item.id);
            return false;
        }
        private static bool FreezeSlide(WorldStateService state, string id, Vector2Int direction)
        {
            bool changed = state.SetProperty(id, x => x.frozen = true);
            Vector2Int? goal = null;
            if (state.TryGetObject(id, out WorldObjectSnapshot target))
                foreach (string tag in target.tags ?? Array.Empty<string>())
                    if (tag.StartsWith("slide-goal:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = tag.Substring("slide-goal:".Length).Split(','); int x, y;
                        if (parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y)) goal = new Vector2Int(x, y);
                    }
            for (int i = 0; i < 128; i++)
            {
                if (goal.HasValue && state.TryGetObject(id, out WorldObjectSnapshot current) && current.cell == goal.Value) break;
                if (!state.TryMoveObject(id, direction)) break;
                changed = true;
            }
            return changed;
        }
        private static bool Reveal(WorldStateService state, string id)
        { bool changed = state.SetProperty(id, x => x.revealed = true); foreach (WorldObjectSnapshot item in state.SnapshotObjects()) if (Array.Exists(item.tags ?? Array.Empty<string>(), t => string.Equals(t, "hidden", StringComparison.OrdinalIgnoreCase))) changed |= state.SetProperty(item.id, x => x.revealed = true); return changed; }
        private string FindNearestTarget()
        { WorldStateService state = WorldStateService.Instance; GridMover player = GetComponent<GridMover>(); if (state == null || player == null) return null; string best = null; int distance = interactionRadius + 1; foreach (WorldObjectSnapshot item in state.SnapshotObjects()) { int d = Mathf.Abs(item.cell.x - player.CurrentCell.x) + Mathf.Abs(item.cell.y - player.CurrentCell.y); if (d <= interactionRadius && d < distance && item.spellCompatible) { distance = d; best = item.id; } } return best; }
        private Vector2Int DirectionFromPlayer() { GridMover player = GetComponent<GridMover>(); Vector2Int movement = player == null ? Vector2Int.zero : (player.DestinationCell - player.CurrentCell); return movement == Vector2Int.zero ? lastDirection : movement; }
        private static Vector2Int ClampDirection(Vector2Int value) { if (value == Vector2Int.zero) return Vector2Int.up; return new Vector2Int(Mathf.Clamp(value.x, -1, 1), Mathf.Clamp(value.y, -1, 1)); }
    }
}
