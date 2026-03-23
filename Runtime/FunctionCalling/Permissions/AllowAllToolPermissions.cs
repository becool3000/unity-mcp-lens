using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.FunctionCalling
{
    class AllowAllToolPermissions : IToolPermissions
    {
        public void ResetTemporaryPermissions() { }
        public void ResetIgnoredObjects() { }
        public void GetTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> permissions) { }
        public Task CheckToolExecution(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckFileSystemAccess(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, string path, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckUnityObjectAccess(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, Type type, Object target, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void IgnoreUnityObject(ToolExecutionContext.CallInfo callInfo, Object target) { }
        public Task CheckCodeExecution(ToolExecutionContext.CallInfo callInfo, string code = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckScreenCapture(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckPlayMode(ToolExecutionContext.CallInfo callInfo, IToolPermissions.PlayModeOperation operation, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CheckAssetGeneration(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
