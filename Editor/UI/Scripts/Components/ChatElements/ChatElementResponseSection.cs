using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class ChatElementResponseSection : ManagedTemplate
    {
        const string k_ReasoningSeparatorClass = "mui-reasoning-separator";
        const string k_ReasoningHiddenClass = "mui-reasoning-hidden";

        readonly List<ChatElementBlock> m_Blocks = new();
        readonly List<ChatElementReasoningSequence> m_ReasoningSequences = new();

        ChatElementReasoningSequence m_CurrentReasoningSequence;
        int m_CurrentSequenceThoughtIndex;

        VisualElement m_ReasoningSection;
        VisualElement m_ReasoningTitle;
        Foldout m_ReasoningFoldout;
        VisualElement m_ReasoningContainer;
        VisualElement m_AnswerContainer;
        VisualElement m_ReasoningLoadingSpinnerContainer;
        LoadingSpinner m_ReasoningLoadingSpinner;
        bool m_IsToggleEnabled;
        bool m_IsWorking = true;

        public ChatElementResponseSection() : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ReasoningSection = view.Q("reasoningContainer");
            m_ReasoningSection.AddToClassList(k_ReasoningHiddenClass);

            m_ReasoningTitle = view.Q("reasoningTitle");
            m_ReasoningTitle.RegisterCallback<ClickEvent>(OnTitleClicked);
            m_ReasoningFoldout = view.Q<Foldout>("reasoningFoldout");
            m_ReasoningFoldout.value = true;

            m_ReasoningFoldout.RegisterValueChangedCallback(OnFoldoutValueChanged);

            var toggle = m_ReasoningFoldout.Q<Toggle>();
            toggle.RegisterCallback<ClickEvent>(evt => evt.StopPropagation(), TrickleDown.TrickleDown);

            m_ReasoningFoldout.SetDisplay(false);
            m_IsToggleEnabled = false;

            m_ReasoningContainer = view.Q("reasoningContent");
            m_AnswerContainer = view.Q("answerContent");

            m_ReasoningLoadingSpinnerContainer = view.Q("reasoningLoadingSpinnerContainer");
            m_ReasoningLoadingSpinner = new LoadingSpinner();
            m_ReasoningLoadingSpinner.style.marginRight = 4;
            m_ReasoningLoadingSpinner.Show();
            m_ReasoningLoadingSpinnerContainer.Add(m_ReasoningLoadingSpinner);
        }

        public void UpdateData(List<IMessageBlockModel> blocks)
        {
            var nonReasoningBlockIndex = 0;
            var sequenceIndex = 0;
            m_CurrentReasoningSequence = null;
            m_CurrentSequenceThoughtIndex = 0;

            foreach (var blockModel in blocks)
            {
                switch (blockModel)
                {
                    case ThoughtBlockModel thoughtModel:
                        AddOrUpdateThought(thoughtModel, ref sequenceIndex);
                        continue;
                    case FunctionCallBlockModel callBlockModel:
                    {
                        var toolId = callBlockModel.Call.FunctionId;
                        var result = callBlockModel.Call.Result;
                        var isEmphasizedSuccess = FunctionCallRendererFactory.IsEmphasized(toolId)
                            && result is { IsDone: true, HasFunctionCallSucceeded: true };

                        if (!isEmphasizedSuccess)
                        {
                            AddOrUpdateFunctionCall(callBlockModel, ref sequenceIndex);
                            continue;
                        }

                        // Emphasized and succeeded: remove from reasoning (if present), promote to regular blocks
                        RemoveFunctionCallFromReasoning(callBlockModel.Call.CallId.ToString());
                        
                        if (!HasUnfinishedFunctionCallInSequence())
                            BreakReasoningSequence();
                        break;
                    }
                    case AnswerBlockModel:
                    case ErrorBlockModel:
                    case AcpPlanBlockModel:
                        BreakReasoningSequence();
                        break;
                }

                // Regular blocks
                ChatElementBlock blockElement;
                if (nonReasoningBlockIndex >= m_Blocks.Count)
                {
                    blockElement = blockModel switch
                    {
                        AnswerBlockModel => new ChatElementBlockAnswer(),
                        FunctionCallBlockModel => new ChatElementBlockFunctionCall(),
                        ErrorBlockModel => new ChatElementBlockError(),
                        AcpToolCallBlockModel => new ChatElementBlockAcpToolCall(),
                        AcpPlanBlockModel => new ChatElementBlockPlan(),
                        _ => throw new ArgumentOutOfRangeException()
                    };

                    blockElement.Initialize(Context);
                    m_Blocks.Add(blockElement);

                    var acpToolCallBlockModel = blockModel as AcpToolCallBlockModel;
                    var isAnswerContainer = blockModel is AnswerBlockModel
                        || blockModel is ErrorBlockModel
                        || blockModel is AcpPlanBlockModel
                        || (acpToolCallBlockModel != null && !acpToolCallBlockModel.IsReasoning);

                    if (isAnswerContainer)
                    {
                        m_AnswerContainer.Add(blockElement);
                        UpdateLoadingSpinner();

                        if (blockModel is AnswerBlockModel)
                            OnResponseBlockCreated();
                    }
                    else
                    {
                        m_ReasoningSection.RemoveFromClassList(k_ReasoningHiddenClass);
                        m_ReasoningContainer.Add(blockElement);
                    }
                }
                else
                {
                    blockElement = m_Blocks[nonReasoningBlockIndex];
                }

                blockElement.SetBlockModel(blockModel);
                nonReasoningBlockIndex++;
            }
        }

        bool HasUnfinishedFunctionCallInSequence()
        {
            return m_CurrentReasoningSequence != null && m_CurrentReasoningSequence.HasPendingFunctionCalls();
        }

        void BreakReasoningSequence()
        {
            m_CurrentReasoningSequence?.Collapse();
            m_CurrentReasoningSequence = null;
            m_CurrentSequenceThoughtIndex = 0;
        }

        void AddOrUpdateThought(ThoughtBlockModel thoughtModel, ref int sequenceIndex)
        {
            EnsureReasoningSequence(ref sequenceIndex);
            if (m_CurrentSequenceThoughtIndex < m_CurrentReasoningSequence.ThoughtCount)
                m_CurrentReasoningSequence.UpdateThought(m_CurrentSequenceThoughtIndex, thoughtModel);
            else
                m_CurrentReasoningSequence.AddThought(thoughtModel);
            m_CurrentSequenceThoughtIndex++;
        }

        void RemoveFunctionCallFromReasoning(string callId)
        {
            foreach (var seq in m_ReasoningSequences)
            {
                if (seq.RemoveFunctionCall(callId))
                    return;
            }
        }

        void AddOrUpdateFunctionCall(FunctionCallBlockModel functionCallModel, ref int sequenceIndex)
        {
            EnsureReasoningSequence(ref sequenceIndex);
            
            var callIdStr = functionCallModel.Call.CallId.ToString();
            var existingBlock = m_CurrentReasoningSequence.GetFunctionCall(callIdStr);
            if (existingBlock != null)
                existingBlock.SetBlockModel(functionCallModel);
            else
                m_CurrentReasoningSequence.AddFunctionCall(functionCallModel);
        }

        void EnsureReasoningSequence(ref int sequenceIndex)
        {
            if (m_CurrentReasoningSequence != null)
                return;

            if (sequenceIndex < m_ReasoningSequences.Count)
            {
                m_CurrentReasoningSequence = m_ReasoningSequences[sequenceIndex];
            }
            else
            {
                m_CurrentReasoningSequence = new ChatElementReasoningSequence();
                m_CurrentReasoningSequence.Initialize(Context);
                m_ReasoningSequences.Add(m_CurrentReasoningSequence);
                m_ReasoningSection.RemoveFromClassList(k_ReasoningHiddenClass);
                m_ReasoningContainer.Add(m_CurrentReasoningSequence);
            }

            m_CurrentSequenceThoughtIndex = 0;
            sequenceIndex++;
        }

        public void SetIsWorkingState(bool isWorking)
        {
            if (m_IsWorking == isWorking)
                return;

            m_IsWorking = isWorking;
            UpdateLoadingSpinner();
        }

        public void OnConversationCancelled()
        {
            foreach (var block in m_Blocks)
                block.OnConversationCancelled();

            foreach (var sequence in m_ReasoningSequences)
            {
                sequence.OnConversationCancelled();
                sequence.Collapse();
            }

            if (m_ReasoningContainer.childCount == 0)
                return;

            if (!m_IsToggleEnabled)
            {
                m_ReasoningFoldout.SetDisplay(true);
                m_IsToggleEnabled = true;
            }
        }

        void UpdateLoadingSpinner()
        {
            // To be visible, this section must be working and no response container
            bool show = m_IsWorking && m_AnswerContainer.childCount == 0;
            if (show)
                m_ReasoningLoadingSpinner.Show();
            else
                m_ReasoningLoadingSpinner.Hide();
        }

        void OnTitleClicked(ClickEvent evt)
        {
            // Only allow toggling if response block has been created
            if (!m_IsToggleEnabled)
                return;

            DisplayReasoning(!m_ReasoningFoldout.value);
        }

        void DisplayReasoning(bool isVisible)
        {
            m_ReasoningFoldout.value = isVisible;
            m_ReasoningContainer.SetDisplay(isVisible);
        }

        void OnFoldoutValueChanged(ChangeEvent<bool> evt)
        {
            if (m_IsToggleEnabled)
                DisplayReasoning(evt.newValue);
        }

        void OnResponseBlockCreated()
        {
            // Only show reasoning UI if there's actual reasoning content
            if (m_ReasoningContainer.childCount == 0)
                return;

            // Show a separator when responses comes in
            m_ReasoningContainer.AddToClassList(k_ReasoningSeparatorClass);

            // Activate the ability to toggle reasoning
            m_ReasoningFoldout.SetDisplay(true);
            m_IsToggleEnabled = true;

            if (AssistantEditorPreferences.CollapseReasoningWhenComplete)
                DisplayReasoning(false);
        }

        public bool TryPushInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement userInteraction)
        {
            // Search emphasized function call blocks
            for (var i = m_Blocks.Count - 1; i >= 0; i--)
            {
                if (m_Blocks[i] is not ChatElementBlockFunctionCall elementBlock)
                    continue;

                if (elementBlock.IsDone)
                    continue;

                if (callInfo.CallId != elementBlock.CallId)
                    continue;

                elementBlock.PushInteraction(userInteraction);
                return true;
            }

            // Search non-emphasized function call blocks in reasoning sequences
            for (var i = m_ReasoningSequences.Count - 1; i >= 0; i--)
            {
                if (m_ReasoningSequences[i].TryPushInteraction(callInfo.CallId, userInteraction))
                    return true;
            }

            return false;
        }

        public bool TryPopInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement userInteraction)
        {
            // Search emphasized function call blocks
            for (var i = m_Blocks.Count - 1; i >= 0; i--)
            {
                if (m_Blocks[i] is not ChatElementBlockFunctionCall elementBlock)
                    continue;

                if (callInfo.CallId != elementBlock.CallId)
                    continue;

                elementBlock.PopInteraction(userInteraction);
                return true;
            }

            // Search non-emphasized function call blocks in reasoning sequences
            for (var i = m_ReasoningSequences.Count - 1; i >= 0; i--)
            {
                if (m_ReasoningSequences[i].TryPopInteraction(callInfo.CallId, userInteraction))
                    return true;
            }

            return false;
        }
    }
}
