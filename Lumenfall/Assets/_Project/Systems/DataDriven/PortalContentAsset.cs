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
        public PortalContentAsset destination;
        public Vector2Int arrivalOffset;
        public Vector2Int ClampedFootprint => new Vector2Int(Mathf.Max(1, footprint.x), Mathf.Max(1, footprint.y));
    }
}
