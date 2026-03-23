using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// A single entry in a popup list. Supports optional icon and checkmark display.
    /// </summary>
    class PopupEntry : ManagedTemplate
    {
        const string k_HoveredClass = "mui-popup-item-hovered";

        readonly PopupItemData m_Data;
        readonly bool m_IsSelected;
        readonly bool m_ShowCheckmark;
        readonly bool m_ShowIcon;

        VisualElement m_Root;
        Image m_Checkmark;
        Image m_Icon;
        Label m_Label;

        public PopupEntry(PopupItemData data, bool isSelected, bool showCheckmark, bool showIcon)
            : base(AssistantUIConstants.UIModulePath)
        {
            m_Data = data;
            m_IsSelected = isSelected;
            m_ShowCheckmark = showCheckmark;
            m_ShowIcon = showIcon;

            RegisterCallback<PointerEnterEvent>(_ => SetHovered(true));
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(false));
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Root = view.Q<VisualElement>("root");
            m_Checkmark = view.Q<Image>("checkmark");
            m_Icon = view.Q<Image>("entryIcon");
            m_Label = view.Q<Label>("entryLabel");

            m_Label.text = m_Data.DisplayText;
            m_Root.tooltip = m_Data.Description ?? "";

            // Configure checkmark visibility
            m_Checkmark.style.display = m_ShowCheckmark ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_ShowCheckmark)
            {
                m_Checkmark.style.visibility = m_IsSelected ? Visibility.Visible : Visibility.Hidden;
            }

            // Configure icon visibility
            m_Icon.style.display = m_ShowIcon ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_ShowIcon)
            {
                m_Icon.image = m_Data.Icon;
            }
        }

        void SetHovered(bool hovered)
        {
            m_Root?.EnableInClassList(k_HoveredClass, hovered);
        }
    }
}
