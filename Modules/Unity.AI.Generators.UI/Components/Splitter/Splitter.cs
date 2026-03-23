using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    [UxmlElement]
    partial class Splitter : VisualElement, INotifyValueChanged<float>
    {
        float m_Value;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Generators.UI/Components/Splitter/Splitter.uxml";

        const string k_DraggingUssClassName = "aitk-splitter--dragging";

        const string k_VerticalUssClassName = "aitk-splitter--vertical";

        const string k_HorizontalUssClassName = "aitk-splitter--horizontal";

        public VisualElement firstPane { get; set; }

        public VisualElement secondPane { get; set; }

        public VisualElement container { get; set; }

        public bool isFirstPaneFixed { get; set; }

        public bool vertical
        {
            get => ClassListContains(k_VerticalUssClassName);
            set
            {
                EnableInClassList(k_VerticalUssClassName, value);
                EnableInClassList(k_HorizontalUssClassName, !value);
            }
        }

        public Splitter()
        {
            pickingMode = PickingMode.Ignore;
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            var zone = this.Q<VisualElement>("zone");
            zone.AddManipulator(new Draggable(OnDragStart, OnDrag, OnDragEnd));

            vertical = true;
        }

        public void Reset() => value = 0;

        public void Refresh() => SetValueWithoutNotify(value);

        public void SetValueWithoutNotify(float newValue)
        {
            if (firstPane == null || secondPane == null || container == null)
                return;

            var firstMinSize = vertical ? firstPane.resolvedStyle.minHeight.value : firstPane.resolvedStyle.minWidth.value;
            var secondMinSize = vertical ? secondPane.resolvedStyle.minHeight.value : secondPane.resolvedStyle.minWidth.value;
            var totalSize = vertical ? container.layout.height : container.layout.width;
            var isSecondVisible = secondPane.resolvedStyle.display == DisplayStyle.Flex;

            if (isSecondVisible)
            {
                newValue = Mathf.Max(isFirstPaneFixed ? firstMinSize : secondMinSize, newValue);
                newValue = Mathf.Min(totalSize - (isFirstPaneFixed ? secondMinSize : firstMinSize), newValue);
                newValue = Mathf.Round(newValue * EditorGUIUtility.pixelsPerPoint) / EditorGUIUtility.pixelsPerPoint;
                if (isFirstPaneFixed)
                {
                    if (vertical)
                        firstPane.style.height = newValue;
                    else
                        firstPane.style.width = newValue;
                }
                else
                {
                    if (vertical)
                        secondPane.style.height = newValue;
                    else
                        secondPane.style.width = newValue;
                }
                m_Value = newValue;
                if (isFirstPaneFixed)
                {
                    if (vertical)
                        secondPane.style.height = totalSize - newValue;
                    else
                        secondPane.style.width = totalSize - newValue;
                }
                else
                {
                    if (vertical)
                        firstPane.style.height = totalSize - newValue;
                    else
                        firstPane.style.width = totalSize - newValue;
                }
            }
            else
            {
                if (vertical)
                    firstPane.style.height = new Length(100, LengthUnit.Percent);
                else
                    firstPane.style.width = new Length(100, LengthUnit.Percent);
            }
        }

        public float value
        {
            get => m_Value; // store a value instead of returning directly a resolved style because it is not up-to-date
            set
            {
                var previousValue = this.value;
                SetValueWithoutNotify(value);
                using var evt = ChangeEvent<float>.GetPooled(previousValue, this.value);
                evt.target = this;
                SendEvent(evt);
            }
        }

        void OnDragStart() => AddToClassList(k_DraggingUssClassName);

        void OnDragEnd() => RemoveFromClassList(k_DraggingUssClassName);

        void OnDrag(Vector3 delta)
        {
            if (vertical)
                value += delta.y * (isFirstPaneFixed ? 1 : -1);
            else
                value += delta.x * (isFirstPaneFixed ? 1 : -1);
        }
    }
}
