using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;

namespace Unity.AI.Generators.UI
{
    [UxmlElement]
    sealed partial class CarouselView : VisualElement, INotifyValueChanged<int>
    {
        public const string ussClassName = "carouselview";

        public const string containerUssClassName = ussClassName + "__container";

        public const int noAutoPlayDuration = -1;

        bool m_Wrap;

        float m_AnimationDirection;

        List<CarouselViewItem> m_StaticItems;

        enum Direction
        {
            Horizontal,

            Vertical
        }

        Direction m_Direction;

        int m_Value = -1;

        ValueAnimation<float> m_Animation;

        int m_VisibleItemCount = k_DefaultVisibleItemCount;

        IVisualElementScheduledItem m_PollHierarchyItem;

        IList m_SourceItems;

        readonly VisualElement m_Container;

        bool m_ForceDisableWrap;

        int m_AutoPlayDuration = noAutoPlayDuration;

        IVisualElementScheduledItem m_AutoPlayAnimation;

        struct ScheduledNextValue
        {
            public bool scheduled;
            public float newAnimationDirection;
            public int newIndex;
            public int previousIndex;
        }

        ScheduledNextValue m_ScheduledNextValue;

        public override VisualElement contentContainer => m_Container;

        const float k_DefaultSnapAnimationSpeed = 2f;

        public float snapAnimationSpeed { get; set; } = k_DefaultSnapAnimationSpeed;

        static readonly Func<float, float> k_DefaultSnapAnimationEasing = Easing.OutCubic;

        Func<float, float> m_SnapAnimationEasing = k_DefaultSnapAnimationEasing;

        public Func<float, float> snapAnimationEasing
        {
            get => m_SnapAnimationEasing;
            set => m_SnapAnimationEasing = value;
        }

        const int k_DefaultVisibleItemCount = 1;

        public int visibleItemCount
        {
            get => m_VisibleItemCount;
            set
            {
                m_VisibleItemCount = value;
                SetValueWithoutNotify(this.value);
            }
        }

        const int k_DefaultAutoPlayDuration = noAutoPlayDuration;

        public int autoPlayDuration
        {
            get => m_AutoPlayDuration;
            set
            {
                if (m_AutoPlayDuration == value)
                    return;

                m_AutoPlayDuration = value;
                if (m_AutoPlayDuration > 0)
                {
                    m_AutoPlayAnimation = schedule.Execute(() => GoToNext());
                    m_AutoPlayAnimation.Every(m_AutoPlayDuration);
                }
                else
                {
                    m_AutoPlayAnimation?.Pause();
                    m_AutoPlayAnimation = null;
                }
            }
        }

        const Direction k_DefaultDirection = Direction.Horizontal;

        Direction direction
        {
            get => m_Direction;
            set
            {
                RemoveFromClassList($"{ussClassName}--{m_Direction}".ToLowerInvariant());
                m_Direction = value;
                AddToClassList($"{ussClassName}--{m_Direction}".ToLowerInvariant());
                SetValueWithoutNotify(this.value);
            }
        }

        Action<CarouselViewItem, int> m_BindItem;

        public Action<CarouselViewItem, int> bindItem
        {
            get => m_BindItem;
            set
            {
                m_BindItem = value;
                RefreshList();
            }
        }

        Action<CarouselViewItem, int> m_UnbindItem;

        public Action<CarouselViewItem, int> unbindItem
        {
            get => m_UnbindItem;
            set
            {
                m_UnbindItem = value;
                RefreshList();
            }
        }

        public IList sourceItems
        {
            get => m_SourceItems;
            set
            {
                m_SourceItems = value;

                // Stop Polling the hierarchy as we provided a new set of items
                m_PollHierarchyItem?.Pause();
                m_PollHierarchyItem = null;

                RefreshList();
            }
        }

        const bool k_DefaultWrap = true;

        public bool wrap
        {
            get => m_Wrap;
            set
            {
                m_Wrap = value;
                RefreshEverything();
            }
        }

        public int count => items?.Count ?? 0;

        public bool shouldWrap => count > visibleItemCount && wrap && !m_ForceDisableWrap;

        IList items => m_SourceItems ?? m_StaticItems;

        CarouselViewItem GetItem(int index)
        {
            foreach (var child in Children())
            {
                if (child is CarouselViewItem item && item.index == index)
                    return item;
            }

            return null;
        }

        public int value
        {
            get => m_Value;
            set => SetValue(value);
        }

        void SetValue(int newValue, bool findAnimationDirection = true)
        {
            if (newValue < 0 || newValue > count - 1)
                return;

            if (findAnimationDirection)
                m_AnimationDirection = FindAnimationDirection(newValue);

            var previousValue = m_Value;
            SetValueWithoutNotify(newValue);
            if (previousValue != m_Value)
            {
                using var evt = ChangeEvent<int>.GetPooled(previousValue, m_Value);
                evt.target = this;
                SendEvent(evt);
            }
        }

