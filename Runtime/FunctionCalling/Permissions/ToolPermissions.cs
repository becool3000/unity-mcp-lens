using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    abstract partial class ToolPermissions : IToolPermissions
    {
        public enum UserAnswer
        {
            AllowOnce,
            AllowAlways,
            DenyOnce
        }

        public enum PermissionType
        {
            ScreenCapture,
            FileSystem,
            UnityObject,
            ToolExecution,
            AssetGeneration,
            CodeExecution
        }

        enum PermissionStatus
        {
            Approved,
            Denied,
            Pending
        }

        IToolUiContainer ToolUiContainer { get; }
        IPermissionsPolicyProvider PolicyProvider { get; }
        PermissionsState State { get; set; } = new();

        internal ToolPermissions(IToolUiContainer toolUiContainer, IPermissionsPolicyProvider policyProvider)
        {
            ToolUiContainer = toolUiContainer;
            PolicyProvider = policyProvider;

            TryLoadState();

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += SaveState;
#endif
        }

        async Task<UserAnswer> WaitForUser(ToolExecutionContext.CallInfo callInfo, IUserInteraction<UserAnswer> userInteraction, CancellationToken cancellationToken, float timeoutSeconds = 600f)
        {
            ToolUiContainer.PushElement(callInfo, userInteraction);

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var userTask = userInteraction.TaskCompletionSource.Task;

            // Wait for user interaction, timeout or cancellation
            var completedTask = await Task.WhenAny(userTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

            // User interaction completed
            if (completedTask == userTask)
            {
                linkedCts.Cancel(); // cancel timeout / cancellation task
                var answer = await userTask;
                return answer;
            }

            // If timeout or cancellation, cancel user task
            userInteraction.CancelInteraction();

            // Cancellation
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException("User interaction was cancelled.");

            // Timeout
            throw new TimeoutException($"User interaction timed out after {timeoutSeconds} seconds.");
        }

        protected virtual void OnPermissionResponse(
            ToolExecutionContext.CallInfo callInfo,
            UserAnswer answer,
            PermissionType permissionType) { }
    }
}
