using System.Collections.Generic;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using UnityEngine.Pool;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementResponse : ChatElementBase
    {
        readonly List<ChatElementResponseSection> k_ResponseSections = new();

        ChatElementFeedback m_Feedback;

        VisualElement m_ResponsesContainer;

        MessageModel Message { get; set; }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ResponsesContainer = view.Q<VisualElement>("responsesContainer");

            m_Feedback = view.Q<ChatElementFeedback>();
            m_Feedback.Initialize(Context);

            // Subscribe to API state changes to hide spinner when API stops working
            RegisterAttachEvents(OnAttachedToPanel, OnDetachedFromPanel);
        }

        /// <summary>
        /// Set the data for this response chat element
        /// </summary>
        /// <param name="message">the message to display</param>
        public override void SetData(MessageModel message)
        {
            Message = message;

            // Partition blocks into sections, where each section ends with a ResponseBlockModel
            using var pooledSections = ListPool<List<IMessageBlockModel>>.Get(out var blockSections);
            PartitionBlocksIntoSections(message.Blocks, blockSections);

            // Create or reuse response sections as needed
            for (int i = 0; i < blockSections.Count; i++)
            {
                ChatElementResponseSection section;

                if (i >= k_ResponseSections.Count)
                {
                    // Create new section
                    section = new ChatElementResponseSection();
                    section.Initialize(Context);
                    k_ResponseSections.Add(section);
                    m_ResponsesContainer.Add(section);
                }
                else
                {
                    section = k_ResponseSections[i];
                }

                section.UpdateData(blockSections[i]);

                // Only the last section can be in progress
                bool isLastSection  = i == blockSections.Count - 1;
                section.SetIsWorkingState(isLastSection && Context.Blackboard.IsAPIWorking && !message.IsComplete);
            }


            m_Feedback.SetData(message);
        }

        public bool TryPushInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement userInteraction)
        {
            if (k_ResponseSections.Count == 0)
                return false;

            for (var i = k_ResponseSections.Count - 1; i >= 0; i--)
            {
                var responseSection = k_ResponseSections[i];
                if (responseSection.TryPushInteraction(callInfo, userInteraction))
                    return true;
            }

            return false;
        }

        public bool TryPopInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement userInteraction)
        {
            if (k_ResponseSections.Count == 0)
                return false;

            for (var i = k_ResponseSections.Count - 1; i >= 0; i--)
            {
                var responseSection = k_ResponseSections[i];
                if (responseSection.TryPopInteraction(callInfo, userInteraction))
                    return true;
            }

            return false;
        }

        void OnAttachedToPanel(AttachToPanelEvent evt)
        {
            if (Context?.API != null)
                Context.API.APIStateChanged += OnAPIStateChanged;
        }

        void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            if (Context?.API != null)
                Context.API.APIStateChanged -= OnAPIStateChanged;
        }

        void OnAPIStateChanged()
        {
            if (Context?.Blackboard?.IsAPIWorking is false && k_ResponseSections.Count > 0)
            {
                var lastSection = k_ResponseSections[^1];
                lastSection.SetIsWorkingState(false);

                // Only call OnConversationCancelled if the message wasn't completed normally
                if (!Message.IsComplete)
                    lastSection.OnConversationCancelled();
            }
        }

        static void PartitionBlocksIntoSections(List<IMessageBlockModel> blocks, List<List<IMessageBlockModel>> outSections)
        {
            using var pooledCurrentSection = ListPool<IMessageBlockModel>.Get(out var currentSection);

            foreach (var block in blocks)
            {
                currentSection.Add(block);

                // When we encounter a ResponseBlockModel, close the current section
                if (block is AnswerBlockModel)
                {
                    // Create a new list with the current section's blocks
                    var section = new List<IMessageBlockModel>(currentSection);
                    outSections.Add(section);
                    currentSection.Clear();
                }
            }

            // If there are remaining blocks that haven't been closed by a ResponseBlockModel, add them as a section
            if (currentSection.Count > 0)
            {
                var section = new List<IMessageBlockModel>(currentSection);
                outSections.Add(section);
            }
        }
    }
}
