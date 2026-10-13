using System;
using System.IO;
using Lumenfall.DataDriven;
using Lumenfall.Quests;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Builds the art for Cinna Cirra's ice trial (<c>C03.IceTrial</c>) and the Out of Alignment relic from the CC0 models in
    /// <c>ThirdParty/PolyPizza</c> and textures in <c>ThirdParty/ambientCG</c>: the materials and prefabs under
    /// <c>Puzzle Elements/Ice Trial</c>, the <see cref="CinnaIceTrialArt"/> asset the runtime builders read, and the static
    /// dressing baked into the area prefab: columns, crystal banks, snowy rocks, braziers on the corner columns, cold lights,
    /// snowfall and pool mist. <see cref="CinnaIceTrialAmbience"/> finds the dressing groups by name and shows the frozen
    /// pieces only once the pool is frozen. Every piece stays on wall cells or outside the map, so no walkable tile changes.
    /// It also draws the switch rune texture. Re-running replaces the previous output.
    /// </summary>
    public static class C03IceTrialArtTool
    {
        private const string AreaPath = "Assets/_Project/World/Areas/Prefabs/C03.IceTrial.prefab";
        private const string OutputPath = "Assets/_Project/Art/Environment/Puzzle Elements/Ice Trial";
        private const string ResourcesPath = "Assets/_Project/Systems/Quests/Resources";
        private const string ModelPath = "Assets/ThirdParty/PolyPizza/";
        private const string TexturePath = "Assets/ThirdParty/ambientCG/";
        private const string StoneTexturePath = "Assets/_Project/Art/Environment/ThirdPartyExtracted/FantasyCastle/Textures/ground_2_";
        private const string RugTexture = "Assets/_Project/Art/Environment/Buildings/Textures/Carpet Base Color.png";
        private const string FurniturePath = "Assets/_Project/Art/Environment/Furnitures/";
        private const string CaveRockPath = "Assets/_Project/Art/Environment/ThirdPartyExtracted/Outdoor Environment - Polytope Studio/Lowpoly_Environments/Prefabs/Rocks/PT_Generic_Rock_01.prefab";
        private const string AreaAssetPath = "Assets/_Project/World/Areas/C03.IceTrial.asset";
        private const string GradePath = OutputPath + "/VP_IceTrial_Frozen.asset";
        private const string RuneTextureName = "TEX_IceTrial_Rune.png";
        private const int RuneTextureSize = 256;
        private const string RequestFileName = "RunC03IceTrialArt.once";
        // Short posts on the low south parapet, so they do not hide the pool row behind it from the camera.
        private const float ColumnHeight = 0.75f;
        // The room is a platform over a chasm; its floor is this far down.
        private const float ChasmDepth = 6f;
        private const float RelicBaseHeight = 0.3f;
        private const float RelicCrystalHeight = 0.5f;

        [MenuItem("Lumenfall/World/Build C03 Ice Trial Art")]
        public static void Execute()
        {
            EnsureFolder(OutputPath);
            EnsureFolder(ResourcesPath);
            foreach (string texture in new[] { "Ice002/Ice002_1K-JPG_NormalGL.jpg", "Ice003/Ice003_1K-JPG_NormalGL.jpg", "Snow013/Snow013_1K-JPG_NormalGL.jpg" })
                MarkNormalMap(TexturePath + texture);

            Palette palette = new Palette
            {
                floor = SaveLit("MAT_IceTrial_Floor", AmbientCg("Snow013"), new Color(0.84f, 0.89f, 0.96f), 0.25f),
                wall = SaveLit("MAT_IceTrial_Wall", AmbientCg("Ice003"), new Color(0.72f, 0.87f, 0.98f), 0.75f),
                water = SaveLit("MAT_IceTrial_Water", default, new Color(0.06f, 0.2f, 0.36f), 0.95f),
                ice = SaveLit("MAT_IceTrial_Ice", AmbientCg("Ice002"), new Color(0.8f, 0.95f, 1f), 0.9f),
                stone = SaveLit("MAT_IceTrial_Stone", default, new Color(0.62f, 0.68f, 0.76f), 0.3f),
                frost = SaveLit("MAT_IceTrial_Frost", AmbientCg("Snow013"), new Color(0.9f, 0.95f, 1f), 0.35f),
                stoneFloor = SaveLit("MAT_IceTrial_StoneFloor", StoneTextures(), new Color(0.82f, 0.8f, 0.76f), 0.2f),
                stoneWall = SaveLit("MAT_IceTrial_StoneWall", StoneTextures(), new Color(0.62f, 0.62f, 0.64f), 0.15f),
                basin = SaveLit("MAT_IceTrial_Basin", StoneTextures(), new Color(0.3f, 0.32f, 0.36f), 0.3f),
                caveGround = SaveLit("MAT_IceTrial_CaveGround", StoneTextures(), new Color(0.3f, 0.32f, 0.37f), 0.15f),
                caveRock = SaveLit("MAT_IceTrial_CaveRock", default, new Color(0.2f, 0.22f, 0.28f), 0.12f),
                rug = SaveLit("MAT_IceTrial_Rug", (RugTexture, null), new Color(0.95f, 0.85f, 0.8f), 0.1f),
                crystal = SaveLit("MAT_IceTrial_Crystal", default, Color.white, 0.85f),
                snowfall = SaveParticle("MAT_IceTrial_Snowfall", Color.white, false),
                flame = SaveParticle("MAT_IceTrial_Flame", new Color(1f, 0.62f, 0.25f), true),
                mist = SaveParticle("MAT_IceTrial_Mist", new Color(0.82f, 0.92f, 1f, 0.35f), false),
                glint = SaveParticle("MAT_IceTrial_Glint", new Color(0.85f, 0.97f, 1f), true),
            };
            // Open water: the darker tint plus a faint ripple normal that the ambience drifts while it is thawed.
            palette.water.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AmbientCg("Ice003").normal));
            palette.water.SetFloat("_BumpScale", 0.35f);
            palette.water.EnableKeyword("_NORMALMAP");
            palette.crystal.EnableKeyword("_EMISSION");
            palette.crystal.SetColor("_EmissionColor", new Color(0.2f, 0.26f, 0.36f));
            palette.crystal.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Material rune = SaveRune(DrawRuneTexture());
            Material snowSurface = SaveFrostSurface("MAT_IceTrial_SnowSurface", AmbientCg("Snow013"), new Color(0.9f, 0.95f, 1f), 0.35f, 0.6f);
            Material iceSurface = SaveFrostSurface("MAT_IceTrial_IceSurface", AmbientCg("Ice002"), new Color(0.74f, 0.92f, 1f), 0.93f, 1f);
            AssetDatabase.SaveAssets();

            GameObject pedestal = SavePrefab("PF_IceTrial_Pedestal", root =>
                AddModel(root, "Quaternius/ColumnRound.glb", "Column", Vector3.zero, 0f, CinnaIceTrialRoom.StandHeight, palette.stone));
            GameObject relic = SavePrefab("PF_Cinna_OrientationRelic", root =>
            {
                AddModel(root, "Quaternius/Pedestal.glb", "Base", Vector3.zero, 0f, RelicBaseHeight, palette.stone);
                AddModel(root, "Quaternius/BigCrystal.glb", CinnaIceTrialArt.CrystalPartName, Vector3.up * RelicBaseHeight, 0f, RelicCrystalHeight, palette.crystal);
            });
            GameObject seal = SavePrefab("PF_IceTrial_SealCrystal", root =>
                AddModel(root, "Quaternius/BigCrystal.glb", "Crystal", Vector3.zero, 20f, 1.3f, palette.crystal));
            GameObject entranceA = SavePrefab("PF_IceTrial_EntranceCrystalA", root =>
                AddModel(root, "iPoly3D/Crystal_Shards.glb", "Crystal", Vector3.zero, 0f, 0.34f));
            GameObject entranceB = SavePrefab("PF_IceTrial_EntranceCrystalB", root =>
                AddModel(root, "iPoly3D/Crystal_ClusterB.glb", "Crystal", Vector3.zero, 0f, 0.28f));
            GameObject icePillar = SavePrefab("PF_IceTrial_Pillar", root =>
                AddModel(root, "iPoly3D/Crystal_ColumnsA.glb", "Crystal", Vector3.zero, 0f, 1.15f));
            GameObject spikeA = SavePrefab("PF_IceTrial_WallSpikeA", root => AddModel(root, "iPoly3D/Crystal_Shards.glb", "Crystal", Vector3.zero, 0f, 0.32f));
            GameObject spikeB = SavePrefab("PF_IceTrial_WallSpikeB", root => AddModel(root, "iPoly3D/Crystal_ColumnsB.glb", "Crystal", Vector3.zero, 0f, 0.26f));
            GameObject spikeC = SavePrefab("PF_IceTrial_WallSpikeC", root => AddModel(root, "iPoly3D/Crystal_Block.glb", "Crystal", Vector3.zero, 0f, 0.2f));
            // An icicle hangs from its pivot: the shard is flipped point-down and its top moved up to the pivot.
            GameObject icicle = SavePrefab("PF_IceTrial_Icicle", root =>
            {
                Transform flip = NewChild("Flip", root);
                flip.localRotation = Quaternion.Euler(180f, 0f, 0f);
                HangFromTop(AddModel(flip, "iPoly3D/Crystal_Shards.glb", "Icicle", Vector3.zero, 0f, 0.36f), root.position.y);
            });

            string artPath = ResourcesPath + "/" + CinnaIceTrialArt.ResourcePath + ".asset";
            CinnaIceTrialArt art = AssetDatabase.LoadAssetAtPath<CinnaIceTrialArt>(artPath);
            if (art == null)
            {
                art = ScriptableObject.CreateInstance<CinnaIceTrialArt>();
                AssetDatabase.CreateAsset(art, artPath);
            }
            art.thawedFloorMaterial = palette.stoneFloor;
            art.thawedWallMaterial = palette.stoneWall;
            art.basinMaterial = palette.basin;
            art.floorMaterial = palette.floor;
            art.wallMaterial = palette.wall;
            art.waterMaterial = palette.water;
            art.iceMaterial = palette.ice;
            art.snowSurfaceMaterial = snowSurface;
            art.iceSurfaceMaterial = iceSurface;
            art.wallSpikes = new[] { spikeA, spikeB, spikeC };
            art.icicle = icicle;
            art.mistMaterial = palette.mist;
            art.runeMaterial = rune;
            art.switchBaseMaterial = palette.stone;
            art.sparkleMaterial = palette.snowfall;
            art.pedestal = pedestal;
            art.icePillar = icePillar;
            art.sealCrystal = seal;
            art.relic = relic;
            art.frostMaterial = palette.frost;
            art.entranceCrystals = new[] { entranceA, entranceB };
            EditorUtility.SetDirty(art);

            VolumeProfile grade = SaveFrozenGrade();
            BuildDressing(palette, grade);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("C03_ICE_TRIAL_ART built: materials, prefabs, CinnaIceTrialArt and the C03.IceTrial dressing.");
        }

        private sealed class Palette
        {
            public Material floor, wall, water, ice, stone, frost, stoneFloor, stoneWall, basin, caveGround, caveRock, rug, crystal;
            public Material snowfall, flame, mist, glint;
        }

        [InitializeOnLoadMethod]
        private static void ExecuteRequested()
        {
            string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", RequestFileName);
            if (!File.Exists(requestPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(requestPath)) return;
                File.Delete(requestPath);
                Execute();
            };
        }

        private static void BuildDressing(Palette palette, VolumeProfile grade)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(AreaPath);
            try
            {
                Transform previous = root.transform.Find(CinnaIceTrialArt.DressingRootName);
                if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
                Transform dressing = NewChild(CinnaIceTrialArt.DressingRootName, root.transform);
                Transform columns = NewChild("Columns", dressing);
                Transform crystals = NewChild(CinnaIceTrialArt.CrystalGroupName, dressing);
                Transform rocks = NewChild(CinnaIceTrialArt.RockGroupName, dressing);
                Transform braziers = NewChild(CinnaIceTrialArt.BrazierGroupName, dressing);
                Transform coldLights = NewChild(CinnaIceTrialArt.ColdLightGroupName, dressing);

                // Posts on the south wall, the only wall left: either side of the pool entrance and at both ends. Each
                // carries a brazier. Columns stay in both looks.
                foreach (Vector2 cell in new[] { new Vector2(0f, 2f), new Vector2(4f, 2f), new Vector2(6f, 2f), new Vector2(11f, 2f) })
                {
                    AddModel(columns, "Quaternius/Column.glb", $"Column ({cell.x:0},{cell.y:0})", new Vector3(cell.x, 0f, cell.y), 0f, ColumnHeight, palette.stone);
                    BuildBrazier(braziers, cell, palette.flame);
                }

                // Crystal banks rising out of the chasm on the three open sides, tall enough for their tips to glow just
                // below the platform; they grow in with the frost. The seal crystal at (12,8) is placed at runtime.
                PlaceChasmCrystal(crystals, "Crystal_ColumnsA", -2.6f, 4.0f, 4.6f, 10f);
                PlaceChasmCrystal(crystals, "Crystal_ClusterA", -3.4f, 7.2f, 4.0f, 70f);
                PlaceChasmCrystal(crystals, "Crystal_Shards", -2.4f, 10.4f, 5.0f, 200f);
                PlaceChasmCrystal(crystals, "Crystal_ColumnsB", -3.0f, 13.6f, 4.4f, 120f);
                PlaceChasmCrystal(crystals, "Crystal_ClusterB", 1.0f, 15.6f, 4.2f, 300f);
                PlaceChasmCrystal(crystals, "Crystal_Shards", 4.2f, 15.0f, 5.2f, 160f);
                PlaceChasmCrystal(crystals, "Crystal_ColumnsA", 7.4f, 15.8f, 4.6f, 40f);
                PlaceChasmCrystal(crystals, "Crystal_ClusterA", 10.4f, 15.2f, 4.0f, 250f);
                PlaceChasmCrystal(crystals, "Crystal_ColumnsB", 13.8f, 13.4f, 4.4f, 180f);
                PlaceChasmCrystal(crystals, "Crystal_Shards", 13.4f, 10.6f, 5.0f, 75f);
                PlaceChasmCrystal(crystals, "Crystal_ClusterB", 14.4f, 5.8f, 4.2f, 220f);
                PlaceChasmCrystal(crystals, "Crystal_ColumnsA", 13.2f, 3.4f, 4.6f, 330f);

                AddModel(rocks, "Quaternius/RockSnow.glb", "Rock Snow W", new Vector3(-1.9f, 0f, 1.9f), 30f, 0.8f);
                AddModel(rocks, "Quaternius/RockSnow.glb", "Rock Snow E", new Vector3(12.9f, 0f, 1.9f), 200f, 0.9f);

                BuildSnowfall(dressing, palette.snowfall);
                BuildMist(dressing, palette.mist);
                BuildGlints(dressing, palette.glint);
                BuildMotes(dressing, palette.glint);
                BuildGrade(dressing, grade);
                BuildProbe(dressing);
                BuildCave(NewChild("Cave", dressing), palette);
                BuildLobby(NewChild("Lobby", dressing), palette);
                AddLight(coldLights, "North Chasm Glow", new Vector3(5.5f, -1.6f, 15f), new Color(0.55f, 0.8f, 1f), 10f, 2.2f);
                AddLight(coldLights, "West Chasm Glow", new Vector3(-2.8f, -1.6f, 8f), new Color(0.55f, 0.8f, 1f), 9f, 1.8f);
                AddLight(coldLights, "East Chasm Glow", new Vector3(13.8f, -1.6f, 8f), new Color(0.55f, 0.8f, 1f), 9f, 1.8f);
                AddLight(coldLights, "Seal Light", new Vector3(12.4f, 1.6f, 8f), new Color(0.62f, 0.86f, 1f), 4f, 1.2f);
                AddLight(coldLights, "Pool Light", new Vector3(5.5f, 2.6f, 7.5f), new Color(0.7f, 0.86f, 1f), 9f, 0.9f);

                PrefabUtility.SaveAsPrefabAsset(root, AreaPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void PlaceChasmCrystal(Transform parent, string model, float x, float y, float height, float yaw) =>
            AddModel(parent, "iPoly3D/" + model + ".glb", $"{model} ({x:0.#},{y:0.#})", new Vector3(x, -ChasmDepth, y), yaw, height);

        // A flame and warm light on top of a corner column while the room is thawed; when it freezes the flame goes out and
        // a frost crown grows in its place.
        private static void BuildBrazier(Transform parent, Vector2 cell, Material flame)
        {
            Transform brazier = NewChild($"Brazier ({cell.x:0},{cell.y:0})", parent);
            brazier.localPosition = new Vector3(cell.x, ColumnHeight, cell.y);
            GameObject host = new GameObject(CinnaIceTrialArt.FlameName);
            host.transform.SetParent(brazier, false);
            host.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), new Color(1f, 0.45f, 0.15f));
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 26f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.08f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(new Color(1f, 0.4f, 0.2f), 0.15f);
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = flame;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            AddLight(brazier, "Warm Light", new Vector3(0f, 0.35f, 0f), new Color(1f, 0.64f, 0.32f), 4.5f, 1.5f);
            AddModel(brazier, "iPoly3D/Crystal_Shards.glb", CinnaIceTrialArt.FrostCrownName, Vector3.zero, 35f * cell.x + 20f * cell.y, 0.42f);
        }

        // Light snow drifting down over the whole room; world-space so it does not follow the area root. The ambience
        // starts it when the pool freezes.
        private static void BuildSnowfall(Transform parent, Material material)
        {
            GameObject host = new GameObject(CinnaIceTrialArt.SnowfallName);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(5.5f, 4.5f, 7f);
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = 7f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.45f, 0.75f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.075f);
            main.startColor = new Color(1f, 1f, 1f, 0.85f);
            main.gravityModifier = 0.02f;
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 45f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            // A box emits along its local +Z; turning it 90 degrees about X points that down, so local Y spans the room's depth.
            shape.scale = new Vector3(15f, 17f, 0.1f);
            shape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.3f;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Slow, faint cold mist hanging just above the frozen pool.
        private static void BuildMist(Transform parent, Material material)
        {
            GameObject host = new GameObject(CinnaIceTrialArt.MistName);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(5.5f, 0.25f, 7.5f);
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
            main.startSize = new ParticleSystem.MinMaxCurve(1.4f, 2.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 7f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(9.5f, 0.1f, 9.5f);
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(Color.white, 0.4f);
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        // Fades in to full alpha at peak, then out, shading from white toward endColour.
        private static ParticleSystem.MinMaxGradient Fade(Color endColour, float peak)
        {
            Gradient gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(endColour, 1f) },
                             new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, peak), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(gradient);
        }

        // Short-lived twinkles on the frozen pool.
        private static void BuildGlints(Transform parent, Material material)
        {
            ParticleSystem particles = NewParticles(parent, CinnaIceTrialArt.GlintsName, new Vector3(5.5f, -0.02f, 7.5f), material);
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
            main.startColor = new Color(0.85f, 0.97f, 1f);
            main.maxParticles = 80;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 16f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(9.8f, 0.02f, 9.8f);
            ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f)));
        }

        // Slow floating motes of cold light drifting around the room.
        private static void BuildMotes(Transform parent, Material material)
        {
            ParticleSystem particles = NewParticles(parent, CinnaIceTrialArt.MotesName, new Vector3(5.5f, 1.3f, 7f), material);
            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.12f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.065f);
            main.startColor = new Color(0.6f, 0.9f, 1f);
            main.maxParticles = 90;
            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 9f;
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(15f, 2.2f, 17f);
            ParticleSystem.NoiseModule noise = particles.noise;
            noise.enabled = true;
            noise.strength = 0.18f;
            noise.frequency = 0.25f;
            ParticleSystem.ColorOverLifetimeModule colour = particles.colorOverLifetime;
            colour.enabled = true;
            colour.color = Fade(Color.white, 0.5f);
        }

        private static ParticleSystem NewParticles(Transform parent, string name, Vector3 position, Material material)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;
            ParticleSystem particles = host.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.playOnAwake = false;
            main.loop = true;
            main.prewarm = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            ParticleSystemRenderer renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particles;
        }

        // A room-wide cold grade the ambience fades in with the frost. Global, but it lives in the area, so it only applies
        // while the ice trial is the active area.
        private static void BuildGrade(Transform parent, VolumeProfile profile)
        {
            GameObject host = new GameObject(CinnaIceTrialArt.GradeName);
            host.transform.SetParent(parent, false);
            Volume volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
        }

        private static VolumeProfile SaveFrozenGrade()
        {
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(GradePath) != null) AssetDatabase.DeleteAsset(GradePath);
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, GradePath);
            ColorAdjustments colour = profile.Add<ColorAdjustments>(true);
            colour.saturation.Override(-18f);
            colour.contrast.Override(10f);
            colour.colorFilter.Override(new Color(0.86f, 0.95f, 1f));
            colour.postExposure.Override(-0.05f);
            WhiteBalance balance = profile.Add<WhiteBalance>(true);
            balance.temperature.Override(-22f);
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(1.1f);
            bloom.threshold.Override(0.85f);
            bloom.tint.Override(new Color(0.75f, 0.9f, 1f));
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.36f);
            vignette.color.Override(new Color(0.03f, 0.06f, 0.12f));
            foreach (VolumeComponent component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // Rendered on request by the ambience, so the ice reflects the crystals, braziers and beams of the current look.
        private static void BuildProbe(Transform parent)
        {
            GameObject host = new GameObject(CinnaIceTrialArt.ReflectionProbeName);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = new Vector3(5.5f, 0.9f, 7f);
            ReflectionProbe probe = host.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.size = new Vector3(13f, 3.5f, 15f);
            probe.boxProjection = true;
            probe.resolution = 128;
            probe.hdr = true;
            probe.importance = 5;
            probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
            probe.backgroundColor = new Color(0.03f, 0.05f, 0.09f);
        }

        // The cavern: solid ground in front of and beside Cinna's lobby, whose edge drops away as a cliff face; the chasm
        // floor far below on the west, north and east; a ring of tall cliffs rising out of it; and mist hanging over the
        // drop so its depth fades into the dark.
        private static void BuildCave(Transform parent, Palette palette)
        {
            Vector2Int size = MapSize();
            float minX = -0.5f, minZ = -0.5f, maxX = size.x - 0.5f, maxZ = size.y - 0.5f;
            const float reach = 14f;
            const float lobbyEdge = 2.5f;
            AddBlock(parent, "Cave Ground S", new Rect(minX - reach, minZ - reach, maxX - minX + reach * 2f, reach), 0f, ChasmDepth, palette.caveGround);
            AddBlock(parent, "Cave Ground SW", new Rect(minX - reach, minZ, reach, lobbyEdge - minZ), 0f, ChasmDepth, palette.caveGround);
            AddBlock(parent, "Cave Ground SE", new Rect(maxX, minZ, reach, lobbyEdge - minZ), 0f, ChasmDepth, palette.caveGround);
            AddBlock(parent, "Chasm Floor", new Rect(minX - reach, lobbyEdge, maxX - minX + reach * 2f, maxZ - lobbyEdge + reach), -ChasmDepth, 0.2f, palette.caveRock);
            Vector3[] cliffs =
            {
                new Vector3(-6.5f, 0f, 4.5f), new Vector3(-7.2f, 0f, 8.5f), new Vector3(-6.8f, 0f, 12.5f), new Vector3(-5.5f, 0f, 16.5f),
                new Vector3(-1.5f, 0f, 18.6f), new Vector3(2.5f, 0f, 19.4f), new Vector3(6.5f, 0f, 19.8f), new Vector3(10.5f, 0f, 19.3f),
                new Vector3(14.5f, 0f, 18.4f), new Vector3(17.6f, 0f, 14.5f), new Vector3(18.2f, 0f, 10.5f), new Vector3(17.8f, 0f, 6.5f),
            };
            for (int i = 0; i < cliffs.Length; i++)
                AddModel(parent, CaveRockPath, "Cliff " + (i + 1), cliffs[i] + Vector3.down * ChasmDepth, i * 67f % 360f,
                    ChasmDepth + 1.8f + (i * 37 % 5) * 0.5f, palette.caveRock);
            foreach (Vector3 spot in new[] { new Vector3(-3f, 0f, 5f), new Vector3(-3.4f, 0f, 11f), new Vector3(2f, 0f, 16.5f),
                                             new Vector3(9f, 0f, 16.8f), new Vector3(14.4f, 0f, 11.5f), new Vector3(14.2f, 0f, 5f) })
            {
                GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Quad);
                blob.name = "Chasm Mist";
                UnityEngine.Object.DestroyImmediate(blob.GetComponent<Collider>());
                blob.transform.SetParent(parent, false);
                blob.transform.localPosition = spot + Vector3.down * 2.2f;
                blob.transform.localRotation = Quaternion.Euler(90f, spot.x * 13f, 0f);
                blob.transform.localScale = new Vector3(10f, 10f, 1f);
                Renderer renderer = blob.GetComponent<Renderer>();
                renderer.sharedMaterial = palette.mist;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        // Cinna's lobby: a rug down the strip in front of the pool (flat, so the walkable cells are unchanged), a desk with
        // books and candles beyond the west end, and a bookcase and chest beyond the east end.
        // Its candle stays lit when the room freezes, so the lobby keeps a warm corner.
        private static void BuildLobby(Transform parent, Palette palette)
        {
            GameObject rug = GameObject.CreatePrimitive(PrimitiveType.Quad);
            rug.name = "Lobby Rug";
            UnityEngine.Object.DestroyImmediate(rug.GetComponent<Collider>());
            rug.transform.SetParent(parent, false);
            rug.transform.localPosition = new Vector3(5.5f, 0.006f, 0.5f);
            rug.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            rug.transform.localScale = new Vector3(9.2f, 1.5f, 1f);
            rug.GetComponent<Renderer>().sharedMaterial = palette.rug;

            GameObject desk = AddModel(parent, FurniturePath + "Table_Large.fbx", "Cinna's Desk", new Vector3(-1.25f, 0f, 0.5f), 90f, 0.55f);
            float top = RenderedBounds(desk.GetComponentsInChildren<Renderer>()).max.y;
            AddModel(parent, FurniturePath + "Book_Stack_1.fbx", "Desk Books", new Vector3(-1.3f, top, 0.15f), 20f, 0.14f);
            AddModel(parent, FurniturePath + "Scroll_1.fbx", "Desk Scroll", new Vector3(-1.15f, top, 0.55f), 80f, 0.08f);
            AddModel(parent, FurniturePath + "CandleStick_Triple.fbx", "Desk Candles", new Vector3(-1.3f, top, 0.95f), 0f, 0.26f);
            AddLight(parent, "Lobby Candle Light", new Vector3(-1.25f, top + 0.4f, 0.9f), new Color(1f, 0.7f, 0.42f), 3.5f, 1.2f);
            AddModel(parent, FurniturePath + "Chair_1.fbx", "Desk Chair", new Vector3(-1.9f, 0f, 0.5f), 90f, 0.5f);
            AddModel(parent, FurniturePath + "Bookcase_2.fbx", "Lobby Bookcase", new Vector3(12.45f, 0f, 0.7f), 270f, 1.35f);
            AddModel(parent, FurniturePath + "Chest_Wood.fbx", "Lobby Chest", new Vector3(12.2f, 0f, -0.35f), 250f, 0.32f);
        }

        // A block of ground with its top at <paramref name="top"/>, reaching <paramref name="depth"/> down.
        private static void AddBlock(Transform parent, string name, Rect area, float top, float depth, Material material)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.SetParent(parent, false);
            block.transform.localPosition = new Vector3(area.center.x, top - 0.001f - depth * 0.5f, area.center.y);
            block.transform.localScale = new Vector3(area.width, depth, area.height);
            block.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static Vector2Int MapSize()
        {
            WorldAreaAsset area = AssetDatabase.LoadAssetAtPath<WorldAreaAsset>(AreaAssetPath);
            return area == null || area.definition == null ? new Vector2Int(12, 14) : new Vector2Int(area.definition.width, area.definition.height);
        }

        // Moves a model so its highest point sits on the given height, for things that hang (icicles).
        private static void HangFromTop(GameObject instance, float height)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return;
            instance.transform.position += Vector3.up * (height - RenderedBounds(renderers).max.y);
        }

        // A horizontal frost or ice surface on the Ice Trial Frost shader; the ambience drives its reveal.
        private static Material SaveFrostSurface(string name, (string color, string normal) textures, Color tint, float smoothness, float coverage)
        {
            Material material = LoadOrCreate(name, "Lumenfall/Ice Trial Frost");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures.color));
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures.normal));
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Coverage", coverage);
            material.SetFloat("_WorldTiling", 3f);
            material.SetFloat("_BumpScale", 0.6f);
            material.renderQueue = (int)RenderQueue.Transparent - 50;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void AddLight(Transform parent, string name, Vector3 position, Color color, float range, float intensity)
        {
            GameObject host = new GameObject(name);
            host.transform.SetParent(parent, false);
            host.transform.localPosition = position;
            Light light = host.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        // Builds a prefab in a preview scene so the open scene is not dirtied.
        private static GameObject SavePrefab(string name, Action<Transform> build)
        {
            Scene scene = EditorSceneManager.NewPreviewScene();
            try
            {
                GameObject root = new GameObject(name);
                SceneManager.MoveGameObjectToScene(root, scene);
                build(root.transform);
                return PrefabUtility.SaveAsPrefabAsset(root, OutputPath + "/" + name + ".prefab");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        /// <summary>
        /// Places a model scaled to <paramref name="height"/> with its rendered base on <paramref name="localPosition"/>'s
        /// height, like the B02 layout tool. A non-null <paramref name="material"/> replaces every material on it.
        /// </summary>
        private static GameObject AddModel(Transform parent, string file, string name, Vector3 localPosition, float yaw, float height, Material material = null)
        {
            string path = file.StartsWith("Assets/", StringComparison.Ordinal) ? file : ModelPath + file;
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            if (source == null) throw new InvalidOperationException("Missing ice trial model: " + path);
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
            instance.name = name;
            Quaternion importedRotation = source.transform.localRotation;
            Vector3 importedScale = source.transform.localScale;
            instance.transform.localPosition = new Vector3(localPosition.x, 0f, localPosition.z);
            instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * importedRotation;
            instance.transform.localScale = importedScale;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            if (material != null)
                foreach (Renderer renderer in renderers)
                {
                    Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                }
            if (renderers.Length == 0) return instance;
            Bounds bounds = RenderedBounds(renderers);
            Debug.Log($"C03_MODEL_BOUNDS {file}: {bounds.size.x:F3} x {bounds.size.y:F3} x {bounds.size.z:F3}");
            if (bounds.size.y > 0.001f) instance.transform.localScale = importedScale * (height / bounds.size.y);
            bounds = RenderedBounds(renderers);
            // Ground by the rendered base, since imported pivots vary between models.
            float baseY = parent.TransformPoint(new Vector3(0f, localPosition.y, 0f)).y;
            instance.transform.position += Vector3.up * (baseY - bounds.min.y);
            return instance;
        }

        private static Bounds RenderedBounds(Renderer[] renderers)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private static (string color, string normal) AmbientCg(string asset) =>
            (TexturePath + asset + "/" + asset + "_1K-JPG_Color.jpg", TexturePath + asset + "/" + asset + "_1K-JPG_NormalGL.jpg");

        private static (string color, string normal) StoneTextures() => (StoneTexturePath + "Albedo 1.png", StoneTexturePath + "Normal 1.png");

        private static Material SaveLit(string name, (string color, string normal) textures, Color tint, float smoothness)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Lit");
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            if (textures.color != null)
            {
                material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures.color));
                if (textures.normal != null)
                {
                    material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures.normal));
                    material.SetFloat("_BumpScale", 0.6f);
                    material.EnableKeyword("_NORMALMAP");
                }
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        // Soft round particles from the built-in particle texture: alpha blended, or additive for flames.
        private static Material SaveParticle(string name, Color color, bool additive)
        {
            Material material = LoadOrCreate(name, "Universal Render Pipeline/Particles/Unlit");
            material.SetTexture("_BaseMap", AssetDatabase.GetBuiltinExtraResource<Texture2D>("Default-Particle.psd"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", additive ? 2f : 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            material.SetFloat("_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(material);
            return material;
        }

        // The switch rune: a cut-out ring the runtime tints and lights. Its colour and emission come from the same texture,
        // so only the rune marks glow.
        private static Material SaveRune(Texture2D texture)
        {
            Material material = LoadOrCreate("MAT_IceTrial_Rune", "Universal Render Pipeline/Lit");
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_EmissionMap", texture);
            material.SetColor("_EmissionColor", Color.black);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            material.SetFloat("_Smoothness", 0.6f);
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// Draws the switch rune: an outer ring, an inner ring, eight glyph marks between them and a six-armed frost star
        /// in the middle, white on transparent.
        /// </summary>
        private static Texture2D DrawRuneTexture()
        {
            string path = OutputPath + "/" + RuneTextureName;
            const int size = RuneTextureSize;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // Supersample each pixel 4x4 so the marks have soft edges for the alpha cut-out.
                    float coverage = 0f;
                    for (int sy = 0; sy < 4; sy++)
                        for (int sx = 0; sx < 4; sx++)
                        {
                            float u = (x + (sx + 0.5f) / 4f) / size * 2f - 1f;
                            float v = (y + (sy + 0.5f) / 4f) / size * 2f - 1f;
                            if (RuneMark(u, v)) coverage += 1f / 16f;
                        }
                    byte alpha = (byte)Mathf.RoundToInt(coverage * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static bool RuneMark(float u, float v)
        {
            float radius = Mathf.Sqrt(u * u + v * v);
            float angle = Mathf.Atan2(v, u);
            if (radius > 0.86f && radius < 0.96f) return true;
            if (radius > 0.6f && radius < 0.65f) return true;
            // Eight glyphs between the rings, alternating a radial bar and a small diamond.
            for (int i = 0; i < 8; i++)
            {
                float centre = i * Mathf.PI / 4f;
                float along = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, centre * Mathf.Rad2Deg) * Mathf.Deg2Rad * radius;
                float across = radius - 0.755f;
                if (i % 2 == 0 ? Mathf.Abs(along) < 0.03f && Mathf.Abs(across) < 0.08f
                               : Mathf.Abs(along) + Mathf.Abs(across) < 0.06f) return true;
            }
            // A six-armed frost star: arms with a short branch on each side.
            if (radius > 0.48f) return false;
            for (int i = 0; i < 6; i++)
            {
                float armAngle = i * Mathf.PI / 3f;
                Vector2 axis = new Vector2(Mathf.Cos(armAngle), Mathf.Sin(armAngle));
                Vector2 point = new Vector2(u, v);
                float along = Vector2.Dot(point, axis);
                float across = Mathf.Abs(axis.x * v - axis.y * u);
                if (along > 0f && along < 0.46f && across < 0.028f) return true;
                if (along > 0.22f && along < 0.36f && Mathf.Abs(across - (along - 0.22f) * 0.9f) < 0.024f) return true;
            }
            return radius < 0.07f;
        }

        private static Material LoadOrCreate(string name, string shaderName)
        {
            string path = OutputPath + "/" + name + ".mat";
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("Missing shader: " + shaderName);
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) { material.shader = shader; return material; }
            material = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void MarkNormalMap(string path)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || importer.textureType == TextureImporterType.NormalMap) return;
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }

        private static Transform NewChild(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
