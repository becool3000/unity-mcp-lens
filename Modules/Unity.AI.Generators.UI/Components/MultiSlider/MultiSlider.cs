using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;

namespace Unity.AI.Generators.UI
{
    [UxmlElement]
    partial class MultiSlider : VisualElement, INotifyValueChanged<float[]>
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Generators.UI/Components/MultiSlider/MultiSlider.uxml";
        const string k_SliderControlClass = "aitk-multi-slider__control";
        const string k_SliderRulerClass = "aitk-multi-slider__ruler";
        const string k_SliderContainerClass = "aitk-multi-slider__container";
        const string k_SliderControlNotDraggableClass = "aitk-multi-slider__control--not-draggable";
        const string k_SliderRulerHighlightClass = "aitk-multi-slider__ruler-highlight";

        readonly VisualElement m_Container;
        readonly VisualElement m_Ruler;
        readonly VisualElement m_RulerHighlight;
        readonly List<VisualElement> m_Controls = new();
        readonly Dictionary<int, Draggable> m_ControlManipulators = new();
        float[] m_Values;
        bool[] m_DraggableControls;
        int m_ControlCount = 3; // Default number of controls
        float m_MinimumGap = 0.025f; // Minimum gap between draggable controls (2.5% of slider width)
        float m_HighlightStart = 0f;
        float m_HighlightEnd = 0f;
        Color m_HighlightColor = Color.green; // Default color

        // Events for drag operations
        public event Action<int> controlDragStarted;
        public event Action<int> controlDragEnded;

        [UxmlAttribute("control-count")]
        public int controlCount
        {
            get => m_ControlCount;
            set
            {
                if (m_ControlCount != value && value > 0)
                {
                    m_ControlCount = value;
                    RebuildControls();
                }
            }
        }

        [UxmlAttribute("minimum-gap")]
        public float minimumGap
        {
            get => m_MinimumGap;
            set => m_MinimumGap = Mathf.Clamp01(value);
        }

        [UxmlAttribute("highlight-start")]
        public float highlightStart
        {
            get => m_HighlightStart;
            set
            {
                value = Mathf.Clamp01(value);
                if (!Mathf.Approximately(m_HighlightStart, value))
                {
                    m_HighlightStart = value;
                    UpdateHighlight();
                }
            }
        }

        [UxmlAttribute("highlight-end")]
        public float highlightEnd
        {
            get => m_HighlightEnd;
            set
            {
                value = Mathf.Clamp01(value);
                if (!Mathf.Approximately(m_HighlightEnd, value))
                {
                    m_HighlightEnd = value;
                    UpdateHighlight();
                }
            }
        }

        [UxmlAttribute("highlight-color")]
        public Color highlightColor
        {
            get => m_HighlightColor;
            set
            {
                if (m_HighlightColor != value)
                {
                    m_HighlightColor = value;
                    UpdateHighlightColor();
                }
            }
        }

        public MultiSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Container = this.Q<VisualElement>(className: k_SliderContainerClass);
            m_Ruler = this.Q<VisualElement>(className: k_SliderRulerClass);
            m_RulerHighlight = this.Q<VisualElement>(className: k_SliderRulerHighlightClass);

            // Initialize with default values
            m_Values = new float[m_ControlCount];
            for (var i = 0; i < m_ControlCount; i++)
            {
                m_Values[i] = 0f;
            }

            // Initialize draggable controls (default: only first and last are draggable)
            m_DraggableControls = new bool[m_ControlCount];
            if (m_ControlCount > 0)
            {
                m_DraggableControls[0] = true; // First control
                if (m_ControlCount > 1)
                {
                    m_DraggableControls[m_ControlCount - 1] = true; // Last control
                }
            }

            // Create controls
            RebuildControls();

