using UnityEngine;

namespace Lumenfall.CameraEffects
{
    public enum CameraAngle
    {
        /// <summary>Due south of the player: the grid lines up with the screen, so W moves straight up.</summary>
        Straight,
        /// <summary>The original corner view from the south-east: W moves up and to the right.</summary>
        Diagonal
    }

    /// <summary>
    /// The player's choice of gameplay camera angle, saved in PlayerPrefs outside story save slots. Movement keys are
    /// never remapped, so the choice only changes which way the world is seen from.
    /// </summary>
    public static class CameraAngleSetting
    {
        private const string Key = "lumenfall.camera-angle";
        public const CameraAngle Default = CameraAngle.Straight;

        private static int cache = -1;

        public static CameraAngle Current
        {
            get
            {
                if (cache < 0) cache = PlayerPrefs.GetInt(Key, (int)Default) == (int)CameraAngle.Diagonal ? 1 : 0;
                return (CameraAngle)cache;
            }
            set
            {
                cache = value == CameraAngle.Diagonal ? 1 : 0;
                PlayerPrefs.SetInt(Key, cache);
                PlayerPrefs.Save();
            }
        }

        /// <summary>Degrees the resting camera offset is turned around the player for <see cref="Current"/>.</summary>
        public static float Yaw => Current == CameraAngle.Diagonal ? -45f : 0f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRuntime() => cache = -1;
    }
}
