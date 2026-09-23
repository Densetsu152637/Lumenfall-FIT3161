using System;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    [CreateAssetMenu(menuName = "Lumenfall/NPC Catalog", fileName = "NpcCatalog")]
    public sealed class NpcCatalogAsset : ScriptableObject
    {
        public NpcDefinition[] npcs = Array.Empty<NpcDefinition>();
        public NpcLayerDefinition[] layers = Array.Empty<NpcLayerDefinition>();
    }
}
