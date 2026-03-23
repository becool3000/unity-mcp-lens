using System.Collections.Generic;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Parsers;
using Markdig.Parsers.Inlines;
using Unity.AI.Assistant.Backend;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup
{
    internal class MarkdownAPI
    {
        private static readonly MarkdownPipeline k_Pipeline;

        static MarkdownAPI()
        {
            var pipelineBuilder = new MarkdownPipelineBuilder();

            pipelineBuilder.BlockParsers.TryRemove<HtmlBlockParser>();

            pipelineBuilder.InlineParsers.TryRemove<EscapeInlineParser>();
            pipelineBuilder.InlineParsers.TryRemove<LinkInlineParser>();
            pipelineBuilder.InlineParsers.TryRemove<AutolinkInlineParser>();
            pipelineBuilder.InlineParsers.AddIfNotAlready<ChatLinkInlineParser>();

            // Tables support
            if (!pipelineBuilder.BlockParsers.Contains<GridTableParser>())
                pipelineBuilder.BlockParsers.Insert(0, new GridTableParser());
            if (!pipelineBuilder.BlockParsers.Contains<PipeTableBlockParser>())
                pipelineBuilder.BlockParsers.Insert(0, new PipeTableBlockParser());

            var lineBreakParser = pipelineBuilder.InlineParsers.FindExact<LineBreakInlineParser>();
            pipelineBuilder.InlineParsers.InsertBefore<EmphasisInlineParser>(new PipeTableParser(lineBreakParser!, null));

            pipelineBuilder.UseCustomContainers();

            k_Pipeline = pipelineBuilder.Build();
        }

        internal static void MarkupText(AssistantUIContext context, string text, IList<SourceBlock> sourceBlocks, IList<VisualElement> newTextElements, VisualElement previousLastElement)
        {
            var ourRenderer = new ChatMarkdownRenderer(context, sourceBlocks, newTextElements, previousLastElement);
            k_Pipeline.Setup(ourRenderer);

            Markdown.Convert(text, ourRenderer, k_Pipeline);
        }
    }
}
