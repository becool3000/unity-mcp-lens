using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.MCP.Editor.Settings;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Settings.UI
{
    class ToolItemControl : VisualElement
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/ToolItemControl.uxml";

        Toggle m_Checkbox;
        Label m_NameLabel;
        Label m_BadgeLabel;
        Label m_DescriptionLabel;
        Button m_ShowDetailsButton;
        VisualElement m_DetailsContainer;
        bool m_DetailsVisible;

        public ToolSettingsEntry Entry { get; private set; }

        public ToolItemControl(ToolSettingsEntry entry)
        {
            Entry = entry;
            LoadTemplate();
            InitializeElements();
            SetupTool();
        }

        void LoadTemplate()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(this);
            }
        }

        void InitializeElements()
        {
            m_Checkbox = this.Q<Toggle>("toolItemCheckbox");
            m_NameLabel = this.Q<Label>("toolItemName");
            m_BadgeLabel = this.Q<Label>("toolItemBadge");
            m_DescriptionLabel = this.Q<Label>("toolItemDescription");
            m_ShowDetailsButton = this.Q<Button>("showDetailsButton");
            m_DetailsContainer = this.Q<VisualElement>("toolItemDetails");
        }

        void SetupTool()
        {
            m_NameLabel.text = Entry.Info.name;
            m_DescriptionLabel.text = Entry.Info.description ?? "";
            m_Checkbox.value = Entry.IsEnabled;

            if (Entry.IsDefault)
            {
                m_BadgeLabel.text = "Default";
            }
            else
            {
                m_BadgeLabel.style.display = DisplayStyle.None;
            }

            m_Checkbox.RegisterValueChangedCallback(evt => {
                UpdateToolState(evt.newValue);
            });

            m_ShowDetailsButton.RegisterCallback<ClickEvent>(OnShowDetailsClicked);

            var header = this.Q<VisualElement>("toolItemHeader");
            header.RegisterCallback<ClickEvent>(OnHeaderClicked);

            UpdateDetailsVisibility();
        }

        void OnShowDetailsClicked(ClickEvent evt)
        {
            m_DetailsVisible = !m_DetailsVisible;
            UpdateDetailsVisibility();
            evt.StopPropagation();
        }

        void OnHeaderClicked(ClickEvent evt)
        {
            // Only toggle checkbox if clicking on the header but not on checkbox or button
            if (evt.target != m_Checkbox && evt.target != m_ShowDetailsButton)
            {
                m_Checkbox.value = !m_Checkbox.value;
            }
            evt.StopPropagation();
        }

        void UpdateDetailsVisibility()
        {
            if (m_DetailsVisible)
            {
                m_DetailsContainer.style.display = DisplayStyle.Flex;
                m_ShowDetailsButton.text = "Hide Details";
            }
            else
            {
                m_DetailsContainer.style.display = DisplayStyle.None;
                m_ShowDetailsButton.text = "Show Details";
            }
        }

        void UpdateToolState(bool enabled)
        {
            var settings = MCPSettingsManager.Settings;
            settings.SetToolEnabled(Entry.Info.name, enabled);

            MCPSettingsManager.MarkDirty();
        }
    }
}