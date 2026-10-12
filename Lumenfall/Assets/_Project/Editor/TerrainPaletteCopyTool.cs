using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class TerrainPaletteCopyTool
    {
        private const string WestForestPath = "Assets/_Project/Art/Environment/Terrain/WestForestTerrainData.asset";
        private const string CampusPath = "Assets/_Project/Art/Environment/Terrain/CampusTerrainData.asset";

        [MenuItem("Lumenfall/Terrain/Copy West Forest Palette To Campus")]
        public static void CopyWestForestPaletteToCampus()
        {
            TerrainData source = AssetDatabase.LoadAssetAtPath<TerrainData>(WestForestPath);
            TerrainData destination = AssetDatabase.LoadAssetAtPath<TerrainData>(CampusPath);

            if (source == null || destination == null)
            {
                throw new InvalidOperationException(
                    $"Could not load both TerrainData assets. Source={source != null}, Destination={destination != null}");
            }

            Undo.RecordObject(destination, "Copy West Forest terrain palette to Campus");

            int terrainLayersAdded = MergeTerrainLayers(source, destination);
            int detailPrototypesAdded = MergeDetailPrototypes(source, destination);
            int treePrototypesAdded = MergeTreePrototypes(source, destination);

            destination.RefreshPrototypes();
            EditorUtility.SetDirty(destination);
            AssetDatabase.SaveAssets();

            string result =
                "Copied West Forest terrain palette to Campus. " +
                $"Added terrain layers: {terrainLayersAdded}; " +
                $"detail prototypes: {detailPrototypesAdded}; " +
                $"tree prototypes: {treePrototypesAdded}. " +
                $"Campus totals are now {destination.terrainLayers.Length} terrain layers, " +
                $"{destination.detailPrototypes.Length} detail prototypes, and " +
                $"{destination.treePrototypes.Length} tree prototypes. " +
                "Existing Campus painting, trees, details, heightmap, and holes were preserved.";

            File.WriteAllText(Path.Combine(Application.dataPath, "../Temp/TerrainPaletteCopyResult.txt"), result);
            Debug.Log(result);
        }

        private static int MergeTerrainLayers(TerrainData source, TerrainData destination)
        {
            var merged = new List<TerrainLayer>(destination.terrainLayers);
            int originalCount = merged.Count;

            foreach (TerrainLayer layer in source.terrainLayers)
            {
                if (layer != null && !merged.Contains(layer))
                {
                    merged.Add(layer);
                }
            }

            destination.terrainLayers = merged.ToArray();
            return merged.Count - originalCount;
        }

        private static int MergeDetailPrototypes(TerrainData source, TerrainData destination)
        {
            var merged = new List<DetailPrototype>(destination.detailPrototypes);
            int originalCount = merged.Count;

            foreach (DetailPrototype prototype in source.detailPrototypes)
            {
                if (!ContainsEquivalentDetailPrototype(merged, prototype))
                {
                    merged.Add(new DetailPrototype(prototype));
                }
            }

            destination.detailPrototypes = merged.ToArray();
            return merged.Count - originalCount;
        }

        private static int MergeTreePrototypes(TerrainData source, TerrainData destination)
        {
            var merged = new List<TreePrototype>(destination.treePrototypes);
            int originalCount = merged.Count;

            foreach (TreePrototype prototype in source.treePrototypes)
            {
                bool alreadyPresent = merged.Exists(existing =>
                    existing.prefab == prototype.prefab &&
                    Mathf.Approximately(existing.bendFactor, prototype.bendFactor) &&
                    existing.navMeshLod == prototype.navMeshLod);

                if (!alreadyPresent)
                {
                    merged.Add(new TreePrototype(prototype));
                }
            }

            destination.treePrototypes = merged.ToArray();
            return merged.Count - originalCount;
        }

        private static bool ContainsEquivalentDetailPrototype(
            List<DetailPrototype> existingPrototypes,
            DetailPrototype candidate)
        {
            string candidateJson = EditorJsonUtility.ToJson(candidate);
            return existingPrototypes.Exists(existing =>
                existing.prototype == candidate.prototype &&
                existing.prototypeTexture == candidate.prototypeTexture &&
                EditorJsonUtility.ToJson(existing) == candidateJson);
        }
    }
}
