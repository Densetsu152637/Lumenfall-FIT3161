using System;
using System.Collections.Generic;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The fallback end of the sequence break (<see cref="SequenceBreakEnding"/>), used when the crushing hand
    /// (<see cref="ScreenCrush"/>) cannot be set up: a still of the game, taken the moment the
    /// glitching voice fell silent, comes back for a breath, cracks twice from one point with a sharp snap, then falls
    /// apart into shards that tumble away into black. The picture is cut into a web of rings and rays around the impact,
    /// so the cracks are the shards' own edges. Drawn in screen pixels with GL from <c>OnGUI</c>; sounds play through
    /// paused audio.
    /// </summary>
    internal sealed class ScreenShatter : IEndingEffect
    {
        /// <summary>Seconds from the still appearing to nothing but black.</summary>
        public const float Duration = 6f;
        private const float FirstCrackAt = 0.7f;
        private const float SecondCrackAt = 1.6f;
        private const float FallAt = 2.5f;
        private const float CrackGrowSeconds = 0.2f;
        private const float ShardFadeSeconds = 1.3f;
        private const int Rays = 16;
        private const int Rings = 5;
        // Rings out to here crack first; the rest crack on the second blow.
        private const int FirstCrackRings = 2;

        private struct Shard
        {
            public Vector2[] Corners;
            public Vector2 Centre;
            public Vector2 Velocity;
            public float Spin;
            public float Delay;
        }

        private struct Crack
        {
            public Vector2 From;
            public Vector2 To;
            public bool First;
        }

        private readonly Texture2D picture;
        private readonly Material pictureMaterial;
        private readonly Material plainMaterial;
        private readonly Vector2 size;
        private readonly Vector2 impact;
        private readonly float reach;
        private readonly float firstReach;
        private readonly float scale;
        private readonly Shard[] shards;
        private readonly Crack[] cracks;
        private readonly GameObject soundHost;
        private readonly AudioSource sound;
        private readonly AudioClip snap;
        private int snapsPlayed;

        public ScreenShatter(Texture2D picture)
        {
            this.picture = picture;
            size = new Vector2(Screen.width, Screen.height);
            scale = Mathf.Max(0.5f, size.y / 1080f);
            Shader shader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Hidden/Internal-Colored");
            pictureMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = picture };
            plainMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, mainTexture = Texture2D.whiteTexture };

            // Struck a little off centre, as if from just in front of the screen.
            impact = new Vector2(size.x * UnityEngine.Random.Range(0.42f, 0.58f), size.y * UnityEngine.Random.Range(0.4f, 0.56f));
            reach = 0f;
            foreach (Vector2 corner in new[] { Vector2.zero, new Vector2(size.x, 0f), new Vector2(0f, size.y), size })
                reach = Mathf.Max(reach, Vector2.Distance(corner, impact));

            // The web: rays out from the impact, crossed by uneven rings. Ring 0 is the impact itself; the last ring lies
            // past every corner, so the shards cover the whole screen.
            float step = Mathf.PI * 2f / Rays;
            float[] angles = new float[Rays];
            for (int j = 0; j < Rays; j++) angles[j] = j * step + UnityEngine.Random.Range(-0.35f, 0.35f) * step;
            Vector2[,] points = new Vector2[Rings + 1, Rays];
            for (int k = 0; k <= Rings; k++)
            {
                float ring = k == Rings ? reach * 1.5f : reach * Mathf.Pow(k / (float)Rings, 1.45f);
                for (int j = 0; j < Rays; j++)
                {
                    float radius = k == 0 || k == Rings ? ring : ring * UnityEngine.Random.Range(0.86f, 1.14f);
                    float angle = angles[j] + (k == 0 ? 0f : UnityEngine.Random.Range(-0.12f, 0.12f) * step);
                    points[k, j] = impact + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                }
            }
            firstReach = reach * Mathf.Pow(FirstCrackRings / (float)Rings, 1.45f) * 1.14f;

            List<Shard> cut = new List<Shard>();
            List<Crack> lines = new List<Crack>();
            for (int k = 0; k < Rings; k++)
            {
                for (int j = 0; j < Rays; j++)
                {
                    int next = (j + 1) % Rays;
                    Vector2[] corners = k == 0
                        ? new[] { impact, points[1, j], points[1, next] }
                        : new[] { points[k, j], points[k + 1, j], points[k + 1, next], points[k, next] };
                    Vector2 centre = Vector2.zero;
                    foreach (Vector2 corner in corners) centre += corner;
                    centre /= corners.Length;
                    Vector2 outward = (centre - impact).sqrMagnitude > 1f ? (centre - impact).normalized : Vector2.up;
                    cut.Add(new Shard
                    {
                        Corners = corners,
                        Centre = centre,
                        // Thrown out from the blow and a little upward before gravity takes them (y is up here).
                        Velocity = (outward * UnityEngine.Random.Range(70f, 260f) + Vector2.up * UnityEngine.Random.Range(40f, 220f)) * scale,
                        Spin = UnityEngine.Random.Range(-240f, 240f),
                        // The middle gives way first, the edges after.
                        Delay = Vector2.Distance(centre, impact) / reach * 0.75f + UnityEngine.Random.Range(0f, 0.18f),
                    });
                    lines.Add(new Crack { From = points[k, j], To = points[k + 1, j], First = k < FirstCrackRings });
                    if (k > 0) lines.Add(new Crack { From = points[k, j], To = points[k, next], First = k < FirstCrackRings });
                }
            }
            shards = cut.ToArray();
            cracks = lines.ToArray();

            soundHost = new GameObject("[Screen Shatter Sound]") { hideFlags = HideFlags.HideAndDontSave };
            sound = soundHost.AddComponent<AudioSource>();
            sound.ignoreListenerPause = true;
            sound.spatialBlend = 0f;
            sound.playOnAwake = false;
            snap = CreateSnap();
        }

        /// <summary>Plays each crack's snap as its moment passes.</summary>
        public void Tick(float t)
        {
            float[] moments = { FirstCrackAt, SecondCrackAt, FallAt };
            float[] volumes = { 0.75f, 1f, 0.55f };
            while (snapsPlayed < moments.Length && t >= moments[snapsPlayed])
            {
                Audio.AudioPlayback.PlayOneShot(sound, snap, volumes[snapsPlayed]);
                snapsPlayed++;
            }
        }

        /// <summary>Draws the moment <paramref name="t"/> seconds in. Call on Repaint only.</summary>
        public void Draw(float t)
        {
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, size.x, 0f, size.y);
            Quad(plainMaterial, Vector2.zero, size, Color.black);

            // Each blow jolts the screen, settling quickly.
            Vector2 shake = Vector2.zero;
            foreach (float blow in new[] { FirstCrackAt, SecondCrackAt })
            {
                if (t < blow) continue;
                float strength = 12f * scale * Mathf.Exp(-(t - blow) * 11f);
                shake += new Vector2(UnityEngine.Random.Range(-strength, strength), UnityEngine.Random.Range(-strength, strength));
            }

            pictureMaterial.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            for (int i = 0; i < shards.Length; i++)
                if (Place(shards[i], t, shake, out float angle, out Vector2 offset, out float alpha))
                    ShardTriangles(shards[i], angle, offset, new Color(1f, 1f, 1f, alpha));
            GL.End();

            plainMaterial.SetPass(0);
            GL.Begin(GL.TRIANGLES);
            if (t < FallAt)
            {
                float firstGrowth = Mathf.Clamp01((t - FirstCrackAt) / CrackGrowSeconds);
                float secondGrowth = Mathf.Clamp01((t - SecondCrackAt) / CrackGrowSeconds);
                float crackRadius = t < SecondCrackAt ? firstReach * firstGrowth : Mathf.Lerp(firstReach, reach * 1.5f, secondGrowth);
                if (t >= FirstCrackAt)
                    foreach (Crack crack in cracks)
                        DrawCrack(crack.From + shake, crack.To + shake, crackRadius, crack.First || t >= SecondCrackAt);
            }
            else
            {
                // Falling, each shard keeps its bright cut edges.
                foreach (Shard shard in shards)
                {
                    if (!Place(shard, t, shake, out float angle, out Vector2 offset, out float alpha)) continue;
                    Color edge = new Color(0.92f, 0.95f, 1f, 0.8f * alpha);
                    for (int c = 0; c < shard.Corners.Length; c++)
                        Line(Moved(shard, shard.Corners[c], angle, offset), Moved(shard, shard.Corners[(c + 1) % shard.Corners.Length], angle, offset), 1.6f * scale, edge);
                }
            }
            GL.End();

            // A white flash with each blow.
            foreach (float blow in new[] { FirstCrackAt, SecondCrackAt })
            {
                float flash = t < blow ? 0f : 0.55f * Mathf.Exp(-(t - blow) * 16f);
                if (flash > 0.01f) Quad(plainMaterial, Vector2.zero, size, new Color(1f, 1f, 1f, flash));
            }
            GL.PopMatrix();
        }

        public void Dispose()
        {
            if (pictureMaterial != null) UnityEngine.Object.Destroy(pictureMaterial);
            if (plainMaterial != null) UnityEngine.Object.Destroy(plainMaterial);
            if (soundHost != null) UnityEngine.Object.Destroy(soundHost);
            if (snap != null) UnityEngine.Object.Destroy(snap);
        }

        // Where a shard is at t: in place until it falls, then thrown, turning and fading. False once it has gone.
        private static bool Place(Shard shard, float t, Vector2 shake, out float angle, out Vector2 offset, out float alpha)
        {
            float falling = t - FallAt - shard.Delay;
            angle = 0f;
            offset = shake;
            alpha = 1f;
            if (falling <= 0f) return true;
            float gravity = -2600f * Mathf.Max(0.5f, Screen.height / 1080f);
            angle = shard.Spin * falling;
            offset = shard.Velocity * falling + new Vector2(0f, 0.5f * gravity * falling * falling);
            alpha = 1f - Mathf.Clamp01((falling - 0.35f) / ShardFadeSeconds);
            return alpha > 0f;
        }

        private static Vector2 Moved(Shard shard, Vector2 point, float angle, Vector2 offset) =>
            shard.Centre + (Vector2)(Quaternion.Euler(0f, 0f, angle) * (point - shard.Centre)) + offset;

        // A fan from the shard's centre, each corner sampling the picture where it started.
        private void ShardTriangles(Shard shard, float angle, Vector2 offset, Color colour)
        {
            Vector2 centre = shard.Centre + offset;
            GL.Color(colour);
            for (int c = 0; c < shard.Corners.Length; c++)
            {
                Vector2 a = shard.Corners[c];
                Vector2 b = shard.Corners[(c + 1) % shard.Corners.Length];
                Vertex(centre, shard.Centre);
                Vertex(Moved(shard, a, angle, offset), a);
                Vertex(Moved(shard, b, angle, offset), b);
            }
        }

        private void Vertex(Vector2 position, Vector2 source)
        {
            GL.TexCoord2(source.x / size.x, source.y / size.y);
            GL.Vertex3(position.x, position.y, 0f);
        }

        // A crack grows outward from the impact: drawn as far as the spreading break has reached.
        private void DrawCrack(Vector2 from, Vector2 to, float radius, bool active)
        {
            if (!active) return;
            float near = Vector2.Distance(from, impact);
            float far = Vector2.Distance(to, impact);
            if (near > far) { (from, to) = (to, from); (near, far) = (far, near); }
            if (radius <= near) return;
            float shown = far - near < 1f ? 1f : Mathf.Clamp01((radius - near) / (far - near));
            Vector2 end = Vector2.Lerp(from, to, shown);
            Line(from, end, 3.2f * scale, new Color(0.08f, 0.08f, 0.1f, 0.55f));
            Line(from, end, 1.4f * scale, new Color(0.95f, 0.97f, 1f, 0.9f));
        }

        private static void Line(Vector2 from, Vector2 to, float width, Color colour)
        {
            Vector2 along = to - from;
            if (along.sqrMagnitude < 0.01f) return;
            Vector2 side = new Vector2(-along.y, along.x).normalized * (width * 0.5f);
            GL.Color(colour);
            GL.TexCoord2(0f, 0f); GL.Vertex3(from.x - side.x, from.y - side.y, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(from.x + side.x, from.y + side.y, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(to.x + side.x, to.y + side.y, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(from.x - side.x, from.y - side.y, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(to.x + side.x, to.y + side.y, 0f);
            GL.TexCoord2(0f, 0f); GL.Vertex3(to.x - side.x, to.y - side.y, 0f);
        }

        private static void Quad(Material material, Vector2 min, Vector2 max, Color colour)
        {
            material.SetPass(0);
            GL.Begin(GL.QUADS);
            GL.Color(colour);
            GL.TexCoord2(0f, 0f); GL.Vertex3(min.x, min.y, 0f);
            GL.TexCoord2(0f, 1f); GL.Vertex3(min.x, max.y, 0f);
            GL.TexCoord2(1f, 1f); GL.Vertex3(max.x, max.y, 0f);
            GL.TexCoord2(1f, 0f); GL.Vertex3(max.x, min.y, 0f);
            GL.End();
        }

        // Glass giving way: a hard click, a burst of noise, and a scatter of bright ringing chips.
        float IEndingEffect.Duration => Duration;

        internal static AudioClip CreateSnap()
        {
            const int rate = 44100;
            const float seconds = 0.9f;
            int count = Mathf.CeilToInt(rate * seconds);
            float[] samples = new float[count];
            System.Random random = new System.Random(3162);
            (float at, float pitch, float level)[] chips = new (float, float, float)[14];
            for (int i = 0; i < chips.Length; i++)
                chips[i] = ((float)random.NextDouble() * 0.45f, 2600f + (float)random.NextDouble() * 5200f, 0.08f + (float)random.NextDouble() * 0.14f);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)rate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                float value = noise * Mathf.Exp(-t * 16f) * 0.75f;
                if (t < 0.006f) value += (1f - t / 0.006f) * (noise > 0f ? 0.9f : -0.9f);
                foreach ((float at, float pitch, float level) in chips)
                    if (t >= at) value += Mathf.Sin(2f * Mathf.PI * pitch * (t - at)) * Mathf.Exp(-(t - at) * 38f) * level;
                samples[i] = Mathf.Clamp(value, -1f, 1f);
            }
            AudioClip clip = AudioClip.Create("Screen Shatter Snap", count, 1, rate, false);
            clip.SetData(samples, 0);
            clip.hideFlags = HideFlags.HideAndDontSave;
            return clip;
        }
    }
}