        const float k_DefaultResistance = 1f;

        float m_Resistance = k_DefaultResistance;

        public float resistance
        {
            get => m_Resistance;
            set
            {
                _ = !Mathf.Approximately(m_Resistance, value);
                m_Resistance = value;
            }
        }

        const int k_DefaultSkipAnimationThreshold = 2;

        public int skipAnimationThreshold { get; set; } = k_DefaultSkipAnimationThreshold;

        public CarouselView()
        {
            AddToClassList(ussClassName);

            pickingMode = PickingMode.Position;
            focusable = true;
            tabIndex = 0;

            m_Container = new VisualElement
            {
                name = containerUssClassName,
                pickingMode = PickingMode.Ignore,
            };
            m_Container.usageHints |= UsageHints.DynamicTransform;
            m_Container.AddToClassList(containerUssClassName);
            hierarchy.Add(m_Container);

            m_PollHierarchyItem = schedule.Execute(PollHierarchy).Every(50L);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            direction = k_DefaultDirection;
            wrap = k_DefaultWrap;
            visibleItemCount = k_DefaultVisibleItemCount;
            autoPlayDuration = k_DefaultAutoPlayDuration;
            resistance = k_DefaultResistance;
            skipAnimationThreshold = k_DefaultSkipAnimationThreshold;
            snapAnimationSpeed = k_DefaultSnapAnimationSpeed;
            snapAnimationEasing = k_DefaultSnapAnimationEasing;
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (!evt.newRect.IsValid())
                return;

            RefreshEverything();
        }

        void RefreshEverything()
        {
            m_AnimationDirection = 0;
            SetValueWithoutNotify(value);
        }

        void OnKeyDown(KeyDownEvent evt)
        {
            var handled = false;
            if (evt.target == this)
            {
                if (direction == Direction.Horizontal)
                {
                    handled = evt.keyCode switch
                    {
                        KeyCode.LeftArrow => GoToPrevious(),
                        KeyCode.RightArrow => GoToNext(),
                        _ => handled
                    };
                }
                else
                {
                    handled = evt.keyCode switch
                    {
                        KeyCode.UpArrow => GoToPrevious(),
                        KeyCode.DownArrow => GoToNext(),
                        _ => handled
                    };
                }
            }

            if (handled)
            {
                evt.StopPropagation();
            }
        }

        float FindAnimationDirection(int newIndex)
        {
            if (newIndex < 0 || newIndex >= count)
                return 0;

            var newItem = GetItem(newIndex);
            if (newItem == null)
                return 0;

            // we use min instead of center because a container can show multiple items at the same time
            var delta = direction switch
            {
                Direction.Horizontal => worldBound.min.x - newItem.worldBound.min.x,
                Direction.Vertical => worldBound.min.y - newItem.worldBound.min.y,
                _ => 0f
            };
            return Mathf.Approximately(0f, delta) ? 0f : Mathf.Sign(-delta);
        }

        public void SetValueWithoutNotify(int newValue)
        {
            if (count == 0)
            {
                m_Value = -1;
                return;
            }

            if (newValue < 0 || newValue > count - 1)
                return;

            if (m_ScheduledNextValue.scheduled)
                m_Value = m_ScheduledNextValue.previousIndex;

            m_ScheduledNextValue.scheduled = false;

            // Recycle previous animation
            if (m_Animation != null && !m_Animation.IsRecycled())
                m_Animation.Recycle();

            RefreshItemsSize();
            SetValueAndScheduleAnimation(newValue);
        }

        void SetValueAndScheduleAnimation(int newValue)
        {
            if (shouldWrap)
            {
                var offScreenItemCount = GetNbOfOffScreenItems();
                if (offScreenItemCount > 0)
                {
                    m_ScheduledNextValue = new ScheduledNextValue
                    {
                        scheduled = true,
                        newAnimationDirection = m_AnimationDirection,
                        newIndex = newValue,
                        previousIndex = m_Value,
                    };
                    if (m_AnimationDirection > 0)
                        SwapFirstToLast(offScreenItemCount);
                    if (m_AnimationDirection < 0)
                        SwapLastToFirst(offScreenItemCount);
                    PostSetValueWithoutNotify(newValue, false);
                    return;
                }
            }
            else
            {
                newValue = Mathf.Clamp(newValue, 0, count - m_VisibleItemCount);
            }

            PostSetValueWithoutNotify(newValue, true);
        }

