using System;
using UnityEngine;

namespace Lumenfall.Startup
{
    /// <summary>
    /// A small vector rasteriser for UI art drawn in code (dialogue frames, spell icons). Shapes are signed distance
    /// functions in a logical coordinate space with y running down, so a design is written once and can be rendered at
    /// any resolution; each fill is anti-aliased over one output pixel and blended over what is already there.
    /// </summary>
    internal sealed class ProceduralCanvas
    {
        public readonly int Width;
        public readonly int Height;
        private readonly float scale;
        private readonly Vector2 origin;
        private readonly Color[] pixels;

        /// <param name="logicalOrigin">The logical point at the canvas's top-left corner.</param>
        /// <param name="scale">Output pixels per logical unit.</param>
        public ProceduralCanvas(Vector2 logicalOrigin, Vector2 logicalSize, float scale)
        {
            origin = logicalOrigin;
            this.scale = scale;
            Width = Mathf.Max(1, Mathf.RoundToInt(logicalSize.x * scale));
            Height = Mathf.Max(1, Mathf.RoundToInt(logicalSize.y * scale));
            pixels = new Color[Width * Height];
        }

        /// <summary>
        /// Blends <paramref name="colour"/> wherever <paramref name="shape"/> is negative, within the logical
        /// <paramref name="bounds"/> (which only limits the work). <paramref name="softness"/> widens the edge, in logical
        /// units, for glows and shadows.
        /// </summary>
        public void Fill(Rect bounds, Func<Vector2, float> shape, Color colour, float softness = 0f)
        {
            ToPixels(bounds, out int x0, out int y0, out int x1, out int y1);
            float edge = 1f / scale + softness;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    Vector2 point = origin + new Vector2((x + 0.5f) / scale, (y + 0.5f) / scale);
                    float coverage = Mathf.Clamp01(0.5f - shape(point) / edge);
                    if (coverage > 0f) Blend(x, y, colour, coverage);
                }
        }

