using Lumenfall.Story;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Sets or lifts the sequence break's lockout (<see cref="SequenceBreakLockout"/>) without playing the ending, for
    /// testing the empty screen and Miki's bargain. The lockout lives in the editor's PlayerPrefs, so it carries into the
    /// next time play mode starts.
    /// </summary>
    public static class SequenceBreakLockoutMenu
    {
        [MenuItem("Lumenfall/Secrets/Sequence Break/Lock Out Next Launch")]
        public static void Lock()
        {
            SequenceBreakLockout.Arm();
            Debug.Log("The next launch opens on the sequence break's empty screen.");
        }

        [MenuItem("Lumenfall/Secrets/Sequence Break/Clear Lockout")]
        public static void Clear()
        {
            SequenceBreakLockout.Clear();
            Debug.Log("The sequence break lockout is cleared; the game opens normally.");
        }

        [MenuItem("Lumenfall/Secrets/Sequence Break/Lock Out Next Launch", true)]
        private static bool CanLock() => !SequenceBreakLockout.IsLockedOut;

        [MenuItem("Lumenfall/Secrets/Sequence Break/Clear Lockout", true)]
        private static bool CanClear() => SequenceBreakLockout.IsLockedOut;
    }
}
