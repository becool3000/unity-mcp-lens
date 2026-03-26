using System;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class DialogToolUiContainer : IToolUiContainer
    {
        readonly UserInteractionQueue m_Queue = new();

        DialogWindow m_DialogWindow;
        UserInteractionBar m_InteractionBar;

        public void PushElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IInteractionSource<TOutput> userInteraction)
        {
            if (userInteraction == null)
            {
                return;
            }

            if (userInteraction is IUserInteraction approval)
            {
                var content = new ApprovalInteractionContent();
                content.SetApprovalData(approval.AllowLabel, approval.DenyLabel, approval.Respond, approval.ShowScope);

                var entry = new UserInteractionEntry
                {
                    Title = approval.Action,
                    Detail = approval.Detail,
                    ContentView = content,
                    OnCancel = userInteraction.CancelInteraction
                };

                m_Queue.Enqueue(entry);
                ShowInteractionBar(userInteraction);
                return;
            }

            var visualElement = userInteraction as VisualElement;
            if (visualElement == null)
            {
                throw new ArgumentException("userInteraction must be of type VisualElement or implement IUserInteraction");
            }

            ShowDialog(visualElement, userInteraction);
        }

        void ShowInteractionBar<TOutput>(IInteractionSource<TOutput> userInteraction)
        {
            if (m_InteractionBar == null)
            {
                m_InteractionBar = new UserInteractionBar(m_Queue);
                m_InteractionBar.Initialize(null);
            }

            ShowDialog(m_InteractionBar, userInteraction);
        }

        void ShowDialog<TOutput>(VisualElement content, IInteractionSource<TOutput> userInteraction)
        {
            if (m_DialogWindow == null)
            {
                m_DialogWindow = ScriptableObject.CreateInstance<DialogWindow>();
                m_DialogWindow.titleContent = new GUIContent("Assistant Dialog");
            }

            m_DialogWindow.SetContent(content);

            // Center the dialog relative to the entire Unity Editor application window
            var editorMainWindowRect = EditorGUIUtility.GetMainWindowPosition();
            var dialogSize = new Vector2(500, 250);

            var centeredPosition = new Rect(
                editorMainWindowRect.x + (editorMainWindowRect.width - dialogSize.x) * 0.5f,
                editorMainWindowRect.y + (editorMainWindowRect.height - dialogSize.y) * 0.5f,
                dialogSize.x,
                dialogSize.y
            );

            m_DialogWindow.position = centeredPosition;

            userInteraction.OnCompleted += Close;
            m_DialogWindow.ShowModalUtility();

            if (!userInteraction.TaskCompletionSource.Task.IsCompleted)
            {
                userInteraction.TaskCompletionSource.SetCanceled();
            }
        }

        public void PopElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IInteractionSource<TOutput> userInteraction)
        {
            if (m_DialogWindow != null)
            {
                m_DialogWindow.Close();
                m_DialogWindow = null;
            }
        }

        void Close<TOutput>(TOutput output)
        {
            m_DialogWindow?.Close();
        }
    }
}
