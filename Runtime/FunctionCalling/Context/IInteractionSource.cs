using System;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    interface IInteractionSource<TOutput>
    {
        public event Action<TOutput> OnCompleted;

        TaskCompletionSource<TOutput> TaskCompletionSource { get; }

        public void CancelInteraction();
    }
}
