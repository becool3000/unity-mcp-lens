using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class PickSessionInteraction : IInteractionSource<SessionProvider.ProfilerSessionInfo>, IUserInteraction
    {
        public string Action => "Select a Capture to Analyze";
        public string Detail { get; }
        public string AllowLabel => "Analyze";
        public string DenyLabel => "Cancel";
        public bool ShowScope => false;

        public List<SessionProvider.ProfilerSessionInfo> Sessions { get; }

        public event Action<SessionProvider.ProfilerSessionInfo> OnCompleted;
        public TaskCompletionSource<SessionProvider.ProfilerSessionInfo> TaskCompletionSource { get; } = new();

        public PickSessionInteraction(List<SessionProvider.ProfilerSessionInfo> profilingSessions)
        {
            Sessions = profilingSessions;
            Detail = profilingSessions.Count > 0 ? profilingSessions[0].FileName : "";
        }

        public void Respond(ToolPermissions.UserAnswer answer)
        {
            if (answer == ToolPermissions.UserAnswer.AllowOnce || answer == ToolPermissions.UserAnswer.AllowAlways)
            {
                var session = Sessions.Count > 0 ? Sessions[0] : null;
                TaskCompletionSource.TrySetResult(session);
                OnCompleted?.Invoke(session);
            }
            else
            {
                CancelInteraction();
            }
        }

        public void CancelInteraction()
        {
            TaskCompletionSource.TrySetCanceled();
        }
    }
}
