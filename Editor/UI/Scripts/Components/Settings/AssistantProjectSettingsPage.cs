using System.IO;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class AssistantProjectSettingsPage : ManagedTemplate
    {
        const string k_DefaultCustomInstructionsFileName = "Assets/AssistantCustomInstructions.txt";

        static readonly string k_FileLimitsTooltip =
            $"The guidelines will be limited to {AssistantConstants.UserGuidelineCharacterLimit:N0} characters";

        static readonly string k_CustomInstructionsTooLongWarning =
            $"The custom instructions exceed the limit of {AssistantConstants.UserGuidelineCharacterLimit:N0} characters. Shorten the content for better responses.";

        ObjectField m_CustomInstructionsField;
        VisualElement m_CustomInstructionsTooLongWarning;
        Button m_NewButton;

        public AssistantProjectSettingsPage() :
            base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            LoadStyle(view, EditorGUIUtility.isProSkin
                ? AssistantUIConstants.AssistantSharedStyleDark
                : AssistantUIConstants.AssistantSharedStyleLight);
            LoadStyle(view, AssistantUIConstants.AssistantBaseStyle, true);

            InitializeCustomInstructionsUI(view);

            RegisterAttachEvents(OnAttach, OnDetach);
        }

        void InitializeCustomInstructionsUI(TemplateContainer view)
        {
            m_CustomInstructionsField = view.Q<ObjectField>("customInstructionsField");
            m_CustomInstructionsField.tooltip = k_FileLimitsTooltip;
            m_CustomInstructionsField.allowSceneObjects = false;
            m_CustomInstructionsField.objectType = typeof(TextAsset);
            m_CustomInstructionsField.RegisterValueChangedCallback(OnCustomInstructionsValueChanged);

            m_CustomInstructionsTooLongWarning = view.Q<VisualElement>("customInstructionsTooLongWarningContainer");
            m_CustomInstructionsTooLongWarning.SetDisplay(false);

            var warningText = view.Q<Label>("customInstructionsTooLongWarningText");
            warningText.text = k_CustomInstructionsTooLongWarning;

            m_NewButton = view.SetupButton("newCustomInstructionsButton", OnNewCustomInstructions);

            RefreshCustomInstructions();
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            AssistantProjectPreferences.CustomInstructionsFilePathChanged -= OnCustomInstructionsPathChanged;
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            AssistantProjectPreferences.CustomInstructionsFilePathChanged += OnCustomInstructionsPathChanged;
            RefreshCustomInstructions();
        }

        void OnCustomInstructionsValueChanged(ChangeEvent<Object> evt)
        {
            var asset = evt.newValue as TextAsset;
            AssistantProjectPreferences.CustomInstructionsFilePath = asset != null
                ? AssetDatabase.GetAssetPath(asset)
                : null;
        }

        void RefreshCustomInstructions()
        {
            var customInstructionsPath = AssistantProjectPreferences.CustomInstructionsFilePath;
            var instructions = AssetDatabase.LoadAssetAtPath<TextAsset>(customInstructionsPath);

            m_CustomInstructionsField.SetValueWithoutNotify(instructions);

            // Check if file contents exceed limits
            m_CustomInstructionsTooLongWarning.SetDisplay(
                instructions != null &&
                instructions.text.Length > AssistantConstants.UserGuidelineCharacterLimit);

            m_NewButton.SetDisplay(instructions == null);
        }

        void OnCustomInstructionsPathChanged()
        {
            RefreshCustomInstructions();
        }

        void OnNewCustomInstructions(PointerUpEvent evt)
        {
            var assetPath = AssetDatabase.GenerateUniqueAssetPath(k_DefaultCustomInstructionsFileName);
            File.WriteAllText(assetPath, "");
            AssetDatabase.ImportAsset(assetPath);

            AssistantProjectPreferences.CustomInstructionsFilePath = assetPath;

            // Highlight the newly created asset
            var createdAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            EditorGUIUtility.PingObject(createdAsset);
        }
    }
}
