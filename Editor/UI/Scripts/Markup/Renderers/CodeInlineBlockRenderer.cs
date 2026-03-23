using Markdig.Renderers;
using Markdig.Syntax.Inlines;
using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class CodeInlineBlockRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, CodeInline>
    {
        protected override void Write(ChatMarkdownRenderer renderer, CodeInline obj)
        {
            //Note: "<noparse>" ensures that quoted code containing tags is not interpreted as actual rich text tags
            renderer.AppendText($"<color={AssistantConstants.InlineCodeTextColor}><b><noparse>{obj.Content}</noparse></b></color>");
        }
    }
}
