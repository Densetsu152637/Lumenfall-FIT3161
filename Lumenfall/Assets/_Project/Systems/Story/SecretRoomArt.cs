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
        /// <summary>
        /// The forgotten man, already dressed (white hair and beard, spectacles, coat), baked by
        /// <c>Lumenfall/Secrets/Bake Forgotten Man</c>. When it is missing the room dresses a model at runtime instead.
        /// </summary>
        public GameObject forgottenMan;
        /// <summary>
        /// PolyOne Studio's rigged VR hands (CC BY 4.0, <c>Assets/ThirdParty/PolyOne</c>). In the sequence break's ending
        /// both hands press the game from either side until it breaks between them (<see cref="ScreenCrush"/>); when
        /// this is missing, the ending shatters the screen instead.
        /// </summary>
        public GameObject crushingHand;

        private static SecretRoomArt loaded;

        /// <summary>The shared asset, or null if it is missing (the rooms then build without furniture).</summary>
        public static SecretRoomArt Load()
        {
            if (loaded == null) loaded = Resources.Load<SecretRoomArt>(ResourcePath);
            return loaded;
        }
    }
}
