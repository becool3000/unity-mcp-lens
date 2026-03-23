using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Groups consecutive reasoning content (thoughts and function calls) into a collapsible foldout.
    /// Shows all content when expanded, with the foldout title showing the last item's type and title.
    /// </summary>
    class ChatElementReasoningSequence : ManagedTemplate
    {
        readonly List<ChatElementBlockThought> m_ThoughtBlocks = new();
        readonly List<ChatElementBlockFunctionCall> m_FunctionCallBlocks = new();

        Foldout m_Foldout;
        VisualElement m_ContentContainer;

        string m_LastTitle;

        public int ThoughtCount => m_ThoughtBlocks.Count;

        public ChatElementReasoningSequence() : base(AssistantUIConstants.UIModulePath)
        {
            SetResourceName("ChatElementReasoningSequence");
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Foldout = view.Q<Foldout>("reasoningFoldout");
            m_ContentContainer = view.Q("reasoningContent");
            m_Foldout.value = true; // Start expanded
            
            m_Foldout.RegisterValueChangedCallback(OnFoldoutChanged);
            UpdateFoldoutTitle(); // Set initial state (expanded = no title)
        }
        
        void OnFoldoutChanged(ChangeEvent<bool> evt)
        {
            UpdateFoldoutTitle();
        }

        /// <summary>
        /// Adds a thought to the sequence.
        /// </summary>
        public void AddThought(ThoughtBlockModel model)
        {
            var thoughtBlock = new ChatElementBlockThought();
            thoughtBlock.Initialize(Context);
            thoughtBlock.SetBlockModel(model);

            m_ThoughtBlocks.Add(thoughtBlock);
            m_ContentContainer.Add(thoughtBlock);

            // Update foldout title with this thought's title
            var title = ChatElementBlockThought.ExtractTitle(model.Content, out _);
            m_LastTitle = title;
            UpdateFoldoutTitle();
        }

        /// <summary>
        /// Updates an existing thought at the given index.
        /// </summary>
        public void UpdateThought(int index, ThoughtBlockModel model)
        {
            if (index >= 0 && index < m_ThoughtBlocks.Count)
            {
                m_ThoughtBlocks[index].SetBlockModel(model);

                // Always update the last title from the most recent thought
                // This ensures title is available when foldout is collapsed
                var title = ChatElementBlockThought.ExtractTitle(model.Content, out _);
                if (!string.IsNullOrEmpty(title))
                {
                    m_LastTitle = title;
                    UpdateFoldoutTitle();
                }
            }
        }

        /// <summary>
        /// Adds a function call block to the sequence (full interactive element).
        /// </summary>
        public ChatElementBlockFunctionCall AddFunctionCall(FunctionCallBlockModel model)
        {
            var block = new ChatElementBlockFunctionCall();
            block.Initialize(Context);
            block.SetBlockModel(model);

            m_FunctionCallBlocks.Add(block);
            m_ContentContainer.Add(block);

            // Update foldout title - use same display name as default agent tool renderer
            m_LastTitle = model.Call.GetDefaultTitle();
            UpdateFoldoutTitle();

            return block;
        }

        /// <summary>
        /// Gets an existing function call block by ID.
        /// </summary>
        public ChatElementBlockFunctionCall GetFunctionCall(string callId)
        {
            return m_FunctionCallBlocks.FirstOrDefault(b => b.CallId.ToString() == callId);
        }

        /// <summary>
        /// Removes a function call block by call ID from this sequence.
        /// </summary>
        public bool RemoveFunctionCall(string callId)
        {
            var block = m_FunctionCallBlocks.FirstOrDefault(b => b.CallId.ToString() == callId);
            if (block == null) return false;
            m_FunctionCallBlocks.Remove(block);
            block.RemoveFromHierarchy();
            return true;
        }

        void UpdateFoldoutTitle()
        {
            // When expanded, show just "Thoughts"; when collapsed, show full title
            if (m_Foldout.value)
            {
                m_Foldout.text = "Thoughts";
            }
            else
            {
                if (!string.IsNullOrEmpty(m_LastTitle))
                    m_Foldout.text = $"Thoughts - {m_LastTitle}";
                else
                    m_Foldout.text = "Thoughts";
            }
        }

        public bool HasPendingFunctionCalls()
        {
            return m_FunctionCallBlocks.Any(b => !b.IsDone);
        }

        public void Collapse()
        {
            m_Foldout.SetValueWithoutNotify(false);
            UpdateFoldoutTitle();
        }

        public bool TryPushInteraction(Guid callId, VisualElement userInteraction)
        {
            for (var i = m_FunctionCallBlocks.Count - 1; i >= 0; i--)
            {
                var block = m_FunctionCallBlocks[i];
                if (block.IsDone || block.CallId != callId)
                    continue;

                block.PushInteraction(userInteraction);
                return true;
            }

            return false;
        }

        public bool TryPopInteraction(Guid callId, VisualElement userInteraction)
        {
            for (var i = m_FunctionCallBlocks.Count - 1; i >= 0; i--)
            {
                var block = m_FunctionCallBlocks[i];
                if (block.CallId != callId)
                    continue;

                block.PopInteraction(userInteraction);
                return true;
            }

            return false;
        }

        public void OnConversationCancelled()
        {
            foreach (var block in m_ThoughtBlocks)
                block.OnConversationCancelled();
            
            foreach (var block in m_FunctionCallBlocks)
                block.OnConversationCancelled();
        }
    }
}
