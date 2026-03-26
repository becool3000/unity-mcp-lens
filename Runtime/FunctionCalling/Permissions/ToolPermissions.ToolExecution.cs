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
            public class ToolExecutionState
            {
                const string k_StateName = "Tool Execution";

                [SerializeField]
                List<string> m_AllowedToolIds = new();

                [SerializeField]
                List<string> m_DeniedToolIds = new();

                public void Reset()
                {
                    m_AllowedToolIds.Clear();
                    m_DeniedToolIds.Clear();
                }

                public void Allow(string toolId) => m_AllowedToolIds.Add(toolId);
                public bool IsAllowed(string toolId) => m_AllowedToolIds.Contains(toolId);

                public void Deny(string toolId) => m_DeniedToolIds.Add(toolId);
                public bool IsDenied(string toolId) => m_DeniedToolIds.Contains(toolId);

                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    foreach (var toolId in m_AllowedToolIds)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{k_StateName} {toolId}", () => m_AllowedToolIds.Remove(toolId));
                        allowedStates.Add(permission);
                    }

                    foreach (var toolId in m_DeniedToolIds)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{k_StateName} {toolId} (Denied)", () => m_DeniedToolIds.Remove(toolId));
                        allowedStates.Add(permission);
                    }
                }
            }
        }

        public async Task CheckToolExecution(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default)
        {
            // Get current tool status
            var currentStatus = GetToolExecutionPermission(callInfo);

            InternalLog.Log($"[Permission] CheckToolExecution: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreateToolExecutionElement(callInfo);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] CheckToolExecution: {callInfo.FunctionId}. Answer: {userAnswer}");

                OnPermissionResponse(callInfo, userAnswer, PermissionType.ToolExecution);

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.ToolExecution.Allow(callInfo.FunctionId);
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.DenyOnce:
                        currentStatus = PermissionStatus.Denied;
                        break;

                    case UserAnswer.DenyAlways:
                        State.ToolExecution.Deny(callInfo.FunctionId);
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
            throw new Exception($"The user denied the request to execute the tool {callInfo.FunctionId}.");
        }

        PermissionStatus GetToolExecutionPermission(ToolExecutionContext.CallInfo callInfo)
        {
            var permissionPolicy = PolicyProvider.GetToolExecutionPolicy(callInfo.FunctionId);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.ToolExecution.IsAllowed(callInfo.FunctionId) ? PermissionStatus.Approved :
                    State.ToolExecution.IsDenied(callInfo.FunctionId) ? PermissionStatus.Denied :
                    PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IInteractionSource<UserAnswer> CreateToolExecutionElement(ToolExecutionContext.CallInfo callInfo);
    }
}
