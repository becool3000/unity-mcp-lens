using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.UI.Editor.Scripts.ConversationSearch
{
    class ConversationSearchResult
    {
        public readonly AssistantMessageId MessageId;
        public readonly int MatchCount;
        public readonly int MessageIndex;

        public ConversationSearchResult(
            AssistantMessageId messageId,
            string renderedMessage,
            string searchString,
            int messageIndex)
        {
            MessageId = messageId;
            MessageIndex = messageIndex;

            MatchCount = Matches(renderedMessage, searchString).Count();
        }

        internal IEnumerable<Tuple<int, int>> Matches(string renderedMessage, string searchString)
        {
            var startIndex = 0;
            while (true)
            {
                var foundIndex =
                    renderedMessage.IndexOf(searchString, startIndex, StringComparison.OrdinalIgnoreCase);

                if (foundIndex == -1)
                    break;

                yield return new Tuple<int, int>(foundIndex, searchString.Length);

                startIndex = foundIndex + 1;
            }
        }
    }
}
