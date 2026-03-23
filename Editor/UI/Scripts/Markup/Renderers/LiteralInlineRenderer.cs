using System;
using System.Text;
using Markdig.Renderers;
using Markdig.Syntax.Inlines;
using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class LiteralInlineRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, LiteralInline>
    {
        private StringBuilder m_CurrentSourceJson = new();
        protected override void Write(ChatMarkdownRenderer renderer, LiteralInline obj)
        {
            switch (renderer.m_VisitingState)
            {
                case ChatMarkdownRenderer.VisitingState.Code:
                    renderer.AppendText(obj.Content.ToString());
                    break;
                case ChatMarkdownRenderer.VisitingState.Text:
                    if (obj.Content.Match("--boundary-"))
                    {
                        renderer.m_VisitingState = ChatMarkdownRenderer.VisitingState.SourceContent;
                        return;
                    }

                    var text = $"<line-height={AssistantConstants.ChatElementLineHeight}>" + obj.Content.ToString();
                    text = MarkupUtil.QuoteCarriageReturn(text);

                    renderer.AppendText(text);
                    break;
                case ChatMarkdownRenderer.VisitingState.SourceContent:
                    m_CurrentSourceJson.Append(obj.Content.ToString());
                    renderer.m_VisitingState = ChatMarkdownRenderer.VisitingState.SourceBoundaryEnd;
                    break;
                case ChatMarkdownRenderer.VisitingState.SourceBoundaryEnd:
                    // source is not complete yet
                    if (!obj.Content.Match("boundary-"))
                        return;

                    var json = m_CurrentSourceJson.ToString();
                    m_CurrentSourceJson.Clear();
                    renderer.AddSource(json);
                    renderer.m_VisitingState = ChatMarkdownRenderer.VisitingState.Text;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
