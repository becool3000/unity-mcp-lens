using System;
using System.Collections.Generic;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class AssistantDropdown : ManagedTemplate
    {
        ScrollView m_ScrollView;
        readonly List<AssistantDropdownEntry> m_Entries = new();

        PopupTracker m_PopupTracker;
        string m_SelectedId;
        VisualElement m_AnchorElement;

        public event Action<string> ItemSelected;

        public AssistantDropdown() : base(AssistantUIConstants.UIModulePath)
        {
            style.display = DisplayStyle.None;
        }

        public void SetItems(IReadOnlyList<AssistantDropdownItemData> items, string selectedId)
        {
            m_SelectedId = selectedId;

            m_ScrollView.Clear();

            foreach (var entry in m_Entries)
            {
                entry.Clicked -= OnEntryClicked;
            }
            m_Entries.Clear();

            foreach (var item in items)
            {
                var entry = new AssistantDropdownEntry();
                entry.Initialize(Context);
                entry.SetData(item.Id, item.DisplayText, item.IconClass);
                entry.SetChecked(item.Id == m_SelectedId);
                entry.Clicked += OnEntryClicked;

                m_Entries.Add(entry);
                m_ScrollView.Add(entry);
            }
        }

        public void SetSelectedId(string id)
        {
            m_SelectedId = id;
            foreach (var entry in m_Entries)
            {
                entry.SetChecked(entry.EntryId == id);
            }
        }

        public void ShowAt(VisualElement button, VisualElement anchor)
        {
            Show();

            m_PopupTracker?.Dispose();
            m_PopupTracker = new PopupTracker(this, button, anchor, autoAlignToAnchor: false);
            m_PopupTracker.Dismiss += HideMenu;

            m_AnchorElement = anchor;
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        public void HideMenu()
        {
            UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            if (m_PopupTracker != null)
            {
                m_PopupTracker.Dismiss -= HideMenu;
                m_PopupTracker.Dispose();
                m_PopupTracker = null;
            }

            Hide();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ScrollView = view.Q<ScrollView>("assistantDropdownScroll");
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            PositionAboveAnchor();
        }

        void PositionAboveAnchor()
        {
            if (m_AnchorElement == null || parent == null)
            {
                return;
            }

            var anchorBounds = m_AnchorElement.worldBound;
            var parentBounds = parent.worldBound;
            var popupHeight = resolvedStyle.height;
            var popupWidth = resolvedStyle.width;

            if (float.IsNaN(popupHeight) || popupHeight <= 0)
            {
                return;
            }
            if (float.IsNaN(popupWidth) || popupWidth <= 0)
            {
                return;
            }

            var left = anchorBounds.xMax - parentBounds.x - popupWidth;
            if (left < 0)
                left = anchorBounds.xMin - parentBounds.x;

            style.left = left;
            style.top = anchorBounds.yMin - parentBounds.y - popupHeight - 4;
        }

        void OnEntryClicked(string id)
        {
            if (id == m_SelectedId)
            {
                HideMenu();
                return;
            }

            m_SelectedId = id;
            foreach (var entry in m_Entries)
            {
                entry.SetChecked(entry.EntryId == id);
            }

            HideMenu();
            ItemSelected?.Invoke(id);
        }
    }
}
