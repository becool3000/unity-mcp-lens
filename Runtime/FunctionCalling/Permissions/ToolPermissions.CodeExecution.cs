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
                PermissionOverride m_Override;

                public void Reset() => m_Override = PermissionOverride.None;

                public void Allow() => m_Override = PermissionOverride.AlwaysAllow;
                public bool IsAllowed() => m_Override == PermissionOverride.AlwaysAllow;

                public void Deny() => m_Override = PermissionOverride.AlwaysDeny;
                public bool IsDenied() => m_Override == PermissionOverride.AlwaysDeny;

                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    switch (m_Override)
                    {
                        case PermissionOverride.AlwaysAllow:
                            allowedStates.Add(new IToolPermissions.TemporaryPermission(k_StateName, Reset));
                            break;
                        case PermissionOverride.AlwaysDeny:
                            allowedStates.Add(new IToolPermissions.TemporaryPermission($"{k_StateName} (Denied)", Reset));
                            break;
                    }
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

                    case UserAnswer.DenyAlways:
                        State.CodeExecution.Deny();
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
                    State.CodeExecution.IsAllowed() ? PermissionStatus.Approved :
                    State.CodeExecution.IsDenied() ? PermissionStatus.Denied :
                    PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IInteractionSource<UserAnswer> CreateCodeExecutionElement(ToolExecutionContext.CallInfo callInfo, string code);
    }
}
