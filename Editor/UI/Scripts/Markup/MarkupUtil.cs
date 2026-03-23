using System.Text.RegularExpressions;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup
{
    /// <summary>
    /// Utility class to help with reformatting text appearing in the chat.
    /// </summary>
    static class MarkupUtil
    {
        static readonly Regex s_CarriageReturnPattern = new Regex("(?<!\\\\)(\\\\r|\\\\n)");

        internal static readonly string[] k_ValidRichTextTags = new[]
        {
            "a", "align", "allcaps", "alpha", "b", "br", "color", "cspace", "font",
            "font-weight", "gradient", "i", "indent", "line-height", "line-indent",
            "lowercase", "margin", "mark", "mspace", "nobr", "noparse", "pos",
            "rotate", "s", "size", "smallcaps", "space", "sprite", "style", "sub",
            "sup", "u", "uppercase", "voffset", "width", "link"
        };

        internal static readonly Regex k_RichTextTagRegex = new Regex(
            $@"<(/?({string.Join("|", k_ValidRichTextTags)})(=([""']?.+?[""']?))?(\s[^>]*)?)>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        public static string QuoteCarriageReturn(string text)
        {
            return s_CarriageReturnPattern.Replace(text, "<noparse>\\$1</noparse>");
        }

        public static string QuoteRichTextTags(string text)
        {
            // Disable RichText tags by wrapping only the '<' and '>' signs with <noparse>
            return k_RichTextTagRegex.Replace(text, match =>
            {
                string tagContent = match.Value.Substring(1, match.Value.Length - 2);
                return $"<noparse><</noparse>{tagContent}<noparse>></noparse>";
            });
        }
    }
}
