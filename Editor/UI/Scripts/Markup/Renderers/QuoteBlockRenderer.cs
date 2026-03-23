using Markdig.Renderers;
using Markdig.Syntax;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class QuoteBlockRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, QuoteBlock>
    {
        // TODO: Needs design for quote block rendering with UI Elements; possible support for nested quote blocks
        protected override void Write(ChatMarkdownRenderer renderer, QuoteBlock obj)
        {
            renderer.PushQuoteBlock(obj);

            // Write quote lines
            for (int i = 0; i < obj.Count; i++)
            {
                renderer.Write(obj[i]);
            }

            renderer.AppendText("\n");

            renderer.PopQuoteBlock();
        }
    }
}
