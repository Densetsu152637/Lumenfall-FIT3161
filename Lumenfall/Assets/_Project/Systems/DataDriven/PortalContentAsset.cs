using UnityEngine;

namespace Lumenfall.DataDriven
{
    [CreateAssetMenu(menuName = "Lumenfall/Portal Connection", fileName = "Portal")]
    public sealed class PortalContentAsset : ScriptableObject
    {
        public string id;
        public WorldAreaAsset area;
        public Vector2Int cell;
        public Vector2Int footprint = Vector2Int.one;
        public bool reserved;
        [Tooltip("Reserved gates stay closed unless explicitly enabled and allowedFlag is true.")]
        public bool allowReservedTravel;
        public string allowedFlag;
        public string[] requiredFlags = System.Array.Empty<string>();
        public PortalContentAsset destination;
        public Vector2Int arrivalOffset;
    }
}
