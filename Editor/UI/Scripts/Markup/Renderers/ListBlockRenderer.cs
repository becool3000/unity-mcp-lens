using Markdig.Renderers;
using Markdig.Syntax;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class ListBlockRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, ListBlock>
    {
        protected override void Write(ChatMarkdownRenderer renderer, ListBlock obj)
        {
            renderer.PushListBlock(obj);

            renderer.WriteChildren(obj);

            renderer.PopListBlock();
        }
    }
}
