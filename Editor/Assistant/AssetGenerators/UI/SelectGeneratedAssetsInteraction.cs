using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    class SelectGeneratedAssetsInteraction : VisualElement, IInteractionSource<SelectAssetsOutput>
    {
        public event Action<SelectAssetsOutput> OnCompleted;
        public TaskCompletionSource<SelectAssetsOutput> TaskCompletionSource { get; } = new();

        public SelectGeneratedAssetsInteraction() => style.display = DisplayStyle.None;

        public void CompleteInteraction(SelectAssetsOutput output)
        {
            TaskCompletionSource.TrySetResult(output);
            OnCompleted?.Invoke(output);
        }

        public void CancelInteraction() => TaskCompletionSource.TrySetCanceled();
    }
}
