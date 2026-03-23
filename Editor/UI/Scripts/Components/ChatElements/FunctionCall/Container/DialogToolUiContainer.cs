using System;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    class DialogToolUiContainer : IToolUiContainer
    {
        DialogWindow m_DialogWindow;

        public void PushElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction)
        {
            if (userInteraction == null)
                return;

            var visualElement = userInteraction as VisualElement;
            if (visualElement == null)
                throw new ArgumentException("userInteraction must be of type VisualElement");

            if (m_DialogWindow == null)
            {
                m_DialogWindow = ScriptableObject.CreateInstance<DialogWindow>();
                m_DialogWindow.titleContent = new GUIContent("Assistant Dialog");
            }

            m_DialogWindow.SetContent(visualElement);

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
                userInteraction.TaskCompletionSource.SetCanceled();
        }

        public void PopElement<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction)
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
