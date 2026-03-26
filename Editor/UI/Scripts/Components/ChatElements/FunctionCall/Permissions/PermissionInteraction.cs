using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class PermissionInteraction : IInteractionSource<ToolPermissions.UserAnswer>, IUserInteraction
    {
        public string Action { get; }
        public string Detail { get; }
        public string AllowLabel => null;
        public string DenyLabel => null;
        public bool ShowScope => true;

        public event Action<ToolPermissions.UserAnswer> OnCompleted;
        public TaskCompletionSource<ToolPermissions.UserAnswer> TaskCompletionSource { get; } = new();

        public PermissionInteraction(string action, string detail = null)
        {
            Action = action;
            Detail = detail;
        }

        public void Respond(ToolPermissions.UserAnswer answer) => Complete(answer);

        public void Complete(ToolPermissions.UserAnswer answer)
        {
            TaskCompletionSource.TrySetResult(answer);
            OnCompleted?.Invoke(answer);
        }

        public void CancelInteraction()
        {
            TaskCompletionSource.TrySetCanceled();
        }
    }
}
