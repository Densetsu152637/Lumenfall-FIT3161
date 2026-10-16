using UnityEngine;
using UnityEngine.Serialization;

namespace Lumenfall.DataDriven
{
    [CreateAssetMenu(menuName = "Lumenfall/Portal Connection", fileName = "Portal")]
    public sealed class PortalContentAsset : ScriptableObject
    {
        public string id;
        public WorldAreaAsset area;
        [FormerlySerializedAs("reserved")]
        [Tooltip("Prevents this portal from being used until gameplay or quest logic enables it.")]
        public bool disabled;
        [Tooltip("Keeps the portal present, but refuses entry and tells the player that the door is locked.")]
        public bool blocked;
        [Tooltip("The heading of the notice a blocked portal shows. Empty shows \"Locked\".")]
        public string blockedTitle;
        [Tooltip("The notice a blocked portal shows. Empty shows \"The door is locked\".")]
        public string blockedMessage;
        public PortalContentAsset destination;

        public void SetDisabled(bool value)
        {
            disabled = value;
        }

        public void SetBlocked(bool value)
        {
            blocked = value;
        }
    }
}
