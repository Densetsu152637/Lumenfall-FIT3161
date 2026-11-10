using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The model for the late student at the academy gate, whose letter blows into the player's hands (see
    /// <see cref="LateToOrientationScene"/>): a background NPC variant on the shared chibi rig. They are a story extra,
    /// not a catalog character, so they have no introduction and no journal entry, and the asset lives in Resources where
    /// the scene can load it.
    /// </summary>
    [CreateAssetMenu(menuName = "Lumenfall/Gate Cast", fileName = "GateCast")]
    public sealed class GateCast : ScriptableObject
    {
        public const string ResourcePath = "GateCast";

        public GameObject lateStudent;

        private static GateCast loaded;

        public static GateCast Load() => loaded != null ? loaded : loaded = Resources.Load<GateCast>(ResourcePath);
    }
}
