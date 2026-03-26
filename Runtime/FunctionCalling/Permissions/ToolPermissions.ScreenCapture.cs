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

                    case UserAnswer.DenyAlways:
                        State.ScreenCapture.Deny();
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
                    State.ScreenCapture.IsAllowed() ? PermissionStatus.Approved :
                    State.ScreenCapture.IsDenied() ? PermissionStatus.Denied :
                    PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IInteractionSource<UserAnswer> CreateScreenCaptureElement(ToolExecutionContext.CallInfo callInfo);
    }
}
