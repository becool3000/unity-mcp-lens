using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Markdig.Renderers;
using Markdig.Syntax;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers
{
    internal class FencedCodeBlockRenderer : MarkdownObjectRenderer<ChatMarkdownRenderer, FencedCodeBlock>
    {
        readonly AssistantUIContext m_Context;

        static readonly Regex k_FilenameRegEx = new Regex(@"filename=""([^""]+)""", RegexOptions.Compiled);

        public FencedCodeBlockRenderer(AssistantUIContext context)
        {
            m_Context = context;
        }

        internal static string ExtractFilename(string input)
        {
            if (input == null)
                return null;

            Match match = k_FilenameRegEx.Match(input);

            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return null;
        }

        internal static bool IsCSharpLanguage(string info)
        {
            return info != null &&
                   (info.Equals(AssistantConstants.CodeBlockCsharpFiletype, StringComparison.OrdinalIgnoreCase) ||
                    info.StartsWith("csharp", StringComparison.OrdinalIgnoreCase));
        }

        internal struct FilenameAndTitleResult
        {
            public string Filename;
            public string CustomTitle; // null if no custom title should be set
        }

        FencedContent GetFencedContent(FencedCodeBlock block)
        {
            var fullCodeBlock = new StringBuilder();

            for (int i = 0; i < block.Lines.Count; i++)
            {
                var line = block.Lines.Lines[i].ToString();

                fullCodeBlock.Append(line);
                if (i < block.Lines.Count - 1)
                    fullCodeBlock.Append("\n");
            }

            return new FencedContent
            {
                Info = block.Info ?? string.Empty,
                Content = fullCodeBlock.ToString(),
                Arguments = block.Arguments ?? string.Empty
            };
        }

        internal FilenameAndTitleResult DetermineFilenameAndTitle(FencedCodeBlock obj, FencedContent content)
        {
            var result = new FilenameAndTitleResult();
            var isCSharpLanguage = IsCSharpLanguage(obj.Info);
            var isCodeRoute = obj.Info != null && obj.Info.Contains(AssistantConstants.CodeBlockCsharpValidateFiletype);

            var extractedClassName = CodeBlockUtils.ExtractClassName(content.Content);
            var defaultName = extractedClassName ?? AssistantConstants.DefaultCodeBlockCsharpFilename;
            var defaultExtension = AssistantConstants.DefaultCodeBlockCsharpExtension;

            // Filenames or non-csharp files are only expected on the /ask route; /run will override the title anyway
            if (!isCodeRoute)
            {
                var filenameAndExtension = ExtractFilename(obj.Arguments);
                if (filenameAndExtension != null)
                {
                    result.Filename = filenameAndExtension;
                    result.CustomTitle = filenameAndExtension;
                }
                else
                {
                    if (!string.IsNullOrEmpty(obj.Info))
                    {
                        if (!isCSharpLanguage)
                        {
                            if (CodeBlockUtils.IsShaderType(obj.Info))
                            {
                                defaultName = AssistantConstants.DefaultCodeBlockShaderFilename;
                                defaultExtension = AssistantConstants.DefaultCodeBlockShaderExtension;
                            }
                            else
                            {
                                defaultName = AssistantConstants.DefaultCodeBlockTextFilename;
                                defaultExtension = obj.Info.Length > 0
                                    ? obj.Info.ToLowerInvariant()
                                    : AssistantConstants.DefaultCodeBlockTextExtension;
                            }
                        }
                    }
                    else
                    {
                        defaultName = AssistantConstants.DefaultCodeBlockTextFilename;
                        defaultExtension = AssistantConstants.DefaultCodeBlockTextExtension;
                    }

                    result.Filename = $"{defaultName}.{defaultExtension}";

                    // Show the filename, if we have a specific name, otherwise keep the default title
                    if (defaultName != AssistantConstants.DefaultCodeBlockCsharpFilename &&
                        defaultName != AssistantConstants.DefaultCodeBlockTextFilename &&
                        defaultName != AssistantConstants.DefaultCodeBlockShaderFilename)
                    {
                        result.CustomTitle = $"{defaultName}.{defaultExtension}";
                    }
                }
            }
            else
            {
                result.Filename = $"{defaultName}.{defaultExtension}";

                // Show the filename, if we have an extracted class name, otherwise keep the default title
                if (defaultName != AssistantConstants.DefaultCodeBlockCsharpFilename)
                {
                    result.CustomTitle = $"{defaultName}.{defaultExtension}";
                }
            }

            return result;
        }

        protected override void Write(ChatMarkdownRenderer renderer, FencedCodeBlock obj)
        {
            var codeText = GetFencedContent(obj);
            var extraCodeText = new List<FencedContent>();
            bool hasUxmlSibling = false;

            foreach (Block block in obj.Parent)
            {
                if (block is FencedCodeBlock otherFenced && otherFenced != obj)
                {
                    extraCodeText.Add(GetFencedContent(otherFenced));
                    if (otherFenced.Info == "uxml")
                    {
                        hasUxmlSibling = true;
                    }
                }
            }

            // Skip USS blocks that have a UXML sibling - they will be handled by ChatElementUI
            if (obj.Info == "uss" && hasUxmlSibling)
                return;

            CommandDisplayTemplate displayBlock;

            // Finish all pending non-code text/formatting
            renderer.CloseTextElement();

            displayBlock = new ChatElementCommandCodeBlock();

            // If this is a shared element, we skip doing the additional setup
            if (!renderer.m_OutputTextElements.Contains(displayBlock))
            {
                var isCSharpLanguage = IsCSharpLanguage(obj.Info);

                var isCodeRoute = obj.Info.Contains(AssistantConstants.CodeBlockCsharpValidateFiletype);

                displayBlock.Fence = obj.Info;
                displayBlock.Initialize(m_Context);
                displayBlock.SetContent(codeText);
                displayBlock.SetCodeType(obj.Info);
                renderer.m_OutputTextElements.Add(displayBlock);

                if (extraCodeText.Count > 0)
                    displayBlock.SetExtraContent(extraCodeText);

                if (!isCSharpLanguage)
                {
                    if (obj.Info != null)
                    {
                        displayBlock.SetCustomTitle(obj.Info.ToUpper());
                    }

                    // Don't treat non-C# code as needing reformatting or code adjustments
                    displayBlock.SetCodeReformatting(false);
                }

                // Create the element if it is opened and complete
                if (!obj.IsOpen)
                {
                    displayBlock.Validate(0);
                    displayBlock.Display();
                }

                var filenameAndTitle = DetermineFilenameAndTitle(obj, codeText);
                displayBlock.SetFilename(filenameAndTitle.Filename);
                if (filenameAndTitle.CustomTitle != null)
                {
                    displayBlock.SetCustomTitle(filenameAndTitle.CustomTitle);
                }
            }
            else
            {
                displayBlock.AddContent(codeText.Content);
            }
        }
    }
}
