using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class ChatScrollView<TD, TV> : VisualElement where TV : ManagedListEntry
    {
        const int k_DelayedScrollActions = 2;
        const int k_ScrollEndThreshold = 5;
        const int k_ScrollEndDisplayThreshold = 30;
        const int k_ScrollEndDisplayThresholdDuringRun = 100;
        const float k_MinScrollableContentRatio = 0.25f;

        readonly IList<TD> k_Data = new List<TD>();
        readonly IList<TV> k_VisualElements = new List<TV>();
        readonly List<int> k_PendingUpdates = new();

        ScrollView m_ScrollView;
        Scroller m_VerticalScroller;
        AssistantUIContext m_Context;

        ScrollState m_ScrollState = ScrollState.None;
        bool m_CheckForScrollLock;
        bool m_EnforcementQueued;
        int m_DelayedScrollActions;
        bool m_UpdateInProgress;
        bool m_RefreshRequired;

        public bool EnableScrollLock = true;
        public bool EnableDelayedElements = false;
        public int DelayedElementOperations = 5;

        enum ScrollState
        {
            None,
            ScrollToEnd,
            Locked
        }

        public IList<TD> Data => k_Data;
        public IList<TV> VisualElements => k_VisualElements;

        public bool HasContent => k_Data.Count > 0;
        
        public bool CanScrollDown
        {
            get
            {
                var viewportHeight = m_ScrollView?.contentViewport?.resolvedStyle.height ?? 0;
                var minScrollable = viewportHeight * k_MinScrollableContentRatio;
                float highValueThreshold = m_Context.Blackboard.IsAPIWorking ? k_ScrollEndDisplayThresholdDuringRun : k_ScrollEndDisplayThreshold;
                float highValueAdjusted = Mathf.Max(0, m_VerticalScroller.highValue - highValueThreshold);
                return m_VerticalScroller.highValue > minScrollable && m_VerticalScroller.value < highValueAdjusted;
            }
        }

        public event Action UserScrolled;
        public event Action GeometryChanged;

        public void Initialize(AssistantUIContext context)
        {
            m_Context = context;

            m_ScrollView = new ScrollView();
            m_ScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            Add(m_ScrollView);

            m_VerticalScroller = m_ScrollView.Q<Scroller>(null, "unity-scroller--vertical");
            m_VerticalScroller.valueChanged += OnVerticallyScrolled;

            m_ScrollView.contentContainer.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                NotifyVisibleElements();
                GeometryChanged?.Invoke();
            });

            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            EditorTask.delayCall -= ContinueCreateElements;
            EditorTask.delayCall -= ContinueUpdateElements;
            EditorTask.delayCall -= EnforceScrollState;
        }

        public void AddData(TD item)
        {
            k_Data.Add(item);
            m_RefreshRequired = true;

            if (!m_UpdateInProgress)
            {
                k_PendingUpdates.Add(k_Data.Count - 1);
                DoRefreshList();
            }
        }

        public void UpdateData(int index, TD data)
        {
            k_Data[index] = data;

            if (!m_UpdateInProgress)
            {
                RefreshElement(index);
            }
        }

        public void RemoveData(int index)
        {
            k_Data.RemoveAt(index);

            var element = k_VisualElements[index];
            k_VisualElements.RemoveAt(index);
            if (element != null)
            {
                m_ScrollView.Remove(element);
            }
        }

        public void ClearData()
        {
            k_Data.Clear();

            for (var i = 0; i < k_VisualElements.Count; i++)
            {
                var element = k_VisualElements[i];
                if (element.parent == m_ScrollView)
                {
                    m_ScrollView.Remove(element);
                }
            }

            k_VisualElements.Clear();
            k_PendingUpdates.Clear();
            ChangeScrollState(ScrollState.None);
        }

        public void BeginUpdate()
        {
            m_UpdateInProgress = true;
        }

        public void EndUpdate(bool scrollToEnd = true)
        {
            m_UpdateInProgress = false;
            DoRefreshList();

            if (scrollToEnd)
            {
                ScrollToEnd();
            }
        }

        public void ScrollToEnd()
        {
            ChangeScrollState(ScrollState.ScrollToEnd, true);
        }

        public void ScrollToEndIfNotLocked()
        {
            if (m_ScrollState == ScrollState.Locked)
            {
                return;
            }

            ChangeScrollState(ScrollState.ScrollToEnd, true);
        }

        void DoRefreshList()
        {
            m_RefreshRequired = false;

            if (EnableDelayedElements)
            {
                EditorTask.delayCall += ContinueCreateElements;
            }
            else
            {
                ContinueCreateElements();
            }
        }

        void RefreshElement(int index)
        {
            QueueUpdate(index);
            if (EnableDelayedElements)
            {
                EditorTask.delayCall += ContinueUpdateElements;
            }
            else
            {
                ContinueUpdateElements();
            }
        }

        void ContinueCreateElements()
        {
            int operations = 0;
            while (k_VisualElements.Count < k_Data.Count)
            {
                operations++;
                var element = CreateItem();
                k_VisualElements.Add(element);
                m_ScrollView.Add(element);

                var index = k_VisualElements.Count - 1;
                var data = k_Data[index];
                element.SetData(index, data);

                if (EnableDelayedElements && operations >= DelayedElementOperations)
                {
                    EditorTask.delayCall += ContinueCreateElements;
                    return;
                }
            }
        }

        void QueueUpdate(int index)
        {
            if (k_PendingUpdates.Contains(index))
            {
                return;
            }

            k_PendingUpdates.Add(index);
        }

        void ContinueUpdateElements()
        {
            if (k_PendingUpdates.Count == 0)
            {
                return;
            }

            int operations = 0;
            while (k_PendingUpdates.Count > 0)
            {
                operations++;
                int index = k_PendingUpdates[0];
                k_PendingUpdates.RemoveAt(0);

                if (k_VisualElements.Count <= index)
                {
                    k_PendingUpdates.Insert(0, index);
                    if (EnableDelayedElements)
                    {
                        EditorTask.delayCall += ContinueUpdateElements;
                    }
                    else
                    {
                        // No retry mechanism — drop index to avoid permanently blocking the queue
                        k_PendingUpdates.RemoveAt(0);
                    }
                    return;
                }

                var data = k_Data[index];
                k_VisualElements[index].SetData(index, data);

                if (EnableDelayedElements && operations >= DelayedElementOperations)
                {
                    EditorTask.delayCall += ContinueUpdateElements;
                    break;
                }
            }

            EnforceScrollState();
        }

        void NotifyVisibleElements()
        {
            var sw = new Stopwatch();
            sw.Start();

            var updatedElements = 0;
            var viewRect = contentContainer.worldBound;

            foreach (var element in k_VisualElements.Where(e => IsVisible(e, viewRect)))
            {
                if (element.CameIntoView())
                    updatedElements++;
            }

            sw.Stop();

            if (updatedElements > 0)
            {
                InternalLog.Log($"NotifyVisibleElements took {sw.ElapsedMilliseconds}ms (updated elements: {updatedElements})");
                QueueEnforceScrollState();
            }

            bool IsVisible(VisualElement element, Rect scrollViewRect)
            {
                var localRect = element.worldBound;
                return localRect.Overlaps(scrollViewRect);
            }
        }

        void ChangeScrollState(ScrollState newState, bool force = false)
        {
            if (!force && m_ScrollState == newState)
            {
                return;
            }

            m_DelayedScrollActions = k_DelayedScrollActions;
            m_ScrollState = newState;
            QueueEnforceScrollState();
        }

        void QueueEnforceScrollState()
        {
            if (m_EnforcementQueued)
            {
                return;
            }

            m_EnforcementQueued = true;
            EditorTask.delayCall += EnforceScrollState;
        }

        void EnforceScrollState()
        {
            m_EnforcementQueued = false;
            
            if (k_Data.Count == 0)
            {
                return;
            }

            if (k_VisualElements.Count < k_Data.Count)
            {
                QueueEnforceScrollState();
                return;
            }

            RefreshIfRequired();
            m_CheckForScrollLock = false;

            if (m_ScrollState == ScrollState.ScrollToEnd)
            {
                var isAtBottom = m_VerticalScroller.value >= m_VerticalScroller.highValue - k_ScrollEndThreshold;
                if (!isAtBottom)
                {
                    m_VerticalScroller.value = m_VerticalScroller.highValue;
                }
            }

            if (m_DelayedScrollActions > 0)
            {
                m_DelayedScrollActions--;
                QueueEnforceScrollState();
            }
            else
            {
                m_CheckForScrollLock = true;
            }
        }

        void RefreshIfRequired()
        {
            if (m_RefreshRequired)
            {
                DoRefreshList();
            }
        }

        void OnVerticallyScrolled(float newValue)
        {
            UserScrolled?.Invoke();
            NotifyVisibleElements();

            if (!EnableScrollLock || !m_CheckForScrollLock)
            {
                return;
            }

            var isAtBottom = newValue >= m_VerticalScroller.highValue - k_ScrollEndThreshold;
            ChangeScrollState(isAtBottom ? ScrollState.ScrollToEnd : ScrollState.Locked);
        }

        TV CreateItem()
        {
            var element = Activator.CreateInstance<TV>();
            element.Initialize(m_Context);
            return element;
        }

        public bool IsItemPopulated(int index)
        {
            if (index < 0 || index >= k_VisualElements.Count)
                return false;

            return k_VisualElements[index].DidComeIntoView;
        }

        public void PopulateItem(int index)
        {
            if (index < 0 || index >= k_VisualElements.Count)
                return;

            if (k_VisualElements[index].CameIntoView())
            {
                m_ScrollView.ScrollTo(k_VisualElements[index]);
            }
        }

        public int GetFirstItemInView()
        {
            for (var i = 0; i < k_VisualElements.Count; i++)
            {
                var element = k_VisualElements[i];
                if (element.worldBound.yMin > 0)
                {
                    return i;
                }
            }

            return k_VisualElements.Count - 1;
        }

        public void ScrollDownBy(float positionY)
        {
            ChangeScrollState(ScrollState.None);

            var newPos = new Vector2(0,
                Mathf.Clamp(
                    m_ScrollView.scrollOffset.y + positionY,
                    m_VerticalScroller.lowValue,
                    m_VerticalScroller.highValue));

            m_ScrollView.scrollOffset = newPos;
        }
        
        public void SetContentEnabled(bool enabled)
        {
            m_ScrollView.contentContainer.SetEnabled(enabled);
        }
    }
}
