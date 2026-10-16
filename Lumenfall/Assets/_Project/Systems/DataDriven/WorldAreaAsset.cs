using System;
using UnityEngine;
using Lumenfall.Audio;

namespace Lumenfall.DataDriven
{
    [CreateAssetMenu(menuName = "Lumenfall/World Area", fileName = "WorldArea")]
    public sealed class WorldAreaAsset : ScriptableObject
    {
        public MapDefinition definition = new MapDefinition();
        public GameObject areaPrefab;
        public AreaAudioProfile audioProfile;
        public PortalContentAsset[] portals = Array.Empty<PortalContentAsset>();
    }
}
