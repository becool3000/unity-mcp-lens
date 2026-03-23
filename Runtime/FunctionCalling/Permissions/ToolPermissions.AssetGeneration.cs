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
            public class AssetGenerationState
            {
                const string k_StateName = "Asset Generation";

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

        public async Task CheckAssetGeneration(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost, CancellationToken cancellationToken = default)
        {
            // Get current tool status
            var currentStatus = GetAssetGenerationPermission(callInfo, path, type);

            InternalLog.Log($"[Permission] CheckAssetGeneration: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreateAssetGenerationElement(callInfo, path, type, cost);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] CheckAssetGeneration: {callInfo.FunctionId}. Answer: {userAnswer}");

                OnPermissionResponse(callInfo, userAnswer, PermissionType.AssetGeneration);

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.AssetGeneration.Allow();
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
            throw new Exception("The user denied the request.");
        }

        PermissionStatus GetAssetGenerationPermission(ToolExecutionContext.CallInfo callInfo, string path, Type type)
        {
            var permissionPolicy = PolicyProvider.GetAssetGenerationPolicy(callInfo.FunctionId, path, type);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.AssetGeneration.IsAllowed() ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreateAssetGenerationElement(ToolExecutionContext.CallInfo callInfo, string path, Type type, long cost);
    }
}
