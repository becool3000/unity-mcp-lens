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
    /// Dialog for new MCP connections. Connections are accepted by default;
    /// this dialog informs the user and lets them revoke access if needed.
    /// Shows process information and signature details.
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

            // Non-utility, no focus steal — accept-by-default means this is informational,
            // so it should not interrupt the user's workflow or reclaim focus from their IDE.
            var window = GetWindow<ConnectionApprovalDialog>(false, "New MCP Connection", false);

            // If the window already has a pending approval for a different connection,
            // skip this one to avoid orphaning the existing TCS.
            // The skipped connection stays in AwaitingApproval and the dialog will
            // re-appear on the next tool call attempt from that client.
            if (window.completionSource != null &&
                !window.completionSource.Task.IsCompleted &&
                window.completionSource != completionSource)
            {
                return null;
            }

            window.decision = decision;
            window.completionSource = completionSource;
            window.hasDecided = false;
            window.tcsCompletedAt = null;

            window.minSize = new Vector2(WindowWidth, WindowHeight);

            // Rebuild UI now that fields are set
            window.CreateGUI();

            window.Show();

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

            // Focus accept button — connections are accepted by default
            actionsView.FocusAcceptButton();
        }

        void Update()
        {
            // Close dialog when user makes a decision externally (e.g., from settings panel)
            // Don't close on cancellation (transport disconnected) — user can still
            // approve/deny the identity for future connections from this dialog.
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
            // Tool calls continue working (accept-by-default policy).
            // User can revoke later via the settings UI.
        }
    }
}
