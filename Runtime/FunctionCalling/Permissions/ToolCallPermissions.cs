using System;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.FunctionCalling
{
    readonly struct ToolCallPermissions
    {
        ToolExecutionContext.CallInfo Call { get; }
        IToolPermissions Permissions { get; }
        CancellationToken CancellationToken { get; }

        internal ToolCallPermissions(ToolExecutionContext.CallInfo callInfo, IToolPermissions permissions, CancellationToken cancellationToken)
        {
            Call = callInfo;
            Permissions = permissions;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Checks if the tool can be executed
        /// </summary>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        internal Task CheckCanExecute() => Permissions.CheckToolExecution(Call, CancellationToken);

        /// <summary>
        /// Checks if the tool has file system permissions
        /// </summary>
        /// <param name="operation">The type of file system operation</param>
        /// <param name="path">A path to check permissions for.</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        public Task CheckFileSystemAccess(IToolPermissions.ItemOperation operation, string path)
            => Permissions.CheckFileSystemAccess(Call, operation, path, CancellationToken);

        /// <summary>
        /// Checks if the tool has UnityEngine.Object permissions
        /// </summary>
        /// <param name="operation">The operation to perform</param>
        /// <param name="type">The type of the object impacted by the operation, optional when a target object is provided</param>
        /// <param name="target">The object targeted by the operation, when applicable</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        public Task CheckUnityObjectAccess(IToolPermissions.ItemOperation operation, Type type, Object target)
            => Permissions.CheckUnityObjectAccess(Call, operation, type, target, CancellationToken);

        /// <summary>
        /// Ignore all permission checks performed on the given object
        /// </summary>
        /// <param name="target">The object instance to ignore</param>
        public void IgnoreUnityObject(Object target) => Permissions.IgnoreUnityObject(Call, target);

        /// <summary>
        /// Checks if the tool has code execution permission
        /// </summary>
        /// <param name="code">The code to be executed.</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        public Task CheckCodeExecution(string code) => Permissions.CheckCodeExecution(Call, code, CancellationToken);

        /// <summary>
        /// Checks if the tool has screen capture permissions
        /// </summary>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        public Task CheckScreenCapture() => Permissions.CheckScreenCapture(Call, CancellationToken);

        /// <summary>
        /// Checks if the tool has Play Mode permissions
        /// </summary>
        /// <param name="operation">The operation to perform</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        public Task CheckPlayMode(IToolPermissions.PlayModeOperation operation)
            => Permissions.CheckPlayMode(Call, operation, CancellationToken);

        /// <summary>
        /// Checks if the tool can use asset generation
        /// </summary>
        /// <param name="path">The path at which the asset will be created</param>
        /// <param name="type">The type of asset that will be created</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        public Task CheckAssetGeneration(string path, Type type, long cost) => Permissions.CheckAssetGeneration(Call, path, type, cost, CancellationToken);
    }
}
