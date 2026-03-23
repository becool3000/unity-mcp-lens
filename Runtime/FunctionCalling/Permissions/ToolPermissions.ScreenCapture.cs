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
            public class ScreenCaptureState
            {
                const string k_StateName = "Screen Capture";

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

        public async Task CheckScreenCapture(ToolExecutionContext.CallInfo callInfo, CancellationToken cancellationToken = default)
        {
            // Get current tool status
            var currentStatus = GetScreenCapturePermission(callInfo);

            InternalLog.Log($"[Permission] CheckScreenCapture: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreateScreenCaptureElement(callInfo);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] CheckScreenCapture: {callInfo.FunctionId}. Answer: {userAnswer}");

                OnPermissionResponse(callInfo, userAnswer, PermissionType.ScreenCapture);

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.ScreenCapture.Allow();
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
            throw new Exception("The user denied the request to capture the screen.");
        }

        PermissionStatus GetScreenCapturePermission(ToolExecutionContext.CallInfo callInfo)
        {
            var permissionPolicy = PolicyProvider.GetScreenCapturePolicy(callInfo.FunctionId);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.ScreenCapture.IsAllowed() ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreateScreenCaptureElement(ToolExecutionContext.CallInfo callInfo);
    }
}
