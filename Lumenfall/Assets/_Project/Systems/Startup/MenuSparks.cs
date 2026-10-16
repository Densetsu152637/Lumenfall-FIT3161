using System.Collections.Generic;
using UnityEngine;
using Lumenfall.GameEvents;

namespace Lumenfall.Startup
{
    /// <summary>
    /// Screen-space light for the startup menu: ambient lantern motes behind the panel, plus short-lived sparks for the
    /// wand trail, click bursts, the title spark and the closing dissolve. Coordinates are IMGUI pixels (origin top-left).
    /// </summary>
    internal sealed class MenuSparks
    {
        private const int MaxSparks = 500;

        private struct Mote
        {
            public float x, y, speed, sway, phase, size, depth;
            public bool gold;
        }

        private struct Spark
        {
            public Vector2 position, velocity;
            public float age, life, size, drag, lift;
            public Color colour;
            public bool star;
        }

        private readonly Mote[] motes;
        private readonly List<Spark> sparks = new List<Spark>(MaxSparks);
        private readonly System.Random random = new System.Random(1170);

        public MenuSparks(int moteCount)
        {
            motes = new Mote[moteCount];
            for (int i = 0; i < motes.Length; i++)
            {
                float depth = Next();
                motes[i] = new Mote
                {
                    x = Next(),
                    y = Next() * 1.1f,
                    speed = 0.012f + 0.028f * Next(),
                    sway = 0.008f + 0.02f * Next(),
                    phase = Next() * Mathf.PI * 2f,
                    size = 6f + 14f * depth,
                    depth = depth,
                    gold = Next() < 0.6f
                };
            }
        }

        public int Count => sparks.Count;

        public float Next() => (float)random.NextDouble();

        public float Range(float min, float max) => min + (max - min) * Next();

        public void Emit(Vector2 position, Vector2 velocity, float life, float size, Color colour, bool star, float drag = 1.5f, float lift = 0f)
        {
            if (sparks.Count >= MaxSparks) sparks.RemoveAt(0);
            sparks.Add(new Spark { position = position, velocity = velocity, life = life, size = size, colour = colour, star = star, drag = drag, lift = lift });
        }

        /// <summary>A ring of sparks thrown outward, used for clicks and the title spark landing.</summary>
        public void Burst(Vector2 centre, int count, float minSpeed, float maxSpeed, float size)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (i + Next() * 0.6f) / count * Mathf.PI * 2f;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Color colour = Next() < 0.55f ? MenuArt.Gold : Next() < 0.5f ? MenuArt.Lavender : MenuArt.Cream;
                Emit(centre, direction * Range(minSpeed, maxSpeed), Range(0.45f, 0.8f), size * Range(0.6f, 1.2f), colour, Next() < 0.4f, 3f, -20f);
            }
        }

        /// <summary>Breaks a rectangle into drifting light: points are sampled across it and pushed away from its centre.</summary>
        public void Dissolve(Rect area, int count)
        {
            for (int i = 0; i < count; i++)
            {
                Vector2 point = new Vector2(Range(area.xMin, area.xMax), Range(area.yMin, area.yMax));
                Vector2 away = point - area.center;
                Vector2 direction = away.sqrMagnitude < 1f ? Vector2.up : away.normalized;
                Color colour = Next() < 0.5f ? MenuArt.Gold : Next() < 0.6f ? MenuArt.Lavender : MenuArt.Cream;
                Emit(point, direction * Range(60f, 240f) + Vector2.down * Range(10f, 60f), Range(0.6f, 1.15f),
                    Range(6f, 16f), colour, Next() < 0.3f, 1.2f, -40f);
            }
        }

        public void Tick(float deltaTime)
        {
            for (int i = sparks.Count - 1; i >= 0; i--)
            {
                Spark spark = sparks[i];
                spark.age += deltaTime;
                if (spark.age >= spark.life)
                {
                    sparks.RemoveAt(i);
                    continue;
                }
                spark.velocity *= Mathf.Exp(-spark.drag * deltaTime);
                spark.velocity.y += spark.lift * deltaTime;
                spark.position += spark.velocity * deltaTime;
                sparks[i] = spark;
            }
        }

        public void Clear() => sparks.Clear();

        /// <summary>Lantern motes drift upward, sway and twinkle. Nearer (larger) motes shift more with the parallax offset.</summary>
        public void DrawMotes(float now, float alpha, Vector2 parallax, Texture glow, Texture star)
        {
            if (alpha <= 0f) return;
            float scale = Mathf.Max(0.6f, UiScale.Height / 1080f);
            for (int i = 0; i < motes.Length; i++)
            {
                Mote mote = motes[i];
                float y = Mathf.Repeat(mote.y - now * mote.speed, 1.1f) - 0.05f;
                float x = mote.x + Mathf.Sin(now * 0.5f + mote.phase) * mote.sway;
                float twinkle = 0.45f + 0.55f * (0.5f + 0.5f * Mathf.Sin(now * (1.2f + mote.speed * 25f) + mote.phase));
                float edge = Mathf.Clamp01(y / 0.25f) * Mathf.Clamp01((1.05f - y) / 0.2f);
                float size = mote.size * scale;
                Vector2 centre = new Vector2(x * UiScale.Width, y * UiScale.Height) - parallax * (6f + 26f * mote.depth);
                Color colour = mote.gold ? MenuArt.Gold : MenuArt.Lavender;
                float a = alpha * twinkle * edge;
                Draw(new Rect(centre.x - size * 1.5f, centre.y - size * 1.5f, size * 3f, size * 3f), glow, colour, 0.35f * a);
                if (mote.size > 15f) Draw(new Rect(centre.x - size * 0.6f, centre.y - size * 0.6f, size * 1.2f, size * 1.2f), star, MenuArt.Cream, 0.8f * a);
                else Draw(new Rect(centre.x - size * 0.25f, centre.y - size * 0.25f, size * 0.5f, size * 0.5f), glow, MenuArt.Cream, 0.9f * a);
            }
        }

        public void DrawSparks(float alpha, Texture glow, Texture star)
        {
            for (int i = 0; i < sparks.Count; i++)
            {
                Spark spark = sparks[i];
                float t = spark.age / spark.life;
                float fadeIn = Mathf.Clamp01(spark.age / 0.08f);
                float a = alpha * fadeIn * (1f - t) * (1f - t);
                float size = spark.size * (1f - 0.5f * t);
                Vector2 p = spark.position;
                Draw(new Rect(p.x - size * 1.4f, p.y - size * 1.4f, size * 2.8f, size * 2.8f), glow, spark.colour, 0.45f * a);
                if (spark.star) Draw(new Rect(p.x - size * 0.7f, p.y - size * 0.7f, size * 1.4f, size * 1.4f), star, MenuArt.Cream, a);
                else Draw(new Rect(p.x - size * 0.3f, p.y - size * 0.3f, size * 0.6f, size * 0.6f), glow, MenuArt.Cream, a);
            }
        }

        private static void Draw(Rect rect, Texture texture, Color tint, float alpha)
        {
            if (texture == null || alpha <= 0.002f) return;
            GUI.color = new Color(tint.r, tint.g, tint.b, alpha);
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, true);
        }
    }
}
