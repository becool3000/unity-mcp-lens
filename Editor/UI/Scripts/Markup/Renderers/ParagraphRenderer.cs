using Markdig.Renderers;
using Markdig.Syntax;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class ParagraphRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, ParagraphBlock>
    {
        readonly AssistantUIContext m_Context;

        public ParagraphRenderer(AssistantUIContext context)
        {
            m_Context = context;
        }

        protected override void Write(ChatMarkdownRenderer renderer, ParagraphBlock obj)
        {
            renderer.m_VisitingState = ChatMarkdownRenderer.VisitingState.Text;
            if (obj.Inline != null)
            {
                renderer.WriteChildren(obj.Inline);

                // NEW: line break
                if (renderer.m_ParagraphAddNewLine)
                {
                    // End a paragraph with a newline, unless we are inside a list or quote block
                    if (!renderer.IsListBlock && !renderer.IsQuoteBlock)
                        renderer.AppendText("\n");

                    renderer.AppendText("\n");
                }
            }

            string text = renderer.ClearText();
            // case with multiple contiguous source blocks - because of the newline in the response text, it looks like multiple markdown paragraphs when it should be one
            if (text.StartsWith(" <sprite") && renderer.m_PreviousLastElement != null)
            {
                var textElement = renderer.m_PreviousLastElement as TextElement;

                if (textElement != null)
                    textElement.text += text;
                else
                    Debug.LogError($"Expected a Text element, not {renderer.m_PreviousLastElement.GetType()}. Cannot append text to the previous element.");
            }
            else
            {
                var textElement = renderer.GetCurrentTextElement();
                textElement.text += text;

                m_Context.SearchHelper?.RegisterSearchableTextElement(textElement);
            }
        }
    }
}
