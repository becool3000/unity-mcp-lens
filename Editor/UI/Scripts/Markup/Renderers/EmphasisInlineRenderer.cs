using Markdig.Renderers;
using Markdig.Syntax.Inlines;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class EmphasisInlineRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, EmphasisInline>
    {
        protected override void Write(ChatMarkdownRenderer renderer, EmphasisInline obj)
        {
            renderer.AppendText($"<i>");
            renderer.WriteChildren(obj);
            renderer.AppendText($"</i>");
        }
    }
}
