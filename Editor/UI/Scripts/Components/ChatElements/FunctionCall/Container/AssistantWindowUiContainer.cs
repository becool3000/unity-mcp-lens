using System;
using System.Collections.Generic;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine.Pool;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class AssistantWindowUiContainer : IToolUiContainer, IDisposable
    {
        class PendingInteraction
        {
            public ToolExecutionContext.CallInfo CallInfo;
            public VisualElement VisualElement;
            public bool IsPop;

            public PendingInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement visualElement, bool isPop)
            {
                CallInfo = callInfo;
                VisualElement = visualElement;
                IsPop = isPop;
            }
        }

        AssistantView AssistantView { get; set; }

        readonly List<PendingInteraction> m_PendingInteractions = new();
        bool m_IsRegisteredForEvents;

        public AssistantWindowUiContainer(AssistantView assistantView)
        {
            AssistantView = assistantView;
            RegisterForEditorEvents();
        }

        void RegisterForEditorEvents()
        {
            if (m_IsRegisteredForEvents)
                return;

            EditorApplication.update += OnEditorUpdate;
            m_IsRegisteredForEvents = true;
        }

        void UnregisterFromEditorEvents()
        {
            if (!m_IsRegisteredForEvents)
                return;

            EditorApplication.update -= OnEditorUpdate;
            m_IsRegisteredForEvents = false;
        }

        void OnEditorUpdate()
        {
            ProcessPendingInteractions();
        }

        void ProcessPendingInteractions()
        {
            if (m_PendingInteractions.Count == 0)
                return;

            if (AssistantView == null)
            {
                InternalLog.LogWarning($"{nameof(AssistantWindowUiContainer)}: AssistantView is null, cannot process pending interactions");
                return;
            }

            using var toRemovePooled = ListPool<PendingInteraction>.Get(out var toRemove);
            foreach (var pendingInteraction in m_PendingInteractions)
            {
                if (pendingInteraction.IsPop)
                {
                    if (AssistantView.TryPopInteraction(pendingInteraction.CallInfo, pendingInteraction.VisualElement))
                        toRemove.Add(pendingInteraction);
                }
                else
                {
                    if (AssistantView.TryPushInteraction(pendingInteraction.CallInfo, pendingInteraction.VisualElement))
                        toRemove.Add(pendingInteraction);
                }
            }

            foreach (var pendingInteraction in toRemove)
            {
                m_PendingInteractions.Remove(pendingInteraction);
            }
        }

        public void PushElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction)
        {
            if (userInteraction == null)
                return;

            var visualElement = userInteraction as VisualElement;
            if (visualElement == null)
                throw new ArgumentException("userInteraction must be of type VisualElement");

            if (AssistantView == null)
                throw new InvalidOperationException("Invalid UI context. No AssistantView found.");

            m_PendingInteractions.Add(new PendingInteraction(callInfo, visualElement, false));
            ProcessPendingInteractions();
        }

        public void PopElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction)
        {
            if (userInteraction == null)
                return;

            var visualElement = userInteraction as VisualElement;
            if (visualElement == null)
                throw new ArgumentException("userInteraction must be of type VisualElement");

            if (AssistantView == null)
                throw new InvalidOperationException("Invalid UI context. No AssistantView found.");

            m_PendingInteractions.Add(new PendingInteraction(callInfo, visualElement, true));
            ProcessPendingInteractions();
        }

        public void Dispose()
        {
            UnregisterFromEditorEvents();
            m_PendingInteractions.Clear();
        }

        ~AssistantWindowUiContainer()
        {
            Dispose();
        }
    }
}
