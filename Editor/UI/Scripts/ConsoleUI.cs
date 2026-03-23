using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Assistant.Bridge.Editor;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    static class ConsoleUI
    {
        const string k_ToolbarButtonStyleName = "ToolbarButton";

        const string k_AddToAssistantTitle = "Add to Assistant";
        const string k_AddToAssistantButtonTooltip = "Select messages to attach to Assistant so you can troubleshoot with AI";

        static GUIContent s_AskAssistantButtonContent;
        static readonly GUIContent k_AddAssistantContextMenuContent = new(k_AddToAssistantTitle);

        static bool s_StylesInitialized;
        static GUIStyle s_CustomToolbarButtonStyle;

        static readonly List<LogData> k_SelectedLogData = new();
        static readonly List<LogData> k_CurrentLogData = new();

        internal static Action<IEnumerable<LogData>> s_OnLogsAdded;

        [InitializeOnLoadMethod]
        static void SetupConsoleIntegration()
        {
            ConsoleUtils.s_DrawCustomToolbarGuiEvent -= OnDrawCustomGuiEvent;
            ConsoleUtils.s_DrawCustomToolbarGuiEvent += OnDrawCustomGuiEvent;

            ConsoleUtils.s_EntryContextClickedEvent -= OnEntryContextClickedEvent;
            ConsoleUtils.s_EntryContextClickedEvent += OnEntryContextClickedEvent;
        }

        static void OnDrawCustomGuiEvent()
        {
            InitGui();

            var selectedLogCount = ConsoleUtils.GetSelectedConsoleLogCount();

            var prevEnabled = GUI.enabled;
            GUI.enabled = selectedLogCount > 0;

            // Shift to overlap better with the previous dropdown element in internal ImGUI toolbar code
            GUILayout.Space(-1);

            if (GUILayout.Button(s_AskAssistantButtonContent, s_CustomToolbarButtonStyle))
            {
                if (selectedLogCount > 0)
                {
                    AddSelectedLogsToAssistantContext();
                }
                else
                {
                    Debug.LogWarning("No logs selected.");
                }
            }

            GUI.enabled = prevEnabled;
        }

        static void OnEntryContextClickedEvent(LogData logData)
        {
            ConsoleUtils.GetConsoleLogs(k_CurrentLogData);
            var idx = ConsoleUtils.FindLogEntry(k_CurrentLogData, logData);

            if (idx >= 0)
            {
                var menu = new GenericMenu();
                menu.AddItem(k_AddAssistantContextMenuContent, false, () =>
                {
                    AddSelectedLogsToAssistantContext();
                });
                menu.ShowAsContext();
            }
            else
            {
                Debug.Log($"Didn't find entry in our LogData list.");
            }
        }

        static void AddSelectedLogsToAssistantContext()
        {
            ConsoleUtils.GetSelectedConsoleLogs(k_SelectedLogData);
            AssistantWindow.ShowWindow();

            EditorApplication.delayCall += () =>
            {
                s_OnLogsAdded?.Invoke(k_SelectedLogData);
            };
        }

        static void InitGui()
        {
            if (s_StylesInitialized)
                return;

            s_StylesInitialized = true;

            var iconFilename =
                EditorGUIUtility.isProSkin
                    ? "Sparkle.png"
                    : "Sparkle_dark.png";

            var iconPath = Path.Combine(AssistantUIConstants.BasePath, AssistantUIConstants.UIEditorPath,
                AssistantUIConstants.AssetFolder, "icons", iconFilename);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

            s_AskAssistantButtonContent = new(k_AddToAssistantTitle, icon, k_AddToAssistantButtonTooltip);

            var toolbarStyle = new GUIStyle(k_ToolbarButtonStyleName);

            s_CustomToolbarButtonStyle = new GUIStyle(toolbarStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(toolbarStyle.padding.left - 8, toolbarStyle.padding.right - 4, toolbarStyle.padding.top, toolbarStyle.padding.bottom)
            };
        }
    }
}
