using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// An interface to check the permissions of a given tool
    /// </summary>
    interface IToolPermissions
    {
        /// <summary>
        /// The type of operation to be performed
        /// </summary>
        public enum ItemOperation
        {
            /// <summary> Only reads data </summary>
            Read,

            /// <summary> Create a new item </summary>
            Create,

            /// <summary> Delete an existing item </summary>
            Delete,

            /// <summary> Modify an existing item </summary>
            Modify
        }

        public enum PlayModeOperation
        {
            /// <summary> Enter play mode </summary>
            Enter,

            /// <summary> Exit play mode </summary>
            Exit
        }

        public struct TemporaryPermission
        {
            public string Name;
            public Action ResetFunction;

            public TemporaryPermission(string name, Action resetFunction)
            {
                Name = name;
                ResetFunction = resetFunction;
            }
        }

        /// <summary>
        /// Reset all temporary permissions granted with the scope of a session
        /// </summary>
        void ResetTemporaryPermissions();

        /// <summary>
        /// Reset all temporary permissions granted to specific object instances
        /// </summary>
        void ResetIgnoredObjects();

        /// <summary>
        /// Get the list of temporary permissions granted for the scope of a session
        /// </summary>
        void GetTemporaryPermissions(IList<TemporaryPermission> permissions);

        /// <summary>
        /// Checks if the tool can be executed
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckToolExecution(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the tool has file system permissions
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="operation">The operation to perform</param>
        /// <param name="path">A path to check permissions for.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckFileSystemAccess(ToolExecutionContext.CallInfo callInfo, ItemOperation operation, string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the tool has UnityEngine.Object permissions
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="operation">The operation to perform</param>
        /// <param name="type">The type of the object impacted by the operation, optional when a target object is provided</param>
        /// <param name="target">The object targeted by the operation, when applicable</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckUnityObjectAccess(ToolExecutionContext.CallInfo callInfo, ItemOperation operation, Type type, Object target, CancellationToken cancellationToken = default);

        /// <summary>
        /// Ignore all permission checks performed on the given object
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="target">The object instance to ignore</param>
        void IgnoreUnityObject(ToolExecutionContext.CallInfo callInfo, Object target);

        /// <summary>
        /// Checks if the code can be executed
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="code">The code to be executed</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckCodeExecution(ToolExecutionContext.CallInfo callInfo, string code, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if screen capture can be performed
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckScreenCapture(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the tool can enter or exit play mode
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="operation">The operation to perform</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckPlayMode(ToolExecutionContext.CallInfo callInfo, PlayModeOperation operation, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the tool can use asset generation
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="path">The path at which the asset will be created</param>
        /// <param name="type">The type of asset that will be created</param>
        /// <param name="cost">Cost of the query</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An asynchronous task that will complete if permissions are granted and throw otherwise</returns>
        Task CheckAssetGeneration(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost, CancellationToken cancellationToken = default);
    }
}
