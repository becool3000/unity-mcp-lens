using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// A popup container that displays a list of selectable entries.
    /// Configurable to show/hide icons and checkmarks.
    /// </summary>
    class PopupBase : ManagedTemplate
    {
        const string k_FirstItemClass = "mui-first-popup-item";
        const string k_LastItemClass = "mui-last-popup-item";

        readonly bool m_ShowCheckmarks;
        readonly bool m_ShowIcons;

        VisualElement m_EntriesRoot;
        ScrollView m_ScrollView;

        public event Action<PopupItemData> OnItemSelected;

        public PopupBase(bool showCheckmarks, bool showIcons, string popupRootClass)
            : base(AssistantUIConstants.UIModulePath)
        {
            m_ShowCheckmarks = showCheckmarks;
            m_ShowIcons = showIcons;

            AddToClassList("mui-popup-shadow");
            AddToClassList(popupRootClass);
            style.display = DisplayStyle.None;
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_EntriesRoot = view.Q<VisualElement>("entriesRoot");
            m_ScrollView = view.Q<ScrollView>("entriesScrollView");
        }

        /// <summary>
        /// Set the maximum height for the popup's scrollable area.
        /// </summary>
        public void SetMaxHeight(float maxHeight)
        {
            if (m_ScrollView != null)
                m_ScrollView.style.maxHeight = maxHeight;
        }

        public void SetItems(IReadOnlyList<PopupItemData> items, string selectedId)
        {
            m_EntriesRoot.Clear();

            int normalItemIndex = 0;
            int normalItemCount = items.Count(i => i.ItemType == PopupItemType.Normal);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];

                if (item.ItemType == PopupItemType.Separator)
                {
                    var separator = new VisualElement();
                    separator.AddToClassList("mui-popup-separator");
                    m_EntriesRoot.Add(separator);
                    continue;
                }

                var isSelected = item.Id == selectedId;

                var entry = new PopupEntry(item, isSelected, m_ShowCheckmarks, m_ShowIcons);
                entry.Initialize(Context);

                if (normalItemIndex == 0)
                    entry.AddToClassList(k_FirstItemClass);
                if (normalItemIndex == normalItemCount - 1)
                    entry.AddToClassList(k_LastItemClass);

                var captured = item;
                entry.RegisterCallback<ClickEvent>(_ => OnItemSelected?.Invoke(captured));

                m_EntriesRoot.Add(entry);
                normalItemIndex++;
            }
        }
    }
}
