using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    [UxmlElement]
    sealed partial class GridView : VisualElement
    {
        enum ScrollingDirection
        {
            None = 0,
            Up,
            Down
        };

        Func<VisualElement> m_MakeItem;
        Action<VisualElement, int> m_BindItem;

        bool m_IsRangeSelectionDirectionUp;

        int m_RowCount = 0;
        int m_ColumnCount = 0;
        int m_FirstVisibleRowIndex = 0;
        int m_VisibleItemCount = 0;
        int m_RangeSelectionOrigin = -1;
        const int k_ExtraRows = 2;

        float m_FixedItemHeight;
        float m_FixedItemWidth;
        float m_MaximumScrollViewHeight;
        IList m_ItemsSource;

        List<ReusableGridViewRow> m_RowPool;
        List<int> m_ItemsSourceIds;
        ScrollView m_ScrollView;

        const string k_GridViewStyleClassName = "grid-view";
        const string k_GridViewItemsScrollViewStyleClassName = "grid-view-rows";

        Vector2 m_ScrollOffset = Vector2.zero;
        Vector3 m_TouchDownPosition;

        readonly List<int> m_SelectedIndices = new List<int>();
        readonly List<int> m_SelectedIds = new List<int>();
        readonly List<object> m_SelectedItems = new List<object>();
        SelectionType m_SelectionType;

        public event Action<IEnumerable<object>> itemsChosen;
        public event Action<IEnumerable<object>> selectionChanged;
        public event Action<IEnumerable<int>> selectedIndicesChanged;
        public event Action itemsBuilt;

        public int rowCount => m_RowCount;
        public int columnCount => m_ColumnCount;

        public const float defaultItemSize = 30f;

        public Action<VisualElement, int> unbindItem { get; set; }

        public Action<VisualElement> destroyItem { get; set; }

        public Func<VisualElement> makeItem
        {
            get { return m_MakeItem; }
            set
            {
                if (m_MakeItem == value)
                    return;
                m_MakeItem = value;
                Rebuild();
            }
        }

        public Action<VisualElement, int> bindItem
        {
            get { return m_BindItem; }
            set
            {
                if (m_BindItem == value)
                    return;
                m_BindItem = value;
                RefreshItems();
            }
        }

        [UxmlAttribute]
        public float fixedItemHeight
        {
            get => m_FixedItemHeight;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(fixedItemHeight), L10n.Tr("Value needs to be positive for virtualization."));

                var tempVal = value == 0 ? defaultItemSize : value;

                if (!Mathf.Approximately(m_FixedItemHeight, tempVal))
                {
                    m_FixedItemHeight = tempVal;
                    ComputeGridSize();
                    RefreshItems();
                }
            }
        }

        [UxmlAttribute]
        public float fixedItemWidth
        {
            get => m_FixedItemWidth;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(fixedItemWidth), L10n.Tr("Value needs to be positive for virtualization."));

                var tempVal = value == 0 ? defaultItemSize : value;

                if (!Mathf.Approximately(m_FixedItemWidth, tempVal))
                {
                    m_FixedItemWidth = tempVal;
                    ComputeGridSize();
                    RefreshItems();
                }
            }
        }

        public int selectedIndex
        {
            get { return m_SelectedIndices.Count == 0 ? -1 : m_SelectedIndices.First(); }
            set { SetSelection(value); }
        }

        public IEnumerable<int> selectedIndices => m_SelectedIndices;

        public object selectedItem => m_SelectedItems.Count == 0 ? null : m_SelectedItems.First();

        public IEnumerable<object> selectedItems => m_SelectedItems;

        public IEnumerable<int> selectedIds => m_SelectedIds;

        public List<ReusableGridViewItem> activeItems => GetActiveItems();

        public int visibleItemCount => m_VisibleItemCount;

        public int firstVisibleIndex => m_FirstVisibleRowIndex * m_ColumnCount;

        public int lastVisibleIndex => m_FirstVisibleRowIndex * m_ColumnCount + (m_VisibleItemCount - 1);

        public bool selectOnPointerUp { get; set; }

        public bool keepScrollPositionWhenHidden { get; set; }

        public SelectionType selectionType
        {
            get { return m_SelectionType; }
            set
            {
                m_SelectionType = value;
                if (m_SelectionType == SelectionType.None)
                {
                    ClearSelection();
                }
                else if (m_SelectionType == SelectionType.Single)
                {
                    if (m_SelectedIndices.Count > 1)
                    {
                        SetSelection(m_SelectedIndices.First());
                    }
                }
            }
        }

        public IList itemsSource
        {
            get { return m_ItemsSource; }
            set
            {
                if (m_ItemsSource is INotifyCollectionChanged oldCollection)
                    oldCollection.CollectionChanged -= OnItemsSourceCollectionChanged;

                m_ItemsSource = value;
                if (m_ItemsSource is INotifyCollectionChanged newCollection)
                    newCollection.CollectionChanged += OnItemsSourceCollectionChanged;

                ComputeGridSize();

                if (m_RowPool == null)
                    BuildItems();
                else
                    RefreshItems();
            }
        }

        public GridView() : this(Array.Empty<string>(), 120, 120) {}

        public GridView(IList itemsSource, float itemFixedWidth, float itemFixedHeight,
            Func<VisualElement> makeItem = null, Action<VisualElement, int> bindItem = null)
        {
            if (itemFixedWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(fixedItemWidth), L10n.Tr("Value needs to be positive for virtualization."));

            if (itemFixedHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(itemFixedHeight), L10n.Tr("Value needs to be positive for virtualization."));

            if (itemFixedWidth == 0)
                itemFixedWidth = defaultItemSize;

            if (itemFixedHeight == 0)
                itemFixedHeight = defaultItemSize;

            m_ItemsSource = itemsSource;
            m_FixedItemHeight = itemFixedHeight;
            m_FixedItemWidth = itemFixedWidth;
            m_BindItem = bindItem;
            m_MakeItem = makeItem;

            AddToClassList(k_GridViewStyleClassName);

            m_ScrollView = new ScrollView();
            m_ScrollView.AddToClassList(k_GridViewItemsScrollViewStyleClassName);
            m_ScrollView.verticalScroller.valueChanged += offset =>
            {
                if (keepScrollPositionWhenHidden && (float.IsNaN(resolvedStyle.width) || resolvedStyle.width <= 0))
                    return;
                OnScroll(new Vector2(0, offset));
            };

            m_ScrollView.RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            m_ScrollView.RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            hierarchy.Add(m_ScrollView);

            m_ScrollView.contentContainer.focusable = true;
            m_ScrollView.contentContainer.usageHints &= ~UsageHints.GroupTransform;
            m_ScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            delegatesFocus = true;
            focusable = true;
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            if (evt.destinationPanel == null)
                return;

            m_ScrollView.contentViewport.RegisterCallback<GeometryChangedEvent>(OnSizeChanged);

            m_ScrollView.RegisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.RegisterCallback<PointerUpEvent>(OnPointerUp);

            ResetAndBuildItems();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            m_ScrollView.contentViewport.UnregisterCallback<GeometryChangedEvent>(OnSizeChanged);

            m_ScrollView.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            m_ScrollView.UnregisterCallback<PointerUpEvent>(OnPointerUp);

            ResetGridViewState();
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            if (!HasValidDataAndBindings() || m_RowPool == null)
                return;

            if (!evt.isPrimary)
                return;

            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
            {
                m_TouchDownPosition = evt.position;
                return;
            }

            if (selectOnPointerUp)
                return;

            DoSelect(evt.localPosition, evt.clickCount, evt.actionKey, evt.shiftKey);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!HasValidDataAndBindings() || m_RowPool == null)
                return;

            if (!evt.isPrimary)
                return;

            if (evt.button != (int)MouseButton.LeftMouse)
                return;

            if (evt.pointerType != UnityEngine.UIElements.PointerType.mouse)
            {
                const float scrollThresholdSquared = 100;
                var delta = evt.position - m_TouchDownPosition;
                if (delta.sqrMagnitude <= scrollThresholdSquared)
                    DoSelect(evt.localPosition, evt.clickCount, evt.actionKey, evt.shiftKey);
            }
            else
            {
                if (selectOnPointerUp)
                {
                    DoSelect(evt.localPosition, evt.clickCount, evt.actionKey, evt.shiftKey);
                    return;
                }

                var clickedIndex = GetIndexByPosition(evt.localPosition);
                var itemIndex = clickedIndex + m_FirstVisibleRowIndex * m_ColumnCount;
                if (selectionType == SelectionType.Multiple
                    && !evt.shiftKey
                    && !evt.actionKey
                    && m_SelectedIndices.Count > 1
                    && m_SelectedIndices.Contains(itemIndex))
                {
                    ProcessSingleClick(itemIndex);
                }
            }
        }

        List<ReusableGridViewItem> GetActiveItems()
        {
            if (m_RowPool == null)
                return new List<ReusableGridViewItem>();

            var activeItems = new List<ReusableGridViewItem>();
            foreach (var reusableRow in m_RowPool)
            {
                var items = reusableRow.GetItems();
                if (items == null)
                    continue;

                activeItems.AddRange(items);
            }

            return activeItems;
        }

        public void ScrollToItem(int itemIndex)
        {
            if (!HasValidDataAndBindings() || m_RowPool == null)
                return;

            if (m_RowPool.Count == 0 || m_ColumnCount == 0 || itemIndex < -1)
                return;

            var rowIndex = itemIndex / m_ColumnCount;

            if (itemIndex == -1)
            {
                if (m_ItemsSource.Count < m_VisibleItemCount)
                    m_ScrollView.scrollOffset = new Vector2(0, 0);
                else
                    m_ScrollView.scrollOffset = new Vector2(0, m_MaximumScrollViewHeight);
            }
            else if (itemIndex == m_ItemsSource.Count - 1) // End.
            {
                m_ScrollView.scrollOffset = new Vector2(0, m_MaximumScrollViewHeight);
            }
            else if (itemIndex == 0) // Home.
            {
                m_ScrollView.scrollOffset = new Vector2(0, 0);
            }
            else if (m_FirstVisibleRowIndex >= rowIndex) // Moving up.
            {
                m_ScrollView.scrollOffset = Vector2.up * (m_FixedItemHeight * Mathf.FloorToInt(itemIndex / (float)m_ColumnCount));
            }
            else
            {
                var visibleRowCount = Mathf.Ceil((float)m_VisibleItemCount / m_ColumnCount);
                if (rowIndex < m_FirstVisibleRowIndex + visibleRowCount - 1)
                    return;

                var itemRow = Mathf.Ceil((float)(itemIndex + 1) / m_ColumnCount);
                var yScrollOffset = m_FixedItemHeight * (itemRow - visibleRowCount + 1);

                m_ScrollView.scrollOffset = new Vector2(m_ScrollView.scrollOffset.x, yScrollOffset);
            }

            m_ScrollOffset = m_ScrollView.scrollOffset;
        }

        bool HasValidDataAndBindings()
        {
            return m_ItemsSource != null && m_MakeItem != null && m_BindItem != null;
        }

        void NotifyOfSelectionChange()
        {
            if (!HasValidDataAndBindings() || m_RowPool == null)
                return;

            selectionChanged?.Invoke(m_SelectedItems);
            selectedIndicesChanged?.Invoke(m_SelectedIndices);
        }

        void DoRangeSelection(int rangeSelectionFinalIndex)
        {
            m_RangeSelectionOrigin = m_IsRangeSelectionDirectionUp ? m_SelectedIndices.Max() : m_SelectedIndices.Min();
            ClearSelectionWithoutValidation();

            var range = new List<int>();
            m_IsRangeSelectionDirectionUp = rangeSelectionFinalIndex < m_RangeSelectionOrigin;
            if (m_IsRangeSelectionDirectionUp)
            {
                for (var i = rangeSelectionFinalIndex; i <= m_RangeSelectionOrigin; i++)
                    range.Add(i);
            }
            else
            {
                for (var i = rangeSelectionFinalIndex; i >= m_RangeSelectionOrigin; i--)
                    range.Add(i);
            }

            AddToSelection(range);
        }

        public void AddToSelection(int index)
        {
            AddToSelection(new[] { index });
        }

        public void AddToSelection(IList<int> indexes)
        {
            if (!HasValidDataAndBindings() || m_RowPool == null || indexes == null || indexes.Count == 0)
                return;

            foreach (var index in indexes)
                AddToSelectionWithoutValidation(index);

            NotifyOfSelectionChange();
        }

        public void RemoveFromSelection(int index)
        {
            if (!HasValidDataAndBindings() || m_RowPool == null)
                return;

            RemoveFromSelectionWithoutValidation(index);
            NotifyOfSelectionChange();
        }

        public void ClearSelection()
        {
            ClearSelectionWithoutNotify();
            NotifyOfSelectionChange();
        }

        void ClearSelectionWithoutValidation()
        {
            foreach (var reusableItem in activeItems)
                reusableItem.SetSelected(false);

            m_SelectedIndices.Clear();
            m_SelectedItems.Clear();
            m_SelectedIds.Clear();
        }

        public void ClearSelectionWithoutNotify()
        {
            if (!HasValidDataAndBindings() || m_RowPool == null || m_SelectedIds.Count == 0)
                return;

            ClearSelectionWithoutValidation();
        }

        public void SetSelection(int itemIndex)
        {
            if (itemIndex < 0 || m_ItemsSource == null || itemIndex >= m_ItemsSource.Count)
            {
                ClearSelection();
                return;
            }

            SetSelection(new[] { itemIndex });
        }

        public void SetSelection(IEnumerable<int> indices)
        {
            switch (selectionType)
            {
                case SelectionType.None:
                    return;
                case SelectionType.Single:
                    if (indices != null)
                        indices = new[] { indices.Last() };
                    break;
                case SelectionType.Multiple:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            SetSelectionInternal(indices, true);
        }

        public void SetSelectionWithoutNotify(IEnumerable<int> indices)
        {
            SetSelectionInternal(indices, false);
        }

        internal void SetSelectionInternal(IEnumerable<int> indices, bool sendNotification)
        {
            if (!HasValidDataAndBindings() || m_RowPool == null || indices == null)
                return;

            ClearSelectionWithoutValidation();

            foreach (var index in indices)
                AddToSelectionWithoutValidation(index);

            if (sendNotification)
                NotifyOfSelectionChange();
        }

        void SelectAll()
        {
            if (!HasValidDataAndBindings() || m_RowPool == null)
                return;

            if (selectionType != SelectionType.Multiple)
                return;

            for (var itemIndex = 0; itemIndex < m_ItemsSource.Count; itemIndex++)
            {
                var item = m_ItemsSource[itemIndex];
                var id = item.GetHashCode();
                if (!m_SelectedIds.Contains(id))
                {
                    m_SelectedIndices.Add(itemIndex);
                    m_SelectedItems.Add(item);
                    m_SelectedIds.Add(id);
                }
            }

            foreach (var reusableItem in activeItems)
                reusableItem.SetSelected(true);

            NotifyOfSelectionChange();
        }

        void AddToSelectionWithoutValidation(int itemIndex)
        {
            if (itemIndex < 0 || itemIndex >= m_ItemsSource.Count || m_SelectedIndices.Contains(itemIndex))
                return;

            var item = m_ItemsSource[itemIndex];
            m_SelectedIndices.Add(itemIndex);
            m_SelectedItems.Add(item);
            m_SelectedIds.Add(item.GetHashCode());

            var elementIndex = itemIndex - m_FirstVisibleRowIndex * m_ColumnCount;
            if (elementIndex >= activeItems.Count || elementIndex < 0)
                return;

            var reusableItem = activeItems[elementIndex];
            reusableItem.SetSelected(true);
        }

        void RemoveFromSelectionWithoutValidation(int itemIndex)
        {
            if (!m_SelectedIndices.Contains(itemIndex))
                return;

            var item = m_ItemsSource[itemIndex];
            m_SelectedIndices.Remove(itemIndex);
            m_SelectedItems.Remove(item);
            m_SelectedIds.Remove(item.GetHashCode());

            var elementIndex = itemIndex - m_FirstVisibleRowIndex * m_ColumnCount;
            if (elementIndex >= activeItems.Count || elementIndex < 0)
                return;

            var reusableItem = activeItems[elementIndex];
            reusableItem.SetSelected(false);
        }

        void DoSelect(Vector2 localPosition, int clickCount, bool actionKey, bool shiftKey)
        {
            var clickedIndex = GetIndexByPosition(localPosition);
            var itemIndex = clickedIndex + m_FirstVisibleRowIndex * m_ColumnCount;

            if (itemIndex > m_ItemsSource.Count - 1 || clickedIndex > m_ItemsSource.Count - 1)
                return;

            switch (clickCount)
            {
                case 1:
                    DoSelectOnSingleClick(itemIndex, actionKey, shiftKey);
                    break;
                case 2:
                {
                    if (itemsChosen != null)
                        ProcessSingleClick(itemIndex);

                    itemsChosen?.Invoke(m_SelectedItems);
                }
                    break;
                default:
                    break;
            }
        }

        void DoSelectOnSingleClick(int itemIndex, bool actionKey, bool shiftKey)
        {
            if (selectionType == SelectionType.None)
                return;

            if (selectionType == SelectionType.Multiple && actionKey)
            {
                m_RangeSelectionOrigin = itemIndex;

                // Add/remove single clicked element
                var id = m_ItemsSourceIds[itemIndex];
                if (m_SelectedIds.Contains(id))
                    RemoveFromSelection(itemIndex);
                else
                    AddToSelection(itemIndex);
            }
            else if (selectionType == SelectionType.Multiple && shiftKey)
            {
                if (m_RangeSelectionOrigin == -1 || !selectedItems.Any())
                {
                    m_RangeSelectionOrigin = itemIndex;
                    SetSelection(itemIndex);
                }
                else
                {
                    DoRangeSelection(itemIndex);
                }
            }
            else if (selectionType == SelectionType.Multiple && m_SelectedIndices.Contains(itemIndex))
            {
                // Do noting, selection will be processed OnPointerUp.
            }
            else // single
            {
                m_RangeSelectionOrigin = itemIndex;
                SetSelection(itemIndex);
            }
        }

        void ProcessSingleClick(int itemIndex)
        {
            m_RangeSelectionOrigin = itemIndex;
            SetSelection(itemIndex);
        }

        internal int GetIndexByPosition(Vector2 localPosition)
        {
            if (m_ColumnCount == 0 || m_RowCount == 0)
                return -1;

            var resolvedRowWidth = m_ScrollView.contentContainer.localBound.width;
            var calculatedRowWidth = m_ColumnCount * m_FixedItemWidth;
            var delta = resolvedRowWidth - calculatedRowWidth;
            var extraElementPadding = m_ColumnCount > 1 ? Mathf.Ceil(delta / (m_ColumnCount - 1)) : 0;

            var offset = m_ScrollOffset.y - Mathf.FloorToInt(m_ScrollOffset.y / m_FixedItemHeight) * m_FixedItemHeight;

            if (offset == 0)
            {
                var index = Mathf.FloorToInt(localPosition.y / m_FixedItemHeight) * m_ColumnCount + Mathf.FloorToInt(localPosition.x / (m_FixedItemWidth + extraElementPadding));
                if (index >= m_ItemsSource.Count)
                    index = -1;

                return index;
            }

            var visibleOffset = m_FixedItemHeight - offset;
            var visibleRowCount = m_VisibleItemCount / m_ColumnCount;

            var lowerBound = 0f;
            for (int i = 0; i <= visibleRowCount; i++)
            {
                var upperBound = visibleOffset + i * m_FixedItemHeight;
                if (localPosition.y >= lowerBound && localPosition.y < upperBound)
                    return i * m_ColumnCount + Mathf.FloorToInt(localPosition.x / (m_FixedItemWidth + extraElementPadding));
                else
                    lowerBound = upperBound;
            }

            return -1;
        }

        void OnScroll(Vector2 offset)
        {
            var newFirstVisibleRowIndex = (int)(offset.y / m_FixedItemHeight);
            m_ScrollOffset.y = offset.y;

            m_ScrollView.contentContainer.style.paddingTop = newFirstVisibleRowIndex * m_FixedItemHeight;
            m_ScrollView.contentContainer.style.height = m_MaximumScrollViewHeight;

            if (m_FirstVisibleRowIndex == newFirstVisibleRowIndex)
                return;

            var direction = m_FirstVisibleRowIndex > newFirstVisibleRowIndex ? ScrollingDirection.Up : ScrollingDirection.Down;
            var delta = Math.Abs(newFirstVisibleRowIndex - m_FirstVisibleRowIndex);
            m_FirstVisibleRowIndex = newFirstVisibleRowIndex;
            if (delta >= m_RowCount)
            {
                RebindActiveItems(newFirstVisibleRowIndex);
                return;
            }

            for (var i = 0; i < delta; ++i)
                OnScrollBindItems(direction);
        }

        void RebindActiveItems(int firstVisibleItemIndex)
        {
            var itemIndex = firstVisibleItemIndex * m_ColumnCount;
            foreach (var reusableItem in activeItems)
            {
                if (reusableItem.index < m_ItemsSource.Count && reusableItem.index != ReusableGridViewItem.UndefinedIndex)
                    UnbindItem(reusableItem, reusableItem.index);

                if (itemIndex >= m_ItemsSource.Count)
                {
                    reusableItem.bindableElement.style.visibility = Visibility.Hidden;
                }
                else
                {
                    BindItem(reusableItem, itemIndex, m_ItemsSourceIds[itemIndex]);
                    itemIndex++;
                }
            }
        }

        void OnScrollBindItems(ScrollingDirection scrollingDirection)
        {
            switch (scrollingDirection)
            {
                case ScrollingDirection.None:
                    break;
                case ScrollingDirection.Down:
                    ScrollingDown();
                    break;
                case ScrollingDirection.Up:
                    ScrollingUp();
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        void ScrollingDown()
        {
            if (m_RowPool == null || m_RowPool.Count == 0)
                return;

            // When scrolling down, if the last item in the last row is already undefined
            // (because it is already outside the range of source items), then don't bind
            // items from the start.
            var lastIndex = m_RowPool.Last().GetLastItemInRow().index;
            var nextElementIndexToBind = lastIndex == ReusableGridViewItem.UndefinedIndex ? ReusableGridViewItem.UndefinedIndex : lastIndex + 1;
            var row = m_RowPool.First();
            for (int i = 0; i < m_ColumnCount; i++)
            {
                var reusableItem = row.GetFirstItemInRow();
                row.RemoveItemAt(0);
                UnbindItem(reusableItem, reusableItem.index);

                row.AddItem(reusableItem);
                if (nextElementIndexToBind != ReusableGridViewItem.UndefinedIndex && nextElementIndexToBind < m_ItemsSource.Count)
                {
                    BindItem(reusableItem, nextElementIndexToBind, m_ItemsSourceIds[nextElementIndexToBind]);
                    nextElementIndexToBind++;
                }
            }

            m_RowPool.RemoveAt(0);
            m_RowPool.Add(row);
            row.bindableElement.BringToFront();
            row.SetRowVisibility();
        }

        void ScrollingUp()
        {
            if (m_RowPool == null || m_RowPool.Count == 0)
                return;

            var itemIndex = m_RowPool.First().GetFirstItemInRow().index - 1;
            var row = m_RowPool.Last();
            for (int i = 0; i < m_ColumnCount; i++)
            {
                var reusableItem = row.GetLastItemInRow();
                row.RemoveItemAt(row.bindableElement.childCount - 1);

                if (reusableItem.index < m_ItemsSource.Count && reusableItem.index != ReusableGridViewItem.UndefinedIndex)
                    UnbindItem(reusableItem, reusableItem.index);

                row.InsertItemAt(0, reusableItem);
                BindItem(reusableItem, itemIndex, m_ItemsSourceIds[itemIndex]);

                itemIndex--;
            }

            m_RowPool.RemoveAt(m_RowPool.Count - 1);
            m_RowPool.Insert(0, row);
            row.bindableElement.SendToBack();
            row.bindableElement.style.display = DisplayStyle.Flex;
        }

        void OnItemsSourceCollectionChanged(object sender, NotifyCollectionChangedEventArgs args)
        {
            RefreshItems();
        }

        public void RefreshItems()
        {
            if (!HasValidDataAndBindings() || m_RowPool == null || m_ItemsSourceIds == null)
                return;

            m_ItemsSourceIds.Clear();
            foreach (var item in m_ItemsSource)
                m_ItemsSourceIds.Add(item.GetHashCode());

            RefreshSelection();

            ResizeScrollView();
            ResizeColumns();
            ResizeRows();

            ReplaceActiveItems();
        }

        void RefreshSelection()
        {
            m_SelectedIndices.Clear();
            m_SelectedItems.Clear();

            if (m_SelectedIds.Count > 0)
            {
                // Add selected objects to working lists.
                for (var index = 0; index < m_ItemsSource.Count; ++index)
                {
                    if (!m_SelectedIds.Contains(m_ItemsSourceIds[index]))
                        continue;

                    m_SelectedIndices.Add(index);
                    m_SelectedItems.Add(m_ItemsSource[index]);
                }

                m_SelectedIds.Clear();
                foreach (var item in m_SelectedItems)
                    m_SelectedIds.Add(item.GetHashCode());
            }
        }

        void ReplaceActiveItems()
        {
            // Unbind and bind elements in the pool only when necessary.
            var firstVisibleItemIndex = m_FirstVisibleRowIndex * m_ColumnCount;
            var endIndex = firstVisibleItemIndex + activeItems.Count;
            var activeItemIndex = 0;
            for (int i = firstVisibleItemIndex; i < endIndex; i++)
            {
                var reusableItem = activeItems[activeItemIndex];
                activeItemIndex++;

                if (i >= m_ItemsSource.Count)
                {
                    if (reusableItem.id != ReusableGridViewItem.UndefinedIndex)
                        UnbindItem(reusableItem, reusableItem.index);

                    continue;
                }

                if (m_ItemsSourceIds[i] == reusableItem.id)
                    continue;

                UnbindItem(reusableItem, i);
            }

            activeItemIndex = 0;
            for (int i = firstVisibleItemIndex; i < endIndex; i++)
            {
                var reusableItem = activeItems[activeItemIndex];
                activeItemIndex++;

                if (m_SelectedIds.Contains(reusableItem.id))
                    reusableItem.SetSelected(true);
                else
                    reusableItem.SetSelected(false);

                if (i >= m_ItemsSource.Count)
                {
                    continue;
                }

                if (m_ItemsSourceIds[i] == reusableItem.id)
                    continue;
                BindItem(reusableItem, i, m_ItemsSourceIds[i]);
            }

            // Hide empty rows that appear in the scrollview.
            foreach (var row in m_RowPool)
                row.SetRowVisibility();
        }

        void ResizeColumns()
        {
            if (m_RowPool == null)
                return;

            var previousColumnCount = m_RowPool.Count > 0 ? m_RowPool[0].bindableElement.childCount : 0;
            if (previousColumnCount > m_ColumnCount) // Column Shrink
            {
                var removeColumnCount = Math.Clamp(previousColumnCount - m_ColumnCount, 0, previousColumnCount);
                foreach (var row in m_RowPool)
                {
                    row.UpdateRow(m_FixedItemWidth, m_FixedItemHeight, m_ColumnCount);
                    for (int i = 0; i < removeColumnCount; i++)
                    {
                        var lastItemInRow = row.GetLastItemInRow();
                        UnbindItem(lastItemInRow, lastItemInRow.index);
                        destroyItem?.Invoke(lastItemInRow.bindableElement);
                        row.RemoveItem(lastItemInRow.bindableElement);
                    }
                }
            }
            else if (previousColumnCount < m_ColumnCount) // Column Grow
            {
                var addColumnCount = m_ColumnCount - previousColumnCount;
                foreach (var row in m_RowPool)
                {
                    row.UpdateRow(m_FixedItemWidth, m_FixedItemHeight, m_ColumnCount);
                    for (int i = 0; i < addColumnCount; i++)
                        CreateReusableGridViewItem(row);
                }
            }
        }

        void ResizeRows()
        {
            if (m_RowPool == null)
                return;

            var previousRowCount = m_RowPool.Count;
            if (previousRowCount > m_RowCount) // Row Shrink
            {
                var removeRowCount = Math.Clamp(previousRowCount - m_RowCount, 0, previousRowCount);
                for (int i = 0; i < removeRowCount; i++)
                {
                    var reusableRow = m_RowPool.Last();
                    for (int j = 0; j < m_ColumnCount; j++)
                    {
                        var reusableItem = reusableRow.GetLastItemInRow();
                        UnbindItem(reusableItem, reusableItem.index);
                        destroyItem?.Invoke(reusableItem.bindableElement);
                        reusableRow.RemoveItemAt(reusableRow.bindableElement.childCount - 1);
                    }

                    m_RowPool.RemoveAt(m_RowPool.Count - 1);
                    m_ScrollView.contentContainer.RemoveAt(m_ScrollView.contentContainer.childCount - 1);
                }
            }
            else if (previousRowCount < m_RowCount) // Row Grow
            {
                var addRowCount = m_RowCount - previousRowCount;
                for (int i = 0; i < addRowCount; i++)
                {
                    var row = CreateReusableGridViewRow();
                    for (int j = 0; j < m_ColumnCount; j++)
                        CreateReusableGridViewItem(row);
                }
            }
        }

        void ResizeScrollView()
        {
            var realRowCount = GetRealRowCount(m_ColumnCount);
            m_MaximumScrollViewHeight = realRowCount * m_FixedItemHeight;
            m_ScrollView.contentContainer.style.height = m_MaximumScrollViewHeight;

            var minVisibleItemCount = Mathf.CeilToInt(m_ScrollView.contentViewport.layout.height / m_FixedItemHeight) * m_ColumnCount;
            m_VisibleItemCount = Math.Min(minVisibleItemCount, m_ItemsSource.Count);

            var scrollableHeight = Mathf.Max(0, m_MaximumScrollViewHeight - m_ScrollView.contentViewport.layout.height);
            var scrollOffset = Mathf.Min(m_ScrollOffset.y, scrollableHeight);

            m_ScrollOffset.y = scrollOffset;
            m_FirstVisibleRowIndex = (int)(scrollOffset / m_FixedItemHeight);
            m_ScrollView.verticalScroller.slider.highValue = scrollableHeight;
            m_ScrollView.verticalScroller.slider.value = scrollOffset;
            m_ScrollView.contentContainer.style.paddingTop = m_FirstVisibleRowIndex * m_FixedItemHeight;
        }

        bool CreateReusableGridViewItem(ReusableGridViewRow row)
        {
            var element = m_MakeItem.Invoke();
            if (element == null)
                return false;

            if (m_RowCount == 1)
                element.style.flexGrow = 1f;

            row.AddItem(element);

            return true;
        }

        ReusableGridViewRow CreateReusableGridViewRow()
        {
            var row = new ReusableGridViewRow();
            row.Init(m_FixedItemWidth, m_FixedItemHeight, m_ColumnCount);
            m_ScrollView.contentContainer.Add(row.bindableElement);
            m_RowPool.Add(row);

            return row;
        }

        void DestroyItems()
        {
            if (m_RowPool == null)
                return;

            foreach (var reusableItem in activeItems)
            {
                UnbindItem(reusableItem, reusableItem.index);
                destroyItem?.Invoke(reusableItem.bindableElement);
            }

            m_RowPool.Clear();
            m_RowPool = null;
        }

        void BindItem(ReusableGridViewItem reusableItem, int itemIndex, int id)
        {
            m_BindItem?.Invoke(reusableItem.bindableElement, itemIndex);
            reusableItem.id = id;
            reusableItem.index = itemIndex;
            reusableItem.bindableElement.style.visibility = Visibility.Visible;
            reusableItem.bindableElement.style.flexGrow = 0f;

            if (m_SelectedIds.Contains(id))
                reusableItem.SetSelected(true);
        }

        void UnbindItem(ReusableGridViewItem reusableItem, int itemIndex)
        {
            var id = reusableItem.id;
            unbindItem?.Invoke(reusableItem.bindableElement, itemIndex);
            reusableItem.id = reusableItem.index = ReusableGridViewItem.UndefinedIndex;
            reusableItem.bindableElement.style.visibility = Visibility.Hidden;

            if (m_RowCount == 1)
                reusableItem.bindableElement.style.flexGrow = 1f;

            if (m_SelectedIds.Contains(id))
                reusableItem.SetSelected(false);
        }

        public void Rebuild()
        {
            if (m_ItemsSource.Count == 0)
            {
                ResetGridViewState();
                ResizeScrollView();
                return;
            }

            ResetAndBuildItems();
        }

        public void Rebuild(float itemWidth, float itemHeight)
        {
            if (!float.IsNaN(itemWidth) && itemWidth > 0)
                m_FixedItemWidth = itemWidth;
            if (!float.IsNaN(itemHeight) && itemHeight > 0)
                m_FixedItemHeight = itemHeight;

            if (m_ItemsSource.Count == 0)
            {
                ResetGridViewState();
                ResizeScrollView();
                return;
            }

            ResetAndBuildItems();
        }

        void ResetGridViewState()
        {
            m_FirstVisibleRowIndex = 0;
            m_VisibleItemCount = 0;
            m_RowCount = 0;
            m_ColumnCount = 0;
            m_RangeSelectionOrigin = -1;
            m_IsRangeSelectionDirectionUp = false;

            ClearSelectionWithoutNotify();

            DestroyItems();
            m_ScrollView.contentContainer.Clear();
        }

        void ResetAndBuildItems()
        {
            ResetGridViewState();

            var scrollViewWidth = m_ScrollView.contentViewport.resolvedStyle.width;
            var scrollViewHeight = m_ScrollView.contentViewport.resolvedStyle.height;

            // When first attached, the size of the scrollView is NaN.
            if (float.IsNaN(scrollViewWidth) || float.IsNaN(scrollViewHeight))
                return;

            ComputeGridSize(scrollViewWidth, scrollViewHeight);
            BuildItems();
        }

        void OnSizeChanged(GeometryChangedEvent evt)
        {
            if (!HasValidDataAndBindings())
                return;

            if (Mathf.Approximately(evt.newRect.width, evt.oldRect.width) &&
                Mathf.Approximately(evt.newRect.height, evt.oldRect.height))
                return;

            ComputeGridSize();

            if (m_RowPool == null)
                BuildItems();
            else
                RefreshItems();
        }

        void BuildItems()
        {
            if (!HasValidDataAndBindings())
                return;

            if (m_RowCount == 0 || m_ColumnCount == 0)
                return;

            ResizeScrollView();

            m_ItemsSourceIds = new List<int>();
            foreach (var item in m_ItemsSource)
                m_ItemsSourceIds.Add(item.GetHashCode());

            m_RowPool = new List<ReusableGridViewRow>();
            var itemIndex = m_FirstVisibleRowIndex * m_ColumnCount;
            for (int i = 0; i < m_RowCount; i++)
            {
                var row = CreateReusableGridViewRow();
                for (int j = 0; j < m_ColumnCount; j++)
                {
                    if (!CreateReusableGridViewItem(row))
                        continue;

                    var reusableItem = row.GetLastItemInRow();
                    if (itemIndex >= m_ItemsSource.Count)
                    {
                        reusableItem.bindableElement.style.visibility = Visibility.Hidden;
                    }
                    else
                    {
                        BindItem(reusableItem, itemIndex, m_ItemsSourceIds[itemIndex]);
                        itemIndex++;
                    }
                }
            }

            OnScroll(m_ScrollOffset);
            itemsBuilt?.Invoke();
        }

        internal void ComputeGridSize()
        {
            var scrollViewWidth = m_ScrollView.contentViewport.layout.width;
            var scrollViewHeight = m_ScrollView.contentViewport.layout.height;

            // When first attached, the size of the scrollView is NaN.
            if (float.IsNaN(scrollViewWidth) || float.IsNaN(scrollViewHeight))
                return;
            ComputeGridSize(scrollViewWidth, scrollViewHeight);
        }

        internal void ComputeGridSize(float gridViewWidth, float gridViewHeight)
        {
            if (float.IsNaN(gridViewWidth) || gridViewWidth < 0)
                throw new ArgumentOutOfRangeException(nameof(gridViewWidth), "Specified gridview width should be non-negative.");
            if (float.IsNaN(gridViewHeight) || gridViewHeight < 0)
                throw new ArgumentOutOfRangeException(nameof(gridViewHeight), "Specified gridview height should be non-negative.");

            var newColumnCount = Math.Max(0, Mathf.FloorToInt(gridViewWidth / m_FixedItemWidth));
            if (newColumnCount == 0)
                newColumnCount = Math.Max(1, m_ColumnCount);
            var displayableRowCount = Math.Max(0, Mathf.CeilToInt(gridViewHeight / m_FixedItemHeight)) + k_ExtraRows;
            var realRowCount = GetRealRowCount(newColumnCount);
            var newRowCount = Math.Min(realRowCount, displayableRowCount);
            SetGridSize(newColumnCount, newRowCount);
        }

        internal void SetGridSize(int newColumnCount, int newRowCount)
        {
            if (newColumnCount < 0)
                throw new ArgumentOutOfRangeException(nameof(newColumnCount), "Specified column count should be non-negative.");
            if (newRowCount < 0)
                throw new ArgumentOutOfRangeException(nameof(newRowCount), "Specified row count should be non-negative.");

            m_ColumnCount = newColumnCount;
            m_RowCount = newRowCount;
        }

        int GetRealRowCount(int columnCount)
        {
            return columnCount <= 0 ? 0 : Mathf.CeilToInt((float)m_ItemsSource.Count / columnCount);
        }
    }
}
