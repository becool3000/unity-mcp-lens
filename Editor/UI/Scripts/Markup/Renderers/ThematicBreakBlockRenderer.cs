using Markdig.Renderers;
using Markdig.Syntax;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class ThematicBreakRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, ThematicBreakBlock>
    {
        protected override void Write(ChatMarkdownRenderer renderer, ThematicBreakBlock obj)
        {
            renderer.InsertSeparatorLine();
        }
    }
}
