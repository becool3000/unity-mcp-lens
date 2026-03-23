using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// A configurable popup selector component. Button content is defined in UXML as children.
    /// The selector handles popup display, positioning, and click-outside dismissal.
    /// </summary>
    [UxmlElement]
    partial class PopupSelector : ManagedTemplate
    {
        Button m_Button;

        PopupBase m_Popup;
        VisualElement m_PopupRoot;
        bool m_PopupVisible;

        // Configuration
        bool m_ShowIcons;
        bool m_ShowCheckmarks;
        string m_PopupStyleClass;

        // Data
        IReadOnlyList<PopupItemData> m_Items = Array.Empty<PopupItemData>();
        string m_SelectedId;

        /// <summary>
        /// Fired when an item is selected from the popup.
        /// </summary>
        public event Action<PopupItemData> ItemSelected;

        public PopupSelector() : base(AssistantUIConstants.UIModulePath)
        {
        }

        /// <summary>
        /// Configure the popup behavior. Call before Initialize().
        /// </summary>
        /// <param name="showIcons">Whether to show icons in popup items.</param>
        /// <param name="showCheckmarks">Whether to show checkmarks for selected items.</param>
        /// <param name="popupStyleClass">CSS class to apply to the popup root.</param>
        public void Configure(bool showIcons, bool showCheckmarks, string popupStyleClass)
        {
            m_ShowIcons = showIcons;
            m_ShowCheckmarks = showCheckmarks;
            m_PopupStyleClass = popupStyleClass;
        }

        /// <summary>
        /// Set the items to display in the popup.
        /// </summary>
        /// <param name="items">The items to display.</param>
        /// <param name="selectedId">The ID of the currently selected item, or null.</param>
        public void SetItems(IReadOnlyList<PopupItemData> items, string selectedId = null)
        {
            m_Items = items ?? Array.Empty<PopupItemData>();
            m_SelectedId = selectedId;

            if (m_PopupVisible)
            {
                m_Popup.SetItems(m_Items, m_SelectedId);
            }
        }

        /// <summary>
        /// Set the currently selected item ID.
        /// </summary>
        /// <param name="id">The item ID to select.</param>
        public void SetSelectedId(string id)
        {
            m_SelectedId = id;

            if (m_PopupVisible)
            {
                m_Popup.SetItems(m_Items, m_SelectedId);
            }
        }

        /// <summary>
        /// Sets the popup host container. The popup will be added as a child.
        /// </summary>
        public void SetPopupHost(VisualElement popupRoot)
        {
            m_PopupRoot = popupRoot;

            if (m_PopupRoot != null && m_Popup != null && m_Popup.parent == null)
            {
                m_PopupRoot.Add(m_Popup);
            }
        }

        /// <summary>
        /// Enable or disable the selector button.
        /// </summary>
        public new void SetEnabled(bool enabled)
        {
            m_Button?.SetEnabled(enabled);
        }

        /// <summary>
        /// Update visibility of the selector.
        /// When hidden, the element still occupies space to prevent layout shifts.
        /// </summary>
        /// <param name="visible">Whether the selector should be visible.</param>
        /// <param name="preserveSpace">If true, uses visibility (preserves layout space). If false, uses display (collapses space).</param>
        public void UpdateVisibility(bool visible, bool preserveSpace = false)
        {
            if (preserveSpace)
            {
                style.visibility = visible ? Visibility.Visible : Visibility.Hidden;
            }
            else
            {
                style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        protected override void InitializeView(TemplateContainer view)
        {
            // Find the button - it should be a child defined in UXML
            m_Button = this.Q<Button>("selectorButton");

            if (m_Button != null)
            {
                m_Button.clicked += TogglePopup;
            }

            // Create the popup
            m_Popup = new PopupBase(m_ShowCheckmarks, m_ShowIcons, m_PopupStyleClass ?? "mui-popup-root");
            m_Popup.Initialize(Context, autoShowControl: false);
            m_Popup.OnItemSelected += HandleItemSelected;

            // Add popup to host if already set
            if (m_PopupRoot != null && m_Popup.parent == null)
            {
                m_PopupRoot.Add(m_Popup);
            }
        }

        void HandleItemSelected(PopupItemData item)
        {
            HidePopup();
            m_SelectedId = item.Id;
            ItemSelected?.Invoke(item);
        }

        void TogglePopup()
        {
            if (m_PopupVisible)
                HidePopup();
            else
                ShowPopup();
        }

        void ShowPopup()
        {
            if (m_PopupRoot == null || m_Button == null)
                return;

            // Limit popup height to 75% of the panel height
            var panelHeight = m_Popup.panel?.visualTree.worldBound.height ?? 600f;
            var maxHeight = panelHeight * 0.75f;
            m_Popup.SetMaxHeight(maxHeight);

            m_Popup.SetItems(m_Items, m_SelectedId);
            m_Popup.style.display = DisplayStyle.Flex;
            m_PopupVisible = true;

            // Position after layout
            m_Popup.RegisterCallback<GeometryChangedEvent>(OnPopupGeometryChanged);

            // Dismiss on click outside
            m_Popup.panel?.visualTree.RegisterCallback<PointerDownEvent>(OnPointerDownOutside, TrickleDown.TrickleDown);
        }

        void OnPopupGeometryChanged(GeometryChangedEvent evt)
        {
            m_Popup.UnregisterCallback<GeometryChangedEvent>(OnPopupGeometryChanged);
            PositionPopup();
        }

        void PositionPopup()
        {
            var buttonBounds = m_Button.worldBound;
            var parentBounds = m_Popup.parent?.worldBound ?? default;
            var popupWidth = m_Popup.resolvedStyle.width;
            var popupHeight = m_Popup.resolvedStyle.height;

            // If dimensions aren't resolved yet, use fallbacks
            if (float.IsNaN(popupWidth) || popupWidth <= 0)
                popupWidth = 120f;
            if (float.IsNaN(popupHeight) || popupHeight <= 0)
                popupHeight = 100f;

            // Align popup's right edge with button's right edge
            var left = buttonBounds.xMax - parentBounds.x - popupWidth;

            // Position above the button
            var top = buttonBounds.yMin - parentBounds.y - popupHeight - 4;

            m_Popup.style.left = left;
            m_Popup.style.top = top;
        }

        void OnPointerDownOutside(PointerDownEvent evt)
        {
            var target = evt.target as VisualElement;

            // Don't dismiss if clicking the button or inside popup
            if (target != null && (target.FindCommonAncestor(m_Button) == m_Button ||
                                   target.FindCommonAncestor(m_Popup) == m_Popup))
                return;

            HidePopup();
        }

        void HidePopup()
        {
            m_Popup.style.display = DisplayStyle.None;
            m_PopupVisible = false;
            m_Popup.panel?.visualTree.UnregisterCallback<PointerDownEvent>(OnPointerDownOutside, TrickleDown.TrickleDown);
        }
    }
}
