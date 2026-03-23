using Markdig.Renderers;
using Markdig.Syntax.Inlines;
using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class LinkInlineRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, LinkInline>
    {
        const string k_CustomLinkColor = "#4D9BF9";

        protected override void Write(ChatMarkdownRenderer renderer, LinkInline obj)
        {
            if (LinkHandlerRegistry.CanHandle(obj.Url))
            {
                renderer.AppendText($"<color={k_CustomLinkColor}><link=\"{obj.Url}\">");
                renderer.WriteChildren(obj);
                renderer.AppendText("</link></color>");
            }
            else
            {
                renderer.AppendText($"<a href=\"{obj.Url}\">");
                renderer.WriteChildren(obj);
                renderer.AppendText("</a>");
            }
        }
    }
}
