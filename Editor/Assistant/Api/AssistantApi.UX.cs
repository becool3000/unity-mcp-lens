using System;
using Unity.AI.Assistant.UI.Editor.Scripts;
using UnityEngine;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.PromptPopup;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Api
{
    static partial class AssistantApi
    {
        /// <summary>
        /// Shows a popup asking the user to enter a prompt
        /// </summary>
        /// <param name="parentRect">The rect from which the position of the popup will be determined</param>
        /// <param name="onPromptSubmitted">An action performed when the user submit its prompt.</param>
        /// <param name="placeholderPrompt">The default prompt when opening the popup.</param>
        /// <param name="attachedContext">Attached context for the prompt.</param>
        /// <param name="onClosed">An action when the popup is closed without submitting.</param>
        static void ShowAssistantPrompt(Rect parentRect, Action<string> onPromptSubmitted, string placeholderPrompt = "", AttachedContext attachedContext = null, Action onClosed = null)
        {
            if (onPromptSubmitted == null)
                throw new ArgumentNullException(nameof(onPromptSubmitted));

            var blackboard = new AssistantBlackboard();
            blackboard.AttachContext(attachedContext);

            PromptPopupWindow.ShowPopup(placeholderPrompt, blackboard, parentRect, onPromptSubmitted, onClosed);
        }

        static Rect GetScreenRect(this VisualElement element)
        {
            var worldBounds = element.worldBound;
            var worldPos = worldBounds.min;
            var screenPos = GUIUtility.GUIToScreenPoint(worldPos);
            var screenRect = new Rect(screenPos, worldBounds.size);
            return screenRect;
        }
    }
}
