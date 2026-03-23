using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantSettingsPage : ManagedTemplate
    {
        const string k_WindowsSendPromptText = "Use <b>Ctrl+Enter</b> to send a prompt";
        const string k_OSXSendPromptText = "Use <b>\u2318Return</b> to send a prompt";

        const string k_WindowsSendPromptTooltip = "Instead of using <b>Enter</b> to send a prompt, you can use <b>Ctrl+Enter</b>";
        const string k_OSXSendPromptTooltip = "Instead of using <b>Return</b> to send a prompt, you can use <b>\u2318Return</b>";

        TemplateContainer m_View;

        Toggle m_PromptModifierToggle;
        Toggle m_AutoRunToggle;
        Toggle m_CollapseReasoningToggle;
        Toggle m_EnablePackageAutoUpdateToggle;
        Toggle m_AnnotationPrivacyNoticeToggle;
        ConfigureDefaultPermissionsView m_ConfigureDefaultPermissionsView;
        ConfigureCheckpointSettingsView m_ConfigureCheckpointSettingsView;

        public AssistantSettingsPage() :
            base(AssistantUIConstants.UIModulePath)
        {
            RegisterAttachEvents(OnAttachToPanel, OnDetachFromPanel);
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_View = view;

            LoadStyle(view,
                EditorGUIUtility.isProSkin
                    ? AssistantUIConstants.AssistantSharedStyleDark
                    : AssistantUIConstants.AssistantSharedStyleLight);

            LoadStyle(view, AssistantUIConstants.AssistantBaseStyle, true);

            var label = view.Q<Label>("promptModifierLabel");
            m_PromptModifierToggle = view.Q<Toggle>("promptModifierToggle");

            m_PromptModifierToggle.value = AssistantEditorPreferences.UseModifierKeyToSendPrompt;
            m_PromptModifierToggle.RegisterValueChangedCallback(evt => AssistantEditorPreferences.UseModifierKeyToSendPrompt = evt.newValue);

            label.text = Application.platform == RuntimePlatform.OSXEditor ? k_OSXSendPromptText : k_WindowsSendPromptText;
            label.tooltip = Application.platform == RuntimePlatform.OSXEditor ? k_OSXSendPromptTooltip : k_WindowsSendPromptTooltip;

            m_AutoRunToggle = view.Q<Toggle>("autoRunToggle");
            m_AutoRunToggle.value = AssistantEditorPreferences.AutoRun;
            m_AutoRunToggle.RegisterValueChangedCallback(evt => AssistantEditorPreferences.AutoRun = evt.newValue);

            m_CollapseReasoningToggle = view.Q<Toggle>("collapseReasoningToggle");
            m_CollapseReasoningToggle.value = AssistantEditorPreferences.CollapseReasoningWhenComplete;
            m_CollapseReasoningToggle.RegisterValueChangedCallback(evt => AssistantEditorPreferences.CollapseReasoningWhenComplete = evt.newValue);

            m_EnablePackageAutoUpdateToggle = view.Q<Toggle>("enablePackageAutoUpdateToggle");
            m_EnablePackageAutoUpdateToggle.value = AssistantEditorPreferences.EnablePackageAutoUpdate;
            m_EnablePackageAutoUpdateToggle.RegisterValueChangedCallback(evt => AssistantEditorPreferences.EnablePackageAutoUpdate = evt.newValue);

            m_AnnotationPrivacyNoticeToggle = view.Q<Toggle>("annotationPrivacyNoticeToggle");
            // Invert the value: when toggle is ON, privacy notice should be shown (acknowledged = false)
            m_AnnotationPrivacyNoticeToggle.value = !AssistantEditorPreferences.AnnotationPrivacyNoticeAcknowledged;
            m_AnnotationPrivacyNoticeToggle.RegisterValueChangedCallback(evt => AssistantEditorPreferences.AnnotationPrivacyNoticeAcknowledged = !evt.newValue);

            m_ConfigureDefaultPermissionsView = view.Q<ConfigureDefaultPermissionsView>("configureDefaultPermissionsView");
            m_ConfigureDefaultPermissionsView.Initialize(Context);

            m_ConfigureCheckpointSettingsView = view.Q<ConfigureCheckpointSettingsView>("configureCheckpointSettingsView");
            m_ConfigureCheckpointSettingsView.Initialize(Context);
            
            InitializeViewForAssetKnowledge(view);

            InitializeThemeAndStyle(view);
        }

        void InitializeThemeAndStyle(VisualElement root)
        {
            LoadStyle(root, EditorGUIUtility.isProSkin ? AssistantUIConstants.AssistantSharedStyleDark : AssistantUIConstants.AssistantSharedStyleLight);
            LoadStyle(root, AssistantUIConstants.AssistantBaseStyle, true);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            // Listen to preference changes from other sources (e.g., SettingsPopup)
            AssistantEditorPreferences.UseModifierKeyToSendPromptChanged += OnUseModifierKeyPreferenceChanged;
            AssistantEditorPreferences.AutoRunChanged += OnAutoRunPreferenceChanged;
            AssistantEditorPreferences.CollapseReasoningWhenCompleteChanged += OnCollapseReasoningPreferenceChanged;
            AssistantEditorPreferences.EnablePackageAutoUpdateChanged += OnEnablePackageAutoUpdatePreferenceChanged;
            AssistantEditorPreferences.AnnotationPrivacyNoticeAcknowledgedChanged += OnAnnotationPrivacyNoticeAcknowledgedPreferenceChanged;
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            AssistantEditorPreferences.UseModifierKeyToSendPromptChanged -= OnUseModifierKeyPreferenceChanged;
            AssistantEditorPreferences.AutoRunChanged -= OnAutoRunPreferenceChanged;
            AssistantEditorPreferences.CollapseReasoningWhenCompleteChanged -= OnCollapseReasoningPreferenceChanged;
            AssistantEditorPreferences.EnablePackageAutoUpdateChanged -= OnEnablePackageAutoUpdatePreferenceChanged;
            AssistantEditorPreferences.AnnotationPrivacyNoticeAcknowledgedChanged -= OnAnnotationPrivacyNoticeAcknowledgedPreferenceChanged;
        }

        void OnUseModifierKeyPreferenceChanged(bool newValue) => m_PromptModifierToggle.SetValueWithoutNotify(newValue);

        void OnAutoRunPreferenceChanged(bool newValue) => m_AutoRunToggle.SetValueWithoutNotify(newValue);

        void OnCollapseReasoningPreferenceChanged(bool newValue) => m_CollapseReasoningToggle.SetValueWithoutNotify(newValue);

        void OnEnablePackageAutoUpdatePreferenceChanged(bool newValue) => m_EnablePackageAutoUpdateToggle.SetValueWithoutNotify(newValue);

        void OnAnnotationPrivacyNoticeAcknowledgedPreferenceChanged(bool newValue) => m_AnnotationPrivacyNoticeToggle.SetValueWithoutNotify(!newValue);
    }
}
