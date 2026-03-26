using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.FunctionCalling
{
    class ToolInteractionAndPermissionBridge : IToolInteractions, IToolPermissions
    {
        public IToolPermissions ToolPermissions { get; }
        public IToolInteractions ToolInteractions { get; }

        public ToolInteractionAndPermissionBridge(IToolPermissions permissions, IToolInteractions interactions)
        {
            ToolPermissions = permissions;
            ToolInteractions = interactions;
        }

        public Task<TOutput> WaitForUser<TOutput>(ToolExecutionContext.CallInfo callInfo, IInteractionSource<TOutput> userInteraction, int timeoutSeconds = 600, CancellationToken cancellationToken = default) => ToolInteractions.WaitForUser(callInfo, userInteraction, timeoutSeconds, cancellationToken);

        public void ResetTemporaryPermissions() => ToolPermissions.ResetTemporaryPermissions();

        public void ResetIgnoredObjects() => ToolPermissions.ResetIgnoredObjects();

        public void GetTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> permissions) => ToolPermissions.GetTemporaryPermissions(permissions);

        public Task CheckToolExecution(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => ToolPermissions.CheckToolExecution(callInfo, cancellationToken);

        public Task CheckFileSystemAccess(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, string path, CancellationToken cancellationToken = default) => ToolPermissions.CheckFileSystemAccess(callInfo, operation, path, cancellationToken);

        public Task CheckUnityObjectAccess(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, Type type, Object target, CancellationToken cancellationToken = default) => ToolPermissions.CheckUnityObjectAccess(callInfo, operation, type, target, cancellationToken);

        public void IgnoreUnityObject(ToolExecutionContext.CallInfo callInfo, Object target) => ToolPermissions.IgnoreUnityObject(callInfo, target);

        public Task CheckCodeExecution(ToolExecutionContext.CallInfo callInfo, string code, CancellationToken cancellationToken = default) => ToolPermissions.CheckCodeExecution(callInfo, code, cancellationToken);

        public Task CheckScreenCapture(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => ToolPermissions.CheckScreenCapture(callInfo, cancellationToken);

        public Task CheckPlayMode(ToolExecutionContext.CallInfo callInfo, IToolPermissions.PlayModeOperation operation, CancellationToken cancellationToken = default) => ToolPermissions.CheckPlayMode(callInfo, operation, cancellationToken);

        public Task CheckAssetGeneration(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost, CancellationToken cancellationToken = default) => ToolPermissions.CheckAssetGeneration(callInfo, path, type, cost, cancellationToken);
    }
}
