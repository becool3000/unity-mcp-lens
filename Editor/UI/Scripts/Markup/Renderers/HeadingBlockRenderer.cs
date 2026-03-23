using System;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class HeadingBlockRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, HeadingBlock>
    {
        protected override void Write(ChatMarkdownRenderer renderer, HeadingBlock obj)
        {
            // TODO: Figure out the size of the heading text to align with our (.uss) styling
            int level = Math.Clamp(obj.Level, 1, 6);

            renderer.CloseTextElement();

            var textElement = renderer.GetCurrentTextElement();

            textElement.name = "responseHeading";

            textElement.AddToClassList("mui-chat-response-heading");
            textElement.AddToClassList($"mui-chat-response-heading-size-{level}");

            if (obj.Inline != null)
                renderer.Write(obj.Inline);

            renderer.CloseTextElement();
        }
    }
}
