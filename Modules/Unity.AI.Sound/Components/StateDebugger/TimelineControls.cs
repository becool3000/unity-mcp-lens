using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class TimelineControls : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/StateDebugger/TimelineControls.uxml";

        public Action<int> OnSetTime;
        public int Time { get; set; }       // Time step as a 0-based index.

        SliderInt m_Slider;
        IntegerField m_Current;
        IntegerField m_Max;
        Button m_Previous;
        Button m_Next;

        public TimelineControls()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<SliderInt>("time");
            m_Current = this.Q<IntegerField>("current");
            m_Max = this.Q<IntegerField>("max");
            m_Previous = this.Q<Button>("previous");
            m_Next = this.Q<Button>("next");
            m_Max.SetEnabled(false);
            this.Q<Button>("previous").clicked += () => SetTime(Math.Max(Time - 1, 0));
            this.Q<Button>("next").clicked += () => SetTime(Math.Min(Time + 1, m_Max.value));

            m_Slider.RegisterValueChangedCallback(evt => SetTime(evt.newValue));
        }

        void SetTime(int at)
        {
            SetTimeWithoutNotify(at);
            OnSetTime?.Invoke(at);
        }

        public void SetTimeWithoutNotify(int target)
        {
            var at = Math.Max(0, Math.Min(target, m_Max.value));
            Time = at;
            m_Slider.SetValueWithoutNotify(Time);
            m_Current.SetValueWithoutNotify(Time);

            m_Next.SetEnabled(Time < m_Max.value);
            m_Previous.SetEnabled(Time > 0);
        }

        public void SetMaxRange(int max)
        {
            m_Slider.highValue = max;
            m_Max.value = max;
        }
    }
}
