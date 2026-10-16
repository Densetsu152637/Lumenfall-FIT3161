using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class VerifiedFurnitureCleaner
    {
        private const string FurnitureFolder = "Assets/_Project/Art/Environment/Furnitures";
        private const string AreaFolder = "Assets/_Project/World/Areas/Prefabs";
        private const string AnalyzeRequest = "RunVerifiedFurnitureAnalyze.once";
        private const string DeleteRequest = "RunVerifiedFurnitureDelete.once";
        private const string ReportName = "VerifiedUnusedFurniture.txt";

        private static readonly Regex FurnitureFilename = new(
            "[\\\"'](?<name>[^/\\\"']+\\.(?:fbx|glb|png|jpg|jpeg|tga|mat|prefab))[\\\"']",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private sealed class Analysis
        {
            public readonly List<string> Areas = new();
            public readonly List<string> Scenes = new();
            public readonly List<string> CodeProtected = new();
            public readonly List<string> Candidates = new();
            public long CandidateBytes;
        }

        [InitializeOnLoadMethod]
        private static void RunRequestedOperation()
        {
            string temp = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp");
            string analyze = Path.Combine(temp, AnalyzeRequest);
            string delete = Path.Combine(temp, DeleteRequest);
            if (!File.Exists(analyze) && !File.Exists(delete)) return;

            EditorApplication.delayCall += () =>
            {
                if (File.Exists(delete))
                {
                    File.Delete(delete);
                    DeleteVerified();
                }
                else if (File.Exists(analyze))
                {
                    File.Delete(analyze);
                    Analyze();
                }
            };
        }

        [MenuItem("Lumenfall/Build/Analyze Unused Furniture")]
        public static void Analyze()
        {
            Analysis analysis = BuildAnalysis();
            ValidateMaps(analysis.Areas, analysis.Scenes);
            WriteReport(analysis);
            Debug.Log($"Furniture analysis found {analysis.Candidates.Count} unused assets " +
                      $"({analysis.CandidateBytes / 1048576d:F2} MiB). Report: Temp/{ReportName}");
        }

        [MenuItem("Lumenfall/Build/Delete Verified Unused Furniture")]
        public static void DeleteVerified()
        {
            Analysis analysis = BuildAnalysis();
            ValidateMaps(analysis.Areas, analysis.Scenes);
            WriteReport(analysis);

            int deleted = 0;
            long bytes = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in analysis.Candidates)
                {
                    long size = new FileInfo(ToAbsolute(path)).Length;
                    if (!AssetDatabase.DeleteAsset(path))
                        throw new InvalidOperationException("Failed to delete unused furniture: " + path);
                    deleted++;
                    bytes += size;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateMaps(analysis.Areas, analysis.Scenes);

            Analysis after = BuildAnalysis();
            if (after.Candidates.Count != 0)
                throw new InvalidOperationException($"Furniture cleanup left {after.Candidates.Count} unused candidates.");

            Debug.Log($"Verified furniture cleanup deleted {deleted} assets ({bytes / 1048576d:F2} MiB); " +
                      $"validated {analysis.Areas.Count} area prefabs and {analysis.Scenes.Count} scenes.");
        }

        private static Analysis BuildAnalysis()
        {
            var analysis = new Analysis();
            string[] allAssets = AssetDatabase.GetAllAssetPaths()
                .Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                .Where(path => File.Exists(ToAbsolute(path)))
                .ToArray();
            string[] furniture = allAssets
                .Where(path => path.StartsWith(FurnitureFolder + "/", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            analysis.Areas.AddRange(AssetDatabase.FindAssets("t:Prefab", new[] { AreaFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path)));
            analysis.Scenes.AddRange(AssetDatabase.FindAssets("t:Scene")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrEmpty(path)));

            var roots = new HashSet<string>(allAssets.Where(path =>
                !path.StartsWith(FurnitureFolder + "/", StringComparison.OrdinalIgnoreCase)),
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in analysis.Areas.Concat(analysis.Scenes)) roots.Add(path);

            ProtectCodeReferencedFurniture(allAssets, furniture, roots, analysis.CodeProtected);

            var used = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true),
                StringComparer.OrdinalIgnoreCase);
            foreach (string path in roots) used.Add(path);

            foreach (string path in furniture.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (used.Contains(path)) continue;
                analysis.Candidates.Add(path);
                analysis.CandidateBytes += new FileInfo(ToAbsolute(path)).Length;
            }

            return analysis;
        }

        private static void ProtectCodeReferencedFurniture(string[] allAssets, string[] furniture,
            HashSet<string> roots, List<string> codeProtected)
        {
            foreach (string script in allAssets.Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                string source = File.ReadAllText(ToAbsolute(script));
                if (!source.Contains("Furnitures", StringComparison.OrdinalIgnoreCase) &&
                    !source.Contains("FurniturePath", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (Match match in FurnitureFilename.Matches(source))
                {
                    string name = match.Groups["name"].Value;
                    foreach (string path in furniture.Where(path =>
                                 Path.GetFileName(path).Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        roots.Add(path);
                        codeProtected.Add(path);
                    }
                }
            }

            codeProtected.Sort(StringComparer.OrdinalIgnoreCase);
            for (int index = codeProtected.Count - 1; index > 0; index--)
                if (codeProtected[index].Equals(codeProtected[index - 1], StringComparison.OrdinalIgnoreCase))
                    codeProtected.RemoveAt(index);
        }

        private static void ValidateMaps(IEnumerable<string> areas, IEnumerable<string> scenes)
        {
            var failures = new List<string>();
            foreach (string path in areas)
            {
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (root == null)
                {
                    failures.Add(path + " could not be loaded");
                    continue;
                }

                int missingScripts = root.GetComponentsInChildren<Transform>(true)
                    .Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
                if (missingScripts > 0) failures.Add($"{path} has {missingScripts} missing scripts");
                ValidateDependencies(path, failures);
            }

            foreach (string path in scenes) ValidateDependencies(path, failures);
            if (failures.Count > 0)
                throw new InvalidOperationException("Map validation failed:\n" + string.Join("\n", failures));
        }

        private static void ValidateDependencies(string root, List<string> failures)
        {
            foreach (string dependency in AssetDatabase.GetDependencies(root, true))
                if (dependency.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                    !File.Exists(ToAbsolute(dependency)))
                    failures.Add($"{root} has missing dependency {dependency}");
        }

        private static void WriteReport(Analysis analysis)
        {
            var report = new StringBuilder();
            report.AppendLine($"Candidates\t{analysis.Candidates.Count}");
            report.AppendLine($"CandidateMiB\t{analysis.CandidateBytes / 1048576d:F2}");
            WriteSection(report, "AREA PREFABS", analysis.Areas);
            WriteSection(report, "SCENES", analysis.Scenes);
            WriteSection(report, "CODE-PROTECTED FURNITURE", analysis.CodeProtected);
            WriteSection(report, "DELETE CANDIDATES", analysis.Candidates);
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Temp", ReportName), report.ToString());
        }

        private static void WriteSection(StringBuilder report, string title, IEnumerable<string> values)
        {
            report.AppendLine();
            report.AppendLine($"[{title}]");
            foreach (string value in values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
                report.AppendLine(value);
        }

        private static string ToAbsolute(string path) =>
            Path.GetFullPath(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, path));
    }
}
