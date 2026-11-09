using System.Collections;
using Lumenfall.Audio;
using Lumenfall.Controls;
using UnityEngine;

namespace Lumenfall.Cutscenes
{
    /// <summary>
    /// The cutscene <c>glitch</c> step's full-screen effect, drawn with IMGUI just in front of the cutscene overlay.
    /// <list type="bullet">
    /// <item><b>Tear</b>: the picture freezes on a still taken that frame, then tears: horizontal bands of it slide
    /// sideways, red and blue copies split from it, blocks of light flicker on a grid and scanlines darken it, harder as
    /// the step goes on, over a stuttering burst of static.</item>
    /// <item><b>Rewind</b>: bright tracking lines roll up the screen with a little static and a whirr, like a tape
    /// rewinding.</item>
    /// </list>
    /// Reduce motion replaces the tear with a slow fade into deep violet, and draws nothing for the rewind but plays its
    /// sound. The palette matches the glitch aura's.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScreenGlitchEffect : MonoBehaviour
    {
        public enum Style { Tear, Rewind }

        private static readonly Color Red = new Color(1f, 0.176f, 0.333f);
        private static readonly Color Blue = new Color(0.239f, 0.482f, 1f);
        private static readonly Color White = new Color(0.91f, 0.94f, 1f);
        private static readonly Color Cyan = new Color(0.561f, 0.827f, 1f);
        private static readonly Color Violet = new Color(0.294f, 0.165f, 0.478f);
        private static readonly Color DeepViolet = new Color(0.078f, 0.047f, 0.141f);
        private const float TickSeconds = 0.045f;
        private const int SampleRate = 22050;

        private struct Band { public float Y, Height, Shift; }
        private struct Block { public Rect Rect; public Color Color; }

        private readonly Band[] bands = new Band[16];
        private readonly Block[] blocks = new Block[28];
        private int bandCount, blockCount;
        private Style style;
        private bool active, capturing, reduce;
        private float startedAt, seconds, nextTick;
        private Texture2D still;
        private AudioSource sound;
        private AudioClip clip;

        public bool IsActive => active;

        /// <summary>Starts the effect in the style named by <paramref name="styleName"/> ("tear" or "rewind").</summary>
        public void Begin(string styleName, float duration)
        {
            Stop();
            style = styleName == "rewind" ? Style.Rewind : Style.Tear;
            seconds = Mathf.Max(0.05f, duration);
            startedAt = Time.unscaledTime;
            nextTick = 0f;
            reduce = GameSettingsPanel.ReduceMotion;
            active = true;
            PlaySound();
            if (style == Style.Tear && !reduce) StartCoroutine(CaptureStill());
        }

        public void Stop()
        {
            StopAllCoroutines();
            capturing = false;
            active = false;
            if (still != null) Destroy(still);
            still = null;
            if (sound != null) sound.Stop();
        }

        private void OnDestroy()
        {
            Stop();
            if (clip != null) Destroy(clip);
        }

        // The picture as it was the moment the glitch began, so the world seems to freeze as it tears.
        private IEnumerator CaptureStill()
        {
            capturing = true;
            yield return new WaitForEndOfFrame();
            try { still = ScreenCapture.CaptureScreenshotAsTexture(); }
            catch (System.Exception exception) { Debug.LogWarning("The screen glitch could not capture the screen: " + exception.Message); }
            capturing = false;
        }

        private void Update()
        {
            if (active && Time.unscaledTime - startedAt >= seconds) Stop();
        }

        private void OnGUI()
        {
            if (!active || capturing || Event.current.type != EventType.Repaint) return;
            // In front of the cutscene overlay (-1000), so the rewind shows over black as the screen fades back in.
            GUI.depth = -1001;
            Color previous = GUI.color;
            float t = Mathf.Clamp01((Time.unscaledTime - startedAt) / seconds);
            if (style == Style.Tear)
            {
                if (reduce) DrawCalmFade(t);
                else DrawTear(t);
            }
            else if (!reduce) DrawRewind(t);
            GUI.color = previous;
        }

        private void DrawCalmFade(float t)
        {
            float eased = t * t * (3f - 2f * t);
            Fill(new Rect(0f, 0f, Screen.width, Screen.height), DeepViolet, eased);
        }

        private void DrawTear(float t)
        {
            float width = Screen.width, height = Screen.height;
            float scale = Mathf.Max(0.5f, height / 1080f);
            float intensity = Mathf.Lerp(0.35f, 1f, t);
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + TickSeconds;
                RollTear(intensity, width, height, scale);
            }
            Rect screen = new Rect(0f, 0f, width, height);

            if (still != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(screen, still);
                // The split: red and blue copies pulled either side of the picture.
                float split = (6f + 18f * intensity) * scale;
                GUI.color = new Color(Red.r, Red.g, Red.b, 0.28f * intensity);
                GUI.DrawTexture(new Rect(split, 0f, width, height), still);
                GUI.color = new Color(Blue.r, Blue.g, Blue.b, 0.28f * intensity);
                GUI.DrawTexture(new Rect(-split, 0f, width, height), still);
                // Bands of the picture torn sideways.
                GUI.color = Color.white;
                for (int i = 0; i < bandCount; i++)
                {
                    Band band = bands[i];
                    Rect coords = new Rect(0f, 1f - (band.Y + band.Height) / height, 1f, band.Height / height);
                    GUI.DrawTextureWithTexCoords(new Rect(band.Shift, band.Y, width, band.Height), still, coords);
                }
            }
            else
            {
                for (int i = 0; i < bandCount; i++) Fill(new Rect(0f, bands[i].Y, width, bands[i].Height), DeepViolet, 0.8f);
            }

            Fill(screen, Violet, 0.16f * intensity);
            for (int i = 0; i < blockCount; i++) Fill(blocks[i].Rect, blocks[i].Color, 0.85f);
            float gap = Mathf.Max(3f, 4f * scale);
            for (float y = 0f; y < height; y += gap) Fill(new Rect(0f, y, width, Mathf.Max(1f, scale)), Color.black, 0.18f);
        }

