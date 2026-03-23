using System.Text;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup
{
    /// <summary>
    /// Overridden inline link parser class to ignore special cases of links we currently don't support to render
    /// inline in text paragraphs.
    /// </summary>
    internal class ChatLinkInlineParser : LinkInlineParser
    {
        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            var c = slice.CurrentChar;
            if (c == '!')
            {
                c = slice.NextChar();
                if (c != '[')
                {
                    return false;
                }
            }

            // Early out and return label (literal) inside brackets if this looks like a footnote
            if (c == '[')
            {
                var saved = slice;

                string label;
                SourceSpan labelSpan;

                if (TryGetFootnoteOrNumbers(ref slice, out label, out labelSpan))
                {
                    var literal = new LiteralInline("[" + label.ToString() + "]")
                    {
                        Span = labelSpan
                    };
                    processor.Inline = literal;

                    return true;
                }
                slice = saved;
            }

            // Use the standard parser for any other case
            return base.Match(processor, ref slice);
        }

        public static bool TryGetFootnoteOrNumbers<T>(ref T lines, out string label, out SourceSpan labelSpan) where T : ICharIterator
        {
            label = null;
            char c = lines.CurrentChar;
            labelSpan = SourceSpan.Empty;
            if (c != '[')
            {
                return false;
            }

            StringBuilder sb = new StringBuilder();

            var startLabel = -1;
            var endLabel = -1;

            while (true)
            {
                c = lines.NextChar();
                if (c == '\0')
                {
                    break;
                }

                if (c == '[')
                {
                    break;
                }

                if (c == ']')
                {
                    lines.SkipChar(); // Skip ]

                    if (lines.PeekChar(0) == ':')
                        return false;

                    // Only valid if buffer is less than 1000 characters
                    if (sb.Length <= 999)
                    {
                        labelSpan.Start = startLabel;
                        labelSpan.End = endLabel;
                        if (labelSpan.Start > labelSpan.End)
                        {
                            labelSpan = SourceSpan.Empty;
                        }
                        goto ReturnValid;
                    }
                    break;
                }

                if ((c < '0' || c > '9')
                    && c != '^')
                {
                    return false;
                }

                if (startLabel < 0)
                {
                    startLabel = lines.Start;
                }
                endLabel = lines.Start;
                sb.Append(c);
            }

            return false;

        ReturnValid:
            label = sb.ToString();
            return true;
        }
    }
}
