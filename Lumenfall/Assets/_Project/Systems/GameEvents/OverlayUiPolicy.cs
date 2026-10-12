using System;
using System.Collections.Generic;

namespace Lumenfall.GameEvents
{
    /// Owned UI locks. Opening/closing input is consumed through the rest of that frame.
    public sealed class OverlayLockSet
    {
        private readonly HashSet<object> owners = new HashSet<object>();
        private int transitionFrame = -1;
        public int Count => owners.Count;
        public bool Owns(object owner) => owner != null && owners.Contains(owner);
        public void Acquire(object owner, int frame)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (owners.Add(owner)) transitionFrame = frame;
        }
        public void Release(object owner, int frame)
        {
            if (owner != null && owners.Remove(owner)) transitionFrame = frame;
        }
        public bool Blocks(object exceptOwner, int frame)
        {
            if (transitionFrame == frame) return true;
            return owners.Count > (Owns(exceptOwner) ? 1 : 0);
        }
        public void Clear() { owners.Clear(); transitionFrame = -1; }
    }

    public static class UiFocusIndex
    {
        public static int Move(int current, int direction, int count, Func<int, bool> enabled = null)
        {
            if (count <= 0) return -1;
            current = Math.Max(0, Math.Min(count - 1, current));
            int step = direction < 0 ? -1 : 1;
            int candidate = current;
            for (int i = 0; i < count; i++)
            {
                if (direction != 0 || i > 0) candidate = (candidate + step + count) % count;
                if (enabled == null || enabled(candidate)) return candidate;
            }
            return -1;
        }
    }
}
