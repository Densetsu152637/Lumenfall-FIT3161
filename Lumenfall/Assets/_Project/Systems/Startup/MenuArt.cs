using UnityEngine;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Palette and runtime-generated textures for the startup menu. Everything is drawn procedurally so the menu needs
    /// no imported UI art beyond the button sheets. Callers own the returned textures and must destroy them.
    /// </summary>
    internal static class MenuArt
    {
        // Night-time academy palette, kept close to the dialogue, sign and autosave overlays.
        public static readonly Color Gold = new Color(0.96f, 0.80f, 0.45f);
        public static readonly Color Cream = new Color(0.97f, 0.93f, 0.85f);
        public static readonly Color Muted = new Color(0.75f, 0.71f, 0.85f);
        public static readonly Color Lavender = new Color(0.72f, 0.60f, 1f);
        public static readonly Color Ember = new Color(1f, 0.80f, 0.58f);
        public static readonly Color Ink = new Color(0.07f, 0.05f, 0.12f);
        public static readonly Color Dusk = new Color(0.25f, 0.21f, 0.36f);
        public static readonly Color PanelFill = new Color(0.11f, 0.085f, 0.18f, 0.95f);

        // Weave thread colours match WeaveCableView: cyan when powered, amber when idle.
        public static readonly Color ThreadLive = new Color(0.45f, 0.95f, 1f);
        public static readonly Color ThreadIdle = new Color(1f, 0.62f, 0.3f);

        public static Color WithAlpha(Color colour, float alpha) => new Color(colour.r, colour.g, colour.b, alpha);

        public static Texture2D NewTexture(int width, int height, string name)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "StartupMenu_" + name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave
            };
        }

        /// <summary>Anti-aliased rounded rectangle with an optional border, suitable for nine-slice drawing.</summary>
        public static Texture2D Rounded(int width, int height, float radius, Color fill, Color border, float borderWidth)
        {
            Texture2D texture = NewTexture(width, height, "Rounded");
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - width * 0.5f) - (width * 0.5f - radius);
                    float qy = Mathf.Abs(y + 0.5f - height * 0.5f) - (height * 0.5f - radius);
                    float distance = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                    float coverage = Mathf.Clamp01(0.5f - distance);
                    float borderMix = borderWidth > 0f ? Mathf.Clamp01(distance + borderWidth + 0.5f) : 0f;
                    Color colour = Color.Lerp(fill, border, borderMix);
                    colour.a *= coverage;
                    pixels[y * width + x] = colour;
                }
            return Finish(texture, pixels);
        }

        public static Texture2D Glow(int size)
        {
            Texture2D texture = NewTexture(size, size, "Glow");
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r2 = u * u + v * v;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Exp(-r2 * 4.5f) * Mathf.Clamp01(1f - r2));
                }
            return Finish(texture, pixels);
        }

        /// <summary>A short strip that is solid in the middle and soft at its long edges, used to draw rotated lines.</summary>
        public static Texture2D Line()
        {
            const int Height = 16;
            Texture2D texture = NewTexture(4, Height, "Line");
            Color[] pixels = new Color[4 * Height];
            for (int y = 0; y < Height; y++)
            {
                float v = Mathf.Abs((y + 0.5f) / Height * 2f - 1f);
                float alpha = Mathf.Clamp01((1f - v) * 2.2f);
                for (int x = 0; x < 4; x++) pixels[y * 4 + x] = new Color(1f, 1f, 1f, alpha);
            }
            return Finish(texture, pixels);
        }

        /// <summary>Indigo-to-plum dusk wash with a soft vignette; the world stays faintly visible in the centre.</summary>
        public static Texture2D Backdrop(int size)
        {
            Texture2D texture = NewTexture(size, size, "Backdrop");
            Color top = new Color(0.05f, 0.05f, 0.16f);
            Color bottom = new Color(0.15f, 0.07f, 0.19f);
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float v = y / (size - 1f);
                    float u = x / (size - 1f) * 2f - 1f;
                    float w = v * 2f - 1f;
                    Color colour = Color.Lerp(bottom, top, v);
                    colour.a = 0.32f + 0.5f * Mathf.Clamp01((u * u + w * w) * 0.7f);
                    pixels[y * size + x] = colour;
                }
            return Finish(texture, pixels);
        }

        /// <summary>
        /// A thin circle with evenly spaced ticks. Major ticks every <paramref name="majorEvery"/> make rotation visible.
        /// </summary>
        public static Texture2D Ring(int size, float thickness, int ticks, int majorEvery, float tickLength)
        {
            Texture2D texture = NewTexture(size, size, "Ring");
            Color[] pixels = new Color[size * size];
            float radius = size * 0.5f - tickLength - 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f;
                    float dy = y + 0.5f - size * 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(thickness * 0.5f + 0.5f - Mathf.Abs(r - radius));
                    if (ticks > 0)
                    {
                        float step = Mathf.PI * 2f / ticks;
                        float angle = Mathf.Repeat(Mathf.Atan2(dy, dx) + step * 0.5f, Mathf.PI * 2f);
                        int index = Mathf.FloorToInt(angle / step);
                        float offset = Mathf.Abs(angle - (index + 0.5f) * step) * r;
                        bool major = majorEvery > 0 && index % majorEvery == 0;
                        float length = major ? tickLength : tickLength * 0.5f;
                        float width = major ? 1.6f : 1f;
                        if (r > radius && r < radius + length)
                            alpha = Mathf.Max(alpha, Mathf.Clamp01(width + 0.5f - offset) * (major ? 1f : 0.7f));
                    }
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            return Finish(texture, pixels);
        }

        /// <summary>Small wand cursor: a dark wooden shaft from bottom-right to a glowing star tip at the top-left.</summary>
        public static Texture2D WandCursor()
        {
            const int Size = 32;
            Texture2D texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "StartupMenu_Wand", hideFlags = HideFlags.HideAndDontSave };
