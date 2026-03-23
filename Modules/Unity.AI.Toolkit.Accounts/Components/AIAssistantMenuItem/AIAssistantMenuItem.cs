using System;
using System.Linq;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    class AIAssistantMenuItem : VisualElement
    {
        [InitializeOnLoadMethod]
        static void Init() => DropdownExtension.RegisterMenuExtension(container => container.Add(new AIAssistantMenuItem()));

        static ListRequest s_ListRequest;
        const string k_ComUnityAIAssistant = "com.unity.ai.assistant";
        const string k_AssistantMenuItem = "Window/AI/Assistant";
        const string k_OpenAssistantCommandId = "AI/Open Assistant";
        const string k_AssistantIsDisabledTooltip = "Your organization has disabled the use of Assistant";

        [Shortcut(k_OpenAssistantCommandId, KeyCode.A, ShortcutModifiers.Alt | ShortcutModifiers.Control)]
        public static void OpenAssistant()
        {
            if (s_ListRequest == null)
            {
                s_ListRequest = Client.List();
                EditorApplication.update += ListPackagesInProject;
            }
        }

        static void ListPackagesInProject()
        {
            if (s_ListRequest?.IsCompleted ?? true)
            {
                if (s_ListRequest?.Result.Any(packageInfo => packageInfo.name == k_ComUnityAIAssistant) ?? false)
                    EditorApplication.ExecuteMenuItem(k_AssistantMenuItem);
                else
                    InstallAIAssistant();

                s_ListRequest = null;
                EditorApplication.update -= ListPackagesInProject;
            }
        }

        static void InstallAIAssistant()
        {
            if (EditorUtility.DisplayDialog("Install AI Assistant", "The AI Assistant package is not installed.\n\nDo you want to install it?", "Yes", "No"))
                Client.Add(k_ComUnityAIAssistant);
        }

        readonly Label m_Shortcut;

        public AIAssistantMenuItem()
        {
            AddToClassList("label-button");
            AddToClassList("text-menu-item");
            AddToClassList("text-menu-item-row");

            var label = new Label("Open Assistant");
            label.AddManipulator(new Clickable(OpenAssistant));
            Add(label);

            m_Shortcut = new Label();
            m_Shortcut.AddToClassList("shortcut");
            Add(m_Shortcut);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Account.session.OnChange += Refresh;
                Refresh();
                RefreshShortcut();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Account.session.OnChange -= Refresh;
            });
            Refresh();
        }

        void Refresh()
        {
            SetEnabled(Account.settings.AiAssistantEnabled);
            tooltip = Account.settings.AiAssistantEnabled ? "" : k_AssistantIsDisabledTooltip;
        }

        void RefreshShortcut()
        {
            var binding = ShortcutManager.instance.GetShortcutBinding(k_OpenAssistantCommandId);
            if (binding.keyCombinationSequence.Any())
                m_Shortcut.text = binding.keyCombinationSequence.Select(k => k.ToString()).FirstOrDefault();
            else
                m_Shortcut.text = "";
        }
    }
}
