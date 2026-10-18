using System;

namespace Lumenfall.Audio
{
    public enum AudioContext { Silent, Forest, Campus, Interior, Dialogue, Mystery, Sleep }
    /// <summary>Plays as the manifest cue "ui." + the lower-case name, e.g. SpellWheelOpen is "ui.spellwheelopen".</summary>
    public enum AudioUiCue { Confirm, Cancel, Open, Error, SpellWheelOpen, SpellWheelClose }

    /// <summary>Priority and mix policy, without engine time or scene dependencies.</summary>
    public static class AudioContextPolicy
    {
        public static AudioContext Resolve(string mapId, bool dialogue, bool mystery, bool sleeping)
        {
            if (sleeping) return AudioContext.Sleep;
            if (dialogue) return AudioContext.Dialogue;
            if (mystery) return AudioContext.Mystery;
            if (string.IsNullOrWhiteSpace(mapId)) return AudioContext.Silent;
            if (IsForest(mapId)) return AudioContext.Forest;
            return string.Equals(mapId, "campus", StringComparison.OrdinalIgnoreCase) ? AudioContext.Campus : AudioContext.Interior;
        }
        /// <summary>The track a map plays: forest maps and Orio's dais room share the forest theme, the dormitory has
        /// its own, and campus, every other building and the trial rooms share the campus theme.</summary>
        public static string MapMusicCue(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId)) return null;
            if (IsForest(mapId) || string.Equals(mapId, "orioDaisRoom", StringComparison.OrdinalIgnoreCase)) return "music.forest";
            if (mapId.StartsWith("B01.", StringComparison.OrdinalIgnoreCase)) return "music.dormitory";
            return "music.campus";
        }
        private static bool IsForest(string mapId) =>
            string.Equals(mapId, "westForest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mapId, "deepForest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(mapId, "eastForest", StringComparison.OrdinalIgnoreCase);
        public static float MusicGain(bool dialogue, bool sleeping) => sleeping ? 0.3f : dialogue ? 0.4f : 1f;
        public static float AmbienceGain(bool dialogue, bool sleeping) => sleeping ? 0.3f : dialogue ? 0.6f : 1f;
        public static bool ShouldPauseVoice(bool ownsActiveCue, bool ui, bool gamePaused, bool applicationPaused)
            => ownsActiveCue && (applicationPaused || (gamePaused && !ui));
        public static bool CanPlayGameplay(bool gamePaused, bool applicationPaused) => !gamePaused && !applicationPaused;
    }
}
