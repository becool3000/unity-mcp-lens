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
            public class UnityObjectState
            {
                const string k_StateName = "Unity Objects";

                [SerializeField]
                List<IToolPermissions.ItemOperation> m_AllowedOperations = new();

                [SerializeField]
                List<UnityEngine.Object> m_IgnoredObjects = new();

                public void Reset()
                {
                    m_AllowedOperations.Clear();
                    ResetIgnoredObjects();
                }

                public void ResetIgnoredObjects()
                {
                    m_IgnoredObjects.Clear();
                }

                public void Allow(IToolPermissions.ItemOperation operation, Type type, UnityEngine.Object target)
                    => m_AllowedOperations.Add(operation);

                public void Ignore(UnityEngine.Object target) => m_IgnoredObjects.Add(target);

                public bool IsAllowed(IToolPermissions.ItemOperation operation, Type type, UnityEngine.Object target)
                {
                    if (m_AllowedOperations.Contains(operation))
                        return true;

                    if (target != null)
                    {
                        if (m_IgnoredObjects.Contains(target))
                            return true;

                        if (target is Component component && m_IgnoredObjects.Contains(component.gameObject))
                            return true;
                    }

                    return false;
                }


                public void AppendTemporaryPermissions(IList<IToolPermissions.TemporaryPermission> allowedStates)
                {
                    foreach (var operation in m_AllowedOperations)
                    {
                        var permission = new IToolPermissions.TemporaryPermission($"{operation} {k_StateName}", () => m_AllowedOperations.Remove(operation));
                        allowedStates.Add(permission);
                    }
                }
            }
        }

        public void IgnoreUnityObject(ToolExecutionContext.CallInfo callInfo, UnityEngine.Object target)
        {
            if (target == null)
                return;

            State.UnityObject.Ignore(target);
        }

        public async Task CheckUnityObjectAccess(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, Type type, UnityEngine.Object target, CancellationToken cancellationToken = default)
        {
            if (type == null && target == null)
                throw new ArgumentException("Either type or target are required");

            if (target != null && type != null && !type.IsAssignableFrom(target.GetType()))
                throw new ArgumentException("Type and target object must match, or only provide the target instance.");

            if (operation != IToolPermissions.ItemOperation.Create && target == null)
                throw new ArgumentException("You must provide a target instance for all operations except creation.");

            if (target != null)
                type = target.GetType();

            // Get current tool status
            var currentStatus = GetUnityObjectAccessPermission(callInfo, operation, type, target);

            InternalLog.Log($"[Permission] CheckUnityObjectAccess: {callInfo.FunctionId}. PermissionStatus: {currentStatus}");

            // Ask user and update status
            if (currentStatus == PermissionStatus.Pending)
            {
                var userInteraction = CreateUnityObjectAccessElement(callInfo, operation, type, target);
                var userAnswer = await WaitForUser(callInfo, userInteraction, cancellationToken);
                InternalLog.Log($"[Permission] CheckUnityObjectAccess: {callInfo.FunctionId}. Answer: {userAnswer}");

                OnPermissionResponse(callInfo, userAnswer, PermissionType.UnityObject);

                switch (userAnswer)
                {
                    case UserAnswer.AllowOnce:
                        currentStatus = PermissionStatus.Approved;
                        break;

                    case UserAnswer.AllowAlways:
                        State.UnityObject.Allow(operation, type, target);
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
                IToolPermissions.ItemOperation.Read => $"The user denied the request to read instances",
                IToolPermissions.ItemOperation.Create => $"The user denied the request to create new instances",
                IToolPermissions.ItemOperation.Delete => $"The user denied the request to delete instances",
                IToolPermissions.ItemOperation.Modify => $"The user denied the request to modify instances",
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
            throw new Exception(errorMessage);
        }

        PermissionStatus GetUnityObjectAccessPermission(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, Type type, UnityEngine.Object target)
        {
            var permissionPolicy = PolicyProvider.GetUnityObjectPolicy(callInfo.FunctionId, operation, type, target);
            return permissionPolicy switch
            {
                IPermissionsPolicyProvider.PermissionPolicy.Allow => PermissionStatus.Approved,
                IPermissionsPolicyProvider.PermissionPolicy.Ask =>
                    State.UnityObject.IsAllowed(operation, type, target) ? PermissionStatus.Approved : PermissionStatus.Pending,
                IPermissionsPolicyProvider.PermissionPolicy.Deny => PermissionStatus.Denied,
                _ => throw new ArgumentOutOfRangeException()
            };
        }

        protected abstract IUserInteraction<UserAnswer> CreateUnityObjectAccessElement(ToolExecutionContext.CallInfo callInfo, IToolPermissions.ItemOperation operation, Type type, UnityEngine.Object target);
    }
}
