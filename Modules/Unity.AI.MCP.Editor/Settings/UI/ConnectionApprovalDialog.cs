using System;
using System.Threading.Tasks;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.Security;
using Unity.AI.MCP.Editor.Settings;
using Unity.AI.MCP.Editor.Settings.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.MCP.Editor.UI
{
    /// <summary>
    /// Modal dialog for approving/denying incoming MCP connections.
    /// Shows process information and signature details to help user make informed decision.
    /// </summary>
    class ConnectionApprovalDialog : EditorWindow
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/ConnectionApprovalDialog.uxml";
        static readonly string UssPath = MCPConstants.uiTemplatesPath + "/ConnectionApprovalDialog.uss";

        ValidationDecision decision;
        TaskCompletionSource<bool> completionSource;
        bool hasDecided;
        DateTime? tcsCompletedAt;

        ConnectionDetailsView detailsView;
        ConnectionActionsView actionsView;
        ScrollView scrollView;

        const float WindowWidth = 500f;
        const float WindowHeight = 450f;

        /// <summary>
        /// Returns true if the dialog is fully initialized (GUI created and ready).
        /// Useful for tests to ensure dialog is ready before interacting with it.
        /// </summary>
        public bool IsInitialized => decision != null && completionSource != null && detailsView != null;

        /// <summary>
        /// Show the approval dialog for a connection attempt.
        /// Must be called from the main thread.
        /// Returns the dialog instance that was created/shown, or null if dialog was not shown.
        /// </summary>
        public static ConnectionApprovalDialog ShowApprovalDialog(ValidationDecision decision, TaskCompletionSource<bool> completionSource)
        {
            // Don't show dialog if decision already made (connection dropped during scheduling)
            if (completionSource.Task.IsCompleted)
                return null;

            var window = GetWindow<ConnectionApprovalDialog>(true, "Connection Approval Required", true);
            window.decision = decision;
            window.completionSource = completionSource;
            window.hasDecided = false;
            window.tcsCompletedAt = null;

            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = new Vector2(WindowWidth, WindowHeight);

            // Rebuild UI now that fields are set
            window.CreateGUI();

            // Unity automatically centers utility windows - no manual positioning needed
            window.Show();
            window.Focus();

            return window;
        }

        void CreateGUI()
        {
            // Note: CreateGUI is called when window is created, before fields are set
            // We'll rebuild the UI after fields are properly initialized
            if (decision == null || completionSource == null)
            {
                return; // Skip building UI until fields are set
            }

            var root = rootVisualElement;
            root.Clear(); // Clear any existing content

            // Load UXML template
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(root);
            }

            // Load USS stylesheet
            var stylesheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (stylesheet != null)
            {
                root.styleSheets.Add(stylesheet);
            }

            // Query elements
            scrollView = root.Q<ScrollView>("scrollView");
            var actionsContainer = root.Q<VisualElement>("actionsContainer");

            // Create and populate connection details view
            detailsView = new ConnectionDetailsView();
            detailsView.SetConnectionInfo(decision.Connection, decision);
            scrollView.Add(detailsView);

            // Create and add action buttons
            actionsView = new ConnectionActionsView();
            actionsView.OnDenyClicked += () => MakeDecision(false);
            actionsView.OnAcceptClicked += () => MakeDecision(true);
            actionsContainer.Add(actionsView);

            // Focus deny button to make it default (Enter key activates it)
            actionsView.FocusDenyButton();
        }

        void Update()
        {
            // Close dialog when user makes a decision (approved or denied)
            // Don't close on cancellation (connection dropped) - let user decide later
            if (completionSource != null && completionSource.Task.IsCompleted && !hasDecided && !completionSource.Task.IsCanceled)
            {
                if (!tcsCompletedAt.HasValue)
                    tcsCompletedAt = DateTime.Now;
                Close();
            }
        }

        /// <summary>
        /// Make an approval decision and close the dialog.
        /// Public to allow tests to programmatically approve/deny connections.
        /// </summary>
        public void MakeDecision(bool approved)
        {
            if (hasDecided)
                return;

            hasDecided = true;
            completionSource.TrySetResult(approved);
            Close();
        }

        void OnDestroy()
        {
            // If window is closed without decision (e.g., user closes window or dismisses),
            // DON'T complete the TaskCompletionSource - just close the UI.
            // The connection will continue waiting with approval_pending heartbeats.
            // User can approve/deny later via the settings UI.
        }
    }
}
