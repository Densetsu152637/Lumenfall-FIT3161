using Lumenfall.GameEvents;
using Lumenfall.Startup;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// Shared runtime art for cutscene effects: world-space glow materials and quads, the startup menu's screen-space
    /// spark textures (so light handed over from the menu looks the same), and generated audio clips.
    /// </summary>
    internal static class CutsceneArt
    {
        public const int SampleRate = 44100;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private static Material glowMaterial, sparkleMaterial, plainMaterial, columnMaterial;
        private static Texture2D screenGlow, screenSparkle;

        /// <summary>A soft round glow for world-space billboards.</summary>
        public static Material GlowMaterial => Ensure() ? glowMaterial : null;
        /// <summary>A bright centre with four thin rays, like the spark that lights the title.</summary>
        public static Material SparkleMaterial => Ensure() ? sparkleMaterial : null;
        /// <summary>Untextured, for trails that colour themselves with a gradient.</summary>
        public static Material PlainMaterial => Ensure() ? plainMaterial : null;
        /// <summary>A tall beam: soft at its sides and fading out towards its top.</summary>
        public static Material ColumnMaterial => Ensure() ? columnMaterial : null;

        /// <summary>The startup menu's spark glow, for screen-space sparks drawn with IMGUI.</summary>
        public static Texture2D ScreenGlow => screenGlow != null ? screenGlow : screenGlow = MenuArt.Glow(64);
        public static Texture2D ScreenSparkle => screenSparkle != null ? screenSparkle : screenSparkle = GameSaveOverlay.CreateSparkleTexture(64);

        // Sprites/Default is transparent, unlit and uses vertex colours, which suits glows and gradient trails.
        public static bool Ensure()
        {
            if (glowMaterial != null) return true;
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return false;
            glowMaterial = new Material(shader) { name = "Cutscene Glow", mainTexture = GlowTexture() };
            sparkleMaterial = new Material(shader) { name = "Cutscene Sparkle", mainTexture = SparkleTexture() };
            plainMaterial = new Material(shader) { name = "Cutscene Trail" };
            columnMaterial = new Material(shader) { name = "Cutscene Light Column", mainTexture = ColumnTexture() };
            return true;
        }

        /// <summary>A collider-free, shadow-free quad under <paramref name="parent"/>.</summary>
        public static Transform Quad(Transform parent, string name, Material material, out Renderer quadRenderer)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quadRenderer = quad.GetComponent<Renderer>();
            quadRenderer.sharedMaterial = material;
            quadRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            quadRenderer.receiveShadows = false;
            return quad.transform;
        }

        /// <summary>Tints one renderer through a property block, so shared materials are never copied.</summary>
        public static void Tint(Renderer quadRenderer, MaterialPropertyBlock block, Color colour, float alpha)
        {
            if (quadRenderer == null) return;
            block.SetColor(ColorId, new Color(colour.r, colour.g, colour.b, Mathf.Clamp01(alpha)));
            quadRenderer.SetPropertyBlock(block);
        }

        /// <summary>Makes a mono clip, scaled down if it would clip.</summary>
        public static AudioClip Clip(string name, float[] samples)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            if (peak > 0.9f)
                for (int i = 0; i < samples.Length; i++) samples[i] *= 0.9f / peak;
            AudioClip clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static Texture2D GlowTexture()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Cutscene Glow", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                    float alpha = Mathf.Clamp01(1f - distance);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                }
            texture.Apply();
            return texture;
        }

        private static Texture2D SparkleTexture()
        {
            const int size = 128;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Cutscene Sparkle", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs((x + 0.5f) / (size / 2f) - 1f);
                    float dy = Mathf.Abs((y + 0.5f) / (size / 2f) - 1f);
                    float radial = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) * 2.2f);
                    float rays = Mathf.Max(Mathf.Exp(-dy * 40f) * (1f - dx), Mathf.Exp(-dx * 40f) * (1f - dy));
                    float alpha = Mathf.Clamp01(radial * radial + rays * 0.9f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            texture.Apply();
            return texture;
        }

        private static Texture2D ColumnTexture()
        {
            const int width = 32, height = 128;
            Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = "Cutscene Light Column", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float across = Mathf.Abs((x + 0.5f) / (width / 2f) - 1f);
                    float up = (y + 0.5f) / height;
                    float side = Mathf.Exp(-across * across * 6f) * Mathf.Clamp01(1f - across);
                    float fade = Mathf.Clamp01(up / 0.08f) * Mathf.Pow(1f - up, 1.6f);
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, side * fade));
                }
            texture.Apply();
            return texture;
        }
    }
}
