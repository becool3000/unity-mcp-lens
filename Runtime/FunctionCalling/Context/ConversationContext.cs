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

        /// <summary>
        /// Event raised when the conversation connection is closed.
        /// </summary>
        public event Action ConnectionClosed
        {
            add
            {
                lock (m_Lock)
                {
                    m_ConnectionClosedDelegates ??= new List<Action>();
                    m_ConnectionClosedDelegates.Add(value);

                    // Subscribe to workflow on first subscriber
                    if (!m_IsSubscribed)
                    {
                        m_Workflow.OnWorkflowStateChanged += OnWorkflowStateChanged;
                        m_IsSubscribed = true;
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
                    if (m_ConnectionClosedDelegates.Count == 0 && m_IsSubscribed)
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

        public ConversationContext(IChatWorkflow workflow)
        {
            m_Workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
            ConversationId = workflow.ConversationId;
            PersistentStorage = new PersistentStorage(ConversationId);
        }

        void OnWorkflowStateChanged(State state)
        {
            if (state != State.Closed)
                return;

            Action[] delegatesToInvoke;
            bool shouldUnsubscribe;

            // Hold lock only to copy delegates and check state
            lock (m_Lock)
            {
                if (m_ConnectionClosedDelegates == null || m_ConnectionClosedDelegates.Count == 0)
                {
                    shouldUnsubscribe = m_IsSubscribed;
                    delegatesToInvoke = null;
                }
                else
                {
                    delegatesToInvoke = m_ConnectionClosedDelegates.ToArray();
                    shouldUnsubscribe = m_IsSubscribed;
                }

                // Mark as unsubscribed
                if (shouldUnsubscribe)
                    m_IsSubscribed = false;
            }

            if (shouldUnsubscribe)
                m_Workflow.OnWorkflowStateChanged -= OnWorkflowStateChanged;

            if (delegatesToInvoke != null)
            {
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
        }
    }
}
