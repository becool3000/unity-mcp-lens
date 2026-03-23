using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class PickSessionInteraction : BaseInteraction<SessionProvider.ProfilerSessionInfo>
    {
        const string k_AnalyzeLabel = "Analyze";

        public PickSessionInteraction(List<SessionProvider.ProfilerSessionInfo> profilingSessions)
        {
            AddToClassList("pick-session-interaction");
            var path = ProfilerUIConstants.UIPath + "/Interactions/PickSessionInteraction.uss";
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(path));

            // Add header row
            var headerRow = new VisualElement();
            headerRow.AddToClassList("pick-session-interaction__header-row");

            var headerTitle = new Label("Select a Capture to Analyze");
            headerTitle.AddToClassList("pick-session-interaction__header-title");
            headerRow.Add(headerTitle);

            var openProfilerButton = new Button(OnOpenProfilerClicked) { text = "Open Profiler" };
            openProfilerButton.AddToClassList("pick-session-interaction__open-profiler-button");
            openProfilerButton.AddToClassList("mui-action-button");
            headerRow.Add(openProfilerButton);

            Add(headerRow);

            // Add session rows
            for (var i = 0; i < profilingSessions.Count; i++)
            {
                var session = profilingSessions[i];
                var row = CreateRow(session);
                row.AddToClassList("pick-session-separator");
                Add(row);
            }
        }

        void OnOpenProfilerClicked()
        {
            var profilerWindow = EditorWindow.GetWindow<ProfilerWindow>();
            profilerWindow.Show();
            CompleteInteraction(null);
        }

        VisualElement CreateRow(SessionProvider.ProfilerSessionInfo info)
        {
            var row = new VisualElement();
            row.AddToClassList("pick-session-interaction__row");

            var left = new VisualElement();
            left.AddToClassList("pick-session-interaction__row-left");

            var label = new Label($"{info.FileName}");
            label.AddToClassList("pick-session-interaction__label");

            left.Add(label);

            var button = new Button(() => CompleteInteraction(info))
            {
                text = k_AnalyzeLabel
            };
            button.AddToClassList("pick-session-interaction__analyze-button");
            button.AddToClassList("mui-action-button");

            row.Add(left);
            row.Add(button);

            return row;
        }
    }
}
