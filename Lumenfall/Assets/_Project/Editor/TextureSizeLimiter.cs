using System;
using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class TextureSizeLimiter
    {
        private const int MaximumTextureSize = 512;

        private static readonly string[] PlatformNames =
        {
            "Standalone",
            "Android",
            "iPhone",
            "WebGL",
            "Windows Store Apps",
            "Server"
        };

        [MenuItem("Lumenfall/Build/Limit All Textures To 512")]
        public static void Apply()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });
            int changed = 0;
            int inspected = 0;

            try
            {
                for (int index = 0; index < guids.Length; index++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[index]);
                    if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                        continue;

                    inspected++;
                    EditorUtility.DisplayProgressBar(
                        "Limiting texture sizes",
                        path,
                        guids.Length == 0 ? 1f : (float)index / guids.Length);

                    bool dirty = false;
                    if (importer.maxTextureSize > MaximumTextureSize)
                    {
                        importer.maxTextureSize = MaximumTextureSize;
                        dirty = true;
                    }

                    foreach (string platformName in PlatformNames)
                    {
                        TextureImporterPlatformSettings settings =
                            importer.GetPlatformTextureSettings(platformName);

                        if (!settings.overridden || settings.maxTextureSize <= MaximumTextureSize)
                            continue;

                        settings.maxTextureSize = MaximumTextureSize;
                        importer.SetPlatformTextureSettings(settings);
                        dirty = true;
                    }

                    if (!dirty)
                        continue;

                    importer.SaveAndReimport();
                    changed++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"TextureSizeLimiter inspected {inspected} textures and limited {changed} importers to {MaximumTextureSize}px.");
        }
    }
}
