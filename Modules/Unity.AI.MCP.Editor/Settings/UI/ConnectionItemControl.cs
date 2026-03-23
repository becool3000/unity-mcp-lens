using System;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.Security;
using Unity.AI.MCP.Editor.Settings.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using ConnectionRecord = Unity.AI.MCP.Editor.ConnectionRecord;

namespace Unity.AI.MCP.Editor.Settings.UI
{
    /// <summary>
    /// UI control for displaying a connection record with expand/collapse functionality.
    /// Shows connection details and allows Accept/Deny/Revoke actions.
    /// </summary>
    class ConnectionItemControl : VisualElement
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/ConnectionItemControl.uxml";

        readonly ConnectionRecord m_Record;
        readonly Action m_OnConnectionChanged;

        // UI Elements - Header
        VisualElement m_StatusIndicator;
        Label m_ConnectionTitle;
        Label m_ConnectionProcessInfo;
        Label m_ConnectionTimestamp;
        Label m_ConnectionStatus;
        Button m_ToggleDetailsButton;
        VisualElement m_ConnectionHeader;

        // UI Elements - Details
        VisualElement m_ConnectionDetails;
        VisualElement m_DetailsContainer;
        ConnectionDetailsView m_DetailsView;

        // Action buttons
        VisualElement m_ActionButtonsContainer;
        ConnectionActionsView m_ActionsView;
        Button m_RevokeButton;

        bool m_IsExpanded = false;

        public ConnectionItemControl(ConnectionRecord record, Action onConnectionChanged)
        {
            m_Record = record ?? throw new ArgumentNullException(nameof(record));
            m_OnConnectionChanged = onConnectionChanged;

            LoadTemplate();
            InitializeElements();
            PopulateData();
            SetupEventHandlers();
            UpdateDetailsVisibility();
            UpdateActionButtons();
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
            // Header elements
            m_ConnectionHeader = this.Q<VisualElement>("connectionHeader");
            m_StatusIndicator = this.Q<VisualElement>("statusIndicator");
            m_ConnectionTitle = this.Q<Label>("connectionTitle");
            m_ConnectionTitle.enableRichText = false; // Disable rich text for security - prevents markup injection from client-provided names
            m_ConnectionProcessInfo = this.Q<Label>("connectionProcessInfo");
            m_ConnectionProcessInfo.enableRichText = false; // Disable rich text for security - prevents markup injection from client-provided names
            m_ConnectionTimestamp = this.Q<Label>("connectionTimestamp");
            m_ConnectionStatus = this.Q<Label>("connectionStatus");
            m_ToggleDetailsButton = this.Q<Button>("toggleDetailsButton");

            // Details elements
            m_ConnectionDetails = this.Q<VisualElement>("connectionDetails");
            m_DetailsContainer = this.Q<VisualElement>("detailsContainer");

            // Create and add ConnectionDetailsView
            m_DetailsView = new ConnectionDetailsView();
            m_DetailsView.SetDetailsFoldoutExpanded(false); // Collapsed by default
            m_DetailsContainer.Add(m_DetailsView);

            // Create action buttons container
            m_ActionButtonsContainer = this.Q<VisualElement>("actionButtonsContainer");

            // Create a wrapper with the same styling as ConnectionActionsView
            var actionsWrapper = new VisualElement();
            actionsWrapper.AddToClassList("umcp-connection-actions");
            m_ActionButtonsContainer.Add(actionsWrapper);

            // Add ConnectionActionsView
            m_ActionsView = new ConnectionActionsView();
            actionsWrapper.Add(m_ActionsView);

            // Add Revoke button to the same row
            m_RevokeButton = new Button();
            m_RevokeButton.text = "Revoke";
            m_RevokeButton.AddToClassList("umcp-action-button");
            actionsWrapper.Add(m_RevokeButton);
        }

        void PopulateData()
        {
            var info = m_Record.Info;
            if (info == null)
                return;

            // Header data - use identity-aware display name with separate title and process info
            var (title, processInfo) = m_Record.GetIdentityDisplayParts();
            m_ConnectionTitle.text = title;
            m_ConnectionProcessInfo.text = processInfo ?? string.Empty;
            m_ConnectionProcessInfo.style.display = string.IsNullOrEmpty(processInfo) ? DisplayStyle.None : DisplayStyle.Flex;

            m_ConnectionTimestamp.text = UIUtils.FormatRelativeTime(info.Timestamp);
            m_ConnectionStatus.text = m_Record.GetStatusDescription();

            // Update status indicator
            UpdateStatusIndicator();

            // Populate connection details view with validation decision
            var decision = new ValidationDecision
            {
                Status = m_Record.Status,
                Reason = m_Record.ValidationReason,
                Connection = info
            };
            m_DetailsView.SetConnectionInfo(info, decision);
        }

        void UpdateStatusIndicator()
        {
            m_StatusIndicator.ClearClassList();
            m_StatusIndicator.AddToClassList("umcp-status-indicator");

            switch (m_Record.Status)
            {
                case ValidationStatus.Pending:
                    m_StatusIndicator.AddToClassList("yellow");
                    break;
                case ValidationStatus.Accepted:
                case ValidationStatus.Warning:
                    m_StatusIndicator.AddToClassList("green");
                    break;
                case ValidationStatus.Rejected:
                case ValidationStatus.CapacityLimit:
                    m_StatusIndicator.AddToClassList("red");
                    break;
            }
        }

        void SetupEventHandlers()
        {
            m_ToggleDetailsButton.clicked += ToggleDetails;
            m_ConnectionHeader.RegisterCallback<ClickEvent>(OnHeaderClicked);

            m_ActionsView.OnAcceptClicked += OnAccept;
            m_ActionsView.OnDenyClicked += OnDeny;
            m_RevokeButton.clicked += OnRevoke;

            // Add context menu for remove option using ContextualMenuManipulator
            m_ConnectionHeader.AddManipulator(new ContextualMenuManipulator(BuildContextMenu));
        }

