using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;

namespace Unity.AI.Assistant.UI.Editor.Scripts.ConversationSearch
{
    class ConversationSearcher
    {
        readonly AssistantUIContext m_Context;

        readonly AssistantSearchMessageConverter m_MessageConverter;

        internal readonly List<ConversationSearchResult> SearchResults = new();
        string m_SearchString;

        public string SearchString
        {
            get => m_SearchString;
            set
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = string.Empty;
                }

                m_SearchString = value;
            }
        }

        internal int TotalResultCount => SearchResults.Sum(r => r.MatchCount);

        internal ConversationSearcher(AssistantSearchMessageConverter messageConverter, AssistantUIContext context)
        {
            m_MessageConverter = messageConverter;
            m_Context = context;
        }

        internal bool SearchActiveConversation()
        {
            SearchResults.Clear();

            var conversation = m_Context.Blackboard.ActiveConversation;

            if (conversation == null || string.IsNullOrEmpty(SearchString))
            {
                return false;
            }

            for (var i = 0; i < conversation.Messages.Count; i++)
            {
                var msg = conversation.Messages[i];

                if (msg.Role != MessageModelRole.User && msg.Role != MessageModelRole.Assistant)
                    continue;

                var content = string.Empty;
                foreach (var block in msg.Blocks)
                {
                    switch (block)
                    {
                        case AnswerBlockModel responseBlock:
                            content += responseBlock.Content;
                            break;
                        case PromptBlockModel promptBlock:
                            content += promptBlock.Content;
                            break;
                        case ThoughtBlockModel thoughtBlock:
                            content += thoughtBlock.Content;
                            break;
                        case FunctionCallBlockModel functionCallBlock:
                            functionCallBlock.Call.GetCodeEditParameters(out _, out var code, out _);
                            
                            if (code != null)
                                content += code;
                            break;
                    }
                }

                var renderedMessageContent = m_MessageConverter.GetRenderedMessage(content, msg.Id);

                if (renderedMessageContent.Contains(SearchString, StringComparison.OrdinalIgnoreCase))
                {
                    SearchResults.Add(new ConversationSearchResult(
                        msg.Id,
                        renderedMessageContent,
                        SearchString,
                        i));
                }
            }

            return true;
        }

        public void Clear()
        {
            SearchResults.Clear();
        }
    }
}
