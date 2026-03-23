using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.FunctionCalling
{
    interface IPermissionsPolicyProvider
    {
        /// <summary>
        /// The policy for a permission
        /// </summary>
        public enum PermissionPolicy
        {
            /// <summary> Always allow this permission </summary>
            Allow,

            /// <summary> Always ask for permission </summary>
            Ask,

            /// <summary> Always deny this permission </summary>
            Deny
        }

        /// <summary>
        /// Get the policy for tool execution
        /// </summary>
        /// <param name="toolId">The ID of the tool</param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetToolExecutionPolicy(string toolId);

        /// <summary>
        /// Get the policy for file operations
        /// </summary>
        /// <param name="operation">The type of operation to perform </param>
        /// <param name="path">The path at which reading will be performed</param>
        /// <param name="toolId">The ID of the tool</param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetFileSystemPolicy(string toolId, IToolPermissions.ItemOperation operation, string path);

        /// <summary>
        /// Get the policy for UnityEngine.Object operations
        /// </summary>
        /// <param name="operation">The type of operation to perform </param>
        /// <param name="type">The type of the object impacted by the operation, optional when a target object is provided</param>
        /// <param name="target">The object targeted by the operation, when applicable</param>
        /// <param name="toolId">The ID of the tool</param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetUnityObjectPolicy(string toolId, IToolPermissions.ItemOperation operation, Type type, Object target);

        /// <summary>
        /// Get the policy for code execution
        /// </summary>
        /// <param name="code">The code to be executed</param>
        /// <param name="toolId">The ID of the tool</param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetCodeExecutionPolicy(string toolId, string code);

        /// <summary>
        /// Get the policy for screen capture
        /// </summary>
        /// <param name="toolId">The ID of the tool</param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetScreenCapturePolicy(string toolId);

        /// <summary>
        /// Get the policy for play mode operation
        /// </summary>
        /// <param name="toolId">The ID of the tool</param>
        /// <param name="operation">The type of operation to perform </param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetPlayModePolicy(string toolId, IToolPermissions.PlayModeOperation operation);

        /// <summary>
        /// Get the asset generation policy
        /// </summary>
        /// <param name="toolId">The ID of the tool</param>
        /// <param name="path">The path at which the asset will be created</param>
        /// <param name="type">The type of asset that will be created</param>
        /// <returns>The policy</returns>
        public PermissionPolicy GetAssetGenerationPolicy(string toolId, string path, Type type);
    }
}