#if UNITY_EDITOR
            // The editor rejects cursor textures without this flag; player builds do not need it.
            texture.alphaIsTransparency = true;
#endif
            Color[] pixels = new Color[Size * Size];
            Vector2 tip = new Vector2(5f, Size - 5f);
            Vector2 butt = new Vector2(Size - 3f, 3f);
            Color wood = new Color(0.36f, 0.22f, 0.16f, 1f);
            Color band = new Color(0.86f, 0.68f, 0.36f, 1f);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    Vector2 axis = butt - tip;
                    float t = Mathf.Clamp01(Vector2.Dot(p - tip, axis) / axis.sqrMagnitude);
                    float distance = Vector2.Distance(p, tip + axis * t);
                    float shaftWidth = Mathf.Lerp(1.1f, 2.1f, t);
                    Color colour = Color.clear;

                    float shaft = Mathf.Clamp01(shaftWidth + 0.5f - distance) * (t > 0.16f ? 1f : 0f);
                    if (shaft > 0f)
                    {
                        Color body = t > 0.62f && t < 0.68f ? band : Color.Lerp(wood, wood * 0.7f, t);
                        body.a = 1f;
                        colour = Color.Lerp(colour, body, shaft);
                        if (distance > shaftWidth - 0.6f) colour = Color.Lerp(colour, new Color(0.08f, 0.05f, 0.08f, colour.a), 0.6f);
                    }

                    // Four-pointed star with a warm halo at the tip.
                    Vector2 d = p - tip;
                    float arms = Mathf.Max(0f, 1f - Mathf.Abs(d.x) / 5.5f) * Mathf.Clamp01(1.2f - Mathf.Abs(d.y))
                               + Mathf.Max(0f, 1f - Mathf.Abs(d.y) / 5.5f) * Mathf.Clamp01(1.2f - Mathf.Abs(d.x));
                    float core = Mathf.Clamp01(2.4f - d.magnitude);
                    float halo = Mathf.Exp(-d.sqrMagnitude / 14f) * 0.55f;
                    float star = Mathf.Clamp01(arms + core);
                    if (star + halo > 0.01f)
                    {
                        Color starColour = Color.Lerp(new Color(1f, 0.85f, 0.45f, halo), new Color(1f, 0.98f, 0.9f, 1f), star);
                        starColour.a = Mathf.Max(halo, star);
                        colour = colour.a <= 0f ? starColour : Color.Lerp(colour, starColour, starColour.a);
                        colour.a = Mathf.Max(colour.a, starColour.a);
                    }
                    pixels[y * Size + x] = colour;
                }
            return Finish(texture, pixels);
        }

        private static Texture2D Finish(Texture2D texture, Color[] pixels)
        {
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
