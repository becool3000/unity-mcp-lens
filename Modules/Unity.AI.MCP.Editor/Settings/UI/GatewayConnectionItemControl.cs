using System;
using UnityEditor;
using UnityEngine.UIElements;
using GatewayConnectionRecord = Unity.AI.MCP.Editor.GatewayConnectionRecord;

namespace Unity.AI.MCP.Editor.Settings.UI
{
    /// <summary>
    /// UI control for displaying a legacy relay connection.
    /// Shows provider name with a dedicated indicator and auto-approved status.
    /// </summary>
    class GatewayConnectionItemControl : VisualElement
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/GatewayConnectionItemControl.uxml";
        static readonly string UssPath = MCPConstants.uiTemplatesPath + "/GatewayConnectionItemControl.uss";

        readonly GatewayConnectionRecord m_Record;

        // UI Elements
        Label m_ProviderName;
        Label m_Timestamp;
        Label m_StatusLabel;

        public GatewayConnectionItemControl(GatewayConnectionRecord record)
        {
            m_Record = record ?? throw new ArgumentNullException(nameof(record));

            LoadTemplate();
            LoadStyles();
            InitializeElements();
            PopulateData();
        }

        void LoadTemplate()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(this);
            }
        }

        void LoadStyles()
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }
        }

        void InitializeElements()
        {
            m_ProviderName = this.Q<Label>("providerName");
            m_ProviderName.enableRichText = false; // Security: prevent markup injection
            m_Timestamp = this.Q<Label>("timestamp");
            m_StatusLabel = this.Q<Label>("statusLabel");
        }

        void PopulateData()
        {
            // Provider name with legacy relay suffix
            var displayName = GetProviderDisplayName(m_Record.Provider) + " (Legacy Relay)";
            m_ProviderName.text = displayName;

            // Timestamp
            m_Timestamp.text = m_Record.ConnectedAt != default
                ? GetRelativeTime(m_Record.ConnectedAt)
                : "Just now";

            // Status is always "Auto-approved" for legacy relay connections
            m_StatusLabel.text = "Auto-approved";
        }

        /// <summary>
        /// Get display name for a provider type.
        /// Maps agent types like "claude-code" to user-friendly names like "Claude Code".
        /// </summary>
        static string GetProviderDisplayName(string agentType)
        {
            return agentType switch
            {
                "claude-code" => "Claude Code",
                "gemini" => "Gemini",
                "codex" => "Codex",
                "cursor" => "Cursor",
                _ => agentType ?? "Unknown"
            };
        }

        /// <summary>
        /// Get relative time string (e.g., "Just now", "2 min ago").
        /// </summary>
        static string GetRelativeTime(DateTime timestamp)
        {
            var elapsed = DateTime.UtcNow - timestamp;
            if (elapsed.TotalSeconds < 60)
                return "Just now";
            if (elapsed.TotalMinutes < 60)
                return $"{(int)elapsed.TotalMinutes} min ago";
            if (elapsed.TotalHours < 24)
                return $"{(int)elapsed.TotalHours} hr ago";
            return timestamp.ToLocalTime().ToString("MMM d");
        }
    }
}
