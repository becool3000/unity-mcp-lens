using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Editor.Api
{
    static partial class AssistantApi
    {
        /// <summary>
        /// Run an agent headless, without the Assistant UI.
        /// </summary>
        /// <param name="agent">The agent to execute for processing the query. If no agent is passed, will use the default behavior.</param>
        /// <param name="userPrompt">The user's input prompt or question.</param>
        /// <param name="attachedContext">Additional context information to include with the query.</param>
        /// <param name="resumeConversationId">Optional. When specified, continues an existing conversation rather than creating a new one.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <param name="onMessageUpdated">An optional callback to receive each message update</param>
        public static async Task<Output> Run(this IAgent agent, string userPrompt, AttachedContext attachedContext = null, string resumeConversationId = null, CancellationToken cancellationToken = default, Action<AssistantMessage> onMessageUpdated = null)
        {
            if (agent == null)
                throw new ArgumentNullException(nameof(agent));

            return await Run(userPrompt, attachedContext, agent, null, resumeConversationId, cancellationToken, onMessageUpdated);
        }
    }
}
