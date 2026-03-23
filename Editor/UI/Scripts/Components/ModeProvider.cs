using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Represents a mode option for the mode dropdown.
    /// </summary>
    record ModeInfo(string Id, string Name, string Description);

    /// <summary>
    /// Provides mode data to the UI by listening to IAssistantProvider events.
    /// Syncs with AssistantBlackboard for Unity provider compatibility (tool filtering).
    /// </summary>
    class ModeProvider : IDisposable
    {
        readonly List<ModeInfo> m_Modes = new();
        readonly AssistantBlackboard m_Blackboard;

        IAssistantProvider m_Provider;
        string m_CurrentModeId;
        bool m_Disposed;

        public IReadOnlyList<ModeInfo> AvailableModes => m_Modes;
        public string CurrentModeId => m_CurrentModeId;

        /// <summary>
        /// Fired when available modes change or when the current mode changes.
        /// </summary>
        public event Action<string> ModeChanged;

        public ModeProvider(AssistantBlackboard blackboard)
        {
            m_Blackboard = blackboard;
        }

        /// <summary>
        /// Bind to an IAssistantProvider. Subscribes to its mode events.
        /// </summary>
        public void BindProvider(IAssistantProvider provider)
        {
            // Unbind previous provider
            if (m_Provider != null)
            {
                m_Provider.ModesAvailable -= OnModesAvailable;
                m_Provider.ModeChanged -= OnModeChanged;
            }

            // Clear state
            m_Modes.Clear();
            m_CurrentModeId = null;

            m_Provider = provider;

            if (m_Provider == null)
                return;

            // Subscribe to new provider
            // Note: Unity provider will immediately fire ModesAvailable when we subscribe
            m_Provider.ModesAvailable += OnModesAvailable;
            m_Provider.ModeChanged += OnModeChanged;
        }

        void OnModesAvailable((string id, string name, string desc)[] modes, string currentModeId)
        {
            m_Modes.Clear();
            foreach (var (id, name, desc) in modes)
            {
                m_Modes.Add(new ModeInfo(id, name, desc));
            }

            m_CurrentModeId = currentModeId;
            SyncWithBlackboard(currentModeId);
            ModeChanged?.Invoke(m_CurrentModeId);
        }

        void OnModeChanged(string modeId)
        {
            if (m_CurrentModeId == modeId)
                return;

            m_CurrentModeId = modeId;
            SyncWithBlackboard(modeId);
            ModeChanged?.Invoke(modeId);
        }

        /// <summary>
        /// Request a mode change via the provider.
        /// </summary>
        public async Task SetModeAsync(string modeId)
        {
            if (m_Provider != null)
                await m_Provider.SetModeAsync(modeId);
        }

        /// <summary>
        /// Sync mode with blackboard for Unity provider compatibility.
        /// Maps mode ID to AssistantMode enum for tool filtering.
        /// </summary>
        void SyncWithBlackboard(string modeId)
        {
            if (m_Blackboard == null)
                return;

            // Map mode ID to AssistantMode enum
            // For Unity provider: "Agent" and "Ask" map directly
            // For ACP providers: we default to Agent mode for tool filtering
            if (Enum.TryParse<AssistantMode>(modeId, out var mode))
            {
                m_Blackboard.ActiveMode = mode;
            }
            else
            {
                // ACP providers may have different mode IDs
                // Default to Agent mode for full tool access
                m_Blackboard.ActiveMode = AssistantMode.Agent;
            }
        }

        public void Dispose()
        {
            if (m_Disposed)
                return;
            m_Disposed = true;

            if (m_Provider != null)
            {
                m_Provider.ModesAvailable -= OnModesAvailable;
                m_Provider.ModeChanged -= OnModeChanged;
            }
        }
    }
}