        void PostSetValueWithoutNotify(int newValue, bool animate)
        {
            var from = m_Value >= 0 ? GetItem(m_Value) : null;
            var to = GetItem(newValue);

            if (animate && paddingRect.IsValid())
                StartAnimation(from, to);

            from?.RemoveFromClassList("is-selected");
            m_Value = newValue;
            to?.AddToClassList("is-selected");
        }

        void StartAnimation(VisualElement from, VisualElement to)
        {
            // Need a valid destination to create the animation
            if (to == null)
                return;

            // Find the position where the container must be at the end of the animation
            var newElementMin = this.WorldToLocal(to.worldBound.min);
            var newElementSize = direction == Direction.Horizontal ? to.worldBound.width : to.worldBound.height;
            var newElementOffset = direction == Direction.Horizontal ? newElementMin.x : newElementMin.y;
            var currentContainerOffset = direction == Direction.Horizontal
                ? m_Container.resolvedStyle.left
                : m_Container.resolvedStyle.top;
            var targetContainerOffset = currentContainerOffset - newElementOffset;

            // Recycle previous animation
            if (m_Animation != null && !m_Animation.IsRecycled())
                m_Animation.Recycle();

            // Find the best duration and distance to use in the animation
            var duration = from == null || Mathf.Approximately(0f, newElementOffset) || Mathf.Approximately(0f, m_AnimationDirection)
                ? 0
                : Mathf.RoundToInt(Mathf.Abs(newElementOffset) / snapAnimationSpeed);

            // The best distance takes in account the max distance based on skipAnimationThreshold property
            var distance = Mathf.Abs(targetContainerOffset - currentContainerOffset);
            var sign = Mathf.Sign(targetContainerOffset - currentContainerOffset);
            distance = Mathf.Min(distance, skipAnimationThreshold * newElementSize);
            currentContainerOffset = targetContainerOffset - sign * distance;

            var isValidAnimation =
                Mathf.Approximately(0, m_AnimationDirection) ||
                (m_AnimationDirection > 0 && targetContainerOffset < currentContainerOffset) ||
                (m_AnimationDirection < 0 && targetContainerOffset > currentContainerOffset);

            if (!isValidAnimation)
            {
                var dir = m_AnimationDirection switch
                {
                    0 => "0",
                    > 0 => "positive",
                    < 0 => "negative",
                    _ => "unknown"
                };
                Debug.Assert(isValidAnimation, "<b>[UI]</b>[CarouselView] Trying to animate in the wrong direction:\n" +
                    $"- Current Direction: {dir}\n" +
                    $"- Current Container Offset: {currentContainerOffset}\n" +
                    $"- Target Container Offset: {targetContainerOffset}");
            }

            // Start the animation
            m_Animation = experimental.animation.Start(currentContainerOffset, targetContainerOffset, duration, (_, f) =>
            {
                if (direction == Direction.Horizontal)
                    m_Container.style.left = f;
                else
                    m_Container.style.top = f;
            }).Ease(snapAnimationEasing).KeepAlive();
        }

        void PollHierarchy()
        {
            if (m_StaticItems == null && childCount > 0 && m_SourceItems == null)
            {
                m_PollHierarchyItem?.Pause();
                m_PollHierarchyItem = null;
                m_StaticItems = new List<CarouselViewItem>();
                foreach (var c in Children())
                {
                    m_StaticItems.Add((CarouselViewItem)c);
                }

                RefreshList();
            }
        }

        void RefreshItemsSize()
        {
            if (!contentRect.IsValid())
                return;

            foreach (var c in Children())
            {
                if (direction == Direction.Horizontal)
                    c.style.width = contentRect.width / m_VisibleItemCount;
                else
                    c.style.height = contentRect.height / m_VisibleItemCount;
            }
        }

        void RefreshList()
        {
            for (var i = 0; i < childCount; i++)
            {
                var item = (CarouselViewItem)ElementAt(i);
                unbindItem?.Invoke(item, i);
                item.UnregisterCallback<GeometryChangedEvent>(OnItemGeometryChanged);
            }

            Clear();

            if (m_SourceItems != null)
            {
                for (var i = 0; i < m_SourceItems.Count; i++)
                {
                    var item = new CarouselViewItem { index = i };
                    bindItem?.Invoke(item, i);
                    Add(item);
                }
            }
            else if (m_StaticItems != null)
            {
                for (var i = 0; i < m_StaticItems.Count; i++)
                {
                    var item = new CarouselViewItem { index = i };
                    if (m_StaticItems[i].childCount > 0)
                        item.Add(m_StaticItems[i].ElementAt(0));
                    Add(item);
                }
            }

            if (childCount > 0)
                ElementAt(0).RegisterCallback<GeometryChangedEvent>(OnItemGeometryChanged);

            m_AnimationDirection = 0;
            if (childCount > 0)
                SetValue(0, false);
            else
                m_Value = -1;
        }

