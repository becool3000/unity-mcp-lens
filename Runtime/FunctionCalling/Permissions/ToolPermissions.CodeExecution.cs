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
            public class CodeExecutionState
            {
                const string k_StateName = "Code Execution";

                [SerializeField]
                bool m_AlwaysAllow;

                public void Reset() => m_AlwaysAllow = false;
                public void Allow() => m_AlwaysAllow = true;
                public bool IsAllowed() => m_AlwaysAllow;

                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    if (m_AlwaysAllow)
                        allowedStates.Add(new IToolPermissions.TemporaryPermission(k_StateName, Reset));
                }
            }
        }

        public async Task CheckCodeExecution(ToolExecutionContext.CallInfo callInfo, string code, CancellationToken cancellationToken = default)
        {
            // Get current tool status
            var currentStatus = GetCodeExecutionPermission(callInfo, code);

            InternalLog.Log($"[Permission] CheckCodeExecution: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreateCodeExecutionElement(callInfo, code);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] CheckCodeExecution: {callInfo.FunctionId}. Answer: {userAnswer}");

                OnPermissionResponse(callInfo, userAnswer, PermissionType.CodeExecution);

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.CodeExecution.Allow();
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
            throw new Exception("The user denied the request to execute this code.");
        }

        PermissionStatus GetCodeExecutionPermission(ToolExecutionContext.CallInfo callInfo, string code)
        {
            var permissionPolicy = PolicyProvider.GetCodeExecutionPolicy(callInfo.FunctionId, code);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.CodeExecution.IsAllowed() ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreateCodeExecutionElement(ToolExecutionContext.CallInfo callInfo, string code);
    }
}