        private void RollTear(float intensity, float width, float height, float scale)
        {
            bandCount = Mathf.Clamp(Mathf.RoundToInt(3f + 13f * intensity), 0, bands.Length);
            for (int i = 0; i < bandCount; i++)
            {
                float bandHeight = Random.Range(4f, 70f) * scale;
                bands[i] = new Band
                {
                    Y = Random.Range(0f, height - bandHeight),
                    Height = bandHeight,
                    Shift = Random.Range(-1f, 1f) * Random.Range(12f, 140f) * intensity * scale
                };
            }
            float cell = 8f * scale;
            blockCount = Mathf.Clamp(Mathf.RoundToInt(6f + 22f * intensity), 0, blocks.Length);
            for (int i = 0; i < blockCount; i++)
            {
                float w = cell * Random.Range(1, 7), h = cell * Random.Range(1, 3);
                float x = Mathf.Round(Random.Range(0f, width - w) / cell) * cell;
                float y = Mathf.Round(Random.Range(0f, height - h) / cell) * cell;
                float pick = Random.value;
                blocks[i] = new Block
                {
                    Rect = new Rect(x, y, w, h),
                    Color = pick < 0.4f ? White : pick < 0.65f ? Cyan : pick < 0.83f ? Blue : Red
                };
            }
        }

