using System.Collections.Generic;
using System.Text;

namespace Lumenfall.DataDriven
{
    /// <summary>
    /// Splits a dialogue line into short pages so the box never shows a wall of text. Pages break after a sentence
    /// (a visible '.', '!' or '?' followed by a space); a sentence longer than a page breaks between words. Tags are
    /// never split, and any tag still open at a page break is closed on that page and reopened on the next, so each
    /// page is valid rich text on its own.
    /// </summary>
    public static class DialoguePaging
    {
        public static List<string> Split(string text, int maxVisibleCharacters)
        {
            var pages = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                pages.Add(text ?? string.Empty);
                return pages;
            }

            List<Piece> pieces = Pieces(text, maxVisibleCharacters);
            var open = new List<string>();
            var page = new StringBuilder();
            int pageVisible = 0;
            foreach (Piece piece in pieces)
            {
                if (pageVisible > 0 && pageVisible + piece.Visible > maxVisibleCharacters)
                {
                    pages.Add(Close(page, open));
                    page.Clear();
                    pageVisible = 0;
                    foreach (string tag in open) page.Append(tag);
                }

                AppendTracked(page, piece.Raw, open, pageVisible == 0);
                pageVisible += piece.Visible;
            }

            if (pageVisible > 0) pages.Add(Close(page, open));
            return pages;
        }

        private readonly struct Piece
        {
            public readonly string Raw;
            public readonly int Visible;
            public Piece(string raw, int visible) { Raw = raw; Visible = visible; }
        }

        /// <summary>Sentences (with their trailing space), each long sentence further cut into word groups.</summary>
        private static List<Piece> Pieces(string text, int max)
        {
            var sentences = new List<Piece>();
            var current = new StringBuilder();
            int visible = 0;
            for (int i = 0; i < text.Length;)
            {
                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        current.Append(text, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }

                char c = text[i];
                current.Append(c);
                visible++;
                i++;
                if ((c == '.' || c == '!' || c == '?') && NextVisibleIsSpaceOrEnd(text, i))
                {
                    // Keep closing tags and the following spaces with the sentence that ends here.
                    while (i < text.Length && (text[i] == ' ' || (text[i] == '<' && i + 1 < text.Length && text[i + 1] == '/')))
                    {
                        if (text[i] == ' ') { current.Append(' '); visible++; i++; continue; }
                        int close = text.IndexOf('>', i + 1);
                        if (close < 0) break;
                        current.Append(text, i, close - i + 1);
                        i = close + 1;
                    }

                    sentences.Add(new Piece(current.ToString(), visible));
                    current.Clear();
                    visible = 0;
                }
            }

            if (current.Length > 0) sentences.Add(new Piece(current.ToString(), visible));

            var pieces = new List<Piece>();
            foreach (Piece sentence in sentences)
            {
                if (sentence.Visible <= max) pieces.Add(sentence);
                else pieces.AddRange(Words(sentence.Raw));
            }

            return pieces;
        }

        /// <summary>Breaks text after each space that is outside a tag.</summary>
        private static IEnumerable<Piece> Words(string raw)
        {
            var current = new StringBuilder();
            int visible = 0;
            for (int i = 0; i < raw.Length;)
            {
                if (raw[i] == '<')
                {
                    int close = raw.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        current.Append(raw, i, close - i + 1);
                        i = close + 1;
                        continue;
                    }
                }

                current.Append(raw[i]);
                visible++;
                if (raw[i++] == ' ')
                {
                    yield return new Piece(current.ToString(), visible);
                    current.Clear();
                    visible = 0;
                }
            }

            if (current.Length > 0) yield return new Piece(current.ToString(), visible);
        }

        private static bool NextVisibleIsSpaceOrEnd(string text, int index)
        {
            for (int i = index; i < text.Length;)
            {
                if (text[i] == '<')
                {
                    int close = text.IndexOf('>', i + 1);
                    if (close > i) { i = close + 1; continue; }
                }

                return char.IsWhiteSpace(text[i]) || text[i] == '"' || text[i] == '\'' || text[i] == ')';
            }

            return true;
        }

        /// <summary>Appends raw text while tracking which tags are open; leading spaces on a fresh page are dropped.</summary>
        private static void AppendTracked(StringBuilder page, string raw, List<string> open, bool trimStart)
        {
            int i = 0;
            if (trimStart)
            {
                while (i < raw.Length && raw[i] == ' ') i++;
            }

            for (; i < raw.Length;)
            {
                if (raw[i] == '<')
                {
                    int close = raw.IndexOf('>', i + 1);
                    if (close > i)
                    {
                        string tag = raw.Substring(i, close - i + 1);
                        Track(tag, open);
                        page.Append(tag);
                        i = close + 1;
                        continue;
                    }
                }

                page.Append(raw[i++]);
            }
        }

        private static void Track(string tag, List<string> open)
        {
            if (tag.Length < 3 || tag.EndsWith("/>")) return;
            if (tag[1] == '/')
            {
                string name = TagName(tag.Substring(2, tag.Length - 3));
                for (int i = open.Count - 1; i >= 0; i--)
                {
                    if (TagName(open[i].Substring(1, open[i].Length - 2)) != name) continue;
                    open.RemoveAt(i);
                    break;
                }
            }
            else
            {
                open.Add(tag);
            }
        }

        private static string TagName(string body)
        {
            int end = body.IndexOfAny(new[] { ' ', '=' });
            return (end >= 0 ? body.Substring(0, end) : body).Trim().ToLowerInvariant();
        }

        /// <summary>Finishes a page: trailing spaces removed, then any open tags closed in reverse order.</summary>
        private static string Close(StringBuilder page, List<string> open)
        {
            string result = page.ToString().TrimEnd(' ');
            var closing = new StringBuilder(result);
            for (int i = open.Count - 1; i >= 0; i--)
                closing.Append("</").Append(TagName(open[i].Substring(1, open[i].Length - 2))).Append('>');
            return closing.ToString();
        }
    }
}
