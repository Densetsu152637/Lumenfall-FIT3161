using Lumenfall.Cutscenes;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// The prologue's sounds, generated like the other cutscene sounds (<see cref="CutsceneArt.Clip"/>) so the preview
    /// needs no imported audio: the night wind, the engine, the phone's buzz, the tyres, the door and its chime, the
    /// crickets, the throw and the title's chord. Loops are built to join without a click. The star reuses the opening
    /// star's own sounds (<see cref="FallingStarEffect"/>).
    /// </summary>
    internal static class PrologueAudio
    {
        private const int Rate = CutsceneArt.SampleRate;

        private static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * Rate)];

        /// <summary>A soft, gusting wind under everything. Loops.</summary>
        public static AudioClip Wind()
        {
            float[] samples = Buffer(6f);
            System.Random noise = new System.Random(3);
            float low = 0f, lower = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                low = Mathf.Lerp(low, (float)noise.NextDouble() * 2f - 1f, 0.02f);
                lower = Mathf.Lerp(lower, low, 0.05f);
                // Whole cycles across the loop, so the gusts line up at the seam.
                float gust = 0.6f + 0.25f * Mathf.Sin(time / 6f * Mathf.PI * 2f) + 0.15f * Mathf.Sin(time / 6f * Mathf.PI * 6f + 1f);
                samples[i] = lower * 3.2f * gust;
            }
            return CutsceneArt.Clip("Prologue Wind", Seamless(samples));
        }

        /// <summary>A low engine drone with road rumble. Loops; its pitch follows the car's speed.</summary>
        public static AudioClip Engine()
        {
            float[] samples = Buffer(2f);
            System.Random noise = new System.Random(5);
            float rumble = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                // 48 Hz and its harmonics fit whole cycles into the two seconds.
                float tone = Mathf.Sin(2f * Mathf.PI * 48f * time) * 0.5f + Mathf.Sin(2f * Mathf.PI * 96f * time) * 0.3f +
                             Mathf.Sin(2f * Mathf.PI * 144f * time) * 0.12f;
                float throb = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 12f * time);
                rumble = Mathf.Lerp(rumble, (float)noise.NextDouble() * 2f - 1f, 0.04f);
                samples[i] = tone * throb * 0.5f + rumble * 0.9f;
            }
            return CutsceneArt.Clip("Prologue Engine", Seamless(samples));
        }

        /// <summary>A phone vibrating twice.</summary>
        public static AudioClip Buzz()
        {
            float[] samples = Buffer(0.46f);
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float pulse = (time < 0.16f || (time > 0.26f && time < 0.42f)) ? 1f : 0f;
                float local = time < 0.2f ? time : time - 0.26f;
                float envelope = pulse * Mathf.Clamp01(local * 80f) * Mathf.Clamp01((0.16f - local) * 80f);
                float tone = Mathf.Sin(2f * Mathf.PI * 175f * time) + 0.35f * Mathf.Sin(2f * Mathf.PI * 525f * time);
                float rattle = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 40f * time);
                samples[i] = tone * rattle * envelope * 0.6f;
            }
            return CutsceneArt.Clip("Prologue Buzz", samples);
        }

        /// <summary>Tyres squealing through the turn, then scrubbing on gravel.</summary>
        public static AudioClip Screech()
        {
            float[] samples = Buffer(2.4f);
            System.Random noise = new System.Random(9);
            float phase = 0f, grit = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float squeal = Mathf.Clamp01(time * 12f) * Mathf.Clamp01((1.5f - time) * 2.5f);
                float frequency = Mathf.Lerp(1180f, 760f, time / 1.5f) + 35f * Mathf.Sin(time * 2f * Mathf.PI * 9f);
                phase += 2f * Mathf.PI * frequency / Rate;
                grit = Mathf.Lerp(grit, (float)noise.NextDouble() * 2f - 1f, 0.3f);
                float gravel = Mathf.Clamp01((time - 0.9f) * 3f) * Mathf.Clamp01((2.4f - time) * 1.4f);
                samples[i] = (Mathf.Sin(phase) * 0.5f + grit * 0.4f) * squeal * 0.55f + grit * gravel * 0.5f;
            }
            return CutsceneArt.Clip("Prologue Screech", samples);
        }

        /// <summary>The engine cutting out: a shudder and a fading rumble.</summary>
        public static AudioClip EngineOff()
        {
            float[] samples = Buffer(0.9f);
            System.Random noise = new System.Random(13);
            float rumble = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                rumble = Mathf.Lerp(rumble, (float)noise.NextDouble() * 2f - 1f, 0.05f);
                float shudder = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(48f, 22f, time / 0.9f) * time) * Mathf.Exp(-time * 4f);
                samples[i] = (shudder * 0.7f + rumble * 0.8f) * Mathf.Exp(-time * 3f);
            }
            return CutsceneArt.Clip("Prologue Engine Off", samples);
        }

        /// <summary>A car door's latch and swing.</summary>
        public static AudioClip Door()
        {
            float[] samples = Buffer(0.4f);
            System.Random noise = new System.Random(17);
            float smoothed = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                smoothed = Mathf.Lerp(smoothed, (float)noise.NextDouble() * 2f - 1f, 0.4f);
                float click = smoothed * Mathf.Exp(-time * 60f);
                float thunk = Mathf.Sin(2f * Mathf.PI * 110f * time) * Mathf.Exp(-time * 18f);
                samples[i] = click * 0.8f + thunk * 0.5f;
            }
            return CutsceneArt.Clip("Prologue Door", samples);
        }

        /// <summary>The door-open chime: one soft ding.</summary>
        public static AudioClip Chime()
        {
            float[] samples = Buffer(0.6f);
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float ding = Mathf.Sin(2f * Mathf.PI * 1318.5f * time) + 0.3f * Mathf.Sin(2f * Mathf.PI * 2637f * time);
                samples[i] = ding * Mathf.Clamp01(time * 200f) * Mathf.Exp(-time * 7f) * 0.4f;
            }
            return CutsceneArt.Clip("Prologue Chime", samples);
        }

        /// <summary>Two crickets in the field, chirping out of step. Loops.</summary>
        public static AudioClip Crickets()
        {
            float[] samples = Buffer(4f);
            System.Random random = new System.Random(21);
            AddCricket(samples, random, 4400f, 0.55f, 0.08f);
            AddCricket(samples, random, 3900f, 0.71f, 0.31f);
            return CutsceneArt.Clip("Prologue Crickets", samples);
        }

        // A cricket: bursts of three quick pulses at roughly even intervals across the loop.
        private static void AddCricket(float[] samples, System.Random random, float pitch, float interval, float offset)
        {
            float length = samples.Length / (float)Rate;
            for (float start = offset; start < length - 0.1f; start += interval + (float)random.NextDouble() * 0.12f)
                for (int pulse = 0; pulse < 3; pulse++)
                {
                    int first = Mathf.FloorToInt((start + pulse * 0.022f) * Rate);
                    int count = Mathf.FloorToInt(0.016f * Rate);
                    for (int i = 0; i < count && first + i < samples.Length; i++)
                    {
                        float time = i / (float)Rate;
                        float envelope = Mathf.Sin(Mathf.PI * i / count);
                        samples[first + i] += Mathf.Sin(2f * Mathf.PI * pitch * time) * envelope * 0.35f;
                    }
                }
        }

        /// <summary>Something small flung hard through the air.</summary>
        public static AudioClip Whoosh()
        {
            float[] samples = Buffer(0.7f);
            System.Random noise = new System.Random(25);
            float low = 0f, band = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                float u = time / 0.7f;
                float raw = (float)noise.NextDouble() * 2f - 1f;
                low = Mathf.Lerp(low, raw, 0.08f + 0.25f * Mathf.Sin(u * Mathf.PI));
                band = Mathf.Lerp(band, low, 0.5f);
                samples[i] = (low - band * 0.6f) * Mathf.Sin(u * Mathf.PI) * 1.4f;
            }
            return CutsceneArt.Clip("Prologue Whoosh", samples);
        }

        /// <summary>A phone landing in long grass.</summary>
        public static AudioClip Thud()
        {
            float[] samples = Buffer(0.35f);
            System.Random noise = new System.Random(29);
            float rustle = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float time = i / (float)Rate;
                rustle = Mathf.Lerp(rustle, (float)noise.NextDouble() * 2f - 1f, 0.25f);
                float thump = Mathf.Sin(2f * Mathf.PI * 90f * time) * Mathf.Exp(-time * 22f);
                samples[i] = thump * 0.6f + rustle * Mathf.Exp(-time * 14f) * 0.35f;
            }
            return CutsceneArt.Clip("Prologue Thud", samples);
        }

        // Crossfades the last tenth of a second into the start, so a looped clip has no click at its seam. The secret
        // rooms' loops use it too (SecretRoomAudio).
        internal static float[] Seamless(float[] samples)
        {
            int fade = Mathf.Min(samples.Length / 4, Rate / 10);
            int length = samples.Length - fade;
            float[] looped = new float[length];
            for (int i = 0; i < length; i++) looped[i] = samples[i];
            for (int i = 0; i < fade; i++)
            {
                float mix = i / (float)fade;
                looped[i] = samples[length + i] * (1f - mix) + samples[i] * mix;
            }
            return looped;
        }
    }
}
