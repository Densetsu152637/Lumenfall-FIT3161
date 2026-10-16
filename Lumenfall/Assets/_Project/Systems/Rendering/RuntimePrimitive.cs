using UnityEngine;

namespace Lumenfall.Rendering
{
    /// <summary>
    /// Builds primitives that render in player builds. GameObject.CreatePrimitive gives a URP project's primitives the
    /// pipeline's default material only in the editor; a build falls back to the built-in Standard material, which URP
    /// cannot draw and skips silently, so rooms made from primitives turn invisible. Primitives made here use
    /// Resources/RuntimeLit (URP Lit with the emission variant enabled), which every build includes.
    /// Callers still tint through renderer.material (a per-object copy) or a MaterialPropertyBlock; never edit or destroy
    /// the shared material itself.
    /// </summary>
    public static class RuntimePrimitive
    {
        public const string MaterialPath = "RuntimeLit";

        private static Material lit;

        /// <summary>The shared URP Lit material, or null if the asset is missing.</summary>
        public static Material LitMaterial
        {
            get
            {
                if (lit == null) lit = Resources.Load<Material>(MaterialPath);
                return lit;
            }
        }

        public static GameObject Create(PrimitiveType type)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            Renderer renderer = primitive.GetComponent<Renderer>();
            Material material = LitMaterial;
            if (renderer != null && material != null) renderer.sharedMaterial = material;
            else if (material == null) Debug.LogError($"Resources/{MaterialPath} is missing, so runtime primitives will not render in builds.");
            return primitive;
        }
    }
}
