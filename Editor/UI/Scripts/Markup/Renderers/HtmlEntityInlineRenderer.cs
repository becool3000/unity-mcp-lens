using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class HtmlEntityInlineRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, HtmlEntityInline>
    {
        protected override void Write(ChatMarkdownRenderer renderer, HtmlEntityInline obj)
        {
            renderer.AppendText(obj.Original.ToString());
        }
    }
}
