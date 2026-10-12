using System;
using UnityEngine;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// Soft "voice" blips while dialogue text reveals. The tone is generated in code, so no audio asset is needed;
    /// each speaker gets their own pitch so characters sound distinct. The Narrator is silent. Volume follows the
    /// master volume through the audio listener.
    /// </summary>
    internal sealed class DialogueBlips
    {
        private const float MinInterval = 0.05f;
        private readonly AudioSource source;
        private readonly AudioClip clip;
        private float lastPlayed = float.NegativeInfinity;

        public DialogueBlips(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0.2f;
            clip = CreateClip();
        }

        public void Play(string speaker)
        {
            float pitch = Pitch(speaker);
            if (pitch <= 0f || Time.unscaledTime - lastPlayed < MinInterval) return;
            lastPlayed = Time.unscaledTime;
            source.pitch = pitch * UnityEngine.Random.Range(0.96f, 1.04f);
            source.PlayOneShot(clip);
        }

        private static float Pitch(string speaker)
        {
            switch (speaker)
            {
                case "Narrator": return 0f;
                case "Stella": return 1.12f;
                case "Cinna Cirra": return 1.0f;
                case "Orio": return 0.8f;
                case "Katrina": return 1.26f;
                default:
                    // Everyone else gets a stable pitch from their name, between 0.88 and 1.08.
                    int hash = 17;
                    foreach (char c in speaker ?? string.Empty) hash = hash * 31 + c;
                    return 0.88f + (Math.Abs(hash) % 21) / 100f;
            }
        }

        /// <summary>A short rounded tone with a quick attack and decay.</summary>
        private static AudioClip CreateClip()
        {
            const int rate = 44100;
            const float duration = 0.06f;
            int samples = Mathf.CeilToInt(rate * duration);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Min(1f, t / 0.004f) * Mathf.Exp(-t * 48f);
                float tone = Mathf.Sin(2f * Mathf.PI * 620f * t) * 0.75f + Mathf.Sin(2f * Mathf.PI * 1240f * t) * 0.2f;
                data[i] = tone * envelope;
            }

            AudioClip result = AudioClip.Create("Dialogue Blip", samples, 1, rate, false);
            result.SetData(data, 0);
            return result;
        }
    }
}
