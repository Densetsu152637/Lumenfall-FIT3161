using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Lumenfall.Tests.GameEvents
{
    public sealed class SerializedAssetImportTests
    {
        [TestCase("Assets/ThirdParty/Low Poly Tarvern Pack/New Terrain.asset", "New Terrain")]
        [TestCase("Assets/ThirdParty/Low Poly Tarvern Pack/TerrainData_ef2baf1a-e0c2-47af-8b8d-c8ecc7214609.asset", "TerrainData_ef2baf1a-e0c2-47af-8b8d-c8ecc7214609")]
        public void TavernTerrainDataLoadsWithRecordedSettings(string path, string name)
        {
            TerrainData terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            Assert.That(terrain, Is.Not.Null, path);
            Assert.That(terrain.name, Is.EqualTo(name));
            Assert.That(terrain.heightmapResolution, Is.EqualTo(513));
            Assert.That(terrain.size, Is.EqualTo(new Vector3(1000f, 600f, 1000f)));
            Assert.That(terrain.alphamapResolution, Is.EqualTo(512));
            Assert.That(terrain.baseMapResolution, Is.EqualTo(1024));
            Assert.That(terrain.detailResolution, Is.EqualTo(1024));
            Assert.That(terrain.enableHolesTextureCompression, Is.True);
            Assert.That(terrain.terrainLayers, Is.Empty);
            Assert.That(terrain.alphamapTextureCount, Is.Zero);
            Assert.That(terrain.detailPrototypes, Is.Empty);
            Assert.That(terrain.treePrototypes, Is.Empty);
            Assert.That(terrain.treeInstances, Is.Empty);
            Assert.That(terrain.detailScatterMode, Is.EqualTo((DetailScatterMode)0));
            Assert.That(terrain.wavingGrassTint, Is.EqualTo(new Color(0.7f, 0.6f, 0.5f, 0f)));
            Assert.That(terrain.wavingGrassStrength, Is.EqualTo(0.5f));
            Assert.That(terrain.wavingGrassAmount, Is.EqualTo(0.5f));
            Assert.That(terrain.wavingGrassSpeed, Is.EqualTo(0.5f));

            float[,] heights = terrain.GetHeights(0, 0, terrain.heightmapResolution, terrain.heightmapResolution);
            Assert.That(heights.Length, Is.EqualTo(263169));
            int nonzeroHeights = 0;
            for (int y = 0; y < heights.GetLength(0); y++)
                for (int x = 0; x < heights.GetLength(1); x++)
                    if (heights[y, x] != 0f) nonzeroHeights++;
            Assert.That(nonzeroHeights, Is.Zero, path + " nonzero height samples");
        }

        [Test]
        public void ElaraPrefabLoadsAndRoundTripsWithOriginalRootAndCollider()
        {
            const string sourcePath = "Assets/Resources/NPCs/Elara.prefab";
            const string tempRoot = "Assets/Temp";
            bool createdTempRoot = !AssetDatabase.IsValidFolder(tempRoot);
            if (createdTempRoot) AssetDatabase.CreateFolder("Assets", "Temp");
            string tempFolder = tempRoot + "/SerializedAssetImportTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder(tempRoot, Path.GetFileName(tempFolder));
            string tempPath = tempFolder + "/Elara.prefab";
            GameObject source = null;
            GameObject roundTrip = null;
            try
            {
                source = PrefabUtility.LoadPrefabContents(sourcePath);
                AssertElara(source);
                bool saved;
                PrefabUtility.SaveAsPrefabAsset(source, tempPath, out saved);
                Assert.That(saved, Is.True);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                roundTrip = PrefabUtility.LoadPrefabContents(tempPath);
                AssertElara(roundTrip);
            }
            finally
            {
                if (roundTrip != null) PrefabUtility.UnloadPrefabContents(roundTrip);
                if (source != null) PrefabUtility.UnloadPrefabContents(source);
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(tempPath))) AssetDatabase.DeleteAsset(tempPath);
                if (AssetDatabase.IsValidFolder(tempFolder)) AssetDatabase.DeleteAsset(tempFolder);
                if (createdTempRoot && AssetDatabase.IsValidFolder(tempRoot) && AssetDatabase.FindAssets(string.Empty, new[] { tempRoot }).Length == 0)
                    AssetDatabase.DeleteAsset(tempRoot);
            }
        }

        [Test]
        public void DecorativeNativeCubemapPreservesSettingsAndDependenciesImport()
        {
            const string nativePath = "Assets/_Project/Art/Environment/Decorations/Textures/diffuse_cube.asset";
            const string fbxPath = "Assets/_Project/Art/Environment/Decorations/FountainFinal.fbx";
            const string campusPath = "Assets/_Project/World/Areas/Prefabs/campus.prefab";
            const string expectedGuid = "efbe61d014df5634baabb44e4fa6b070";

            Cubemap cube = AssetDatabase.LoadAssetAtPath<Cubemap>(nativePath);
            Assert.That(cube, Is.Not.Null, nativePath);
            Assert.That(cube.name, Is.EqualTo("diffuse_cube"));
            Assert.That(cube.width, Is.EqualTo(16));
            Assert.That(cube.height, Is.EqualTo(16));
            Assert.That(cube.mipmapCount, Is.EqualTo(1));
            Assert.That(cube.graphicsFormat, Is.EqualTo((GraphicsFormat)87));
            Assert.That(cube.format, Is.EqualTo(TextureFormat.ARGB32));
            Assert.That(GraphicsFormatUtility.IsSRGBFormat(cube.graphicsFormat), Is.True);
            Assert.That(cube.isReadable, Is.False);
            Assert.That(cube.filterMode, Is.EqualTo(FilterMode.Bilinear));
            Assert.That(cube.anisoLevel, Is.EqualTo(1));
            Assert.That(cube.wrapModeU, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(cube.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(cube.wrapModeW, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(cube.mipMapBias, Is.Zero);
            Assert.That(cube.streamingMipmaps, Is.False);
            Assert.That(cube.streamingMipmapsPriority, Is.Zero);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(cube, out string guid, out long fileId);
            Assert.That(guid, Is.EqualTo(expectedGuid));
            Assert.That(fileId, Is.EqualTo(8900000));

            Assert.That(AssetDatabase.GetDependencies(fbxPath, true), Does.Contain(nativePath));
            Assert.That(AssetDatabase.GetDependencies(campusPath, true), Does.Contain(nativePath));

            AssetDatabase.ImportAsset(nativePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(fbxPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(campusPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            cube = AssetDatabase.LoadAssetAtPath<Cubemap>(nativePath);
            Assert.That(cube, Is.Not.Null);
            Assert.That(cube.isReadable, Is.False);
            Assert.That(AssetDatabase.GetDependencies(fbxPath, true), Does.Contain(nativePath));
            Assert.That(AssetDatabase.GetDependencies(campusPath, true), Does.Contain(nativePath));
        }

        private static void AssertElara(GameObject root)
        {
            Assert.That(root, Is.Not.Null);
            Assert.That(root.name, Is.EqualTo("Elara"));
            Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(root.transform.localRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(root.transform.localScale, Is.EqualTo(Vector3.one));
            BoxCollider collider = root.GetComponent<BoxCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.center, Is.EqualTo(Vector3.zero));
            Assert.That(collider.size, Is.EqualTo(new Vector3(1f, 2f, 1f)));
            Assert.That(collider.isTrigger, Is.False);
            Assert.That(root.GetComponentsInChildren<Renderer>(true), Is.Empty);
        }
    }
}
