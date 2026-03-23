using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// UI component for configuring default permissions for the Assistant.
    /// </summary>
    [UxmlElement]
    partial class ConfigureDefaultPermissionsView : ManagedTemplate
    {
        // Centralized mapping between permission policy enum values and their string representations
        static readonly Dictionary<IPermissionsPolicyProvider.PermissionPolicy, string> k_PolicyToStringMap = new()
        {
            { IPermissionsPolicyProvider.PermissionPolicy.Allow, "Allow" },
            { IPermissionsPolicyProvider.PermissionPolicy.Ask, "Ask Permission" },
            { IPermissionsPolicyProvider.PermissionPolicy.Deny, "Deny" }
        };

        // List of choices for dropdown, derived from the centralized mapping
        static readonly List<string> k_PermissionChoices = k_PolicyToStringMap.Values.ToList();

        DropdownField m_FileSystemReadProjectDropdown;
        DropdownField m_FileSystemReadExternalDropdown;
        DropdownField m_FileSystemModifyProjectDropdown;
        DropdownField m_FileSystemCreateProjectDropdown;
        DropdownField m_FileSystemDeleteProjectDropdown;
        DropdownField m_UnityObjectsReadDropdown;
        DropdownField m_UnityObjectsModifyDropdown;
        DropdownField m_UnityObjectsCreateDropdown;
        DropdownField m_UnityObjectsDeleteDropdown;
        DropdownField m_EnterPlayModeDropdown;
        DropdownField m_ExitPlayModeDropdown;
        DropdownField m_ScreenCaptureDropdown;
        DropdownField m_CodeExecutionDropdown;
        DropdownField m_AssetGenerationDropdown;
        DropdownField m_ThirdPartyToolsDropdown;

        public ConfigureDefaultPermissionsView() : base(AssistantUIConstants.UIModulePath)
        {
            RegisterAttachEvents(OnAttachedToPanel, OnDetachedFromPanel);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            // Query and initialize all dropdowns with permission choices
            m_FileSystemReadProjectDropdown = InitializeDropdown(view, "fileSystemReadProjectDropdown", AssistantEditorPreferences.Permissions.GetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Read), OnFileSystemReadProjectPolicyChanged);
            m_FileSystemReadExternalDropdown = InitializeDropdown(view, "fileSystemReadExternalDropdown", AssistantEditorPreferences.Permissions.GetFileSystemOutsideProjectPathPolicy(IToolPermissions.ItemOperation.Read), OnFileSystemReadExternalPolicyChanged);
            m_FileSystemModifyProjectDropdown = InitializeDropdown(view, "fileSystemModifyProjectDropdown", AssistantEditorPreferences.Permissions.GetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Modify), OnFileSystemModifyProjectPolicyChanged);
            m_FileSystemCreateProjectDropdown = InitializeDropdown(view, "fileSystemCreateProjectDropdown", AssistantEditorPreferences.Permissions.GetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Create), OnFileSystemCreateProjectPolicyChanged);
            m_FileSystemDeleteProjectDropdown = InitializeDropdown(view, "fileSystemDeleteProjectDropdown", AssistantEditorPreferences.Permissions.GetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Delete), OnFileSystemDeleteProjectPolicyChanged);
            m_UnityObjectsReadDropdown = InitializeDropdown(view, "unityObjectsReadDropdown", AssistantEditorPreferences.Permissions.GetUnityObjectPolicy(IToolPermissions.ItemOperation.Read), OnUnityObjectsReadPolicyChanged);
            m_UnityObjectsModifyDropdown = InitializeDropdown(view, "unityObjectsModifyDropdown", AssistantEditorPreferences.Permissions.GetUnityObjectPolicy(IToolPermissions.ItemOperation.Modify), OnUnityObjectsModifyPolicyChanged);
            m_UnityObjectsCreateDropdown = InitializeDropdown(view, "unityObjectsCreateDropdown", AssistantEditorPreferences.Permissions.GetUnityObjectPolicy(IToolPermissions.ItemOperation.Create), OnUnityObjectsCreatePolicyChanged);
            m_UnityObjectsDeleteDropdown = InitializeDropdown(view, "unityObjectsDeleteDropdown", AssistantEditorPreferences.Permissions.GetUnityObjectPolicy(IToolPermissions.ItemOperation.Delete), OnUnityObjectsDeletePolicyChanged);
            m_EnterPlayModeDropdown = InitializeDropdown(view, "enterPlayModeDropdown", AssistantEditorPreferences.Permissions.GetPlayModePolicy(IToolPermissions.PlayModeOperation.Enter), OnEnterPlayModePolicyChanged);
            m_ExitPlayModeDropdown = InitializeDropdown(view, "exitPlayModeDropdown", AssistantEditorPreferences.Permissions.GetPlayModePolicy(IToolPermissions.PlayModeOperation.Exit), OnExitPlayModePolicyChanged);
            m_ScreenCaptureDropdown = InitializeDropdown(view, "screenCaptureDropdown", AssistantEditorPreferences.Permissions.ScreenCapturePolicy, OnScreenCapturePolicyChanged);
            m_CodeExecutionDropdown = InitializeDropdown(view, "codeExecutionDropdown", AssistantEditorPreferences.Permissions.CodeExecutionPolicy, OnCodeExecutionPolicyChanged);
            m_AssetGenerationDropdown = InitializeDropdown(view, "assetGenerationDropdown", AssistantEditorPreferences.Permissions.AssetGenerationProjectPathPolicy, OnAssetGenerationPolicyChanged);
            m_ThirdPartyToolsDropdown = InitializeDropdown(view, "thirdPartyToolsDropdown", AssistantEditorPreferences.Permissions.ThirdPartyToolPolicy, OnThirdPartyToolsPolicyChanged);
        }

        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            AssistantEditorPreferences.Permissions.FileSystemProjectPathPolicyChanged += OnFileSystemProjectPathPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.FileSystemOutsideProjectPathPolicyChanged += OnFileSystemOutsideProjectPathPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.UnityObjectPolicyChanged += OnUnityObjectPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.PlayModePolicyChanged += OnPlayModePolicyChangedExternally;
            AssistantEditorPreferences.Permissions.ScreenCapturePolicyChanged += OnScreenCapturePolicyChangedExternally;
            AssistantEditorPreferences.Permissions.CodeExecutionPolicyChanged += OnCodeExecutionPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.AssetGenerationProjectPathPolicyChanged += OnAssetGenerationPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.ThirdPartyToolPolicyChanged += OnThirdPartyToolsPolicyChangedExternally;
        }

        void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            AssistantEditorPreferences.Permissions.FileSystemProjectPathPolicyChanged -= OnFileSystemProjectPathPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.FileSystemOutsideProjectPathPolicyChanged -= OnFileSystemOutsideProjectPathPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.UnityObjectPolicyChanged -= OnUnityObjectPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.PlayModePolicyChanged -= OnPlayModePolicyChangedExternally;
            AssistantEditorPreferences.Permissions.ScreenCapturePolicyChanged -= OnScreenCapturePolicyChangedExternally;
            AssistantEditorPreferences.Permissions.CodeExecutionPolicyChanged -= OnCodeExecutionPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.AssetGenerationProjectPathPolicyChanged -= OnAssetGenerationPolicyChangedExternally;
            AssistantEditorPreferences.Permissions.ThirdPartyToolPolicyChanged -= OnThirdPartyToolsPolicyChangedExternally;
        }

        DropdownField InitializeDropdown(TemplateContainer view, string dropdownName, IPermissionsPolicyProvider.PermissionPolicy policy, EventCallback<ChangeEvent<string>> changeCallback)
        {
            var dropdown = view.Q<DropdownField>(dropdownName);
            dropdown.choices = k_PermissionChoices;
            dropdown.value = PolicyToString(policy);
            dropdown.RegisterValueChangedCallback(changeCallback);
            return dropdown;
        }

        // File System event handlers
        void OnFileSystemReadProjectPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Read, policy);
        }

        void OnFileSystemReadExternalPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetFileSystemOutsideProjectPathPolicy(IToolPermissions.ItemOperation.Read, policy);
        }

        void OnFileSystemModifyProjectPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Modify, policy);
        }

        void OnFileSystemCreateProjectPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Create, policy);
        }

        void OnFileSystemDeleteProjectPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation.Delete, policy);
        }

        // Unity Objects event handlers
        void OnUnityObjectsReadPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetUnityObjectPolicy(IToolPermissions.ItemOperation.Read, policy);
        }

        void OnUnityObjectsModifyPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetUnityObjectPolicy(IToolPermissions.ItemOperation.Modify, policy);
        }

        void OnUnityObjectsCreatePolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetUnityObjectPolicy(IToolPermissions.ItemOperation.Create, policy);
        }

        void OnUnityObjectsDeletePolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetUnityObjectPolicy(IToolPermissions.ItemOperation.Delete, policy);
        }

        void OnEnterPlayModePolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetPlayModePolicy(IToolPermissions.PlayModeOperation.Enter, policy);
        }

        void OnExitPlayModePolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.SetPlayModePolicy(IToolPermissions.PlayModeOperation.Exit, policy);
        }

        void OnScreenCapturePolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.ScreenCapturePolicy = policy;
        }

        void OnCodeExecutionPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.CodeExecutionPolicy = policy;
        }

        void OnAssetGenerationPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.AssetGenerationProjectPathPolicy = policy;
        }

        void OnThirdPartyToolsPolicyChanged(ChangeEvent<string> evt)
        {
            var policy = StringToPolicy(evt.newValue);
            AssistantEditorPreferences.Permissions.ThirdPartyToolPolicy = policy;
        }

        void OnFileSystemProjectPathPolicyChangedExternally(IToolPermissions.ItemOperation operation, IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            var dropdown = operation switch
            {
                IToolPermissions.ItemOperation.Read => m_FileSystemReadProjectDropdown,
                IToolPermissions.ItemOperation.Modify => m_FileSystemModifyProjectDropdown,
                IToolPermissions.ItemOperation.Create => m_FileSystemCreateProjectDropdown,
                IToolPermissions.ItemOperation.Delete => m_FileSystemDeleteProjectDropdown,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
            dropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnFileSystemOutsideProjectPathPolicyChangedExternally(IToolPermissions.ItemOperation operation, IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            var dropdown = operation switch
            {
                IToolPermissions.ItemOperation.Read => m_FileSystemReadExternalDropdown,
                _ => null
            };
            dropdown?.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnUnityObjectPolicyChangedExternally(IToolPermissions.ItemOperation operation, IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            var dropdown = operation switch
            {
                IToolPermissions.ItemOperation.Read => m_UnityObjectsReadDropdown,
                IToolPermissions.ItemOperation.Modify => m_UnityObjectsModifyDropdown,
                IToolPermissions.ItemOperation.Create => m_UnityObjectsCreateDropdown,
                IToolPermissions.ItemOperation.Delete => m_UnityObjectsDeleteDropdown,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
            dropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnPlayModePolicyChangedExternally(IToolPermissions.PlayModeOperation operation, IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            var dropdown = operation switch
            {
                IToolPermissions.PlayModeOperation.Enter => m_EnterPlayModeDropdown,
                IToolPermissions.PlayModeOperation.Exit => m_ExitPlayModeDropdown,
                _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
            };
            dropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnScreenCapturePolicyChangedExternally(IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            m_ScreenCaptureDropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnCodeExecutionPolicyChangedExternally(IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            m_CodeExecutionDropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnAssetGenerationPolicyChangedExternally(IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            m_AssetGenerationDropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        void OnThirdPartyToolsPolicyChangedExternally(IPermissionsPolicyProvider.PermissionPolicy newPolicy)
        {
            m_ThirdPartyToolsDropdown.SetValueWithoutNotify(PolicyToString(newPolicy));
        }

        static string PolicyToString(IPermissionsPolicyProvider.PermissionPolicy policy)
        {
            return k_PolicyToStringMap[policy];
        }

        static IPermissionsPolicyProvider.PermissionPolicy StringToPolicy(string value)
        {
            var kvp = k_PolicyToStringMap.FirstOrDefault(kvp => kvp.Value == value);
            if (kvp.Value == null)
                throw new ArgumentException($"Unknown permission policy string: {value}");
            return kvp.Key;
        }
    }
}
