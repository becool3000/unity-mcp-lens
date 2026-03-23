using Markdig.Renderers;
using Markdig.Syntax;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class ListItemBlockRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, ListItemBlock>
    {
        protected override void Write(ChatMarkdownRenderer renderer, ListItemBlock obj)
        {
            var isFirstItem = obj.Parent?.Count > 0 && obj.Parent[0] == obj;

            if (!isFirstItem)
            {
                renderer.PushListElement(obj);
            }

            var bullet = renderer.GetCurrentListBullet(obj);
            renderer.GetCurrentListElement().SetBulletSymbol(bullet);

            renderer.WriteChildren(obj);

            if (!isFirstItem)
                renderer.PopListElement();
        }
    }
}
