using System;
using System.Text;

namespace Lumenfall.Accessibility
{
    public static class AccessibleDialogueText
    {
        /// <summary>Authored dark rich-text colors must not override the high-contrast foreground.</summary>
        public static string RemoveColorTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
            StringBuilder result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length;)
            {
                if (text[i] == '<')
                {
                    int end = text.IndexOf('>', i + 1);
                    if (end >= 0)
                    {
                        string tag = text.Substring(i + 1, end - i - 1).Trim();
                        if (tag.Equals("/color", StringComparison.OrdinalIgnoreCase) ||
                            tag.Equals("color", StringComparison.OrdinalIgnoreCase) ||
                            tag.StartsWith("color=", StringComparison.OrdinalIgnoreCase) ||
                            tag.StartsWith("color ", StringComparison.OrdinalIgnoreCase))
                        {
                            i = end + 1;
                            continue;
                        }
                    }
                }
                result.Append(text[i++]);
            }
            return result.ToString();
        }
    }
}