        private void DrawRewind(float t)
        {
            float width = Screen.width, height = Screen.height;
            float scale = Mathf.Max(0.5f, height / 1080f);
            float elapsed = Time.unscaledTime - startedAt;
            float fade = 1f - t * t;
            // Tracking lines rolling upwards, each a soft band with a bright edge.
            for (int i = 0; i < 4; i++)
            {
                float speed = 1.6f + i * 0.55f;
                float y = (1f - Mathf.Repeat(i * 0.27f + elapsed * speed, 1f)) * height;
                float bandHeight = (14f + 22f * i) * scale;
                Fill(new Rect(0f, y, width, bandHeight), White, 0.16f * fade);
                Fill(new Rect(0f, y, width, Mathf.Max(1f, 2f * scale)), White, 0.55f * fade);
            }
            if (Time.unscaledTime >= nextTick)
            {
                nextTick = Time.unscaledTime + TickSeconds;
                float cell = 6f * scale;
                blockCount = 18;
                for (int i = 0; i < blockCount; i++)
                {
                    float w = cell * Random.Range(1, 5);
                    blocks[i] = new Block
                    {
                        Rect = new Rect(Random.Range(0f, width - w), Random.Range(0f, height), w, cell),
                        Color = Random.value < 0.7f ? White : Cyan
                    };
                }
            }
            for (int i = 0; i < blockCount; i++) Fill(blocks[i].Rect, blocks[i].Color, 0.5f * fade);
            float gap = Mathf.Max(3f, 4f * scale);
            for (float y = 0f; y < height; y += gap) Fill(new Rect(0f, y, width, Mathf.Max(1f, scale)), Color.black, 0.12f * fade);
        }

        private static void Fill(Rect rect, Color color, float alpha)
        {
            if (alpha <= 0f) return;
            GUI.color = new Color(color.r, color.g, color.b, alpha);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
        }

        // ---- Sound ----

        private void PlaySound()
        {
            if (sound == null)
            {
                sound = gameObject.AddComponent<AudioSource>();
                sound.playOnAwake = false;
                sound.loop = false;
                sound.spatialBlend = 0f;
                sound.ignoreListenerPause = true;
            }
            if (clip != null) Destroy(clip);
            clip = style == Style.Tear ? TearClip(seconds, reduce) : RewindClip(seconds);
            AudioPlayback.PlayOneShot(sound, clip, 0.7f * AudioMixSettings.Sfx);
        }

        // Static cut into a stutter: bursts of noise and a crushed buzz, switching on and off every few hundredths of a
        // second, louder towards the end. Reduce motion keeps a softer, steadier hiss.
        private static AudioClip TearClip(float seconds, bool calm)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(1729);
            bool on = true;
            int switchAt = 0;
            float buzz = 0f;
            for (int i = 0; i < length; i++)
            {
                if (i >= switchAt)
                {
                    on = calm || random.NextDouble() < 0.65;
                    switchAt = i + (int)(SampleRate * (0.02 + random.NextDouble() * 0.07));
                    buzz = (float)(110 + random.NextDouble() * 330);
                }
                float t = i / (float)length;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                float square = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * buzz * i / SampleRate));
                float sample = on ? noise * 0.55f + square * 0.25f : 0f;
                // Crush to a few levels for a digital edge.
                sample = Mathf.Round(sample * 6f) / 6f;
                float envelope = calm ? 0.25f * Mathf.Sin(Mathf.PI * t) : Mathf.Lerp(0.35f, 0.9f, t) * Mathf.Min(1f, (1f - t) * 40f);
                data[i] = sample * envelope;
            }
            AudioClip result = AudioClip.Create("Glitch Tear", length, 1, SampleRate, false);
            result.SetData(data, 0);
            return result;
        }

        // A tape whirr: a pitch that dives then climbs, fluttering, under soft hiss.
        private static AudioClip RewindClip(float seconds)
        {
            int length = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            float[] data = new float[length];
            System.Random random = new System.Random(4242);
            float phase = 0f;
            float low = 0f;
            for (int i = 0; i < length; i++)
            {
                float t = i / (float)length;
                float pitch = Mathf.Lerp(900f, 180f, Mathf.Sin(t * Mathf.PI));
                phase += 2f * Mathf.PI * pitch / SampleRate;
                float flutter = 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * 32f * i / SampleRate);
                low = Mathf.Lerp(low, (float)(random.NextDouble() * 2.0 - 1.0), 0.15f);
                float envelope = Mathf.Sin(Mathf.PI * t);
                data[i] = (Mathf.Sin(phase) * 0.35f * flutter + low * 0.4f) * envelope;
            }
            AudioClip result = AudioClip.Create("Glitch Rewind", length, 1, SampleRate, false);
            result.SetData(data, 0);
            return result;
        }
    }
}
