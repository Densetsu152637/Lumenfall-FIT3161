using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Lumenfall.EditorTools
{
    public static class VerifiedUnusedAssetCleaner
    {
        private const string AnalyzeRequest = "RunVerifiedUnusedAnalyze.once";
        private const string DeleteRequest = "RunVerifiedUnusedDelete.once";

        private static readonly string[] TargetFolders =
        {
            "Assets/ThirdParty",
            "Assets/_Project/Art/Environment"
        };

        private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".dll", ".asmdef", ".asmref", ".rsp", ".shader", ".cginc", ".hlsl",
            ".compute", ".uxml", ".uss", ".txt", ".md", ".pdf"
        };

        private static readonly Regex AssetPathLiteral = new(
            "[\\\"'](?<path>Assets/(?:ThirdParty|_Project/Art/Environment)/[^\\\"']*)[\\\"']",
            RegexOptions.Compiled);

        private sealed class Analysis
        {
            public readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase);
            public readonly List<string> AreaPrefabs = new();
            public readonly List<string> TerrainData = new();
            public readonly List<string> TerrainBindings = new();
            public readonly List<string> RenderingRoots = new();
            public readonly List<string> Candidates = new();
            public long CandidateBytes;
        }

        // The open Editor consumes one-shot analysis, deletion, and verification requests after a safe domain reload.
        [InitializeOnLoadMethod]
        private static void RunRequestedOperation()
        {
            string temp = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp");
            string analyzePath = Path.Combine(temp, AnalyzeRequest);
            string deletePath = Path.Combine(temp, DeleteRequest);
            if (!File.Exists(analyzePath) && !File.Exists(deletePath)) return;

            EditorApplication.delayCall += () =>
            {
                if (File.Exists(deletePath))
                {
                    File.Delete(deletePath);
                    DeleteVerified();
                    return;
                }

                if (File.Exists(analyzePath))
                {
                    File.Delete(analyzePath);
                    Analyze();
                }
            };
        }

        [MenuItem("Lumenfall/Build/Analyze Verified Unused Assets")]
        public static void Analyze()
        {
            Analysis analysis = BuildAnalysis();
            WriteReport(analysis);
            Debug.Log($"Verified unused asset analysis found {analysis.Candidates.Count} candidates " +
                      $"({analysis.CandidateBytes / 1048576d:F2} MiB). Report: Temp/VerifiedUnusedAssets.txt");
        }

        [MenuItem("Lumenfall/Build/Delete Verified Unused Assets")]
        public static void DeleteVerified()
        {
            Analysis analysis = BuildAnalysis();
            WriteReport(analysis);

            int deleted = 0;
            long deletedBytes = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in analysis.Candidates)
                {
                    long bytes = new FileInfo(ToAbsolutePath(path)).Length;
                    if (!AssetDatabase.DeleteAsset(path))
                        throw new InvalidOperationException($"Unity could not delete candidate: {path}");
                    deleted++;
                    deletedBytes += bytes;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            DeleteEmptyTargetFolders();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateRequiredAssets(analysis);

            Debug.Log($"Verified unused asset cleanup deleted {deleted} assets " +
                      $"({deletedBytes / 1048576d:F2} MiB). Area, terrain, and rendering dependencies remain present.");
        }

        private static Analysis BuildAnalysis()
        {
            var analysis = new Analysis();
            string[] allAssetPaths = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .Where(path => File.Exists(ToAbsolutePath(path)))
                .ToArray();

            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Any asset outside the two cleanup folders may legitimately depend on art inside them.
            foreach (string path in allAssetPaths)
            {
                if (!IsInTarget(path)) roots.Add(path);
                if (IsInResourcesOrStreamingAssets(path)) roots.Add(path);
                if (IsProtectedSource(path)) roots.Add(path);
            }

            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                if (scene.enabled && File.Exists(ToAbsolutePath(scene.path))) roots.Add(scene.path);

            analysis.AreaPrefabs.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/World/Areas/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path)));
            foreach (string path in analysis.AreaPrefabs) roots.Add(path);

            CollectTerrainRoots(analysis, roots);
            CollectRenderingRoots(analysis, roots);
            CollectHardcodedPathRoots(allAssetPaths, roots);

            foreach (UnityEngine.Object asset in PlayerSettings.GetPreloadedAssets())
            {
                string path = AssetDatabase.GetAssetPath(asset);
                if (!string.IsNullOrEmpty(path)) roots.Add(path);
            }

            foreach (string dependency in AssetDatabase.GetDependencies(roots.ToArray(), true))
                analysis.Protected.Add(dependency);
            foreach (string root in roots)
                analysis.Protected.Add(root);

            foreach (string path in allAssetPaths.Where(IsInTarget).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (analysis.Protected.Contains(path)) continue;
                analysis.Candidates.Add(path);
                analysis.CandidateBytes += new FileInfo(ToAbsolutePath(path)).Length;
            }

            return analysis;
        }

        private static void CollectTerrainRoots(Analysis analysis, HashSet<string> roots)
        {
            var terrainPaths = new HashSet<string>(
                AssetDatabase.FindAssets("t:TerrainData").Select(AssetDatabase.GUIDToAssetPath),
                StringComparer.OrdinalIgnoreCase);

            // Preserve terrain-looking assets even if a third-party sample failed type indexing/import.
            foreach (string path in AssetDatabase.GetAllAssetPaths())
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                    Path.GetFileNameWithoutExtension(path).Contains("Terrain", StringComparison.OrdinalIgnoreCase))
                    terrainPaths.Add(path);
            }

            foreach (string path in terrainPaths)
            {
                if (string.IsNullOrEmpty(path)) continue;
                TerrainData terrain = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                analysis.TerrainData.Add(path);
                roots.Add(path);
                if (terrain == null) continue;

                foreach (TerrainLayer layer in terrain.terrainLayers ?? Array.Empty<TerrainLayer>())
                    AddTerrainBinding(layer, "layer", analysis, roots);

                foreach (TreePrototype tree in terrain.treePrototypes ?? Array.Empty<TreePrototype>())
                    AddTerrainBinding(tree.prefab, "tree", analysis, roots);

                foreach (DetailPrototype detail in terrain.detailPrototypes ?? Array.Empty<DetailPrototype>())
                {
                    AddTerrainBinding(detail.prototype, "detail-prefab", analysis, roots);
                    AddTerrainBinding(detail.prototypeTexture, "detail-texture", analysis, roots);
                }
            }
        }

        private static void AddTerrainBinding(UnityEngine.Object asset, string kind, Analysis analysis,
            HashSet<string> roots)
        {
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return;
            roots.Add(path);
            analysis.TerrainBindings.Add($"{kind}\t{path}");
        }

        private static void CollectRenderingRoots(Analysis analysis, HashSet<string> roots)
        {
            foreach (string path in AssetDatabase.FindAssets(string.Empty, new[] { "Assets/_Project/Settings" })
                         .Select(AssetDatabase.GUIDToAssetPath)
                         .Where(path => !string.IsNullOrEmpty(path) && File.Exists(ToAbsolutePath(path))))
            {
                roots.Add(path);
                analysis.RenderingRoots.Add(path);
            }

            RenderPipelineAsset activePipeline = GraphicsSettings.defaultRenderPipeline;
            if (activePipeline != null)
            {
                string path = AssetDatabase.GetAssetPath(activePipeline);
                if (!string.IsNullOrEmpty(path))
                {
                    roots.Add(path);
                    analysis.RenderingRoots.Add(path);
                }
            }

            foreach (string filter in new[] { "t:RenderPipelineAsset", "t:RenderPipelineGlobalSettings", "t:ScriptableRendererData" })
            {
                foreach (string path in AssetDatabase.FindAssets(filter).Select(AssetDatabase.GUIDToAssetPath))
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    roots.Add(path);
                    analysis.RenderingRoots.Add(path);
                }
            }

            analysis.RenderingRoots.Sort(StringComparer.OrdinalIgnoreCase);
            analysis.RenderingRoots.RemoveAllDuplicateEntries();
        }

        private static void CollectHardcodedPathRoots(string[] allAssetPaths, HashSet<string> roots)
        {
            foreach (string scriptPath in allAssetPaths.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                string source = File.ReadAllText(ToAbsolutePath(scriptPath));
                foreach (Match match in AssetPathLiteral.Matches(source))
                {
                    string prefix = match.Groups["path"].Value.TrimEnd('/');
                    foreach (string assetPath in allAssetPaths)
                    {
                        if (assetPath.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                            assetPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase) ||
                            assetPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            roots.Add(assetPath);
                    }
                }
            }
        }

        private static void ValidateRequiredAssets(Analysis analysis)
        {
            IEnumerable<string> required = analysis.AreaPrefabs
                .Concat(analysis.TerrainData)
                .Concat(analysis.TerrainBindings.Select(binding => binding.Split('\t')[1]))
                .Concat(analysis.RenderingRoots);

            string[] missing = required.Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(path => !File.Exists(ToAbsolutePath(path)))
                .ToArray();
            if (missing.Length > 0)
                throw new InvalidOperationException("Cleanup removed required assets:\n" + string.Join("\n", missing));
        }

        private static void WriteReport(Analysis analysis)
        {
            var output = new StringBuilder();
            output.AppendLine($"Candidates\t{analysis.Candidates.Count}");
            output.AppendLine($"CandidateMiB\t{analysis.CandidateBytes / 1048576d:F2}");
            WriteSection(output, "AREA PREFABS", analysis.AreaPrefabs);
            WriteSection(output, "TERRAIN DATA", analysis.TerrainData);
            WriteSection(output, "TERRAIN BINDINGS", analysis.TerrainBindings);
            WriteSection(output, "RUNTIME RENDERING ROOTS", analysis.RenderingRoots);
            WriteSection(output, "DELETE CANDIDATES", analysis.Candidates);
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp", "VerifiedUnusedAssets.txt"), output.ToString());
        }

        private static void WriteSection(StringBuilder output, string title, IEnumerable<string> values)
        {
            output.AppendLine();
            output.AppendLine($"[{title}]");
            foreach (string value in values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                output.AppendLine(value);
        }

        private static void DeleteEmptyTargetFolders()
        {
            foreach (string target in TargetFolders)
            {
                string targetAbsolute = ToAbsolutePath(target);
                foreach (string directory in Directory.GetDirectories(targetAbsolute, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length))
                {
                    if (Directory.EnumerateFileSystemEntries(directory).Any()) continue;
                    string assetPath = "Assets" + directory.Substring(Application.dataPath.Length).Replace('\\', '/');
                    AssetDatabase.DeleteAsset(assetPath);
                }
            }
        }

        private static bool IsInTarget(string path) => TargetFolders.Any(folder =>
            path.Equals(folder, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));

        private static bool IsInResourcesOrStreamingAssets(string path)
        {
            string normalized = "/" + path.Replace('\\', '/') + "/";
            return normalized.Contains("/Resources/", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Contains("/StreamingAssets/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsProtectedSource(string path)
        {
            if (ProtectedExtensions.Contains(Path.GetExtension(path))) return true;
            string name = Path.GetFileNameWithoutExtension(path);
            return Regex.IsMatch(name, "^(license|licence|readme|credits|notice|copying)", RegexOptions.IgnoreCase);
        }

        private static string ToAbsolutePath(string assetPath) =>
            Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath));

        private static void RemoveAllDuplicateEntries(this List<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            values.RemoveAll(value => !seen.Add(value));
        }
    }
}
