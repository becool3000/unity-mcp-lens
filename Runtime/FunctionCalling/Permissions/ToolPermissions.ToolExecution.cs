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

                public void Reset() => m_AllowedToolIds.Clear();
                public void Allow(string toolId) => m_AllowedToolIds.Add(toolId);
                public bool IsAllowed(string toolId) => m_AllowedToolIds.Contains(toolId);

                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    foreach (var toolId in m_AllowedToolIds)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{k_StateName} {toolId}", () => m_AllowedToolIds.Remove(toolId));
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
                    State.ToolExecution.IsAllowed(callInfo.FunctionId) ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreateToolExecutionElement(ToolExecutionContext.CallInfo callInfo);
    }
}
