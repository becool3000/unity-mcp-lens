using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    abstract class UserInteractionElement<TOutput> : ManagedTemplate, IUserInteraction<TOutput>
    {
        public event Action<TOutput> OnCompleted;

        public TaskCompletionSource<TOutput> TaskCompletionSource { get; } = new();

        protected UserInteractionElement() : base(AssistantUIConstants.UIModulePath) { }

        protected void CompleteInteraction(TOutput output)
        {
            TaskCompletionSource.SetResult(output);
            OnCompleted?.Invoke(output);
        }

        public void CancelInteraction()
        {
            TaskCompletionSource.TrySetCanceled();
            OnCanceled();
        }

        protected virtual void OnCanceled() { }
    }
}
