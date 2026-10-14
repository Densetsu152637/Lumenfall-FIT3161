using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.EditorTools
{
    /// <summary>
    /// Gives the campus garden's white blossom trees (<c>Old+Tree</c>, from <c>white_trees.fbx</c>) a light canopy mesh.
    /// The model's canopy is about 850,000 triangles: 634,000 of bark, mostly hidden under 216,000 of blossom. Each tree
    /// is drawn again for every shadow cascade, which made the garden lag. Unity's Mesh LOD (enabled in the model's import
    /// settings) has already simplified both parts, but a single LOD level cannot keep the blossoms: they vanish below
    /// level 1. So this tool takes each part from its own level, drops the vertices nothing uses, saves the result as
    /// <see cref="LightMeshPath"/> and points the trees in <c>campus.prefab</c> at it. The model itself is unchanged, so
    /// reverting the prefab override restores the full mesh. Run it from Lumenfall > World > Build Light Campus Trees.
    /// </summary>
    public static class CampusTreeOptimizer
    {
        private const string ModelPath = "Assets/_Project/Art/Environment/Plants/white_trees.fbx";
        private const string LightMeshPath = "Assets/_Project/Art/Environment/Plants/white_trees_canopy_light.asset";
        private const string CampusPath = "Assets/_Project/World/Areas/Prefabs/campus.prefab";
        private const string CanopyMeshName = "BRANCH.001";
        private const string TreeNamePrefix = "Old+Tree";

        // Mesh LOD level per sub-mesh (0 is full detail). The bark keeps 66,000 of its 634,000 triangles; the blossoms keep
        // 49,000 of 216,000, as level 2 and beyond have none left. Lower a number for more detail.
        private const int BarkLod = 4;
        private const int BlossomLod = 1;

        /// <summary>Creating Temp/ + this file name makes the next script reload run the tool once, unattended.</summary>
        public const string RequestFileName = "lumenfall-build-light-trees.request";

        [InitializeOnLoadMethod]
        private static void ApplyRequested()
        {
            string requestPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", RequestFileName);
            if (!File.Exists(requestPath)) return;
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(requestPath)) return;
                File.Delete(requestPath);
                Apply();
            };
        }

        [MenuItem("Lumenfall/World/Build Light Campus Trees")]
        public static void Apply()
        {
            Mesh source = null;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
                if (asset is Mesh mesh && mesh.name == CanopyMeshName) source = mesh;
            if (source == null) { Debug.LogWarning("Missing the " + CanopyMeshName + " mesh in " + ModelPath); return; }
            if (source.lodCount <= Mathf.Max(BarkLod, BlossomLod))
            {
                Debug.LogWarning(ModelPath + " has " + source.lodCount + " Mesh LOD levels; turn on Generate Mesh LODs in its import settings.");
                return;
            }

            Mesh light = BuildLightMesh(source, new[] { BarkLod, BlossomLod });
            Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(LightMeshPath);
            if (saved == null) AssetDatabase.CreateAsset(light, LightMeshPath);
            else
            {
                EditorUtility.CopySerialized(light, saved);
                Object.DestroyImmediate(light);
                light = saved;
            }
            AssetDatabase.SaveAssets();

            int trees = AssignToCampusTrees(light);
            Debug.Log($"Built the light campus tree canopy: {Triangles(source):N0} triangles down to {Triangles(light):N0}, " +
                $"used by {trees} tree(s) in campus.prefab.");
        }

        // Each sub-mesh from its own LOD level, keeping only the vertices those triangles use.
        private static Mesh BuildLightMesh(Mesh source, int[] levels)
        {
            List<Vector3> positions = new List<Vector3>(); source.GetVertices(positions);
            List<Vector3> normals = new List<Vector3>(); source.GetNormals(normals);
            List<Vector4> tangents = new List<Vector4>(); source.GetTangents(tangents);
            List<Color32> colours = new List<Color32>(); source.GetColors(colours);
            List<Vector4>[] uvs = new List<Vector4>[4];
            for (int channel = 0; channel < uvs.Length; channel++) { uvs[channel] = new List<Vector4>(); source.GetUVs(channel, uvs[channel]); }

            Dictionary<int, int> remap = new Dictionary<int, int>();
            List<int> kept = new List<int>();
            int[][] submeshes = new int[source.subMeshCount][];
            for (int submesh = 0; submesh < source.subMeshCount; submesh++)
            {
                int level = submesh < levels.Length ? levels[submesh] : 0;
                int[] indices = source.GetIndices(submesh: submesh, meshLod: level, applyBaseVertex: true);
                for (int i = 0; i < indices.Length; i++)
                {
                    if (!remap.TryGetValue(indices[i], out int index))
                    {
                        index = kept.Count;
                        remap.Add(indices[i], index);
                        kept.Add(indices[i]);
                    }
                    indices[i] = index;
                }
                submeshes[submesh] = indices;
            }

            Mesh light = new Mesh { name = source.name + " (light)", indexFormat = kept.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            light.SetVertices(Pick(positions, kept));
            if (normals.Count == positions.Count) light.SetNormals(Pick(normals, kept));
            if (tangents.Count == positions.Count) light.SetTangents(Pick(tangents, kept));
            if (colours.Count == positions.Count) light.SetColors(Pick(colours, kept));
            for (int channel = 0; channel < uvs.Length; channel++)
                if (uvs[channel].Count == positions.Count) light.SetUVs(channel, Pick(uvs[channel], kept));
            light.subMeshCount = submeshes.Length;
            for (int submesh = 0; submesh < submeshes.Length; submesh++)
                light.SetIndices(submeshes[submesh], MeshTopology.Triangles, submesh, false);
            light.RecalculateBounds();
            light.UploadMeshData(false);
            return light;
        }

        private static List<T> Pick<T>(List<T> values, List<int> kept)
        {
            List<T> picked = new List<T>(kept.Count);
            foreach (int index in kept) picked.Add(values[index]);
            return picked;
        }

        private static int AssignToCampusTrees(Mesh light)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(CampusPath);
            int trees = 0;
            try
            {
                foreach (Transform item in root.GetComponentsInChildren<Transform>(true))
                {
                    if (!item.name.StartsWith(TreeNamePrefix, System.StringComparison.Ordinal)) continue;
                    bool changed = false;
                    foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(true))
                    {
                        if (filter.sharedMesh == null || (filter.sharedMesh.name != CanopyMeshName && filter.sharedMesh != light)) continue;
                        filter.sharedMesh = light;
                        changed = true;
                    }
                    if (changed) trees++;
                }
                PrefabUtility.SaveAsPrefabAsset(root, CampusPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return trees;
        }

        private static long Triangles(Mesh mesh)
        {
            long total = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                // A mesh with Mesh LODs counts its full-detail level only.
                total += mesh.lodCount > 1 ? mesh.GetLod(submesh, 0).indexCount / 3 : mesh.GetIndexCount(submesh) / 3;
            return total;
        }
    }
}
