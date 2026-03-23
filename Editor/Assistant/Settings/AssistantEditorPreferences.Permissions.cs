using UnityEditor;
using System;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Partial class containing permission-related settings
    /// </summary>
    static partial class AssistantEditorPreferences
    {
        /// <summary>
        /// Permission-related settings for the AI Assistant
        /// </summary>
        public static class Permissions
        {
            const string k_FirstPartyToolPolicy = k_SettingsPrefix + "FirstPartyToolPolicy";
            const string k_ThirdPartyToolPolicy = k_SettingsPrefix + "ThirdPartyToolPolicy";
            const string k_FileSystemProjectPathPolicyPrefix = k_SettingsPrefix + "FileSystemProjectPath.";
            const string k_FileSystemOutsideProjectPathPolicyPrefix = k_SettingsPrefix + "FileSystemOutsideProjectPath.";
            const string k_UnityObjectPolicyPrefix = k_SettingsPrefix + "UnityObject.";
            const string k_CodeExecutionPolicy = k_SettingsPrefix + "CodeExecutionPolicy";
            const string k_ScreenCapturePolicy = k_SettingsPrefix + "ScreenCapturePolicy";
            const string k_PlayModePolicyPrefix = k_SettingsPrefix + "PlayModePolicy.";
            const string k_AssetGenerationProjectPathPolicy = k_SettingsPrefix + "AssetGenerationProjectPathPolicy";
            const string k_AssetGenerationOutsideProjectPathPolicy = k_SettingsPrefix + "AssetGenerationOutsideProjectPathPolicy";

            public static event Action<IPermissionsPolicyProvider.PermissionPolicy> FirstPartyToolPolicyChanged;
            public static event Action<IPermissionsPolicyProvider.PermissionPolicy> ThirdPartyToolPolicyChanged;
            public static event Action<IToolPermissions.ItemOperation, IPermissionsPolicyProvider.PermissionPolicy> FileSystemProjectPathPolicyChanged;
            public static event Action<IToolPermissions.ItemOperation, IPermissionsPolicyProvider.PermissionPolicy> FileSystemOutsideProjectPathPolicyChanged;
            public static event Action<IToolPermissions.ItemOperation, IPermissionsPolicyProvider.PermissionPolicy> UnityObjectPolicyChanged;
            public static event Action<IPermissionsPolicyProvider.PermissionPolicy> CodeExecutionPolicyChanged;
            public static event Action<IPermissionsPolicyProvider.PermissionPolicy> ScreenCapturePolicyChanged;
            public static event Action<IToolPermissions.PlayModeOperation, IPermissionsPolicyProvider.PermissionPolicy> PlayModePolicyChanged;
            public static event Action<IPermissionsPolicyProvider.PermissionPolicy> AssetGenerationProjectPathPolicyChanged;
            public static event Action<IPermissionsPolicyProvider.PermissionPolicy> AssetGenerationOutsideProjectPathPolicyChanged;

            /// <summary>
            /// Permission policy for first-party tool execution (Unity AI Assistant tools)
            /// </summary>
            public static IPermissionsPolicyProvider.PermissionPolicy FirstPartyToolPolicy
            {
                get => (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(k_FirstPartyToolPolicy, (int)IPermissionsPolicyProvider.PermissionPolicy.Allow);
                set
                {
                    if (FirstPartyToolPolicy != value)
                    {
                        EditorPrefs.SetInt(k_FirstPartyToolPolicy, (int)value);
                        FirstPartyToolPolicyChanged?.Invoke(value);
                    }
                }
            }

            /// <summary>
            /// Permission policy for third-party tool execution (non-Unity AI Assistant tools)
            /// </summary>
            public static IPermissionsPolicyProvider.PermissionPolicy ThirdPartyToolPolicy
            {
                get => (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(k_ThirdPartyToolPolicy, (int)IPermissionsPolicyProvider.PermissionPolicy.Ask);
                set
                {
                    if (ThirdPartyToolPolicy != value)
                    {
                        EditorPrefs.SetInt(k_ThirdPartyToolPolicy, (int)value);
                        ThirdPartyToolPolicyChanged?.Invoke(value);
                    }
                }
            }

            /// <summary>
            /// Get or set the permission policy for file system operations within the project path
            /// </summary>
            /// <param name="operation">The file system operation type</param>
            /// <returns>The permission policy for the specified operation</returns>
            public static IPermissionsPolicyProvider.PermissionPolicy GetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation operation)
            {
                var key = k_FileSystemProjectPathPolicyPrefix + operation;
                var defaultPolicy = operation switch
                {
                    IToolPermissions.ItemOperation.Read => IPermissionsPolicyProvider.PermissionPolicy.Allow,
                    _ => IPermissionsPolicyProvider.PermissionPolicy.Ask
                };
                return (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(key, (int)defaultPolicy);
            }

            /// <summary>
            /// Set the permission policy for file system operations within the project path
            /// </summary>
            /// <param name="operation">The file system operation type</param>
            /// <param name="policy">The permission policy to set</param>
            public static void SetFileSystemProjectPathPolicy(IToolPermissions.ItemOperation operation, IPermissionsPolicyProvider.PermissionPolicy policy)
            {
                var currentValue = GetFileSystemProjectPathPolicy(operation);
                if (currentValue != policy)
                {
                    var key = k_FileSystemProjectPathPolicyPrefix + operation;
                    EditorPrefs.SetInt(key, (int)policy);
                    FileSystemProjectPathPolicyChanged?.Invoke(operation, policy);
                }
            }

            /// <summary>
            /// Get or set the permission policy for file system operations outside the project path
            /// </summary>
            /// <param name="operation">The file system operation type</param>
            /// <returns>The permission policy for the specified operation</returns>
            public static IPermissionsPolicyProvider.PermissionPolicy GetFileSystemOutsideProjectPathPolicy(IToolPermissions.ItemOperation operation)
            {
                var key = k_FileSystemOutsideProjectPathPolicyPrefix + operation;
                var defaultPolicy = operation switch
                {
                    IToolPermissions.ItemOperation.Read => IPermissionsPolicyProvider.PermissionPolicy.Ask,
                    _ => IPermissionsPolicyProvider.PermissionPolicy.Deny
                };
                return (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(key, (int)defaultPolicy);
            }

            /// <summary>
            /// Set the permission policy for file system operations outside the project path
            /// </summary>
            /// <param name="operation">The file system operation type</param>
            /// <param name="policy">The permission policy to set</param>
            public static void SetFileSystemOutsideProjectPathPolicy(IToolPermissions.ItemOperation operation, IPermissionsPolicyProvider.PermissionPolicy policy)
            {
                var currentValue = GetFileSystemOutsideProjectPathPolicy(operation);
                if (currentValue != policy)
                {
                    var key = k_FileSystemOutsideProjectPathPolicyPrefix + operation;
                    EditorPrefs.SetInt(key, (int)policy);
                    FileSystemOutsideProjectPathPolicyChanged?.Invoke(operation, policy);
                }
            }

            /// <summary>
            /// Get the permission policy for Unity Object operations
            /// </summary>
            /// <param name="operation">The Unity Object operation type</param>
            /// <returns>The permission policy for the specified operation</returns>
            public static IPermissionsPolicyProvider.PermissionPolicy GetUnityObjectPolicy(IToolPermissions.ItemOperation operation)
            {
                var key = k_UnityObjectPolicyPrefix + operation;
                var defaultPolicy = operation switch
                {
                    IToolPermissions.ItemOperation.Read => IPermissionsPolicyProvider.PermissionPolicy.Allow,
                    _ => IPermissionsPolicyProvider.PermissionPolicy.Ask
                };
                return (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(key, (int)defaultPolicy);
            }

            /// <summary>
            /// Set the permission policy for Unity Object operations
            /// </summary>
            /// <param name="operation">The Unity Object operation type</param>
            /// <param name="policy">The permission policy to set</param>
            public static void SetUnityObjectPolicy(IToolPermissions.ItemOperation operation, IPermissionsPolicyProvider.PermissionPolicy policy)
            {
                var currentValue = GetUnityObjectPolicy(operation);
                if (currentValue != policy)
                {
                    var key = k_UnityObjectPolicyPrefix + operation;
                    EditorPrefs.SetInt(key, (int)policy);
                    UnityObjectPolicyChanged?.Invoke(operation, policy);
                }
            }

            /// <summary>
            /// Get the permission policy for Play Mode operation
            /// </summary>
            /// <param name="operation">The operation type</param>
            /// <returns>The permission policy for the specified operation</returns>
            public static IPermissionsPolicyProvider.PermissionPolicy GetPlayModePolicy(IToolPermissions.PlayModeOperation operation)
            {
                var key = k_PlayModePolicyPrefix + operation;
                var defaultPolicy = IPermissionsPolicyProvider.PermissionPolicy.Ask;
                return (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(key, (int)defaultPolicy);
            }

            /// <summary>
            /// Set the permission policy for Play Mode operations
            /// </summary>
            /// <param name="operation">The operation type</param>
            /// <param name="policy">The permission policy to set</param>
            public static void SetPlayModePolicy(IToolPermissions.PlayModeOperation operation, IPermissionsPolicyProvider.PermissionPolicy policy)
            {
                var currentValue = GetPlayModePolicy(operation);
                if (currentValue != policy)
                {
                    var key = k_PlayModePolicyPrefix + operation;
                    EditorPrefs.SetInt(key, (int)policy);
                    PlayModePolicyChanged?.Invoke(operation, policy);
                }
            }

            /// <summary>
            /// Permission policy for code execution
            /// </summary>
            public static IPermissionsPolicyProvider.PermissionPolicy CodeExecutionPolicy
            {
                get => (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(k_CodeExecutionPolicy, (int)IPermissionsPolicyProvider.PermissionPolicy.Ask);
                set
                {
                    if (CodeExecutionPolicy != value)
                    {
                        EditorPrefs.SetInt(k_CodeExecutionPolicy, (int)value);
                        CodeExecutionPolicyChanged?.Invoke(value);
                    }
                }
            }

            /// <summary>
            /// Permission policy for screen capture
            /// </summary>
            public static IPermissionsPolicyProvider.PermissionPolicy ScreenCapturePolicy
            {
                get => (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(k_ScreenCapturePolicy, (int)IPermissionsPolicyProvider.PermissionPolicy.Ask);
                set
                {
                    if (ScreenCapturePolicy != value)
                    {
                        EditorPrefs.SetInt(k_ScreenCapturePolicy, (int)value);
                        ScreenCapturePolicyChanged?.Invoke(value);
                    }
                }
            }

            /// <summary>
            /// Permission policy for asset generation within the project path
            /// </summary>
            public static IPermissionsPolicyProvider.PermissionPolicy AssetGenerationProjectPathPolicy
            {
                get => (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(k_AssetGenerationProjectPathPolicy, (int)IPermissionsPolicyProvider.PermissionPolicy.Ask);
                set
                {
                    if (AssetGenerationProjectPathPolicy != value)
                    {
                        EditorPrefs.SetInt(k_AssetGenerationProjectPathPolicy, (int)value);
                        AssetGenerationProjectPathPolicyChanged?.Invoke(value);
                    }
                }
            }

            /// <summary>
            /// Permission policy for asset generation outside the project path
            /// </summary>
            public static IPermissionsPolicyProvider.PermissionPolicy AssetGenerationOutsideProjectPathPolicy
            {
                get => (IPermissionsPolicyProvider.PermissionPolicy)EditorPrefs.GetInt(k_AssetGenerationOutsideProjectPathPolicy, (int)IPermissionsPolicyProvider.PermissionPolicy.Deny);
                set
                {
                    if (AssetGenerationOutsideProjectPathPolicy != value)
                    {
                        EditorPrefs.SetInt(k_AssetGenerationOutsideProjectPathPolicy, (int)value);
                        AssetGenerationOutsideProjectPathPolicyChanged?.Invoke(value);
                    }
                }
            }
        }
    }
}
