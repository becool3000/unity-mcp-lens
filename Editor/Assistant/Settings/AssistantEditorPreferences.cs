using System;
using UnityEditor;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// User preferences shown in Edit -> Preferences...
    /// </summary>
    static partial class AssistantEditorPreferences
    {
        [InitializeOnLoadMethod]
        static void InitAiGatewayEnabledTracking()
        {
            ForceAiGatewayChanged += _ => AiGatewayEnabledChanged?.Invoke();
            Toolkit.Accounts.Services.Account.settings.OnChange += () => AiGatewayEnabledChanged?.Invoke();
        }

        internal const string k_SettingsPrefix = "AIAssistant.";
        const string k_SendPromptModifierKey = k_SettingsPrefix + "SendPromptUseModifierKey";
        const string k_AutoRun = k_SettingsPrefix + "AutoRun";
        const string k_CollapseReasoningWhenComplete = k_SettingsPrefix + "CollapseReasoningWhenComplete";
        const string k_ForceAiGateway = k_SettingsPrefix + "ForceAiGateway";
        const string k_SelectedModelPrefix = k_SettingsPrefix + "SelectedModel.";
        const string k_EnablePackageAutoUpdate = k_SettingsPrefix + "EnablePackageAutoUpdate";
        const string k_ShowPackageUpdateBanner = k_SettingsPrefix + "ShowPackageUpdateBanner";
        const string k_McpToolCallTimeout = k_SettingsPrefix + "McpToolCallTimeout";
        const string k_AnnotationPrivacyNoticeAcknowledged = k_SettingsPrefix + "AnnotationPrivacyNoticeAcknowledged";

        /// <summary>
        /// Default timeout in seconds for MCP tool calls.
        /// </summary>
        public const int DefaultMcpToolCallTimeoutSeconds = 30;

        public static event Action<bool> UseModifierKeyToSendPromptChanged;
        public static event Action<bool> AutoRunChanged;
        public static event Action<bool> CollapseReasoningWhenCompleteChanged;
        public static event Action<bool> ForceAiGatewayChanged;

        /// <summary>
        /// Raised when a source of <see cref="AiGatewayEnabled"/> changes.
        /// Subscribers should read the property to get the current value.
        /// </summary>
        public static event Action AiGatewayEnabledChanged;

        public static event Action<string, string> SelectedModelChanged;
        public static event Action<bool> EnablePackageAutoUpdateChanged;
        public static event Action<bool> ShowPackageUpdateBannerChanged;
        public static event Action<int> McpToolCallTimeoutChanged;
        public static event Action<bool> AnnotationPrivacyNoticeAcknowledgedChanged;

        /// <summary>
        /// When true, pressing enter with modifier key (ex: ctrl) sends the prompt without having to click the button to send the prompt
        /// </summary>
        public static bool UseModifierKeyToSendPrompt
        {
            get => EditorPrefs.GetBool(k_SendPromptModifierKey, false);
            set
            {
                if (UseModifierKeyToSendPrompt != value)
                {
                    EditorPrefs.SetBool(k_SendPromptModifierKey, value);
                    UseModifierKeyToSendPromptChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// When enabled, do not ask the user for permissions.
        /// </summary>
        public static bool AutoRun
        {
            get => EditorPrefs.GetBool(k_AutoRun, false);
            set
            {
                if (AutoRun != value)
                {
                    EditorPrefs.SetBool(k_AutoRun, value);
                    AutoRunChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// If true, when the reasoning is completed, it'll collapse the reasoning section.
        /// </summary>
        public static bool CollapseReasoningWhenComplete
        {
            get => EditorPrefs.GetBool(k_CollapseReasoningWhenComplete, false);
            set
            {
                if (CollapseReasoningWhenComplete != value)
                {
                    EditorPrefs.SetBool(k_CollapseReasoningWhenComplete, value);
                    CollapseReasoningWhenCompleteChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// When enabled, forces the AI Gateway to be shown in the Assistant UI,
        /// regardless of the IsMcpProEnabled setting from the account.
        /// </summary>
        public static bool ForceAiGateway
        {
            get => EditorPrefs.GetBool(k_ForceAiGateway, false);
            set
            {
                if (ForceAiGateway != value)
                {
                    EditorPrefs.SetBool(k_ForceAiGateway, value);
                    ForceAiGatewayChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Returns true if the AI Gateway should be enabled in the Assistant UI.
        /// Controlled by <see cref="k_EnableAiGatewayForMcpProUsers"/>.
        /// </summary>
        public static bool AiGatewayEnabled => ForceAiGateway || Toolkit.Accounts.Services.Account.settings.IsMcpProEnabled;

        /// <summary>
        /// Get the selected model for a provider.
        /// Returns null if no selection has been made (the session's current model will be used).
        /// </summary>
        /// <param name="providerId">The provider ID.</param>
        /// <returns>The selected model ID, or null if not set.</returns>
        public static string GetSelectedModel(string providerId)
        {
            var value = EditorPrefs.GetString(k_SelectedModelPrefix + providerId, null);
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Set the selected model for a provider.
        /// </summary>
        /// <param name="providerId">The provider ID.</param>
        /// <param name="modelId">The model ID to select.</param>
        public static void SetSelectedModel(string providerId, string modelId)
        {
            var current = GetSelectedModel(providerId);
            if (current != modelId)
            {
                EditorPrefs.SetString(k_SelectedModelPrefix + providerId, modelId);
                SelectedModelChanged?.Invoke(providerId, modelId);
            }
        }

        /// <summary>
        /// If true, automatically check for and prompt to update the Assistant package when a new version is available.
        /// </summary>
        public static bool EnablePackageAutoUpdate
        {
            get => EditorPrefs.GetBool(k_EnablePackageAutoUpdate, true);
            set
            {
                if (EnablePackageAutoUpdate != value)
                {
                    EditorPrefs.SetBool(k_EnablePackageAutoUpdate, value);
                    EnablePackageAutoUpdateChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Debug setting to show or hide the package update banner for testing purposes.
        /// </summary>
        public static bool ShowPackageUpdateBanner
        {
            get => EditorPrefs.GetBool(k_ShowPackageUpdateBanner, true);
            set
            {
                if (ShowPackageUpdateBanner != value)
                {
                    EditorPrefs.SetBool(k_ShowPackageUpdateBanner, value);
                    ShowPackageUpdateBannerChanged?.Invoke(value);
                }
            }
        }

        /// <summary>
        /// Timeout in seconds for MCP tool calls. Must be greater than 0.
        /// </summary>
        public static int McpToolCallTimeout
        {
            get => EditorPrefs.GetInt(k_McpToolCallTimeout, DefaultMcpToolCallTimeoutSeconds);
            set
            {
                // Ensure timeout is at least 1 second
                var validValue = value > 0 ? value : DefaultMcpToolCallTimeoutSeconds;
                if (McpToolCallTimeout != validValue)
                {
                    EditorPrefs.SetInt(k_McpToolCallTimeout, validValue);
                    McpToolCallTimeoutChanged?.Invoke(validValue);
                }
            }
        }

        /// <summary>
        /// When true, the annotation privacy notice will not be shown again.
        /// Can be reset via Preferences to re-enable the privacy notice dialog.
        /// </summary>
        public static bool AnnotationPrivacyNoticeAcknowledged
        {
            get => EditorPrefs.GetBool(k_AnnotationPrivacyNoticeAcknowledged, false);
            set
            {
                if (AnnotationPrivacyNoticeAcknowledged != value)
                {
                    EditorPrefs.SetBool(k_AnnotationPrivacyNoticeAcknowledged, value);
                    AnnotationPrivacyNoticeAcknowledgedChanged?.Invoke(value);
                }
            }
        }

        const string k_AiGatewayDisclaimerAcceptedPrefix = k_SettingsPrefix + "AiGatewayDisclaimerAccepted.";
        public static event Action AiGatewayDisclaimerAcceptedChanged;

        /// <summary>
        /// Returns true if the current user has accepted the AI Gateway third-party disclaimer.
        /// Keyed per-user via CloudProjectSettings.userId so different accounts on the same machine
        /// each need to accept independently.
        /// </summary>
        public static bool GetAiGatewayDisclaimerAccepted()
        {
            var userId = CloudProjectSettings.userId;
            if (string.IsNullOrEmpty(userId)) return false;
            return EditorPrefs.GetBool(k_AiGatewayDisclaimerAcceptedPrefix + userId, false);
        }

        /// <summary>
        /// Stores the AI Gateway disclaimer acceptance state for the current user.
        /// </summary>
        public static void SetAiGatewayDisclaimerAccepted(bool value)
        {
            var userId = CloudProjectSettings.userId;
            if (string.IsNullOrEmpty(userId)) return;
            EditorPrefs.SetBool(k_AiGatewayDisclaimerAcceptedPrefix + userId, value);
            AiGatewayDisclaimerAcceptedChanged?.Invoke();
        }
    }
}
