using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Lumenfall.Startup;

namespace Lumenfall.Spells
{
    /// <summary>
    /// The spell icons, drawn in code in the menus' plum-and-gold style: a gold symbol with a dark plum outline and a soft
    /// glow in the spell's element colour, on a clear background so the spell wheel's own orb shows around it. Each icon
    /// is drawn once, the first time it is needed, and shared after that.
    /// </summary>
    internal static class SpellIconArt
    {
        private const float Size = 100f;
        private const int Pixels = 256;
        private static readonly Color Ink = new Color(0.12f, 0.08f, 0.2f, 0.95f);
        private static readonly Color GoldTop = new Color(1f, 0.94f, 0.7f);
        private static readonly Color GoldBottom = new Color(0.86f, 0.6f, 0.25f);
        private static readonly Dictionary<SpellId, Texture2D> Cache = new Dictionary<SpellId, Texture2D>();
        // Drawn on a worker thread at start-up; only the texture upload happens on the main thread.
        private static readonly Dictionary<SpellId, Task<ProceduralImage>> Drawing = new Dictionary<SpellId, Task<ProceduralImage>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            Drawing.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Prewarm()
        {
            foreach (SpellId spell in (SpellId[])Enum.GetValues(typeof(SpellId)))
                if (!Drawing.ContainsKey(spell)) Drawing[spell] = Task.Run(() => Draw(spell));
        }

        public static Texture2D Get(SpellId spell)
        {
            if (Cache.TryGetValue(spell, out Texture2D icon) && icon != null) return icon;
            ProceduralImage image;
            try { image = Drawing.TryGetValue(spell, out Task<ProceduralImage> drawing) ? drawing.Result : Draw(spell); }
            catch (AggregateException) { image = Draw(spell); }
            return Cache[spell] = image.ToTexture();
        }

        internal static ProceduralImage Draw(SpellId spell)
        {
            Func<Vector2, float> symbol = Symbol(spell);
            ProceduralCanvas canvas = new ProceduralCanvas(Vector2.zero, new Vector2(Size, Size), Pixels / Size);
            Rect all = new Rect(0f, 0f, Size, Size);
            Color glow = SpellWheelView.ElementColor(spell);
            glow.a = 0.5f;
            canvas.Fill(all, p => symbol(p) - 3f, glow, 9f);
            canvas.Fill(all, p => symbol(p) - 3.2f, Ink);
            canvas.Fill(all, symbol, p => Color.Lerp(GoldTop, GoldBottom, Mathf.Clamp01((p.y - 10f) / 80f)));
            // A thin bright edge along the top of each stroke, like the menus' gilded trim.
            canvas.Fill(all, p => Mathf.Max(symbol(p) + 1.2f, -symbol(p - new Vector2(0f, 2.2f))),new Color(1f, 0.98f, 0.88f, 0.75f));
            return canvas.ToImage("SpellIcon_" + spell);
        }

