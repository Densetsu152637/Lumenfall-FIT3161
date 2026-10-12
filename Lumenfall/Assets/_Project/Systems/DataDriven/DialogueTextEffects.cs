using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// Prepares dialogue text for the dialogue box. Standard rich-text tags pass through untouched. Writers can add
    /// <c>&lt;wave&gt;</c>, <c>&lt;shake&gt;</c> and <c>&lt;spell&gt;</c>; spell names are highlighted automatically, and the
    /// word before an exclamation mark gets a small wave. Effects are reported per glyph, in the order the text
    /// renderer emits them: one glyph per visible non-whitespace character.
    /// </summary>
    public static class DialogueTextEffects
    {
        public enum Effect { None, Wave, Shake }

        public sealed class Prepared
        {
            public string RichText = string.Empty;
            public Effect[] GlyphEffects = Array.Empty<Effect>();
        }

        public const string SpellColour = "#6B3FC4";
        private static readonly Regex SpellName = new Regex(@"(?<!\w)(?<!<spell>)(Lost Echoes|Ampere|Weave|Vis|Glaciate|Rotara)(?!\w)(?!</spell>)", RegexOptions.Compiled);

        public static Prepared Prepare(string text)
        {
            if (string.IsNullOrEmpty(text)) return new Prepared();

            // Spell names: bare ones are wrapped, then every <spell> span is styled.
            string styled = SpellName.Replace(text, "<spell>$1</spell>");
            styled = Regex.Replace(styled, "<spell>", $"<color={SpellColour}><b>", RegexOptions.IgnoreCase);
            styled = Regex.Replace(styled, "</spell>", "</b></color>", RegexOptions.IgnoreCase);

            // Strip effect tags, recording which visible characters they cover.
            var output = new StringBuilder(styled.Length);
            var visible = new StringBuilder(styled.Length);
            var effects = new List<Effect>();
            var open = new Stack<Effect>();
            for (int i = 0; i < styled.Length;)
            {
                if (styled[i] == '<')
                {
                    int close = styled.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        string tag = styled.Substring(i + 1, close - i - 1).Trim().ToLowerInvariant();
                        Effect? effect = tag switch { "wave" => Effect.Wave, "shake" => Effect.Shake, _ => null };
                        if (effect.HasValue) { open.Push(effect.Value); i = close + 1; continue; }
                        if ((tag == "/wave" || tag == "/shake") && open.Count > 0) { open.Pop(); i = close + 1; continue; }
                        output.Append(styled, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }

                output.Append(styled[i]);
                visible.Append(styled[i]);
                effects.Add(open.Count > 0 ? open.Peek() : Effect.None);
                i++;
            }

            // A small wave on the word just before each '!', unless it already has an effect.
            string plain = visible.ToString();
            for (int i = 0; i < plain.Length; i++)
            {
                if (plain[i] != '!') continue;
                int start = i;
                while (start > 0 && (char.IsLetterOrDigit(plain[start - 1]) || plain[start - 1] == '\'')) start--;
                for (int j = start; j <= i; j++)
                    if (effects[j] == Effect.None) effects[j] = Effect.Wave;
            }

            var glyphs = new List<Effect>(plain.Length);
            for (int i = 0; i < plain.Length; i++)
                if (!char.IsWhiteSpace(plain[i])) glyphs.Add(effects[i]);
            return new Prepared { RichText = output.ToString(), GlyphEffects = glyphs.ToArray() };
        }
    }
}
