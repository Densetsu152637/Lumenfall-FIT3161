using System;
using UnityEngine;

namespace Lumenfall.Spells
{
    /// Provisional contiguous trap ordering. Configuration stays authored; saves carry only visits.
    public sealed class IceSlideLane
    {
        public Vector2Int Start { get; private set; }
        public Vector2Int Goal { get; private set; }
        public int TrapCount => Goal.x - Start.x;
        public Vector2Int TrapCell(int index) => Start + Vector2Int.right * (index + 1);
        public bool ContainsFootprint(Vector2Int cell, Vector2Int size)
        { return cell.y == Start.y && Mathf.Max(1, size.y) == 1 && cell.x >= Start.x && cell.x + Mathf.Max(1, size.x) - 1 <= Goal.x; }
        public static bool HasLane(string[] tags)
        { return Array.Exists(tags ?? Array.Empty<string>(), t => t != null && t.StartsWith("slide-lane:", StringComparison.OrdinalIgnoreCase)); }
        public static bool TryParse(string[] tags, out IceSlideLane lane)
        {
            lane = null;
            foreach (string tag in tags ?? Array.Empty<string>())
            {
                if (tag == null || !tag.StartsWith("slide-lane:", StringComparison.OrdinalIgnoreCase)) continue;
                if (lane != null) return false;
                string[] p = tag.Substring("slide-lane:".Length).Split(',');
                if (p.Length != 4 || !int.TryParse(p[0], out int x) || !int.TryParse(p[1], out int y) ||
                    !int.TryParse(p[2], out int gx) || !int.TryParse(p[3], out int gy) ||
                    x < 0 || y < 0 || gy != y || gx <= x || (long)gx - x > 128) return false;
                lane = new IceSlideLane { Start = new Vector2Int(x, y), Goal = new Vector2Int(gx, gy) };
            }
            return lane != null;
        }
        public bool MatchesProgress(WorldObjectSnapshot state)
        {
            if (state.slideInterrupted) return state.visitedTrapCount == 0 && ContainsFootprint(state.cell, state.size);
            return state.visitedTrapCount >= 0 && state.visitedTrapCount <= TrapCount &&
                state.cell == Start + Vector2Int.right * state.visitedTrapCount && ContainsFootprint(state.cell, state.size) &&
                (state.visitedTrapCount == 0 || state.frozen);
        }
    }
}
