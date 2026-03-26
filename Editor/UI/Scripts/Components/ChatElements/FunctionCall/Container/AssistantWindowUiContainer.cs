using System;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class AssistantWindowUiContainer : IToolUiContainer, IDisposable
    {
        readonly AssistantUIContext m_Context;

        public AssistantWindowUiContainer(AssistantUIContext context)
        {
            m_Context = context;
        }

        public void PushElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IInteractionSource<TOutput> userInteraction)
        {
            if (userInteraction is IUserInteraction interaction)
            {
                var content = new ApprovalInteractionContent();
                content.SetApprovalData(interaction.AllowLabel, interaction.DenyLabel, interaction.Respond, interaction.ShowScope);

                var entry = new UserInteractionEntry
                {
                    Title = interaction.Action,
                    Detail = interaction.Detail,
                    ContentView = content,
                    OnCancel = userInteraction.CancelInteraction
                };

                m_Context.InteractionQueue.Enqueue(entry);
            }
        }

        public void PopElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IInteractionSource<TOutput> userInteraction)
        {
            // No-op: the queue auto-advances when an entry is completed or cancelled.
            // The PopElement call happens after WaitForUser completes, at which point
            // the entry has already been removed from the queue.
        }

        public void Dispose()
        {
            m_Context.InteractionQueue.CancelAll();
        }

        ~AssistantWindowUiContainer()
        {
            Dispose();
        }
    }
}
