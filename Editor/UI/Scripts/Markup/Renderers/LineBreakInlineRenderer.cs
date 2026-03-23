using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class LineBreakInlineRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, LineBreakInline>
    {
        protected override void Write(ChatMarkdownRenderer renderer, LineBreakInline obj)
        {
        }
    }
}