        private static Func<Vector2, float> Symbol(SpellId spell)
        {
            switch (spell)
            {
                case SpellId.Ampere:
                {
                    Vector2[] bolt =
                    {
                        new Vector2(60f, 5f), new Vector2(25f, 55f), new Vector2(46f, 55f), new Vector2(37f, 95f),
                        new Vector2(76f, 40f), new Vector2(54f, 40f), new Vector2(68f, 5f),
                    };
                    return p => Sdf.Polygon(p, bolt);
                }
                case SpellId.Weave:
                    // Three loops woven through one another.
                    return p => Mathf.Min(Sdf.Ring(p, new Vector2(50f, 34f), 19f, 7f),
                        Mathf.Min(Sdf.Ring(p, new Vector2(34f, 62f), 19f, 7f), Sdf.Ring(p, new Vector2(66f, 62f), 19f, 7f)));
                case SpellId.Rotara:
                {
                    Vector2 centre = new Vector2(50f, 50f);
                    Vector2[] headA = ArrowHead(centre, 32f, 330f);
                    Vector2[] headB = ArrowHead(centre, 32f, 150f);
                    return p => Mathf.Min(Mathf.Min(Sdf.Arc(p, centre, 32f, 9f, 200f, 128f), Sdf.Arc(p, centre, 32f, 9f, 20f, 128f)),
                        Mathf.Min(Sdf.Polygon(p, headA), Sdf.Polygon(p, headB)));
                }
                case SpellId.Glaciate:
                    return p =>
                    {
                        Vector2 centre = new Vector2(50f, 50f);
                        float d = Sdf.Circle(p, centre, 7f);
                        for (int k = 0; k < 6; k++)
                        {
                            float angle = k * 60f - 90f;
                            Vector2 arm = Sdf.Direction(angle);
                            d = Mathf.Min(d, Sdf.Segment(p, centre, centre + arm * 42f, 7f));
                            Vector2 fork = centre + arm * 26f;
                            d = Mathf.Min(d, Sdf.Segment(p, fork, fork + Sdf.Direction(angle - 45f) * 13f, 5f));
                            d = Mathf.Min(d, Sdf.Segment(p, fork, fork + Sdf.Direction(angle + 45f) * 13f, 5f));
                        }
                        return d;
                    };
                case SpellId.Vis:
                {
                    // A relic rising, lifted on two chevrons.
                    Vector2[] gem = { new Vector2(50f, 6f), new Vector2(70f, 30f), new Vector2(50f, 54f), new Vector2(30f, 30f) };
                    return p => Mathf.Min(Sdf.Polygon(p, gem), Mathf.Min(
                        Mathf.Min(Sdf.Segment(p, new Vector2(28f, 76f), new Vector2(50f, 62f), 8f), Sdf.Segment(p, new Vector2(50f, 62f), new Vector2(72f, 76f), 8f)),
                        Mathf.Min(Sdf.Segment(p, new Vector2(33f, 93f), new Vector2(50f, 82f), 7f), Sdf.Segment(p, new Vector2(50f, 82f), new Vector2(67f, 93f), 7f))));
                }
                case SpellId.LostEchoes:
                {
                    // A small star sending out three ripples.
                    Vector2 source = new Vector2(30f, 50f);
                    Vector2[] star = Sdf.StarPoints(source, 13f, 4, 0.38f);
                    return p =>
                    {
                        float d = Sdf.Polygon(p, star);
                        for (int ring = 1; ring <= 3; ring++)
                            d = Mathf.Min(d, Sdf.Arc(p, source, 10f + ring * 14f, 6f, -50f, 100f));
                        return d;
                    };
                }
                case SpellId.Gravitate:
                    return p =>
                    {
                        Vector2 centre = new Vector2(50f, 50f);
                        float d = Mathf.Min(Sdf.Circle(p, centre, 10f), Sdf.Ring(p, centre, 38f, 6f));
                        for (int k = 0; k < 4; k++)
                        {
                            Vector2 arm = Sdf.Direction(k * 90f - 45f), across = new Vector2(-arm.y, arm.x);
                            Vector2[] head = { centre + arm * 18f, centre + arm * 30f + across * 9f, centre + arm * 30f - across * 9f };
                            d = Mathf.Min(d, Sdf.Polygon(p, head));
                        }
                        return d;
                    };
                default:
                    return p => Sdf.Circle(p, new Vector2(50f, 50f), 30f);
            }
        }

        // An arrowhead at the clockwise end of an arc, pointing along it.
        private static Vector2[] ArrowHead(Vector2 centre, float radius, float endDegrees)
        {
            Vector2 end = centre + Sdf.Direction(endDegrees) * radius;
            Vector2 along = Sdf.Direction(endDegrees + 90f), outward = Sdf.Direction(endDegrees);
            return new[] { end + along * 15f, end + outward * 12f - along * 2f, end - outward * 12f - along * 2f };
        }
    }
}
