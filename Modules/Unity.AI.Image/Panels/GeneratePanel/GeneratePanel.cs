using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Panel
{
    [UxmlElement]
    partial class GeneratePanel : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Panels/GeneratePanel/GeneratePanel.uxml";

        readonly ScrollView m_ScrollView;

        IVisualElementScheduledItem m_ScheduledScroll;

        public GeneratePanel()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_ScrollView = this.Q<ScrollView>("generatePanelScrollView");
            this.Use(state => state.SelectPendingPing(this), OnPingPending);
        }

        void OnPingPending(string newReference)
        {
            if (!string.IsNullOrEmpty(newReference))
            {
                var item = m_ScrollView.Q<VisualElement>(newReference);
                if (item != null)
                {
                    // need to schedule the scroll because the splitter will be reset at this current frame.
                    m_ScheduledScroll?.Pause();
                    m_ScheduledScroll = schedule.Execute(() => DelayedScrollTo(item));
                }
                this.Dispatch(GenerationSettingsActions.setPendingPing, string.Empty);
            }
        }

        void DelayedScrollTo(VisualElement item)
        {
            // need to delay the scroll to the next frame to ensure the layout of the scrollview is now correct.
            m_ScrollView.schedule.Execute(() =>
            {
                m_ScrollView.ScrollTo(item);
                item.AddToClassList(PingManipulator.pingUssClassName);
            });
        }
    }
}
