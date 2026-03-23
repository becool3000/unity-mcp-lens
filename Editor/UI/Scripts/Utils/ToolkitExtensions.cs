using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.ServerCompatibility;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class ToolkitExtensions
    {
        /// <summary>
        /// Helper method to register an element as session tracked, which means it's disable state is based on the session status
        /// </summary>
        /// <param name="element">The element to track</param>
        public static void AddSessionAndCompatibilityStatusManipulators(this VisualElement element, IAssistantProvider assistantProvider, bool enableOnProviderError = false)
        {
            if (!assistantProvider.SessionStatusTrackingEnabled)
                return;

            // NOTE:
            // - Manipulators can not compose right now, so we had to disable the native AI Toolkit tracker
            // - and integrate it into the assistant internal one (former ServerCompatibilityTracker)
            // - We can not use `AssistantSessionStatusTracker` from AI toolkit directly until they can compose together
            element.AddManipulator(new AssistantStatusTracker(enableOnProviderError));
        }

        public static void AddSessionRefreshManipulators(this VisualElement element, IAssistantProvider assistantProvider)
        {
            if (!assistantProvider.SessionStatusTrackingEnabled)
                return;

            element.AddManipulator(new AccountRefreshManipulator());
        }
    }
}
