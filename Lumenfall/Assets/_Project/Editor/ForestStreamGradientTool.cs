using UnityEditor;
using UnityEngine;

namespace Lumenfall.EditorTools
{
    public static class ForestStreamGradientTool
    {
        private const string PrefabPath = "Assets/_Project/World/Areas/Prefabs/westForest.prefab";
        private const string MaterialPath = "Assets/_Project/Art/Environment/Water/StreamEdgeGradient.mat";

        public static void Apply()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var stream = Find(root.transform, "Stream");
                if (stream == null) throw new System.InvalidOperationException("Stream group not found.");
                var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (material == null)
                {
                    var shader = Shader.Find("Lumenfall/Environment/Stream Edge Gradient");
                    if (shader == null) throw new System.InvalidOperationException("Gradient shader not imported.");
                    material = new Material(shader) { name = "StreamEdgeGradient" };
                    AssetDatabase.CreateAsset(material, MaterialPath);
                    AssetDatabase.SaveAssets();
                }
                foreach (Transform child in stream)
                {
                    var renderer = child.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root) { var result = Find(child, name); if (result != null) return result; }
            return null;
        }
    }
}
