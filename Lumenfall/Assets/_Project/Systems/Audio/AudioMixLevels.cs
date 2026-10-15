using System;

namespace Lumenfall.Audio
{
    public enum AudioBus { Master, Music, Sfx, Ambience }

    /// <summary>Finite, normalized bus levels. Master is applied by the listener only.</summary>
    public sealed class AudioMixLevels
    {
        private readonly float[] values = { 1f, 0.7f, 0.85f, 0.65f };
        public static float DefaultVolume(AudioBus bus)
        {
            switch (bus) { case AudioBus.Master: return 1f; case AudioBus.Music: return 0.7f;
                case AudioBus.Sfx: return 0.85f; case AudioBus.Ambience: return 0.65f;
                default: throw new ArgumentOutOfRangeException(nameof(bus)); }
        }
        public static float Normalize(float value, float fallback)
        { return float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(0f, Math.Min(1f, value)); }
        public float GetVolume(AudioBus bus) { Validate(bus); return values[(int)bus]; }
        public bool SetVolume(AudioBus bus, float value)
        {
            Validate(bus); float next = Normalize(value, DefaultVolume(bus));
            if (values[(int)bus] == next) return false;
            values[(int)bus] = next; return true;
        }
        private static void Validate(AudioBus bus)
        { if ((int)bus < 0 || (int)bus > 3) throw new ArgumentOutOfRangeException(nameof(bus)); }
    }
}
