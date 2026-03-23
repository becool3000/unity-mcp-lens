using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Markup.Renderers;
using Unity.AI.Assistant.UI.Editor.Scripts.Preview;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    internal class ChatElementUI : CommandDisplayTemplate
    {
        const string k_ActiveClass = "mui-ui-preview-active";

        UIPreviewSystem m_PreviewSystem;
        UIPreviewContainer m_PreviewContainer;

        // Tab buttons
        Button m_PreviewTab;
        Button m_UxmlTab;
        List<Button> m_UssTabs;

        // Content areas
        VisualElement m_PreviewArea;
        VisualElement m_UxmlArea;
        List<VisualElement> m_UssAreas;
        Label m_PreviewError;

        // Tab header
        VisualElement m_TabHeader;
        VisualElement m_ContentArea;

        // Code blocks
        CodeBlockElement m_UxmlCodeBlock;
        List<CodeBlockElement> m_UssCodeBlocks;

        // Data
        ContentGroup m_UxmlContent;
        ContentGroup[] m_UssContents;
        string[] m_UssCodes;

        public ChatElementUI() : base(AssistantUIConstants.UIModulePath)
        {
            m_PreviewSystem = new UIPreviewSystem();
            m_UssTabs = new List<Button>();
            m_UssAreas = new List<VisualElement>();
            m_UssCodeBlocks = new List<CodeBlockElement>();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            base.InitializeView(view);

            m_PreviewTab = view.Q<Button>("previewTab");
            m_UxmlTab = view.Q<Button>("uxmlTab");
            m_TabHeader = view.Q<VisualElement>("tabHeader");
            m_ContentArea = view.Q<VisualElement>("contentArea");

            m_PreviewArea = view.Q<VisualElement>("previewArea");
            m_UxmlArea = view.Q<VisualElement>("uxmlArea");
            m_PreviewError = view.Q<Label>("previewError");

            // Setup tab events
            m_PreviewTab.clicked += ShowPreviewTab;
            m_UxmlTab.clicked += ShowUxmlTab;

            // Initialize UXML code block
            m_UxmlCodeBlock = new CodeBlockElement();
            m_UxmlCodeBlock.Initialize(Context);
            m_UxmlCodeBlock.SetCodeType(CodeFormat.Uxml);
            m_UxmlCodeBlock.SetActions(copy: true, save: true, select: true, edit: false);
            m_UxmlArea.Add(m_UxmlCodeBlock);
        }

        public override void Display(bool isUpdate = false)
        {
            if (ContentGroups.Count > 0)
            {
                var uxmlContent = new ContentGroup(ContentGroups[0]);
                var ussContents = ExtractUssFromMessage();
                SetUIContent(uxmlContent, ussContents);
            }
        }

        void SetUIContent(ContentGroup uxmlContent, ContentGroup[] ussContents = null)
        {
            m_UxmlContent = uxmlContent;
            m_UssContents = ussContents;
            var ussCodes = new List<string>();

            // Setup UXML code block
            var uxmlFilenameWithExtension = FencedCodeBlockRenderer.ExtractFilename(m_UxmlContent.Arguments);
            uxmlFilenameWithExtension = string.IsNullOrEmpty(uxmlFilenameWithExtension) ? "UXML" : uxmlFilenameWithExtension;
            m_UxmlCodeBlock.SetFilename(uxmlFilenameWithExtension);
            m_UxmlCodeBlock.SetCustomTitle(uxmlFilenameWithExtension);
            m_UxmlTab.text = uxmlFilenameWithExtension;
            m_UxmlCodeBlock.SetCode(m_UxmlContent.Content);

            // Clear existing USS tabs and areas
            ClearUssTabs();

            // Create USS tabs if we have USS content
            if (m_UssContents is {Length: > 0})
            {
                for (var i = 0; i < m_UssContents.Length; i++)
                {
                    CreateUssTab(i, m_UssContents[i]);
                    ussCodes.Add(m_UssContents[i].Content);
                }
            }

            m_UssCodes = ussCodes.ToArray();

            // Generate preview
            GeneratePreview();
        }

        void ClearUssTabs()
        {
            // Remove existing USS tabs
            foreach (var tab in m_UssTabs)
            {
                m_TabHeader.Remove(tab);
            }
            m_UssTabs.Clear();

            // Remove existing USS content areas
            foreach (var area in m_UssAreas)
            {
                m_ContentArea.Remove(area);
            }
            m_UssAreas.Clear();
            m_UssCodeBlocks.Clear();
        }

        void CreateUssTab(int index, ContentGroup ussContent)
        {
            // Create tab button
            var ussTab = new Button();
            ussTab.AddToClassList("mui-ui-preview-tab");
            ussTab.AddToClassList("mui-ui-preview-tab-divider");

            int tabIndex = index;
            ussTab.clicked += () => ShowUssTab(tabIndex);

            m_TabHeader.Add(ussTab);
            m_UssTabs.Add(ussTab);

            // Create content area
            var ussArea = new VisualElement();
            ussArea.name = $"ussArea{index}";
            ussArea.AddToClassList("mui-ui-preview-content");
            m_ContentArea.Add(ussArea);
            m_UssAreas.Add(ussArea);

            // Create and setup USS code block
            var ussCodeBlock = new CodeBlockElement();
            ussCodeBlock.Initialize(Context);
            ussCodeBlock.SetCodeType(CodeFormat.Uss);
            ussCodeBlock.SetActions(copy: true, save: true, select: true, edit: false);
            var ussFilenameWithExtension = FencedCodeBlockRenderer.ExtractFilename(ussContent.Arguments);
            var defaultTitle = m_UssContents.Length > 1 ? $"USS {index + 1}" : "USS";
            ussFilenameWithExtension = string.IsNullOrEmpty(ussFilenameWithExtension) ? defaultTitle : ussFilenameWithExtension;
            ussCodeBlock.SetFilename(ussFilenameWithExtension);
            ussCodeBlock.SetCustomTitle(ussFilenameWithExtension);
            ussTab.text = ussFilenameWithExtension;
            ussCodeBlock.SetCode(ussContent.Content);
            ussArea.Add(ussCodeBlock);
            m_UssCodeBlocks.Add(ussCodeBlock);
        }


        void GeneratePreview()
        {
            try
            {
                // Clear existing preview
                m_PreviewArea.Clear();

                // Create preview container with USS content support
                m_PreviewContainer = m_PreviewSystem.CreatePreviewFromMemory(
                    m_UxmlContent.Content,
                    m_UssCodes
                );

                if (m_PreviewContainer != null)
                {
                    m_PreviewContainer.SetupForDisplay();
                    m_PreviewContainer.AddToClassList("mui-ui-preview-bordered");
                    m_PreviewArea.Add(m_PreviewContainer);
                }
                else
                {
                    // Show error message
                    m_PreviewError.text = "Failed to generate preview";
                    m_PreviewError.style.display = DisplayStyle.Flex;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error generating UI preview: {ex.Message}");
                m_PreviewError.text = $"Preview Error: {ex.Message}";
                m_PreviewError.style.display = DisplayStyle.Flex;
            }
        }

        void ShowPreviewTab()
        {
            // Deactivate all content areas
            m_PreviewArea.RemoveFromClassList(k_ActiveClass);
            m_UxmlArea.RemoveFromClassList(k_ActiveClass);
            foreach (var area in m_UssAreas)
            {
                area.RemoveFromClassList(k_ActiveClass);
            }

            // Deactivate all tabs
            m_PreviewTab.RemoveFromClassList(k_ActiveClass);
            m_UxmlTab.RemoveFromClassList(k_ActiveClass);
            foreach (var tab in m_UssTabs)
            {
                tab.RemoveFromClassList(k_ActiveClass);
            }

            // Activate preview
            m_PreviewArea.AddToClassList(k_ActiveClass);
            m_PreviewTab.AddToClassList(k_ActiveClass);
        }

        void ShowUxmlTab()
        {
            // Deactivate all content areas
            m_PreviewArea.RemoveFromClassList(k_ActiveClass);
            m_UxmlArea.RemoveFromClassList(k_ActiveClass);
            foreach (var area in m_UssAreas)
            {
                area.RemoveFromClassList(k_ActiveClass);
            }

            // Deactivate all tabs
            m_PreviewTab.RemoveFromClassList(k_ActiveClass);
            m_UxmlTab.RemoveFromClassList(k_ActiveClass);
            foreach (var tab in m_UssTabs)
            {
                tab.RemoveFromClassList(k_ActiveClass);
            }

            // Activate UXML
            m_UxmlArea.AddToClassList(k_ActiveClass);
            m_UxmlTab.AddToClassList(k_ActiveClass);
        }

        void ShowUssTab(int index)
        {
            if (index < 0 || index >= m_UssAreas.Count)
                return;

            // Deactivate all content areas
            m_PreviewArea.RemoveFromClassList(k_ActiveClass);
            m_UxmlArea.RemoveFromClassList(k_ActiveClass);
            foreach (var area in m_UssAreas)
            {
                area.RemoveFromClassList(k_ActiveClass);
            }

            // Deactivate all tabs
            m_PreviewTab.RemoveFromClassList(k_ActiveClass);
            m_UxmlTab.RemoveFromClassList(k_ActiveClass);
            foreach (var tab in m_UssTabs)
            {
                tab.RemoveFromClassList(k_ActiveClass);
            }

            // Activate the selected USS tab and area
            m_UssAreas[index].AddToClassList(k_ActiveClass);
            m_UssTabs[index].AddToClassList(k_ActiveClass);
        }

        ContentGroup[] ExtractUssFromMessage()
        {
            if (ExtraContent == null || ExtraContent.Count == 0)
                return null;

            var ussBlocks = new List<ContentGroup>();

            // Check ExtraContent for USS blocks
            foreach (var contentGroup in ExtraContent)
            {
                if (!string.IsNullOrEmpty(contentGroup.Content) && contentGroup.Info == CodeFormat.Uss)
                    ussBlocks.Add(new ContentGroup(contentGroup));
            }

            return ussBlocks.Count > 0 ? ussBlocks.ToArray() : null;
        }
    }
}
