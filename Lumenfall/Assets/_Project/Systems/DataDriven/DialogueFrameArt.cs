using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Lumenfall.Startup;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// The dialogue frames, drawn in code in the menus' plum-and-gold style. Each speaker's frame is a cream writing panel
    /// in a plum band edged with gold, with a medallion and ribbon on each end in the speaker's colour: Stella a gear and
    /// star, Cinna Cirra a cloud, Orio a leaf over a wave, Katrina a gem in pearls, and everyone else a gold star.
    /// <para>
    /// The design is laid out on the 2172x724 frame the dialogue box was built around and rendered at half size into the
    /// three slices <see cref="DialogueView"/> stretches across the screen: a 440-wide left cap, a 64-wide plain middle
    /// strip and a 590-wide right cap. The writing area and text positions in DialogueBox.uss are unchanged.
    /// </para>
    /// </summary>
    internal static class DialogueFrameArt
    {
        public const string DefaultFrame = "Random NPC";
        private const float FrameWidth = 2172f, FrameHeight = 724f;
        private const float LeftWidth = 440f, MiddleWidth = 64f, RightWidth = 590f;
        private const float RightStart = FrameWidth - RightWidth;
        private const float Scale = 0.5f;
        private static readonly Rect Panel = new Rect(200f, 70f, RightStart + 330f - 200f, 590f);
        private const float Band = 22f, Corner = 30f;

        private static readonly Color Gold = new Color(0.93f, 0.76f, 0.4f);
        private static readonly Color GoldLight = new Color(1f, 0.92f, 0.66f);
        private static readonly Color Plum = new Color(0.22f, 0.16f, 0.32f);
        private static readonly Color PlumDeep = new Color(0.13f, 0.09f, 0.2f);
        private static readonly Color Cream = new Color(0.97f, 0.93f, 0.84f);
        private static readonly Color CreamEdge = new Color(0.92f, 0.85f, 0.72f);
        private static readonly Color Shadow = new Color(0.05f, 0.03f, 0.1f, 0.45f);

        private static readonly string[] Frames = { "Stella", "CinnaCirra", "Orio", "Katrina", DefaultFrame };
        private sealed class Pieces { public Texture2D Left, Middle, Right, Full; }
        internal sealed class Images { public ProceduralImage Left, Middle, Right, Full; }
        private static readonly Dictionary<string, Pieces> Cache = new Dictionary<string, Pieces>(StringComparer.OrdinalIgnoreCase);
        // Every frame is drawn on a worker thread at start-up, so a speaker's first line does not stall while their frame
        // is drawn; only turning the pixels into textures happens on the main thread, when the frame is first shown.
        private static readonly Dictionary<string, Task<Images>> Drawing = new Dictionary<string, Task<Images>>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            Drawing.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Prewarm()
        {
            foreach (string frame in Frames)
                if (!Drawing.ContainsKey(frame)) Drawing[frame] = Task.Run(() => DrawImages(frame));
        }

        /// <summary>The frame name for a speaker; speakers without their own frame share <see cref="DefaultFrame"/>.</summary>
        public static string FrameFor(string speaker)
        {
            if (string.Equals(speaker, "Stella", StringComparison.OrdinalIgnoreCase)) return "Stella";
            if (string.Equals(speaker, "Cinna Cirra", StringComparison.OrdinalIgnoreCase)) return "CinnaCirra";
            if (string.Equals(speaker, "Orio", StringComparison.OrdinalIgnoreCase)) return "Orio";
            if (string.Equals(speaker, "Katrina", StringComparison.OrdinalIgnoreCase)) return "Katrina";
            return DefaultFrame;
        }

        /// <summary>The whole frame, named after it, for a box that is not stretched.</summary>
        public static Texture2D Full(string frame) => Get(frame).Full;

        public static bool TryGetPieces(string frame, out Texture2D left, out Texture2D middle, out Texture2D right)
        {
            Pieces pieces = Get(frame);
            left = pieces.Left;
            middle = pieces.Middle;
            right = pieces.Right;
            return left != null && middle != null && right != null;
        }

        private static Pieces Get(string frame)
        {
            frame = string.IsNullOrEmpty(frame) ? DefaultFrame : frame;
            if (Cache.TryGetValue(frame, out Pieces pieces) && pieces.Left != null) return pieces;
            Images images;
            try { images = Drawing.TryGetValue(frame, out Task<Images> drawing) ? drawing.Result : DrawImages(frame); }
            catch (AggregateException) { images = DrawImages(frame); }
            pieces = new Pieces
            {
                Left = images.Left.ToTexture(),
                Middle = images.Middle.ToTexture(),
                Right = images.Right.ToTexture(),
                Full = images.Full.ToTexture(),
            };
            return Cache[frame] = pieces;
        }

        internal static Images DrawImages(string frame)
        {
            Images images = new Images
            {
                Left = Render(frame, 0f, LeftWidth, " Left"),
                Middle = Render(frame, LeftWidth, MiddleWidth, " Middle"),
                Right = Render(frame, RightStart, RightWidth, " Right"),
            };
            images.Full = Compose(frame, images);
            return images;
        }

        private static ProceduralImage Render(string frame, float x, float width, string suffix)
        {
            ProceduralCanvas canvas = new ProceduralCanvas(new Vector2(x, 0f), new Vector2(width, FrameHeight), Scale);
            Draw(canvas, frame);
            return canvas.ToImage(frame + suffix);
        }

        // The full frame, named after it, is the left cap, the middle strip repeated, and the right cap, side by side.
        private static ProceduralImage Compose(string frame, Images images)
        {
            int height = images.Left.Height;
            int width = Mathf.RoundToInt(FrameWidth * Scale);
            Color32[] pixels = new Color32[width * height];
            ProceduralImage left = images.Left, middle = images.Middle, right = images.Right;
            int rightStart = width - right.Width;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = x < left.Width ? left.Pixels[y * left.Width + x]
                        : x >= rightStart ? right.Pixels[y * right.Width + x - rightStart]
                        : middle.Pixels[y * middle.Width + (x - left.Width) % middle.Width];
            return new ProceduralImage(frame, width, height, pixels);
        }

        // ---- The design ----

        private static void Draw(ProceduralCanvas canvas, string frame)
        {
            Color accent = AccentFor(frame);
            Rect panelShadow = new Rect(Panel.x, Panel.y + 8f, Panel.width, Panel.height);
            Rect inner = new Rect(Panel.x + Band, Panel.y + Band, Panel.width - Band * 2f, Panel.height - Band * 2f);
            Rect around = new Rect(Panel.x - 40f, Panel.y - 40f, Panel.width + 80f, Panel.height + 80f);

            // Panel: a soft shadow, the plum band, gold lines on both edges of the band, and the cream writing area.
            canvas.Fill(around, p => Sdf.RoundedRect(p, panelShadow, Corner), Shadow, 16f);
            canvas.Fill(around, p => Sdf.RoundedRect(p, Panel, Corner), Plum);
            canvas.Fill(around, p => Sdf.RectOutline(p, Panel, Corner, 5f), Gold);
            canvas.Fill(around, p => Sdf.RectOutline(p, Inset(Panel, 9f), Corner - 9f, 1.5f), GoldLight);
            canvas.Fill(around, p => Sdf.RoundedRect(p, inner, Corner - Band), p =>
                Color.Lerp(CreamEdge, Cream, Mathf.Clamp01(-Sdf.RoundedRect(p, inner, Corner - Band) / 46f)));
            canvas.Fill(around, p => Sdf.RectOutline(p, inner, Corner - Band, 3f), Gold);

            // Corner jewels on the panel's four corners.
            foreach (Vector2 corner in new[]
                     {
                         new Vector2(Panel.xMin + 4f, Panel.yMin + 4f), new Vector2(Panel.xMin + 4f, Panel.yMax - 4f),
                         new Vector2(Panel.xMax - 4f, Panel.yMin + 4f), new Vector2(Panel.xMax - 4f, Panel.yMax - 4f),
                     })
            {
                Vector2[] diamond = { corner + new Vector2(0f, -17f), corner + new Vector2(17f, 0f), corner + new Vector2(0f, 17f), corner + new Vector2(-17f, 0f) };
                canvas.Fill(Sdf.Around(corner, 24f), p => Sdf.Polygon(p, diamond), Gold);
                canvas.Fill(Sdf.Around(corner, 24f), p => Sdf.Circle(p, corner, 5f), accent);
            }

            // Left cap: a ribbon falling from a medallion, and a few sparkles.
            Ribbon(canvas, new[] { new Vector2(128f, 340f), new Vector2(150f, 450f), new Vector2(104f, 560f), new Vector2(118f, 660f) }, accent);
            Medallion(canvas, new Vector2(128f, 262f), 106f, accent, frame);
            Sparkle(canvas, new Vector2(42f, 112f), 17f);
            Sparkle(canvas, new Vector2(48f, 548f), 12f);
            Sparkle(canvas, new Vector2(232f, 696f), 10f);

            // Right cap: a larger medallion and ribbon, a small star medallion beneath, and sparkles.
            float r = RightStart;
            Ribbon(canvas, new[] { new Vector2(r + 470f, 350f), new Vector2(r + 520f, 450f), new Vector2(r + 548f, 560f), new Vector2(r + 530f, 662f) }, accent);
            Medallion(canvas, new Vector2(r + 456f, 250f), 120f, accent, frame);
            Medallion(canvas, new Vector2(r + 428f, 566f), 58f, accent, null);
            Sparkle(canvas, new Vector2(r + 566f, 70f), 15f);
            Sparkle(canvas, new Vector2(r + 350f, 34f), 11f);
            Sparkle(canvas, new Vector2(r + 360f, 694f), 10f);
        }

        private static Rect Inset(Rect rect, float inset) =>
            new Rect(rect.x + inset, rect.y + inset, rect.width - inset * 2f, rect.height - inset * 2f);

        // The same colours as each speaker's name ribbon (DialogueOverlay.SpeakerColor).
        private static Color AccentFor(string frame)
        {
            switch (frame)
            {
                case "Stella": return new Color(0.76f, 0.22f, 0.3f);
                case "CinnaCirra": return new Color(0.3f, 0.48f, 0.72f);
                case "Orio": return new Color(0.18f, 0.53f, 0.43f);
                case "Katrina": return new Color(0.64f, 0.33f, 0.58f);
                default: return new Color(0.42f, 0.36f, 0.3f);
            }
        }

        private static void Sparkle(ProceduralCanvas canvas, Vector2 centre, float size)
        {
            Vector2[] star = Sdf.StarPoints(centre, size, 4, 0.28f);
            canvas.Fill(Sdf.Around(centre, size * 2.2f), p => Sdf.Circle(p, centre, size * 0.5f), new Color(1f, 0.9f, 0.6f, 0.35f), size);
            canvas.Fill(Sdf.Around(centre, size + 2f), p => Sdf.Polygon(p, star), GoldLight);
        }

        /// <summary>A ribbon along a path: dark edges, the speaker's colour, a gold stitch, and a swallowtail end.</summary>
        private static void Ribbon(ProceduralCanvas canvas, Vector2[] controls, Color accent)
        {
            Vector2[] path = Smooth(controls, 12);
            Rect bounds = Bounds(path, 40f);
            Func<Vector2, float, float> band = (p, width) =>
            {
                float d = float.MaxValue;
                for (int i = 1; i < path.Length; i++) d = Mathf.Min(d, Sdf.Segment(p, path[i - 1], path[i], width));
                return d;
            };
            Vector2 end = path[path.Length - 1], before = path[path.Length - 2];
            Vector2 along = (end - before).normalized, across = new Vector2(-along.y, along.x);
            Vector2[] tail = { end - along * 6f + across * 19f, end + along * 26f + across * 19f, end + along * 10f, end + along * 26f - across * 19f, end - along * 6f - across * 19f };
            Color dark = Color.Lerp(accent, PlumDeep, 0.55f);
            canvas.Fill(bounds, p => Mathf.Min(band(p, 40f), Sdf.Polygon(p, tail) - 2f), dark);
            canvas.Fill(bounds, p => Mathf.Min(band(p, 32f), Sdf.Polygon(p, tail) + 2f), accent);
            canvas.Fill(bounds, p => band(p, 2.5f), new Color(GoldLight.r, GoldLight.g, GoldLight.b, 0.8f));
        }

        // A Catmull-Rom curve through the control points, so a ribbon bends instead of zigzagging.
        private static Vector2[] Smooth(Vector2[] controls, int stepsPerSpan)
        {
            List<Vector2> points = new List<Vector2>();
            for (int i = 0; i < controls.Length - 1; i++)
            {
                Vector2 p0 = controls[Mathf.Max(i - 1, 0)], p1 = controls[i], p2 = controls[i + 1], p3 = controls[Mathf.Min(i + 2, controls.Length - 1)];
                for (int step = 0; step < stepsPerSpan; step++)
                {
                    float t = step / (float)stepsPerSpan, t2 = t * t, t3 = t2 * t;
                    points.Add(0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (3f * p1 - p0 - 3f * p2 + p3) * t3));
                }
            }
            points.Add(controls[controls.Length - 1]);
            return points.ToArray();
        }

        private static Rect Bounds(Vector2[] points, float margin)
        {
            Vector2 min = points[0], max = points[0];
            foreach (Vector2 point in points) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            return new Rect(min.x - margin, min.y - margin, max.x - min.x + margin * 2f, max.y - min.y + margin * 2f);
        }

        /// <summary>A plum medallion with a gold rim and the speaker's emblem; no frame gives a plain gold star.</summary>
        private static void Medallion(ProceduralCanvas canvas, Vector2 c, float radius, Color accent, string frame)
        {
            Rect bounds = Sdf.Around(c, radius + 24f);
            canvas.Fill(bounds, p => Sdf.Circle(p, c + new Vector2(0f, 7f), radius), Shadow, 12f);
            canvas.Fill(bounds, p => Sdf.Circle(p, c, radius), Gold);
            canvas.Fill(bounds, p => Sdf.Circle(p, c, radius - 9f), p =>
                Color.Lerp(Plum, PlumDeep, Mathf.Clamp01((p.y - c.y + radius) / (radius * 2f))));
            canvas.Fill(bounds, p => Sdf.Ring(p, c, radius - 19f, 2.5f), new Color(Gold.r, Gold.g, Gold.b, 0.8f));
            canvas.Fill(bounds, p => Sdf.Circle(p, c, radius * 0.62f), new Color(accent.r, accent.g, accent.b, 0.35f), radius * 0.4f);
            Emblem(canvas, c, radius, accent, frame);
        }

        private static void Emblem(ProceduralCanvas canvas, Vector2 c, float r, Color accent, string frame)
        {
            Rect bounds = Sdf.Around(c, r);
            Color light = Color.Lerp(accent, Color.white, 0.55f);
            switch (frame)
            {
                case "Stella":
                {
                    Vector2[] star = Sdf.StarPoints(c, r * 0.3f, 8, 0.45f);
                    canvas.Fill(bounds, p => Sdf.Gear(p, c, r * 0.5f, 10, r * 0.1f, r * 0.33f), Gold);
                    canvas.Fill(bounds, p => Sdf.Polygon(p, star) - 2f, PlumDeep);
                    canvas.Fill(bounds, p => Sdf.Polygon(p, star), light);
                    break;
                }
                case "CinnaCirra":
                {
                    Func<Vector2, float> cloud = p => Mathf.Min(Mathf.Min(Sdf.Circle(p, c + new Vector2(-0.3f, 0.06f) * r, r * 0.26f),
                            Sdf.Circle(p, c + new Vector2(0.04f, -0.14f) * r, r * 0.34f)),
                        Mathf.Min(Sdf.Circle(p, c + new Vector2(0.34f, 0.06f) * r, r * 0.24f),
                            Sdf.RoundedRect(p, new Rect(c.x - r * 0.54f, c.y + r * 0.02f, r * 1.08f, r * 0.28f), r * 0.14f)));
                    canvas.Fill(bounds, p => cloud(p) - 4f, accent);
                    canvas.Fill(bounds, cloud, new Color(0.93f, 0.96f, 1f));
                    Sparkle(canvas, c + new Vector2(0.44f, -0.46f) * r, r * 0.13f);
                    Sparkle(canvas, c + new Vector2(-0.48f, -0.34f) * r, r * 0.09f);
                    break;
                }
                case "Orio":
                {
                    Vector2 diagonal = new Vector2(0.22f, 0.22f) * r;
                    Func<Vector2, float> leaf = p => Mathf.Max(Sdf.Circle(p, c + diagonal - new Vector2(0f, r * 0.12f), r * 0.56f),
                        Sdf.Circle(p, c - diagonal - new Vector2(0f, r * 0.12f), r * 0.56f));
                    canvas.Fill(bounds, p => leaf(p) - 3f, Gold);
                    canvas.Fill(bounds, leaf, accent);
                    canvas.Fill(bounds, p => Sdf.Segment(p, c + new Vector2(-0.34f, 0.22f) * r, c + new Vector2(0.34f, -0.46f) * r, r * 0.04f), GoldLight);
                    canvas.Fill(bounds, p =>
                    {
                        float x = (p.x - c.x) / (r * 0.16f);
                        float wave = Mathf.Abs(p.y - (c.y + r * 0.52f + Mathf.Sin(x) * r * 0.07f)) - r * 0.035f;
                        return Mathf.Max(wave, Mathf.Abs(p.x - c.x) - r * 0.5f);
                    }, light);
                    break;
                }
                case "Katrina":
                {
                    Vector2[] gem =
                    {
                        c + new Vector2(-0.34f, -0.16f) * r, c + new Vector2(-0.18f, -0.34f) * r, c + new Vector2(0.18f, -0.34f) * r,
                        c + new Vector2(0.34f, -0.16f) * r, c + new Vector2(0f, 0.36f) * r,
                    };
                    canvas.Fill(bounds, p => Sdf.Polygon(p, gem) - 3f, Gold);
                    canvas.Fill(bounds, p => Sdf.Polygon(p, gem), p => Color.Lerp(light, accent, Mathf.Clamp01((p.y - c.y + r * 0.34f) / (r * 0.7f))));
                    canvas.Fill(bounds, p => Mathf.Min(Sdf.Segment(p, gem[0], gem[3], 2f),
                        Mathf.Min(Sdf.Segment(p, gem[1], gem[4], 1.5f), Sdf.Segment(p, gem[2], gem[4], 1.5f))), GoldLight);
                    for (int i = 0; i < 12; i++)
                    {
                        Vector2 pearl = c + Sdf.Direction(i * 30f + 15f) * r * 0.64f;
                        canvas.Fill(Sdf.Around(pearl, r * 0.1f), p => Sdf.Circle(p, pearl, r * 0.065f), new Color(0.98f, 0.95f, 0.9f));
                    }
                    break;
                }
                default:
                {
                    Vector2[] star = Sdf.StarPoints(c, r * 0.5f, 4, 0.3f);
                    canvas.Fill(bounds, p => Sdf.Circle(p, c, r * 0.2f), new Color(1f, 0.85f, 0.5f, 0.5f), r * 0.25f);
                    canvas.Fill(bounds, p => Sdf.Polygon(p, star), GoldLight);
                    break;
                }
            }
        }
    }
}
