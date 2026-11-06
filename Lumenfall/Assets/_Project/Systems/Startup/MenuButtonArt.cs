using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Lumenfall.Startup
{
    /// <summary>
    /// The startup menu's buttons, drawn in code as astrolabe plaques to match the menu's night-glass panels: a plum glass
    /// plate in gold trim, a gold socket on its left end where the menu's Weave thread plugs in (amber at rest, cyan when
    /// lit, like the thread), a faint constellation and a gold star at its right tip. Each button has its own
    /// constellation. The label is not part of the art; <see cref="StartupMenu"/> draws it as text, so Back and Cancel can
    /// share the Exit plaque and still say what they do.
    /// <para>
    /// Each sheet stacks four 496x148 frames, top to bottom: normal, hover, pressed and disabled, the layout the menu
    /// already reads, so button sizes and the thread's end point are unchanged. Sheets are drawn on worker threads as the
    /// game starts and uploaded when the menu first needs them.
    /// </para>
    /// </summary>
    internal static class MenuButtonArt
    {
        public const float FrameWidth = 496f, FrameHeight = 148f;
        /// <summary>Where the label goes, as fractions of the frame's width: right of the socket, left of the constellation.</summary>
        public const float LabelLeft = 0.1f, LabelRight = 0.76f;
        public static readonly string[] Keys = { "start", "load", "options", "exit", "save" };
        private const float Scale = 0.75f;

        private static readonly Color GoldLight = new Color(1f, 0.93f, 0.7f);
        private static readonly Color ThreadLive = new Color(0.45f, 0.95f, 1f);
        private static readonly Color ThreadIdle = new Color(1f, 0.62f, 0.3f);

        private enum State { Normal, Hover, Pressed, Disabled }

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Task<ProceduralImage>> Drawing = new Dictionary<string, Task<ProceduralImage>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Cache.Clear();
            Drawing.Clear();
        }

        // Started before the first scene loads, because the startup menu is the first thing on screen.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Prewarm()
        {
            foreach (string key in Keys)
                if (!Drawing.ContainsKey(key)) Drawing[key] = Task.Run(() => DrawSheet(key));
        }

        public static Texture2D Sheet(string key)
        {
            if (Cache.TryGetValue(key, out Texture2D sheet) && sheet != null) return sheet;
            ProceduralImage image;
            try { image = Drawing.TryGetValue(key, out Task<ProceduralImage> drawing) ? drawing.Result : DrawSheet(key); }
            catch (AggregateException) { image = DrawSheet(key); }
            return Cache[key] = image.ToTexture();
        }

        internal static ProceduralImage DrawSheet(string key)
        {
            ProceduralCanvas canvas = new ProceduralCanvas(Vector2.zero, new Vector2(FrameWidth, FrameHeight * 4f), Scale);
            foreach (State state in (State[])Enum.GetValues(typeof(State)))
                DrawFrame(canvas, key, state, (int)state * FrameHeight);
            return canvas.ToImage("MenuButton_" + key);
        }

        private static void DrawFrame(ProceduralCanvas canvas, string key, State state, float top)
        {
            bool disabled = state == State.Disabled, lit = state == State.Hover, pressed = state == State.Pressed;
            Color gold = disabled ? new Color(0.55f, 0.53f, 0.58f) : lit ? GoldLight : pressed ? new Color(0.85f, 0.68f, 0.38f) : MenuArt.Gold;
            Color glassTop = disabled ? new Color(0.17f, 0.16f, 0.2f, 0.9f) : lit ? new Color(0.25f, 0.19f, 0.4f, 0.95f)
                : pressed ? new Color(0.12f, 0.09f, 0.2f, 0.95f) : new Color(0.17f, 0.13f, 0.28f, 0.92f);
            Color glassBottom = new Color(0.08f, 0.06f, 0.14f, 0.95f);
            float sink = pressed ? 3f : 0f;
            Rect plate = new Rect(14f, top + 30f + sink, 458f, 88f);
            Rect frame = new Rect(0f, top, FrameWidth, FrameHeight);
            Func<Vector2, float> shape = p => Sdf.RoundedRect(p, plate, 24f);

            if (lit) canvas.Fill(frame, p => shape(p) - 4f, MenuArt.WithAlpha(MenuArt.Lavender, 0.45f), 14f);
            if (!pressed) canvas.Fill(frame, p => Sdf.RoundedRect(p, new Rect(plate.x, plate.y + 6f, plate.width, plate.height), 24f), MenuArt.WithAlpha(MenuArt.Ink, 0.6f), 8f);

            // Night glass with a soft sheen across its upper half.
            canvas.Fill(frame, shape, p => Color.Lerp(glassTop, glassBottom, (p.y - plate.y) / plate.height));
            canvas.Fill(frame, p => Mathf.Max(shape(p) + 3f, p.y - (plate.y + plate.height * 0.45f)), new Color(1f, 1f, 1f, disabled ? 0.03f : lit ? 0.09f : 0.05f));

            // This button's constellation, faint at rest and brighter when lit.
            Vector2[] stars = Constellation(key, new Vector2(plate.xMax - 112f, plate.y + 18f));
            float starLight = disabled ? 0.25f : lit || pressed ? 1f : 0.6f;
            for (int i = 1; i < stars.Length; i++)
            {
                Vector2 a = stars[i - 1], b = stars[i];
                canvas.Fill(frame, p => Sdf.Segment(p, a, b, 0.9f), MenuArt.WithAlpha(MenuArt.Lavender, 0.5f * starLight));
            }
            foreach (Vector2 star in stars)
                canvas.Fill(Sdf.Around(star, 7f), p => Sdf.Circle(p, star, 2f), MenuArt.WithAlpha(MenuArt.Cream, starLight), 1.5f);

            // Gold trim: the edge and a fine inner line.
            canvas.Fill(frame, p => Sdf.RectOutline(p, plate, 24f, 3f), gold);
            canvas.Fill(frame, p => Sdf.RectOutline(p, new Rect(plate.x + 7f, plate.y + 7f, plate.width - 14f, plate.height - 14f), 17f, 1.2f),
                MenuArt.WithAlpha(gold, disabled ? 0.25f : 0.4f));

            // The socket the thread plugs into, on the left end.
            Vector2 socket = new Vector2(plate.x + 2f, plate.center.y);
            canvas.Fill(Sdf.Around(socket, 22f), p => Sdf.Circle(p, socket, 15f), MenuArt.Ink);
            canvas.Fill(Sdf.Around(socket, 22f), p => Sdf.Ring(p, socket, 12.5f, 4f), gold);
            if (!disabled) canvas.Fill(Sdf.Around(socket, 22f), p => Sdf.Circle(p, socket, 4.5f), lit || pressed ? ThreadLive : ThreadIdle, lit || pressed ? 4f : 1f);

            // A gold star at the right tip.
            Vector2 tip = new Vector2(plate.xMax + 2f, plate.center.y);
            Vector2[] point = Sdf.StarPoints(tip, 17f, 4, 0.3f);
            if (!disabled) canvas.Fill(Sdf.Around(tip, 30f), p => Sdf.Circle(p, tip, 7f), MenuArt.WithAlpha(MenuArt.Gold, lit ? 0.6f : 0.3f), 10f);
            canvas.Fill(Sdf.Around(tip, 20f), p => Sdf.Polygon(p, point), gold);
        }

        /// <summary>A small constellation of five stars inside a 100x52 box, different for each button.</summary>
        private static Vector2[] Constellation(string key, Vector2 origin)
        {
            Vector2[] shape;
            switch (key)
            {
                case "start": shape = new[] { new Vector2(0f, 30f), new Vector2(24f, 14f), new Vector2(50f, 22f), new Vector2(72f, 4f), new Vector2(96f, 16f) }; break;
                case "load": shape = new[] { new Vector2(4f, 8f), new Vector2(28f, 26f), new Vector2(52f, 10f), new Vector2(76f, 34f), new Vector2(96f, 22f) }; break;
                case "options": shape = new[] { new Vector2(10f, 40f), new Vector2(30f, 12f), new Vector2(58f, 6f), new Vector2(80f, 28f), new Vector2(58f, 46f) }; break;
                case "save": shape = new[] { new Vector2(0f, 20f), new Vector2(26f, 38f), new Vector2(50f, 18f), new Vector2(74f, 40f), new Vector2(98f, 12f) }; break;
                default: shape = new[] { new Vector2(6f, 4f), new Vector2(26f, 30f), new Vector2(52f, 20f), new Vector2(70f, 46f), new Vector2(94f, 30f) }; break;
            }
            return Array.ConvertAll(shape, p => origin + p);
        }
    }
}
