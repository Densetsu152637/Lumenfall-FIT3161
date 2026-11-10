using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// What the prologue's student is made from (see <see cref="PrologueSet"/>): the player's own witch model, so the
    /// student is the player before they ever reach the academy, with its hat taken off and its robe recoloured into
    /// everyday clothes through the model's customisable material, and the NPC animator, whose humanoid clips (idle,
    /// walk, angry, yelling, sad) play on the witch's humanoid rig. Kept in Resources, where the set can load it.
    /// </summary>
    [CreateAssetMenu(menuName = "Lumenfall/Prologue Art", fileName = "PrologueArt")]
    public sealed class PrologueArt : ScriptableObject
    {
        public const string ResourcePath = "PrologueArt";

        /// <summary>The player's model, as the player prefab in the scene uses it.</summary>
        public GameObject student;
        /// <summary>The witch's customisable material: hair, skin and three cloth colours, each set separately.</summary>
        public Material everydayClothes;
        /// <summary>The NPC animator, for its idle, walk and mood states.</summary>
        public RuntimeAnimatorController animator;

        private static PrologueArt loaded;

        public static PrologueArt Load() => loaded != null ? loaded : loaded = Resources.Load<PrologueArt>(ResourcePath);
    }
}
