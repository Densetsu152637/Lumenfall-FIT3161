using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The models the secret rooms are furnished with (see <see cref="SecretRooms"/>). The rooms are built in code, so
    /// the asset lives in Resources where the code can load it. Furniture comes from the Kenney Furniture Kit. The three
    /// developers start from background NPC models on the shared chibi rig, recoloured and dressed by
    /// <see cref="DevChibis"/>; <see cref="person"/> is the janitor.
    /// </summary>
    [CreateAssetMenu(menuName = "Lumenfall/Secret Room Art", fileName = "SecretRoomArt")]
    public sealed class SecretRoomArt : ScriptableObject
    {
        public const string ResourcePath = "SecretRoomArt";

        public GameObject person;
        public GameObject devNick;
        public GameObject devGlasses;
        public GameObject devCurls;
        public GameObject desk;
        public GameObject deskChair;
        public GameObject computerScreen;
        public GameObject keyboard;
        public GameObject laptop;
        public GameObject bed;
        public GameObject lamp;
        public GameObject bookcase;
        public GameObject books;
        public GameObject plant;

        private static SecretRoomArt loaded;

        /// <summary>The shared asset, or null if it is missing (the rooms then build without furniture).</summary>
        public static SecretRoomArt Load()
        {
            if (loaded == null) loaded = Resources.Load<SecretRoomArt>(ResourcePath);
            return loaded;
        }
    }
}
