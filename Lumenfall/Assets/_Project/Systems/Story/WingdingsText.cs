using System;
using System.Text;
using UnityEngine;

namespace Lumenfall.Story
{
    /// <summary>
    /// Text in the voice that speaks in hands (<see cref="SequenceBreakEnding"/>): plain English drawn in the operating
    /// system's own Wingdings font, so every letter and mark is the real Wingdings glyph and the lines can be decoded.
    /// Wingdings is not shipped with the game; where it is not installed, the capitals fall back to the Unicode symbols
    /// Wingdings draws for them (<see cref="AsSymbols"/>), from the system's fallback fonts.
    /// </summary>
    public static class WingdingsText
    {
        public const string FontName = "Wingdings";

        // Wingdings' glyph for each capital, A to Z, as its Unicode symbol.
        private static readonly string[] Capitals =
        {
            "✌", "👌", "👍", "👎", "☜", "☞", "☝", "☟", "✋", "☺", "😐", "☹", "💣",
            "☠", "⚐", "🏱", "✈", "☼", "💧", "❄", "🕆", "✞", "🕈", "✠", "✡", "☪",
        };

        private static Font loaded;
        private static bool looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            loaded = null;
            looked = false;
        }

        /// <summary>The installed Wingdings font, or null where the system has none.</summary>
        public static Font Font
        {
            get
            {
                if (looked) return loaded;
                looked = true;
                try
                {
                    if (Array.IndexOf(UnityEngine.Font.GetOSInstalledFontNames(), FontName) >= 0)
                        loaded = UnityEngine.Font.CreateDynamicFontFromOSFont(FontName, 32);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("Wingdings could not be loaded; the hand lines use Unicode symbols instead. " + exception.Message);
                    loaded = null;
                }
                return loaded;
            }
        }

        /// <summary>
        /// <paramref name="text"/> as the characters to draw in <paramref name="font"/>. A symbol font keeps its glyphs
        /// either at the letters' own codes or in the private use area at U+F000 plus the code, depending on how the
        /// platform reads it, so each character goes wherever the font has it.
        /// </summary>
        public static string ForFont(Font font, string text)
        {
            if (font == null || string.IsNullOrEmpty(text)) return text;
            StringBuilder result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                char symbol = (char)(0xF000 + c);
                result.Append(c != ' ' && c < 0x100 && !font.HasCharacter(c) && font.HasCharacter(symbol) ? symbol : c);
            }
            return result.ToString();
        }

        /// <summary>
        /// <paramref name="text"/> with each capital replaced by the Unicode symbol Wingdings draws for it, for systems
        /// without the font. Everything else is left as written.
        /// </summary>
        public static string AsSymbols(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            StringBuilder result = new StringBuilder(text.Length * 2);
            foreach (char c in text)
            {
                char upper = char.ToUpperInvariant(c);
                result.Append(upper >= 'A' && upper <= 'Z' ? Capitals[upper - 'A'] : c.ToString());
            }
            return result.ToString();
        }
    }
}
