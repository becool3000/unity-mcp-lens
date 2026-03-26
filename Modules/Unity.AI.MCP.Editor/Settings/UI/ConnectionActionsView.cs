using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.MCP.Editor.Settings.UI
{
    /// <summary>
    /// Reusable component for Accept/Deny action buttons.
    /// Used by both ConnectionApprovalDialog and ConnectionItemControl.
    /// </summary>
    class ConnectionActionsView : VisualElement
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/ConnectionActionsView.uxml";
        static readonly string UssPath = MCPConstants.uiTemplatesPath + "/ConnectionActionsView.uss";

        Button m_DenyButton;
        Button m_AcceptButton;

        public event Action OnAcceptClicked;
        public event Action OnDenyClicked;

        public ConnectionActionsView()
        {
            LoadTemplate();
            InitializeElements();
            SetupEventHandlers();
        }

        void LoadTemplate()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(this);
            }

            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (stylesheet != null)
            {
                styleSheets.Add(stylesheet);
            }
        }

        void InitializeElements()
        {
            m_DenyButton = this.Q<Button>("denyButton");
            m_AcceptButton = this.Q<Button>("acceptButton");
        }

        void SetupEventHandlers()
        {
            m_DenyButton.clicked += () => OnDenyClicked?.Invoke();
            m_AcceptButton.clicked += () => OnAcceptClicked?.Invoke();
        }

        /// <summary>
        /// Focus the deny button.
        /// </summary>
        public void FocusDenyButton()
        {
            m_DenyButton?.Focus();
        }

        /// <summary>
        /// Focus the accept button (default action for accept-by-default policy).
        /// </summary>
        public void FocusAcceptButton()
        {
            m_AcceptButton?.Focus();
        }
    }
}
