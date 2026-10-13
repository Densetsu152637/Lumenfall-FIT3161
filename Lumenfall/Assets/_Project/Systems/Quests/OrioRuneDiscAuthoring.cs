using UnityEngine;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Marks the editable Rune Dais hierarchy in the Deep Forest prefab. Child names are the stable puzzle IDs used
    /// by <see cref="OrioRuneDiscPuzzle"/>; moving those children changes their registered grid cells at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OrioRuneDiscAuthoring : MonoBehaviour { }
}
