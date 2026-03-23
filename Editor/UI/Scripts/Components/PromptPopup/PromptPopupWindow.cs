using UnityEditor;
using UnityEngine;
using System;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Toolkit.Accounts.Services;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.PromptPopup
{
    class PromptPopupWindow : EditorWindow
    {
        PromptPopupView m_PopupView;
        Action m_OnClosed;

        const float k_Width = 500f;
        const float k_EstimatedHeight = 60f;
        const float k_NoPointsHeight = 40f;

        public static void ShowPopup(string startingPrompt, AssistantBlackboard blackboard, Rect parentRect,
            Action<string> onPromptSubmitted = null, Action onClosed = null)
        {
            var popupWindow = CreateInstance<PromptPopupWindow>();
            popupWindow.titleContent = GUIContent.none;
            popupWindow.minSize = new Vector2(k_Width, Account.pointsBalance.LowPoints ? k_NoPointsHeight + k_EstimatedHeight : k_EstimatedHeight);
            popupWindow.maxSize = popupWindow.minSize;

            popupWindow.m_OnClosed = onClosed;

            var contextList = ContextSerializationHelper.BuildPromptSelectionContext(blackboard.ObjectAttachments, blackboard.VirtualAttachments, blackboard.ConsoleAttachments);

            var popupView = new PromptPopupView(contextList.m_ContextList);
            popupView.Initialize(null); // No context required
            popupView.SetPrompt(startingPrompt);
            popupView.InitializeThemeAndStyle();

            if (onPromptSubmitted != null)
                popupView.OnPromptSubmitted += onPromptSubmitted;
            if (onClosed != null)
                popupView.OnCancelled += onClosed;

            popupWindow.rootVisualElement.Add(popupView);

            popupWindow.ShowAsDropDown(parentRect, popupWindow.minSize);
        }

        void OnDisable()
        {
            m_OnClosed?.Invoke();
            m_OnClosed = null;
        }
    }
}
