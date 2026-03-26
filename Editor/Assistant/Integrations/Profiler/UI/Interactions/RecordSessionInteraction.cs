using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class RecordSessionInteraction : IInteractionSource<SessionProvider.ProfilerSessionInfo>, IUserInteraction
    {
        public string Action => "Open Profiler";
        public string Detail => "No existing profiler captures. Record a new capture and prompt again.";
        public string AllowLabel => "Open Profiler";
        public string DenyLabel => "Cancel";
        public bool ShowScope => false;

        public event Action<SessionProvider.ProfilerSessionInfo> OnCompleted;
        public TaskCompletionSource<SessionProvider.ProfilerSessionInfo> TaskCompletionSource { get; } = new();

        public void Respond(ToolPermissions.UserAnswer answer)
        {
            if (answer == ToolPermissions.UserAnswer.AllowOnce || answer == ToolPermissions.UserAnswer.AllowAlways)
            {
                var profilerWindow = EditorWindow.GetWindow<ProfilerWindow>();
                profilerWindow.Show();
            }

            TaskCompletionSource.TrySetResult(null);
            OnCompleted?.Invoke(null);
        }

        public void CancelInteraction()
        {
            TaskCompletionSource.TrySetCanceled();
        }
    }
}
