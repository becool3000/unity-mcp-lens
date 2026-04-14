using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Socket.Workflows.Chat;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Context for the current conversation.
    /// </summary>
    class ConversationContext
    {
        /// <summary>
        /// The ID of the conversation.
        /// </summary>
        public string ConversationId { get; }
        
        /// <summary>
        /// A persistent storage for this conversation
        /// </summary>
        public PersistentStorage PersistentStorage { get; }

        internal bool IsSynthetic { get; }
        internal bool RequiresExplicitClose { get; }

        /// <summary>
        /// Event raised when the conversation connection is closed.
        /// </summary>
        public event Action ConnectionClosed
        {
            add
            {
                bool invokeImmediately = false;

                lock (m_Lock)
                {
                    if (m_IsClosed)
                        invokeImmediately = true;

                    if (!invokeImmediately)
                    {
                        m_ConnectionClosedDelegates ??= new List<Action>();
                        m_ConnectionClosedDelegates.Add(value);

                        // Subscribe to workflow on first subscriber
                        if (m_Workflow != null && !m_IsSubscribed)
                        {
                            m_Workflow.OnWorkflowStateChanged += OnWorkflowStateChanged;
                            m_IsSubscribed = true;
                        }
                    }
                }

                if (invokeImmediately && value != null)
                {
                    try
                    {
                        value();
                    }
                    catch (Exception ex)
                    {
                        UnityEngine.Debug.LogException(ex);
                    }
                }
            }

            remove
            {
                lock (m_Lock)
                {
                    if (m_ConnectionClosedDelegates == null)
                        return;

                    m_ConnectionClosedDelegates.Remove(value);

                    // Unsubscribe from workflow when last subscriber removed
                    if (m_Workflow != null && m_ConnectionClosedDelegates.Count == 0 && m_IsSubscribed)
                    {
                        m_Workflow.OnWorkflowStateChanged -= OnWorkflowStateChanged;
                        m_IsSubscribed = false;
                    }
                }
            }
        }

        readonly IChatWorkflow m_Workflow;
        readonly object m_Lock = new object();
        List<Action> m_ConnectionClosedDelegates;
        bool m_IsSubscribed;
        bool m_IsClosed;

        public ConversationContext(IChatWorkflow workflow)
            : this(workflow, workflow?.ConversationId, isSynthetic: false, requiresExplicitClose: false)
        {
            if (workflow == null)
                throw new ArgumentNullException(nameof(workflow));
        }

        ConversationContext(IChatWorkflow workflow, string conversationId, bool isSynthetic, bool requiresExplicitClose)
        {
            if (string.IsNullOrEmpty(conversationId))
                throw new ArgumentException("Conversation ID cannot be null or empty", nameof(conversationId));

            m_Workflow = workflow;
            ConversationId = conversationId;
            PersistentStorage = new PersistentStorage(ConversationId);
            IsSynthetic = isSynthetic;
            RequiresExplicitClose = requiresExplicitClose;
        }

        internal static ConversationContext CreateExternal(string conversationId, bool requiresExplicitClose)
        {
            return new ConversationContext(null, conversationId, isSynthetic: true, requiresExplicitClose: requiresExplicitClose);
        }

        internal void Close()
        {
            Action[] delegatesToInvoke;
            IChatWorkflow workflowToUnsubscribe = null;

            lock (m_Lock)
            {
                if (m_IsClosed)
                    return;

                m_IsClosed = true;
                delegatesToInvoke = m_ConnectionClosedDelegates?.ToArray() ?? Array.Empty<Action>();
                m_ConnectionClosedDelegates?.Clear();

                if (m_Workflow != null && m_IsSubscribed)
                {
                    workflowToUnsubscribe = m_Workflow;
                    m_IsSubscribed = false;
                }
            }

            if (workflowToUnsubscribe != null)
                workflowToUnsubscribe.OnWorkflowStateChanged -= OnWorkflowStateChanged;

            foreach (var del in delegatesToInvoke)
            {
                try
                {
                    del();
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        void OnWorkflowStateChanged(State state)
        {
            if (state != State.Closed)
                return;

            Close();
        }
    }
}