        float SwapLastToFirst(int times)
        {
            var newPosition = direction == Direction.Horizontal ? m_Container.resolvedStyle.left : m_Container.resolvedStyle.top;
            if (times <= 0)
                return 0;

            if (direction == Direction.Horizontal)
            {
                newPosition -= contentRect.width * times;
                m_Container.style.left = newPosition;
            }
            else
            {
                newPosition -= contentRect.height * times;
                m_Container.style.top = newPosition;
            }

            while (times > 0)
            {
                var item = ElementAt(childCount - 1);
                item.SendToBack();
                times--;
            }

            return newPosition;
        }

        float SwapFirstToLast(int times)
        {
            var newPosition = direction == Direction.Horizontal ? m_Container.resolvedStyle.left : m_Container.resolvedStyle.top;
            if (times == 0)
                return newPosition;

            if (direction == Direction.Horizontal)
            {
                newPosition += contentRect.width * times;
                m_Container.style.left = newPosition;
            }
            else
            {
                newPosition += contentRect.height * times;
                m_Container.style.top = newPosition;
            }

            while (times > 0)
            {
                var item = ElementAt(0);
                item.BringToFront();
                times--;
            }

            return newPosition;
        }

        int GetNbOfOffScreenItems()
        {
            if (Mathf.Approximately(0, m_AnimationDirection))
                return 0;

            var containerMin = direction == Direction.Horizontal ? m_Container.layout.x : m_Container.layout.y;
            var containerMax = direction == Direction.Horizontal ? m_Container.layout.xMax : m_Container.layout.yMax;
            var size = direction == Direction.Horizontal ? paddingRect.width : paddingRect.height;

            if (shouldWrap)
            {
                var nbOffScreenStart = containerMin < 0 ? Mathf.FloorToInt(-containerMin / size) : 0;
                if (m_AnimationDirection > 0 && nbOffScreenStart > 0) // one or more elements are off-screen on the start
                    return nbOffScreenStart;
                var nbOffScreenEnd = containerMax > size ? Mathf.FloorToInt((containerMax - size) / size) : 0;
                if (m_AnimationDirection < 0 && nbOffScreenEnd > 0) // one or more elements are off-screen on the end
                    return nbOffScreenEnd;
            }

            return 0;
        }

        void OnItemGeometryChanged(GeometryChangedEvent evt)
        {
            if (m_ScheduledNextValue.scheduled)
            {
                var nextValue = m_ScheduledNextValue;
                m_ScheduledNextValue = new ScheduledNextValue();
                m_AnimationDirection = nextValue.newAnimationDirection;
                SetValue(nextValue.newIndex, false);
            }
        }

        public bool canGoToNext => shouldWrap || (value + 1 < childCount && value + 1 >= 0);

        public bool canGoToPrevious => shouldWrap || (value - 1 < childCount && value - 1 >= 0);

        public bool GoToNext()
        {
            if (!canGoToNext)
                return false;

            var nextIndex = shouldWrap
                ? (int)Mathf.Repeat(value + 1, childCount)
                : Mathf.Clamp(value + 1, 0, childCount - visibleItemCount);

            if (nextIndex == value)
                return false;

            m_AnimationDirection = 1f;
            SetValue(nextIndex, false);

            return true;
        }

        public bool GoToPrevious()
        {
            if (!canGoToPrevious)
                return false;

            var nextIndex = shouldWrap
                ? (int)Mathf.Repeat(value - 1, childCount)
                : Mathf.Clamp(value - 1, 0, childCount - visibleItemCount);

            if (nextIndex == value)
                return false;

            m_AnimationDirection = -1f;
            SetValue(nextIndex, false);

            return true;
        }
    }

    static class ValueAnimationExtensionsBridge
    {
        // We have to use Reflection since the field is private and there is no public API to check if an animation is recycled.
        static readonly PropertyInfo k_Recycled =
            typeof(ValueAnimation<float>).GetProperty("recycled", BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsRecycled(this ValueAnimation<float> animation)
        {
            return (bool)k_Recycled.GetValue(animation);
        }
    }

    static class RectExtensions
    {
        public static bool IsValid(this Rect rect)
        {
            return rect != default &&
                !float.IsNaN(rect.width) && !float.IsNaN(rect.height) &&
                !float.IsInfinity(rect.width) && !float.IsInfinity(rect.height) &&
                !float.IsNegative(rect.width) && !float.IsNegative(rect.height) &&
                !Mathf.Approximately(0, rect.width) && !Mathf.Approximately(0, rect.height);
        }
    }
}
