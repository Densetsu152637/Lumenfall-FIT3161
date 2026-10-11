using UnityEngine;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Runtime-generated props for the in-game menus: the relic orb used by switches, the volume lantern, the pause frost,
    /// the hourglass, the wax seal and the silk bookmark ribbon. Like <see cref="MenuArt"/>, callers own the textures.
    /// Shapes are drawn with signed distances in pixel space (y measured down from the top) and anti-aliased edges.
    /// </summary>
    internal static class MenuProps
    {
        /// <summary>Shaded sphere with a bright core, darker rim and a small highlight. White, so it can be tinted.</summary>
        public static Texture2D Orb(int size)
        {
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(u * u + v * v);
                    float coverage = Mathf.Clamp01((1f - r) * size * 0.5f + 0.5f);
                    float shade = 0.55f + 0.45f * (1f - r * r);
                    float hx = u + 0.35f, hy = v - 0.4f;
                    float highlight = Mathf.Exp(-(hx * hx + hy * hy) * 18f) * 0.9f;
                    float c = Mathf.Min(1f, shade + highlight);
                    pixels[y * size + x] = new Color(c, c, c, coverage);
                }
            return Finish(MenuArt.NewTexture(size, size, "Orb"), pixels);
        }

        /// <summary>A hanging lantern: ring, cap, barred glass and base. White frame with faint glass, for tinting brass.</summary>
        public static Texture2D Lantern()
        {
            const int W = 32, H = 48;
            Color[] pixels = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float px = x + 0.5f, py = H - (y + 0.5f);
                    float frame = Stroke(Mathf.Sqrt(Sq(px - 16f) + Sq(py - 4.5f)) - 3f, 1.1f);
                    if (py >= 7.5f && py <= 13f)
                    {
                        float half = Mathf.Lerp(5f, 10f, (py - 7.5f) / 5.5f);
                        frame = Mathf.Max(frame, Mathf.Clamp01(half + 0.5f - Mathf.Abs(px - 16f)));
                    }
                    frame = Mathf.Max(frame, Stroke(RectDistance(px, py, 8f, 13f, 24f, 37f), 1.4f));
                    if (py > 13f && py < 37f)
                    {
                        frame = Mathf.Max(frame, Mathf.Clamp01(0.95f - Mathf.Abs(px - 13.3f)) * 0.8f);
                        frame = Mathf.Max(frame, Mathf.Clamp01(0.95f - Mathf.Abs(px - 18.7f)) * 0.8f);
                    }
                    frame = Mathf.Max(frame, Fill(RectDistance(px, py, 6f, 37f, 26f, 42f)));
                    frame = Mathf.Max(frame, Fill(RectDistance(px, py, 10f, 42f, 22f, 44.5f)));
                    float glass = Fill(RectDistance(px, py, 8f, 13f, 24f, 37f)) * 0.16f;
                    pixels[y * W + x] = new Color(1f, 1f, 1f, Mathf.Max(frame, glass));
                }
            return Finish(MenuArt.NewTexture(W, H, "Lantern"), pixels);
        }

        /// <summary>
        /// Icy vignette for the pause: dense at the edges and corners, thinning inward through noisy, jagged crystal
        /// growth with fine fern-like streaks. Coloured, so it is drawn with a white tint.
        /// </summary>
        public static Texture2D Frost(int size)
        {
            Color[] pixels = new Color[size * size];
            Color ice = new Color(0.84f, 0.95f, 1f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = x / (size - 1f), v = y / (size - 1f);
                    float edgeDistance = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    float cornerDistance = (Mathf.Min(u, 1f - u) + Mathf.Min(v, 1f - v)) * 0.5f;
                    float noise = Fbm(u * 7f, v * 7f);
                    float edge = 1f - Mathf.SmoothStep(0f, 0.2f, edgeDistance + (noise - 0.5f) * 0.12f);
                    float corner = 1f - Mathf.SmoothStep(0f, 0.24f, cornerDistance + (noise - 0.5f) * 0.1f);
                    float growth = Mathf.Max(edge, corner);

                    // Thin, wandering crystal lines in four directions, broken up so they read as frost, not a grid.
                    float streaks = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        float angle = 0.35f + k * 0.8f;
                        float along = u * Mathf.Cos(angle) + v * Mathf.Sin(angle);
                        float across = -u * Mathf.Sin(angle) + v * Mathf.Cos(angle);
                        float lineShape = Mathf.Pow(1f - Mathf.Abs(Mathf.Sin(along * 38f + ValueNoise(across * 9f, k * 3.1f) * 4f)), 22f);
                        streaks = Mathf.Max(streaks, lineShape * (0.4f + 0.6f * ValueNoise(across * 14f + k, along * 3f)));
                    }
                    float alpha = growth * (0.35f + 0.45f * noise) + streaks * growth * 0.9f;
                    float c = 0.82f + 0.18f * streaks;
                    pixels[y * size + x] = new Color(ice.r * c, ice.g * c, ice.b, Mathf.Clamp01(alpha));
                }
            return Finish(MenuArt.NewTexture(size, size, "Frost"), pixels);
        }

        /// <summary>A small brass hourglass with sand frozen mid-fall: a broken stream and a few hanging grains.</summary>
        public static Texture2D Hourglass()
        {
            const int W = 16, H = 24;
            Color[] pixels = new Color[W * H];
            Color sand = Color.Lerp(MenuArt.Ember, MenuArt.Cream, 0.3f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float px = x + 0.5f, py = H - (y + 0.5f);
                    float dx = Mathf.Abs(px - 8f);
                    float half = py < 12f ? Mathf.Lerp(5f, 0.9f, (py - 3f) / 9f) : Mathf.Lerp(0.9f, 5f, (py - 12f) / 9f);
                    bool inBulb = py > 3f && py < 21f;
                    float bars = Mathf.Max(Fill(RectDistance(px, py, 1.5f, 1f, 14.5f, 3f)), Fill(RectDistance(px, py, 1.5f, 21f, 14.5f, 23f)));
                    float glass = inBulb ? Mathf.Clamp01(1f - Mathf.Abs(dx - half)) : 0f;
                    float frame = Mathf.Max(bars, glass);

                    float grains = 0f;
                    if (inBulb && dx < half - 0.7f)
                    {
                        if (py > 7f && py < 11.6f) grains = 1f;
                        if (py > 21f - 2.6f + dx * 0.45f) grains = 1f;
                    }
                    if (dx < 0.55f && py > 12f && py < 15.5f) grains = 1f;
                    if (Sq(px - 8f) + Sq(py - 16.8f) < 0.3f || Sq(px - 8f) + Sq(py - 18f) < 0.3f) grains = 1f;

                    Color colour = Color.clear;
                    if (grains > 0f) colour = new Color(sand.r, sand.g, sand.b, grains);
                    if (frame > 0f) colour = Color.Lerp(colour, new Color(MenuArt.Gold.r, MenuArt.Gold.g, MenuArt.Gold.b, 1f), frame);
                    colour.a = Mathf.Max(grains, frame);
                    pixels[y * W + x] = colour;
                }
            return Finish(MenuArt.NewTexture(W, H, "Hourglass"), pixels);
        }

        /// <summary>A pressed wax seal: a wobbly disc, a sunken ring and a raised four-pointed star. Greyscale, for tinting.</summary>
        public static Texture2D WaxSeal(int size)
        {
            Color[] pixels = new Color[size * size];
            float radius = size * 0.5f - 1.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float angle = Mathf.Atan2(dy, dx);
                    float edge = radius * (1f + 0.06f * Mathf.Sin(angle * 7f) + 0.04f * Mathf.Sin(angle * 11f + 1f));
                    float coverage = Mathf.Clamp01(edge - r + 0.5f);
                    float shade = 0.82f - 0.12f * (r / radius);
                    float ring = Mathf.Clamp01(1.2f - Mathf.Abs(r - radius * 0.66f));
                    shade = Mathf.Lerp(shade, 0.58f, ring);
                    float u = Mathf.Abs(dx) / (radius * 0.48f), v = Mathf.Abs(dy) / (radius * 0.48f);
                    float star = Mathf.Clamp01((1f - (Mathf.Sqrt(u) + Mathf.Sqrt(v))) * 6f);
                    shade = Mathf.Lerp(shade, 1f, star);
                    pixels[y * size + x] = new Color(shade, shade, shade, coverage);
                }
            return Finish(MenuArt.NewTexture(size, size, "WaxSeal"), pixels);
        }

        /// <summary>A silk bookmark ribbon with a swallowtail notch at its right end and a soft sheen. White, for tinting.</summary>
        public static Texture2D Ribbon(int width, int height, float notch)
        {
            Color[] pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    float v = (y + 0.5f) / height;
                    float limit = width - notch * (1f - Mathf.Abs(v - 0.5f) * 2f);
                    float coverage = Mathf.Clamp01(limit - (x + 0.5f) + 0.5f);
                    float centre = 1f - Mathf.Abs(v - 0.5f) * 2f;
                    float shade = 0.72f + 0.28f * centre * centre;
                    if (Mathf.Abs(v - 0.18f) < 0.025f || Mathf.Abs(v - 0.82f) < 0.025f) shade = Mathf.Min(1f, shade + 0.2f);
                    pixels[y * width + x] = new Color(shade, shade, shade, coverage);
                }
            return Finish(MenuArt.NewTexture(width, height, "Ribbon"), pixels);
        }

        public static Texture2D Flat()
        {
            Texture2D texture = MenuArt.NewTexture(2, 2, "Flat");
            return Finish(texture, new[] { Color.white, Color.white, Color.white, Color.white });
        }

        private static float Sq(float value) => value * value;

        private static float Stroke(float distance, float width) => Mathf.Clamp01(width * 0.5f + 0.5f - Mathf.Abs(distance));

        private static float Fill(float distance) => Mathf.Clamp01(0.5f - distance);

        private static float RectDistance(float px, float py, float x0, float y0, float x1, float y1)
        {
            float qx = Mathf.Abs(px - (x0 + x1) * 0.5f) - (x1 - x0) * 0.5f;
            float qy = Mathf.Abs(py - (y0 + y1) * 0.5f) - (y1 - y0) * 0.5f;
            return new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f);
        }

        private static float Hash(int x, int y)
        {
            float h = Mathf.Sin(x * 127.1f + y * 311.7f) * 43758.5453f;
            return h - Mathf.Floor(h);
        }

        private static float ValueNoise(float x, float y)
        {
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Mathf.Lerp(Hash(ix, iy), Hash(ix + 1, iy), fx);
            float b = Mathf.Lerp(Hash(ix, iy + 1), Hash(ix + 1, iy + 1), fx);
            return Mathf.Lerp(a, b, fy);
        }

        private static float Fbm(float x, float y) => ValueNoise(x, y) * 0.6f + ValueNoise(x * 2.3f, y * 2.3f) * 0.3f + ValueNoise(x * 5.1f, y * 5.1f) * 0.1f;

        private static Texture2D Finish(Texture2D texture, Color[] pixels)
        {
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
