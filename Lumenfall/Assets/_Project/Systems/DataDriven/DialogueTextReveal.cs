using System;
using System.Collections.Generic;
using System.Text;

namespace Lumenfall.DataDriven
{
    /// <summary>Pure reveal helpers; rich-text tags are emitted whole and do not consume reveal time.</summary>
    public static class DialogueTextReveal
    {
        public static bool IsPunctuationPause(char character)
        {
            return character == '.' || character == '!' || character == '?' || character == ',';
        }

        public static float PunctuationPause(char character)
        {
            return IsPunctuationPause(character) ? 0.12f : 0f;
        }

        public static int VisibleCharacterCount(string richText)
        {
            if (string.IsNullOrEmpty(richText)) return 0;
            int count = 0; bool tag = false;
            for (int i = 0; i < richText.Length; i++)
            {
                if (richText[i] == '<') { tag = true; continue; }
                if (tag) { if (richText[i] == '>') tag = false; continue; }
                count++;
            }
            return count;
        }

        public static char VisibleCharacterAt(string richText, int index)
        {
            if (string.IsNullOrEmpty(richText) || index < 0) return '\0';
            int visible = 0; bool tag = false;
            for (int i = 0; i < richText.Length; i++)
            {
                if (richText[i] == '<') { tag = true; continue; }
                if (tag) { if (richText[i] == '>') tag = false; continue; }
                if (visible++ == index) return richText[i];
            }
            return '\0';
        }

        public static string Reveal(string richText, int visibleCharacters)
        {
            if (string.IsNullOrEmpty(richText) || visibleCharacters <= 0) return string.Empty;
            int visible = 0;
            List<string> openTags = new List<string>();
            StringBuilder result = new StringBuilder(richText.Length);
            for (int i = 0; i < richText.Length;)
            {
                char c = richText[i];
                if (c == '<')
                {
                    int close = richText.IndexOf('>', i + 1);
                    if (close >= 0)
                    {
                        string tag = richText.Substring(i, close - i + 1);
                        result.Append(tag);
                        UpdateTagStack(tag, openTags);
                        i = close + 1;
                        continue;
                    }
                }
                if (visible++ >= visibleCharacters) break;
                result.Append(c);
                i++;
            }
            for (int i = openTags.Count - 1; i >= 0; i--) result.Append("</").Append(openTags[i]).Append('>');
            return result.ToString();
        }

        private static void UpdateTagStack(string tag, List<string> openTags)
        {
            if (tag.Length < 3 || tag[1] == '!' || tag[1] == '?') return;
            bool closing = tag[1] == '/';
            string body = tag.Substring(closing ? 2 : 1, tag.Length - (closing ? 3 : 2)).Trim();
            bool selfClosing = body.EndsWith("/", StringComparison.Ordinal);
            if (selfClosing || body.Length == 0) return;
            int space = body.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            string name = (space >= 0 ? body.Substring(0, space) : body).TrimEnd('/');
            if (closing)
            {
                for (int i = openTags.Count - 1; i >= 0; i--)
                {
                    if (!string.Equals(openTags[i], name, StringComparison.OrdinalIgnoreCase)) continue;
                    openTags.RemoveAt(i);
                    break;
                }
            }
            else openTags.Add(name);
        }
    }
}
