using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    static class WhatsNewMenuLink
    {
        const string k_WhatsNewUrl = "https://discussions.unity.com/c/ai-assistant-beta/";

        static void OpenUrl()
        {
            Application.OpenURL(k_WhatsNewUrl);
        }

        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMainMenuExtension(container => container.Add(new AssistantToolbarMenuItem()), 0);

        class AssistantToolbarMenuItem : VisualElement
        {
            public AssistantToolbarMenuItem()
            {
                AddToClassList("label-button");
                AddToClassList("text-menu-item");
                AddToClassList("dropdown-item-with-margin");

                var label = new Label("See What's New");
                label.AddManipulator(new Clickable(OpenUrl));
                Add(label);
            }
        }
    }
}
