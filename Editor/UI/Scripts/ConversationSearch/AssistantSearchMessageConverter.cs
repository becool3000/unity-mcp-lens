using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.ConversationSearch
{
    /// <summary>
    /// Handles conversion of messages for search results to remove tags, etc. that should not be searchable.
    /// </summary>
    class AssistantSearchMessageConverter
    {
        static readonly Regex k_StripRegexTickedBlocks = new(
            @"```([^\n]*)\n([\s\S]*?)```",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex k_StripRegexBackTicks = new(
            "`+",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Regex k_StripRegexBlocks = new(
            ":::.*?:::",
            RegexOptions.Compiled | RegexOptions.Singleline);

        // Matches lines containing code blocks with any language identifier
        static readonly Regex k_CodeBlockRegex = new(
            @"^[ \t]*```([a-zA-Z]*)[^\r\n]*",
            RegexOptions.Compiled | RegexOptions.Multiline);

        static readonly Dictionary<AssistantMessageId, List<Tuple<VisualElement, string>>> k_AdditionalTextForMessages =
            new();

        internal string GetRenderedMessage(string message, AssistantMessageId? messageId = null)
        {
            // Append additional text registered for this message,
            // the position doesn't matter, we just need it to be part of the search string:
            if (messageId.HasValue && k_AdditionalTextForMessages.TryGetValue(messageId.Value, out var text))
            {
                message += string.Join("", text.Select(t => t.Item2).Where(t => !string.IsNullOrEmpty(t)));
            }
            else
            {
                // If there is no additional text registered for the message, but it contains code tags, add the AI tag here:
                var codeMatches = k_StripRegexTickedBlocks.Matches(message);
                if (codeMatches.Count > 0)
                {
                    foreach (Match match in codeMatches)
                    {
                        // Use k_CodeBlockRegex to properly extract the language identifier from the opening line
                        var langMatch = k_CodeBlockRegex.Match(match.Value);
                        var format = langMatch.Success && !string.IsNullOrEmpty(langMatch.Groups[1].Value)
                            ? langMatch.Groups[1].Value
                            : CodeFormat.CSharp;
                        message += AssistantConstants.GetDisclaimerHeader(format);
                    }
                }
            }

            var lastLength = message.Length;
            var noRichText = message;

            // Repeat until no more changes are made, to ensure tags within tags are also removed:
            do
            {
                lastLength = noRichText.Length;

                // Remove rich text tags
                noRichText = MarkupUtil.k_RichTextTagRegex.Replace(noRichText, string.Empty);

                // Replace triple backtick blocks with their inner lines (excluding the first line)
                noRichText = k_StripRegexTickedBlocks.Replace(noRichText, m =>
                {
                    var inner = m.Groups[2].Value;
                    return inner;
                });

                // Remove single backticks
                noRichText = k_StripRegexBackTicks.Replace(noRichText, string.Empty);

                // Remove ::: blocks
                noRichText = k_StripRegexBlocks.Replace(noRichText, string.Empty);
            } while (noRichText.Length != lastLength);

            return noRichText;
        }

        internal void RegisterAdditionalMessageText(
            AssistantMessageId messageId,
            VisualElement element,
            string additionalText)
        {
            if (!k_AdditionalTextForMessages.TryGetValue(messageId, out var additionalTextList))
            {
                additionalTextList = new();
                k_AdditionalTextForMessages[messageId] = additionalTextList;
            }

            for (var i = 0; i < additionalTextList.Count; i++)
                if (additionalTextList[i].Item1 == element)
                    additionalTextList.RemoveAt(i--);

            additionalTextList.Add(new Tuple<VisualElement, string>(element, additionalText));
        }

        internal void UnregisterAdditionalMessageText(
            AssistantMessageId messageId,
            VisualElement element)
        {
            if (!k_AdditionalTextForMessages.TryGetValue(messageId, out var additionalTextList))
                return;

            for (var i = 0; i < additionalTextList.Count; i++)
                if (additionalTextList[i].Item1 == element)
                    additionalTextList.RemoveAt(i--);

            if (additionalTextList.Count == 0)
                k_AdditionalTextForMessages.Remove(messageId);
        }

        public void Clear()
        {
            k_AdditionalTextForMessages.Clear();
        }
    }
}
