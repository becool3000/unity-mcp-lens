using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class RecordSessionInteraction : BaseInteraction<SessionProvider.ProfilerSessionInfo>
    {
        public RecordSessionInteraction()
        {
            AddToClassList("record-session-interaction");
            var path = ProfilerUIConstants.UIPath + "/Interactions/RecordSessionInteraction.uss";
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(path));

            // Row 1: Icon + Title
            var headerRow = new VisualElement();
            headerRow.AddToClassList("record-session-interaction__header-row");

            var icon = new VisualElement();
            icon.AddToClassList("record-session-interaction__icon");
            icon.AddToClassList("mui-icon-info");
            headerRow.Add(icon);

            var title = new Label("No Existing Profiler Captures");
            title.AddToClassList("record-session-interaction__title");
            headerRow.Add(title);

            Add(headerRow);

            // Row 2: Description
            var description = new Label("Assistant can analyze existing profiling captures, but there are none available. Record a new capture and prompt again.");
            description.AddToClassList("record-session-interaction__description");
            Add(description);

            // Row 3: Button
            var button = new Button(OnOpenProfilerClicked) { text = "Open Profiler" };
            button.AddToClassList("record-session-interaction__button");
            button.AddToClassList("mui-action-button");
            Add(button);
        }

        void OnOpenProfilerClicked()
        {
            var profilerWindow = EditorWindow.GetWindow<ProfilerWindow>();
            profilerWindow.Show();

            CompleteInteraction(null);
        }
    }
}