            // Initialize highlight (hidden by default)
            UpdateHighlight();
            UpdateHighlightColor();
        }

        void RebuildControls()
        {
            // Clear existing controls
            foreach (var control in m_Controls)
            {
                m_Container.Remove(control);
            }
            m_Controls.Clear();

            // Resize values array if needed
            if (m_Values == null || m_Values.Length != m_ControlCount)
            {
                var oldValues = m_Values ?? Array.Empty<float>();
                m_Values = new float[m_ControlCount];

                // Preserve existing values
                for (var i = 0; i < m_ControlCount; i++)
                {
                    m_Values[i] = i < oldValues.Length ? oldValues[i] : 0f;
                }
            }

            // Resize draggable controls array if needed
            if (m_DraggableControls == null || m_DraggableControls.Length != m_ControlCount)
            {
                var oldDraggable = m_DraggableControls ?? Array.Empty<bool>();
                m_DraggableControls = new bool[m_ControlCount];

                // Preserve existing draggable states or set defaults
                if (oldDraggable.Length == 0)
                {
                    // Default: only first and last are draggable
                    if (m_ControlCount > 0)
                    {
                        m_DraggableControls[0] = true; // First control
                        if (m_ControlCount > 1)
                        {
                            m_DraggableControls[m_ControlCount - 1] = true; // Last control
                        }
                    }
                }
                else
                {
                    // Preserve existing draggable states
                    for (var i = 0; i < m_ControlCount; i++)
                    {
                        m_DraggableControls[i] = i < oldDraggable.Length ? oldDraggable[i] : false;
                    }
                }
            }

            // Create new controls
            for (var i = 0; i < m_ControlCount; i++)
            {
                var control = new VisualElement();
                control.AddToClassList(k_SliderControlClass);

                // Store the index in userData, but don't rely on it for drag operations
                control.userData = i;

                // Apply draggable state
                UpdateControlDraggable(control, i);

                m_Container.Add(control);
                m_Controls.Add(control);

                // Position control based on value
                UpdateControlPosition(i);
            }
        }

        void UpdateControlDraggable(VisualElement control, int controlIndex)
        {
            var isDraggable = m_DraggableControls[controlIndex];

            // Clear existing manipulators
            if (m_ControlManipulators.TryGetValue(controlIndex, out var existingManipulator))
            {
                control.RemoveManipulator(existingManipulator);
                m_ControlManipulators.Remove(controlIndex);
            }

            if (isDraggable)
            {
                // Create a local copy of the index to ensure closure captures the correct value
                var capturedControlIndex = controlIndex;

                // Add drag manipulator for draggable controls with proper context
                var manipulator = new Draggable(
                    () => { controlDragStarted?.Invoke(capturedControlIndex); }, // OnDragStart - Trigger event
                    (delta) => OnControlDrag(control, delta, capturedControlIndex), // OnDrag - Pass the control index explicitly
                    () => { controlDragEnded?.Invoke(capturedControlIndex); } // OnDragEnd - Trigger event
                );
                control.AddManipulator(manipulator);
                m_ControlManipulators[controlIndex] = manipulator;
                control.RemoveFromClassList(k_SliderControlNotDraggableClass);
            }
            else
            {
                // Visual indication for non-draggable controls
                control.AddToClassList(k_SliderControlNotDraggableClass);
            }
        }

        void OnControlDrag(VisualElement control, Vector3 delta, int controlIndex)
        {
            var containerWidth = m_Container.layout.width;
            if (containerWidth <= 0) return;

            // Calculate value change based on drag delta
            var valueDelta = delta.x / containerWidth;
            var newValue = Mathf.Clamp01(m_Values[controlIndex] + valueDelta);

            // Check if the new position would violate the minimum gap with any draggable siblings
            newValue = EnforceMinimumGap(controlIndex, newValue);

            // Update individual value without triggering change event
            var oldValue = m_Values[controlIndex];
            m_Values[controlIndex] = newValue;
            UpdateControlPosition(controlIndex);

            // If value changed, notify
            if (!Mathf.Approximately(oldValue, newValue))
            {
                using var evt = ChangeEvent<float[]>.GetPooled(m_Values, m_Values);
                evt.target = this;
                SendEvent(evt);
            }
        }

        float EnforceMinimumGap(int controlIndex, float newValue)
        {
            // Find the closest draggable control to the left based on index, not position
            var leftLimit = 0f;
            for (var i = controlIndex - 1; i >= 0; i--)
            {
                if (m_DraggableControls[i])
                {
                    // Ensure proper ordering by checking if this control has a higher index
                    leftLimit = m_Values[i] + m_MinimumGap;
                    // Never allow a control to go to the left of a control with a lower index
                    if (newValue < m_Values[i])
                    {
                        newValue = m_Values[i] + m_MinimumGap;
                    }
                    break;
                }
            }

            // Find the closest draggable control to the right based on index, not position
            var rightLimit = 1f;
            for (var i = controlIndex + 1; i < m_ControlCount; i++)
            {
                if (m_DraggableControls[i])
                {
                    rightLimit = m_Values[i] - m_MinimumGap;
                    // Never allow a control to go to the right of a control with a higher index
                    if (newValue > m_Values[i])
                    {
                        newValue = m_Values[i] - m_MinimumGap;
                    }
                    break;
                }
            }

            // Clamp the new value between the left and right limits
            return Mathf.Clamp(newValue, leftLimit, rightLimit);
        }

        void UpdateControlPosition(int controlIndex)
        {
            if (controlIndex >= 0 && controlIndex < m_Controls.Count)
            {
                var control = m_Controls[controlIndex];
                var value = m_Values[controlIndex];
                control.style.left = Length.Percent(value * 100);
            }
        }

        public void SetValueWithoutNotify(float[] newValues)
        {
            if (newValues == null || newValues.Length != m_ControlCount)
                return;

            // First, copy all values
            for (var i = 0; i < m_ControlCount; i++)
            {
                m_Values[i] = Mathf.Clamp01(newValues[i]);
            }

            // Then validate to ensure proper ordering of draggable controls
            ValidateControlOrder();

            // Update all control positions
            for (var i = 0; i < m_ControlCount; i++)
            {
                UpdateControlPosition(i);
            }
        }

        // New method to validate and fix control ordering
        void ValidateControlOrder()
        {
            // Find all draggable controls and their indices
            List<(int index, float value)> draggableControls = new List<(int, float)>();
            for (var i = 0; i < m_ControlCount; i++)
            {
                if (m_DraggableControls[i])
                {
                    draggableControls.Add((i, m_Values[i]));
                }
            }

            // Sort by index
            draggableControls.Sort((a, b) => a.index.CompareTo(b.index));

            // Ensure proper ordering by value (lower index must have lower or equal value)
            for (var i = 0; i < draggableControls.Count - 1; i++)
            {
                var current = draggableControls[i];
                var next = draggableControls[i + 1];

                // If they're out of order
                if (current.value > next.value)
                {
                    // Fix by setting the higher index control to be at least minimumGap ahead
                    m_Values[next.index] = m_Values[current.index] + m_MinimumGap;

                    // Ensure it stays within bounds
                    m_Values[next.index] = Mathf.Clamp01(m_Values[next.index]);
                }

                // Ensure minimum gap is respected
                if (next.value - current.value < m_MinimumGap)
                {
                    m_Values[next.index] = m_Values[current.index] + m_MinimumGap;
                    m_Values[next.index] = Mathf.Clamp01(m_Values[next.index]);
                }
            }
        }

        public float[] value
        {
            get => (float[])m_Values.Clone();
            set
            {
                if (value == null || value.Length != m_ControlCount)
                    return;

                var oldValues = (float[])m_Values.Clone();
                SetValueWithoutNotify(value);

                using var evt = ChangeEvent<float[]>.GetPooled(oldValues, m_Values);
                evt.target = this;
                SendEvent(evt);
            }
        }

        public bool[] draggableControls
        {
            get => (bool[])m_DraggableControls.Clone();
            set
            {
                if (value == null || value.Length != m_ControlCount)
                    return;

                m_DraggableControls = (bool[])value.Clone();

                // Update draggable state for all controls
                for (var i = 0; i < m_Controls.Count; i++)
                {
                    UpdateControlDraggable(m_Controls[i], i);
                }
            }
        }

        public void SetDraggable(int controlIndex, bool draggable)
        {
            if (controlIndex >= 0 && controlIndex < m_ControlCount)
            {
                m_DraggableControls[controlIndex] = draggable;
                if (controlIndex < m_Controls.Count)
                {
                    UpdateControlDraggable(m_Controls[controlIndex], controlIndex);
                }
            }
        }

        public void SetHighlightRange(float start, float end)
        {
            start = Mathf.Clamp01(start);
            end = Mathf.Clamp01(end);

            if (start > end)
            {
                // Swap values if start is greater than end
                (start, end) = (end, start);
            }

            if (!Mathf.Approximately(m_HighlightStart, start) || !Mathf.Approximately(m_HighlightEnd, end))
            {
                m_HighlightStart = start;
                m_HighlightEnd = end;
                UpdateHighlight();
            }
        }

        public void ClearHighlight()
        {
            m_HighlightStart = 0f;
            m_HighlightEnd = 0f;
            UpdateHighlight();
        }

        void UpdateHighlight()
        {
            if (m_RulerHighlight != null)
            {
                if (Mathf.Approximately(m_HighlightStart, m_HighlightEnd))
                {
                    // Hide highlight when start equals end
                    m_RulerHighlight.style.width = 0;
                }
                else
                {
                    // Calculate position and width
                    var start = Mathf.Min(m_HighlightStart, m_HighlightEnd);
                    var end = Mathf.Max(m_HighlightStart, m_HighlightEnd);
                    var width = end - start;

                    m_RulerHighlight.style.left = Length.Percent(start * 100);
                    m_RulerHighlight.style.width = Length.Percent(width * 100);
                }
            }
        }

        void UpdateHighlightColor()
        {
            if (m_RulerHighlight != null)
            {
                m_RulerHighlight.style.backgroundColor = new StyleColor(m_HighlightColor);
            }
        }

        // Add method to set tooltip on a specific control
        public void SetControlTooltip(int controlIndex, string tooltip)
        {
            if (controlIndex >= 0 && controlIndex < m_Controls.Count)
            {
                m_Controls[controlIndex].tooltip = tooltip;
            }
        }
    }
}
