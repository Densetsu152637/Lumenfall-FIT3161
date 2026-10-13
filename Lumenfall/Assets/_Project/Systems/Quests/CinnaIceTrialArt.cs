using UnityEngine;

namespace Lumenfall.Quests
{
    /// <summary>
    /// Optional art for Cinna Cirra's ice trial (<see cref="CinnaIceTrialRoom"/>), the Out of Alignment relic
    /// (<see cref="CinnaAlignmentRelic"/>) and the campus entrance (<see cref="CinnaIceTrialEntrance"/>). All three are built
    /// at runtime, so they read their models and materials from <c>Resources/CinnaIceTrialArt</c> and fall back to coloured
    /// primitives when it, or any one slot, is missing. <c>Lumenfall/World/Build C03 Ice Trial Art</c> creates and fills it.
    /// </summary>
    public sealed class CinnaIceTrialArt : ScriptableObject
    {
        public const string ResourcePath = "CinnaIceTrialArt";
        /// <summary>Name of the relic children that take its state colours; every other part keeps its own material.</summary>
        public const string CrystalPartName = "Crystal";

        // The baked dressing in C03.IceTrial.prefab, which CinnaIceTrialAmbience finds by these names.
        public const string DressingRootName = "[Ice Trial Dressing]";
        public const string CrystalGroupName = "Crystals";
        public const string RockGroupName = "Snowy Rocks";
        public const string BrazierGroupName = "Braziers";
        public const string ColdLightGroupName = "Cold Lights";
        public const string SnowfallName = "Snowfall";
        public const string MistName = "Mist";
        public const string FlameName = "Flame";
        public const string FrostCrownName = "Frost Crown";
        public const string GlintsName = "Glints";
        public const string MotesName = "Motes";
        public const string GradeName = "Frozen Grade";
        public const string ReflectionProbeName = "Ice Reflections";

        [Header("Ice trial, thawed")]
        [Tooltip("Stone floor before Glaciate. When set, the room blends between this thawed look and the frozen one.")]
        public Material thawedFloorMaterial;
        public Material thawedWallMaterial;
        public Material waterMaterial;

        [Tooltip("Dark stone lining the sunken pool basin.")]
        public Material basinMaterial;

        [Header("Ice trial, frozen")]
        [Tooltip("Snow texture material (used by the fallback looks and the campus frost patch).")]
        public Material floorMaterial;
        [Tooltip("Ice that climbs the walls.")]
        public Material wallMaterial;
        [Tooltip("Ice texture material (used by the fallback looks).")]
        public Material iceMaterial;
        [Tooltip("Ice Trial Frost shader: a frost dusting revealed over the floor from the relic.")]
        public Material snowSurfaceMaterial;
        [Tooltip("Ice Trial Frost shader: the ice revealed over the pool from the relic.")]
        public Material iceSurfaceMaterial;
        [Tooltip("Crystal spikes that grow along the wall tops.")]
        public GameObject[] wallSpikes;
        [Tooltip("An icicle pivoted at its top, hung from the walls' inner faces.")]
        public GameObject icicle;
        [Tooltip("Soft particle material for breath and slide spray.")]
        public Material mistMaterial;

        [Header("Switches")]
        [Tooltip("Cut-out rune ring on each switch plate; tinted and lit through a property block.")]
        public Material runeMaterial;
        public Material switchBaseMaterial;
        [Tooltip("Particle material for the frost burst when a switch toggles.")]
        public Material sparkleMaterial;

        [Header("Props")]
        [Tooltip("Crystal pillar standing in the pool, pivoted at its base.")]
        public GameObject icePillar;
        [Tooltip("Pedestal the relic is set on, pivoted at its base; its top sits at CinnaIceTrialRoom.StandHeight.")]
        public GameObject pedestal;
        [Tooltip("Crystal outside the seal gap in the east wall, tinted with the seal.")]
        public GameObject sealCrystal;

        [Header("Relic and campus entrance")]
        [Tooltip("Relic model pivoted at its base, about 0.8 tall. Children named Crystal take the state colours.")]
        public GameObject relic;
        [Tooltip("Frost patch under the campus entrance ring.")]
        public Material frostMaterial;
        [Tooltip("Small crystals set around the campus entrance tile.")]
        public GameObject[] entranceCrystals;

        private static CinnaIceTrialArt cached;

        /// <summary>The art asset, or null when it has not been built.</summary>
        public static CinnaIceTrialArt Instance
        {
            get
            {
                if (cached == null) cached = Resources.Load<CinnaIceTrialArt>(ResourcePath);
                return cached;
            }
        }
    }
}