        void ToggleDetails()
        {
            m_IsExpanded = !m_IsExpanded;
            UpdateDetailsVisibility();
        }

        void OnHeaderClicked(ClickEvent evt)
        {
            // Toggle details when clicking anywhere in the header except the toggle button
            if (evt.target != m_ToggleDetailsButton)
            {
                ToggleDetails();
            }
        }

        void UpdateDetailsVisibility()
        {
            if (m_IsExpanded)
            {
                m_ConnectionDetails.style.display = DisplayStyle.Flex;
                m_ToggleDetailsButton.text = "▼";
                AddToClassList("expanded");
            }
            else
            {
                m_ConnectionDetails.style.display = DisplayStyle.None;
                m_ToggleDetailsButton.text = "▶";
                RemoveFromClassList("expanded");
            }
        }

        void UpdateActionButtons()
        {
            bool isPending = m_Record.Status == ValidationStatus.Pending;
            bool isAccepted = m_Record.Status == ValidationStatus.Accepted || m_Record.Status == ValidationStatus.Warning;
            bool isRejected = m_Record.Status == ValidationStatus.Rejected;
            bool isCapacityLimit = m_Record.Status == ValidationStatus.CapacityLimit;

            // Pending/Rejected/CapacityLimit: Show Accept to allow (re-)approval
            m_ActionsView.style.display = (isPending || isRejected || isCapacityLimit) ? DisplayStyle.Flex : DisplayStyle.None;

            // Accepted: Show Revoke
            m_RevokeButton.SetDisplay(isAccepted);
        }

        void OnAccept()
        {
            if (m_Record.Info?.ConnectionId == null)
                return;

            // Signal any pending approval for this identity
            if (m_Record.Identity != null)
            {
                Bridge.CompletePendingApproval(m_Record.Identity.CombinedIdentityKey, approved: true);
            }

            // Clear DialogShown flag so next connection attempt will be allowed through
            ConnectionRegistry.instance.ClearDialogShown(m_Record.Info.ConnectionId);

            ConnectionRegistry.instance.UpdateConnectionStatus(
                m_Record.Info.ConnectionId,
                ValidationStatus.Accepted,
                "Approved by user from settings"
            );

            m_OnConnectionChanged?.Invoke();
        }

        void OnDeny()
        {
            if (m_Record.Info?.ConnectionId == null)
                return;

            // Signal any pending approval for this identity
            if (m_Record.Identity != null)
            {
                Bridge.CompletePendingApproval(m_Record.Identity.CombinedIdentityKey, approved: false);
            }

            // Disconnect any active connections for this identity
            // This handles the case where user clicks Deny after Accept but before UI updates to show Revoke
            if (m_Record.Identity != null)
            {
                UnityMCPBridge.DisconnectConnectionByIdentity(m_Record.Identity);
            }

            ConnectionRegistry.instance.UpdateConnectionStatus(
                m_Record.Info.ConnectionId,
                ValidationStatus.Rejected,
                "Denied by user from settings"
            );

            m_OnConnectionChanged?.Invoke();
        }

        void OnRevoke()
        {
            if (m_Record.Info?.ConnectionId == null)
                return;

            // Signal any pending approval for this identity with denial
            // This ensures the waiting connection (if any) receives proper denial
            if (m_Record.Identity != null)
            {
                Bridge.CompletePendingApproval(m_Record.Identity.CombinedIdentityKey, approved: false);
            }

            // Update status to rejected
            ConnectionRegistry.instance.UpdateConnectionStatus(
                m_Record.Info.ConnectionId,
                ValidationStatus.Rejected,
                "Revoked by user from settings"
            );

            // Disconnect any active connections with this identity
            if (m_Record.Identity != null)
            {
                UnityMCPBridge.DisconnectConnectionByIdentity(m_Record.Identity);
            }

            m_OnConnectionChanged?.Invoke();
        }

        void OnRemove()
        {
            if (m_Record.Info?.ConnectionId == null)
                return;

            // Confirm removal
            if (EditorUtility.DisplayDialog(
                "Remove Connection",
                $"Remove connection from '{m_Record.Info.DisplayName}'?\n\nThis will forget the connection and reset approval status. The connection will appear as new if it attempts to connect again.",
                "Remove",
                "Cancel"))
            {
                // Disconnect any active connections first
                if (m_Record.Identity != null)
                {
                    UnityMCPBridge.DisconnectConnectionByIdentity(m_Record.Identity);
                }

                // Remove from history
                ConnectionRegistry.instance.RemoveConnection(m_Record.Info.ConnectionId);

                m_OnConnectionChanged?.Invoke();
            }
        }

        void OnRemoveAll()
        {
            // Confirm removal of all connections
            if (EditorUtility.DisplayDialog(
                "Remove All Connections",
                "Remove all connections?\n\nThis will forget all connections and reset their approval status. Connections will appear as new if they attempt to connect again.",
                "Remove All",
                "Cancel"))
            {
                // Disconnect all active connections first
                UnityMCPBridge.DisconnectAll();

                // Remove all from history
                ConnectionRegistry.instance.ClearAllConnections();

                m_OnConnectionChanged?.Invoke();
            }
        }

        void BuildContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Remove Connection", _ => OnRemove(), DropdownMenuAction.AlwaysEnabled);
            evt.menu.AppendAction("Remove All Connections", _ => OnRemoveAll(), DropdownMenuAction.AlwaysEnabled);
        }
    }
}

