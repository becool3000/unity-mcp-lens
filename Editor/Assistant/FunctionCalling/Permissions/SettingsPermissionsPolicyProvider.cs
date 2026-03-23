using System;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor
{
    class SettingsPermissionsPolicyProvider : IPermissionsPolicyProvider
    {
        enum ToolType
        {
            FirstParty,
            ThirdParty
        }

        static string[] s_AllowedLocations = {
            "Unity.AI.Assistant",
            "Unity.AI.Agents",
        };

        bool AutoRun => AssistantEditorPreferences.AutoRun;

        public IPermissionsPolicyProvider.PermissionPolicy GetToolExecutionPolicy(string toolId)
        {
            var toolType = GetToolType(toolId);
            var policy = toolType switch
            {
                ToolType.FirstParty => AssistantEditorPreferences.Permissions.FirstPartyToolPolicy,
                ToolType.ThirdParty => AssistantEditorPreferences.Permissions.ThirdPartyToolPolicy,
                _ => throw new ArgumentOutOfRangeException(nameof(toolType), toolType, null)
            };
            return ApplyAutoRunState(policy);
        }

        public IPermissionsPolicyProvider.PermissionPolicy GetFileSystemPolicy(string toolId, IToolPermissions.ItemOperation operation, string path)
        {
            var isProjectPath = PathUtils.IsProjectPath(path);
            var policy = isProjectPath
                ? AssistantEditorPreferences.Permissions.GetFileSystemProjectPathPolicy(operation)
                : AssistantEditorPreferences.Permissions.GetFileSystemOutsideProjectPathPolicy(operation);
            return ApplyAutoRunState(policy);
        }

        public IPermissionsPolicyProvider.PermissionPolicy GetUnityObjectPolicy(string toolId, IToolPermissions.ItemOperation operation, Type type, Object target)
        {
            if (type == null && target == null)
                throw new ArgumentException("Either type or target are required");

            if (target != null && type != null && target.GetType() != type)
                throw new ArgumentException("Type and target object must match, or only provide the target instance.");

            if (operation != IToolPermissions.ItemOperation.Create && target == null)
                throw new ArgumentException("You must provide a target instance for all operations except creation.");

            var policy = AssistantEditorPreferences.Permissions.GetUnityObjectPolicy(operation);
            return ApplyAutoRunState(policy);
        }

        public IPermissionsPolicyProvider.PermissionPolicy GetCodeExecutionPolicy(string toolId, string code)
        {
            var policy = AssistantEditorPreferences.Permissions.CodeExecutionPolicy;
            return ApplyAutoRunState(policy);
        }

        public IPermissionsPolicyProvider.PermissionPolicy GetScreenCapturePolicy(string toolId)
        {
            var policy = AssistantEditorPreferences.Permissions.ScreenCapturePolicy;
            return ApplyAutoRunState(policy);
        }

        public IPermissionsPolicyProvider.PermissionPolicy GetPlayModePolicy(string toolId, IToolPermissions.PlayModeOperation operation)
        {
            var policy = AssistantEditorPreferences.Permissions.GetPlayModePolicy(operation);
            return ApplyAutoRunState(policy);
        }

        public IPermissionsPolicyProvider.PermissionPolicy GetAssetGenerationPolicy(string toolId, string path, Type type)
        {
            var isProjectPath = PathUtils.IsProjectPath(path);
            var policy = isProjectPath
                ? AssistantEditorPreferences.Permissions.AssetGenerationProjectPathPolicy
                : AssistantEditorPreferences.Permissions.AssetGenerationOutsideProjectPathPolicy;
            return ApplyAutoRunState(policy);
        }

        IPermissionsPolicyProvider.PermissionPolicy ApplyAutoRunState(IPermissionsPolicyProvider.PermissionPolicy policy)
        {
            if (AutoRun && policy == IPermissionsPolicyProvider.PermissionPolicy.Ask)
                return IPermissionsPolicyProvider.PermissionPolicy.Allow;

            return policy;
        }

        static ToolType GetToolType(string toolId)
        {
            if (ToolRegistry.FunctionToolbox.TryGetMethod(toolId, out var tool))
            {
                switch (tool)
                {
                    case IAssemblyFunction laf:

                        var location = laf.Assembly?.Location;

                        if (location != null)
                        {
                            foreach (var allowedLocation in s_AllowedLocations)
                            {
                                if (location.Contains(allowedLocation))
                                    return ToolType.FirstParty;
                            }
                        }
                        break;
                }
            }

            return ToolType.ThirdParty;
        }
    }
}
