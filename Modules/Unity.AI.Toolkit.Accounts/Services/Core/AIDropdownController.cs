using System;
using System.Configuration;
using Unity.AI.Toolkit.Accounts.Components;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Services
{
#if UNITY_6000_3_OR_NEWER
    static class AIDropdownController
    {
        const string key = "HideAIMenu";

        internal static AIDropdownContent dropdownContent;
        internal static Button aiButton;

        [InitializeOnLoadMethod]
        internal static void Init() => AIDropdownConfig.instance.RegisterController(new()
        {
            button = button =>
            {
                aiButton = button;
                AIToolbarButton.Init();
                SetButtonVisibility(EditorPrefs.GetBool(key, false));
                PreferencesUtils.RegisterHideMenuChanged(SetButtonVisibility);
            },
            content = dropdownContent ??= new()
        });

        static void SetButtonVisibility(bool hidden) =>
            aiButton.style.display = hidden ? DisplayStyle.None : DisplayStyle.Flex;

        internal static void Reset()
        {
            dropdownContent = null;
            aiButton = null;
            AIDropdownConfig.instance.RegisterController(null);
        }
    }
#endif
}
