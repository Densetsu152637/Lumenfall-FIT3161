using System;

namespace Lumenfall.Audio
{
    public enum AudioContext { Silent, Forest, Campus, Interior, Dialogue, Mystery, Sleep }
    public enum AudioUiCue { Confirm, Cancel, Open, Error }

    /// <summary>Priority and mix policy, without engine time or scene dependencies.</summary>
    public static class AudioContextPolicy
    {
        public static AudioContext Resolve(string mapId, bool dialogue, bool mystery, bool sleeping)
        {
            if (sleeping) return AudioContext.Sleep;
            if (dialogue) return AudioContext.Dialogue;
            if (mystery) return AudioContext.Mystery;
            if (string.IsNullOrWhiteSpace(mapId)) return AudioContext.Silent;
            if (string.Equals(mapId, "westForest", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapId, "eastForest", StringComparison.OrdinalIgnoreCase)) return AudioContext.Forest;
            return string.Equals(mapId, "campus", StringComparison.OrdinalIgnoreCase) ? AudioContext.Campus : AudioContext.Interior;
        }
        public static float MusicGain(bool dialogue, bool sleeping) => sleeping ? 0.3f : dialogue ? 0.4f : 1f;
        public static float AmbienceGain(bool dialogue, bool sleeping) => sleeping ? 0.3f : dialogue ? 0.6f : 1f;
        public static bool ShouldPauseVoice(bool ownsActiveCue, bool ui, bool gamePaused, bool applicationPaused)
            => ownsActiveCue && (applicationPaused || (gamePaused && !ui));
        public static bool CanPlayGameplay(bool gamePaused, bool applicationPaused) => !gamePaused && !applicationPaused;
    }
}
