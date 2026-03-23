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
            public class PlayModeState
            {
                [SerializeField]
                List<IToolPermissions.PlayModeOperation> m_AllowedOperations = new();

                public void Reset()
                {
                    m_AllowedOperations.Clear();
                }

                public void Allow(IToolPermissions.PlayModeOperation operation)
                {
                    m_AllowedOperations.Add(operation);
                }

                public bool IsAllowed(IToolPermissions.PlayModeOperation operation)
                {
                    return m_AllowedOperations.Contains(operation);
                }

                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    foreach (var operation in m_AllowedOperations)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{operation} Play Mode", () => m_AllowedOperations.Remove(operation));
                        allowedStates.Add(permission);
                    }
                }
            }
        }

        public async Task CheckPlayMode(ToolExecutionContext.CallInfo callInfo, IToolPermissions.PlayModeOperation operation, CancellationToken cancellationToken = default)
        {
            // Get current tool status
            var currentStatus = GetPlayModePermission(callInfo, operation);

            InternalLog.Log($"[Permission] PlayMode: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreatePlayModeElement(callInfo, operation);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] PlayMode: {callInfo.FunctionId}. Answer: {userAnswer}");

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.PlayMode.Allow(operation);
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
                IToolPermissions.PlayModeOperation.Enter => $"The user denied the request to enter play mode",
                IToolPermissions.PlayModeOperation.Exit => $"The user denied the request to exit play mode",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
            throw new Exception(errorMessage);
        }

        PermissionStatus GetPlayModePermission(ToolExecutionContext.CallInfo callInfo, IToolPermissions.PlayModeOperation operation)
        {
            var permissionPolicy = PolicyProvider.GetPlayModePolicy(callInfo.FunctionId, operation);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.PlayMode.IsAllowed(operation) ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreatePlayModeElement(ToolExecutionContext.CallInfo callInfo, IToolPermissions.PlayModeOperation operation);
    }
}
