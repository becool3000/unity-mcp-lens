using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.FunctionCalling
{
    partial class ToolPermissions
    {
        partial class PermissionsState
        {
            [Serializable]
            public class FileSystemState
            {
                const string k_ProjectFilesName = "Project Files";
                const string k_ExternalFilesName = "External Files";

                [SerializeField]
                List<IToolPermissions.ItemOperation> m_AllowedProjectOperations = new();

                [SerializeField]
                List<IToolPermissions.ItemOperation> m_AllowedExternalOperations = new();

                public void Reset()
                {
                    m_AllowedProjectOperations.Clear();
                    m_AllowedExternalOperations.Clear();
                }

                public void Allow(IToolPermissions.ItemOperation operation, string path)
                {
                    var isProjectPath = PathUtils.IsProjectPath(path);
                    if (isProjectPath)
                        m_AllowedProjectOperations.Add(operation);
                    else
                        m_AllowedExternalOperations.Add(operation);
                }

                public bool IsAllowed(IToolPermissions.ItemOperation operation, string path)
                {
                    var isProjectPath = PathUtils.IsProjectPath(path);
                    return isProjectPath
                        ? m_AllowedProjectOperations.Contains(operation)
                        : m_AllowedExternalOperations.Contains(operation);
                }

                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    foreach (var operation in m_AllowedProjectOperations)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{operation} {k_ProjectFilesName}", () => m_AllowedProjectOperations.Remove(operation));
                        allowedStates.Add(permission);
                    }

                    foreach (var operation in m_AllowedExternalOperations)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{operation} {k_ExternalFilesName}", () => m_AllowedExternalOperations.Remove(operation));
                        allowedStates.Add(permission);
                    }
                }
            }
        }

        public async Task CheckFileSystemAccess(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, string path, CancellationToken cancellationToken = default)
        {
            // Get current tool status
            var currentStatus = GetFileSystemAccessPermission(callInfo, operation, path);

            InternalLog.Log($"[Permission] CheckFileSystemAccess: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreateFileSystemAccessElement(callInfo, operation, path);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] CheckFileSystemAccess: {callInfo.FunctionId}. Answer: {userAnswer}");

                OnPermissionResponse(callInfo, userAnswer, PermissionType.FileSystem);

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.FileSystem.Allow(operation, path);
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.DenyOnce:
                        currentStatus = PermissionStatus.Denied;
                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            // Permission approved, nothing more to do
            if (currentStatus == PermissionStatus.Approved)
                return;

            // Permission denied
            var errorMessage = operation switch
            {
                IToolPermissions.ItemOperation.Read => $"The user denied the request to read path: {path}",
                IToolPermissions.ItemOperation.Create => $"The user denied the request to create path: {path}",
                IToolPermissions.ItemOperation.Delete => $"The user denied the request to delete path: {path}",
                IToolPermissions.ItemOperation.Modify => $"The user denied the request to write at path: {path}",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
            throw new Exception(errorMessage);
        }

        PermissionStatus GetFileSystemAccessPermission(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, string path)
        {
            var permissionPolicy = PolicyProvider.GetFileSystemPolicy(callInfo.FunctionId, operation, path);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.FileSystem.IsAllowed(operation, path) ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreateFileSystemAccessElement(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, string path);
    }
}
