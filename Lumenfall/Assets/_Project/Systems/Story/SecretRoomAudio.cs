using Lumenfall.Cutscenes;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The two quiet secret rooms' sounds, generated like the prologue's (<see cref="PrologueAudio"/>), so nothing is
    /// imported. The forgotten room has an old building's hum, a clock somewhere out of sight and the click of a failing
    /// lamp; the dog's room has only the dog, snoring. Both rooms silence the music (see
    /// <see cref="Lumenfall.Audio.AudioContextPolicy.MapMusicCue"/>), so these are all there is to hear.
    /// </summary>
    internal static class SecretRoomAudio
    {
        private const int Rate = CutsceneArt.SampleRate;

        private static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        /// <summary>The hum of an old, empty building: a low mains drone that slowly beats, and air. Loops.</summary>
        public static AudioClip RoomTone()
        {
            float[] samples = Buffer(6f);
            System.Random noise = new System.Random(41);
            float air = 0f, lower = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                // Whole cycles across the six seconds (330 and 333), so the drone and its slow beat meet at the seam.
                float drone = Mathf.Sin(2f * Mathf.PI * 55f * time) * 0.5f + Mathf.Sin(2f * Mathf.PI * 55.5f * time) * 0.5f +
                              Mathf.Sin(2f * Mathf.PI * 110f * time) * 0.18f;
                air = Mathf.Lerp(air, (float)noise.NextDouble() * 2f - 1f, 0.03f);
                lower = Mathf.Lerp(lower, air, 0.08f);
                samples[i] = drone * 0.22f + lower * 1.6f;
            }
            return CutsceneArt.Clip("Forgotten Room Tone", PrologueAudio.Seamless(samples));
        }

        /// <summary>One tick of a clock that is never seen; <paramref name="tock"/> is the lower half of the pair.</summary>
        public static AudioClip Tick(bool tock)
        {
            float[] samples = Buffer(0.09f);
            System.Random noise = new System.Random(tock ? 43 : 47);
            float pitch = tock ? 1500f : 2100f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float click = ((float)noise.NextDouble() * 2f - 1f) * Mathf.Exp(-time * 900f);
                float ping = Mathf.Sin(2f * Mathf.PI * pitch * time) * Mathf.Exp(-time * 70f);
                float body = Mathf.Sin(2f * Mathf.PI * 380f * time) * Mathf.Exp(-time * 120f);
                samples[i] = click * 0.5f + ping * 0.35f + body * 0.3f;
            }
            return CutsceneArt.Clip(tock ? "Forgotten Tock" : "Forgotten Tick", samples);
        }

        /// <summary>A lamp failing: the snap of the filament and a short electric buzz.</summary>
        public static AudioClip LampClick()
        {
            float[] samples = Buffer(0.22f);
            System.Random noise = new System.Random(53);
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float snap = ((float)noise.NextDouble() * 2f - 1f) * Mathf.Exp(-time * 400f);
                // Mains buzz: a square-ish 100 Hz, rough at the edges.
                float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 100f * time)) * 0.25f + Mathf.Sin(2f * Mathf.PI * 300f * time) * 0.15f;
                samples[i] = snap * 0.7f + buzz * Mathf.Clamp01((time - 0.01f) * 60f) * Mathf.Exp(-time * 16f) * 0.6f;
            }
            return CutsceneArt.Clip("Forgotten Lamp Click", samples);
        }

        /// <summary>A soft rising shimmer as a photograph remembers itself (Lost Echoes).</summary>
        public static AudioClip Remember()
        {
            float[] samples = Buffer(2.6f);
            float[] notes = { 659.25f, 783.99f, 987.77f, 1318.51f };
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float sum = 0f;
                for (int n = 0; n < notes.Length; n++)
                {
                    float start = n * 0.18f;
                    if (time < start) continue;
                    float local = time - start;
                    sum += Mathf.Sin(2f * Mathf.PI * notes[n] * local) * Mathf.Clamp01(local * 30f) * Mathf.Exp(-local * 1.6f) / (1f + n * 0.3f);
                }
                samples[i] = sum * 0.22f;
            }
            return CutsceneArt.Clip("Forgotten Remember", samples);
        }

        /// <summary>
        /// A small dog snoring: a rattling breath in, a pause, a soft whistling breath out, and quiet. Loops, and
        /// <see cref="SnoreSeconds"/> long, so the dog can breathe in time with it.
        /// </summary>
        public static AudioClip Snore()
        {
            float[] samples = Buffer(SnoreSeconds);
            System.Random noise = new System.Random(59);
            float breath = 0f, low = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float inhale = Envelope(time, 0.15f, 1.35f);
                float exhale = Envelope(time, 1.65f, 2.75f);
                breath = Mathf.Lerp(breath, (float)noise.NextDouble() * 2f - 1f, 0.12f);
                low = Mathf.Lerp(low, breath, 0.1f);
                // The rattle: a low buzz that flutters thirty times a second, only on the way in.
                float rattle = Mathf.Sin(2f * Mathf.PI * 72f * time) * (0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 31f * time));
                float whistle = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(880f, 720f, Mathf.InverseLerp(1.65f, 2.75f, time)) * time);
                samples[i] = inhale * (rattle * 0.35f + low * 1.4f) + exhale * (breath * 0.18f + whistle * 0.03f);
            }
            return CutsceneArt.Clip("Dog Snore", samples);
        }

        /// <summary>How long one snore (breath in and out) lasts.</summary>
        public const float SnoreSeconds = 3.4f;

        /// <summary>
        /// How full of air the dog is, 0 to 1, <paramref name="time"/> seconds into a snore: rising with the breath in,
        /// falling with the breath out. Its body swells with this, so it moves with the sound.
        /// </summary>
        public static float Breath(float time)
        {
            time = Mathf.Repeat(time, SnoreSeconds);
            if (time < 0.15f) return 0f;
            if (time < 1.35f) return Mathf.SmoothStep(0f, 1f, (time - 0.15f) / 1.2f);
            if (time < 1.65f) return 1f;
            if (time < 2.75f) return Mathf.SmoothStep(1f, 0f, (time - 1.65f) / 1.1f);
            return 0f;
        }

        /// <summary>A small happy "boop" for the dog floating up (Vis) or settling back down.</summary>
        public static AudioClip Boop(bool rising)
        {
            float[] samples = Buffer(0.4f);
            float phase = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float u = time / 0.4f;
                phase += 2f * Mathf.PI * (rising ? Mathf.Lerp(330f, 660f, u) : Mathf.Lerp(660f, 330f, u)) / Rate;
                samples[i] = Mathf.Sin(phase) * Mathf.Clamp01(time * 80f) * Mathf.Exp(-time * 6f) * 0.4f;
            }
            return CutsceneArt.Clip(rising ? "Dog Float Up" : "Dog Float Down", samples);
        }

        // Rises and falls smoothly across [from, to].
        private static float Envelope(float time, float from, float to)
        {
            if (time <= from || time >= to) return 0f;
            return Mathf.Sin(Mathf.PI * (time - from) / (to - from));
        }
    }
}
