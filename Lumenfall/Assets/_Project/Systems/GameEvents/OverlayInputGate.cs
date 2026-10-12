using UnityEngine;

namespace Lumenfall.GameEvents
{
    /// Shared across GameEvents and Runtime without creating an assembly dependency cycle.
    public static class OverlayInputGate
    {
        private static readonly OverlayLockSet Locks = new OverlayLockSet();
        public static bool IsBlocked => Locks.Blocks(null, Time.frameCount);
        public static bool IsBlockedExcept(object owner) => Locks.Blocks(owner, Time.frameCount);
        public static bool Owns(object owner) => Locks.Owns(owner);
        public static void Acquire(object owner) => Locks.Acquire(owner, Time.frameCount);
        public static void Release(object owner) => Locks.Release(owner, Time.frameCount);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Locks.Clear();
    }

}