        /// <summary>Like <see cref="Fill"/>, but the colour comes from the point, for gradients.</summary>
        public void Fill(Rect bounds, Func<Vector2, float> shape, Func<Vector2, Color> colour)
        {
            ToPixels(bounds, out int x0, out int y0, out int x1, out int y1);
            float edge = 1f / scale;
            for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    Vector2 point = origin + new Vector2((x + 0.5f) / scale, (y + 0.5f) / scale);
                    float coverage = Mathf.Clamp01(0.5f - shape(point) / edge);
                    if (coverage > 0f) Blend(x, y, colour(point), coverage);
                }
        }

        private void ToPixels(Rect bounds, out int x0, out int y0, out int x1, out int y1)
        {
            x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.xMin - origin.x) * scale) - 2, 0, Width);
            y0 = Mathf.Clamp(Mathf.FloorToInt((bounds.yMin - origin.y) * scale) - 2, 0, Height);
            x1 = Mathf.Clamp(Mathf.CeilToInt((bounds.xMax - origin.x) * scale) + 2, 0, Width);
            y1 = Mathf.Clamp(Mathf.CeilToInt((bounds.yMax - origin.y) * scale) + 2, 0, Height);
        }

        // Straight-alpha "over" blending; rows are stored top to bottom.
        private void Blend(int x, int y, Color colour, float coverage)
        {
            int index = y * Width + x;
            Color under = pixels[index];
            float alpha = colour.a * coverage;
            float outAlpha = alpha + under.a * (1f - alpha);
            if (outAlpha <= 0f) return;
            Color mixed = (colour * alpha + under * under.a * (1f - alpha)) / outAlpha;
            mixed.a = outAlpha;
            pixels[index] = mixed;
        }

        /// <summary>The finished image in texture row order (bottom to top). Safe to call off the main thread.</summary>
        public ProceduralImage ToImage(string name)
        {
            Color32[] flipped = new Color32[pixels.Length];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    flipped[(Height - 1 - y) * Width + x] = pixels[y * Width + x];
            return new ProceduralImage(name, Width, Height, flipped);
        }

        public Texture2D ToTexture(string name) => ToImage(name).ToTexture();
    }

    /// <summary>
    /// Drawn pixels waiting to become a texture. Drawing is plain arithmetic, so art can be prepared on a worker thread;
    /// only <see cref="ToTexture"/> must run on the main thread.
    /// </summary>
    internal sealed class ProceduralImage
    {
        public readonly string Name;
        public readonly int Width;
        public readonly int Height;
        public readonly Color32[] Pixels;

        public ProceduralImage(string name, int width, int height, Color32[] pixels)
        {
            Name = name;
            Width = width;
            Height = height;
            Pixels = pixels;
        }

        public Texture2D ToTexture()
        {
            Texture2D texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                name = Name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            texture.SetPixels32(Pixels);
            texture.Apply(false, true);
            return texture;
        }
    }

    /// <summary>Signed distance functions for <see cref="ProceduralCanvas"/>: negative inside, positive outside.</summary>
    internal static class Sdf
    {
        public static float Circle(Vector2 p, Vector2 centre, float radius) => Vector2.Distance(p, centre) - radius;

        public static float Ring(Vector2 p, Vector2 centre, float radius, float width) =>
            Mathf.Abs(Vector2.Distance(p, centre) - radius) - width * 0.5f;

        public static float RoundedRect(Vector2 p, Rect rect, float radius)
        {
            Vector2 half = rect.size * 0.5f - new Vector2(radius, radius);
            Vector2 q = new Vector2(Mathf.Abs(p.x - rect.center.x), Mathf.Abs(p.y - rect.center.y)) - half;
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        /// <summary>The outline of a rounded rectangle, <paramref name="width"/> wide and centred on its edge.</summary>
        public static float RectOutline(Vector2 p, Rect rect, float radius, float width) =>
            Mathf.Abs(RoundedRect(p, rect, radius)) - width * 0.5f;

        public static float Segment(Vector2 p, Vector2 a, Vector2 b, float width)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
            return Vector2.Distance(p, a + ab * t) - width * 0.5f;
        }

        /// <summary>Exact distance to a closed polygon, signed by the even-odd rule.</summary>
        public static float Polygon(Vector2 p, Vector2[] points)
        {
            float distance = (p - points[0]).sqrMagnitude;
            float sign = 1f;
            for (int i = 0, j = points.Length - 1; i < points.Length; j = i++)
            {
                Vector2 edge = points[j] - points[i];
                Vector2 w = p - points[i];
                Vector2 b = w - edge * Mathf.Clamp01(Vector2.Dot(w, edge) / Mathf.Max(edge.sqrMagnitude, 1e-6f));
                distance = Mathf.Min(distance, b.sqrMagnitude);
                bool above = p.y >= points[i].y, below = p.y < points[j].y, left = edge.x * w.y > edge.y * w.x;
                if ((above && below && left) || (!above && !below && !left)) sign = -sign;
            }
            return sign * Mathf.Sqrt(distance);
        }

        /// <summary>A star with <paramref name="points"/> tips; <paramref name="inner"/> is the waist as a fraction of the size.</summary>
        public static Vector2[] StarPoints(Vector2 centre, float size, int points, float inner, float rotationDegrees = 0f)
        {
            Vector2[] result = new Vector2[points * 2];
            for (int i = 0; i < result.Length; i++)
            {
                float angle = (rotationDegrees - 90f + i * 180f / points) * Mathf.Deg2Rad;
                float radius = i % 2 == 0 ? size : size * inner;
                result[i] = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            }
            return result;
        }

        /// <summary>
        /// A ring arc from <paramref name="startDegrees"/> sweeping <paramref name="sweepDegrees"/> clockwise (y down),
        /// with round ends.
        /// </summary>
        public static float Arc(Vector2 p, Vector2 centre, float radius, float width, float startDegrees, float sweepDegrees)
        {
            Vector2 d = p - centre;
            float angle = Mathf.Repeat(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg - startDegrees, 360f);
            if (angle <= sweepDegrees) return Mathf.Abs(d.magnitude - radius) - width * 0.5f;
            Vector2 a = centre + Direction(startDegrees) * radius;
            Vector2 b = centre + Direction(startDegrees + sweepDegrees) * radius;
            return Mathf.Min(Vector2.Distance(p, a), Vector2.Distance(p, b)) - width * 0.5f;
        }

        public static Vector2 Direction(float degrees) => new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));

        /// <summary>A cog: a disc whose rim carries <paramref name="teeth"/> rounded teeth, with a hole in the middle.</summary>
        public static float Gear(Vector2 p, Vector2 centre, float radius, int teeth, float toothDepth, float hole)
        {
            Vector2 d = p - centre;
            float angle = Mathf.Atan2(d.y, d.x);
            float tooth = Mathf.Clamp01(Mathf.Cos(angle * teeth) * 1.6f + 0.3f);
            float outer = d.magnitude - (radius + toothDepth * tooth);
            return Mathf.Max(outer, hole - d.magnitude);
        }

        public static Rect Around(Vector2 centre, float radius) => new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f);
    }
}
