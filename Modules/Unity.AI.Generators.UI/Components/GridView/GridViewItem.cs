using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class ReusableGridViewItem
    {
        public const int UndefinedIndex = -1;

        const string k_GridViewSelectedItemStyleClassName = "grid-view-items__selected";

        public VisualElement bindableElement { get; private set; }
        public int index { get; set; }
        public int id { get; set; }

        public ReusableGridViewItem()
        {
            index = id = UndefinedIndex;
        }

        public void Init(VisualElement element, float itemWidth, float itemHeight)
        {
            bindableElement = element;
            SetupItem(itemWidth, itemHeight);
        }

        public void SetupItem(float itemWidth, float itemHeight)
        {
            bindableElement.style.height = itemHeight;
            bindableElement.style.width = itemWidth;
            bindableElement.style.flexShrink = 0;
            bindableElement.style.visibility = Visibility.Hidden;
        }

        public void SetSelected(bool selected)
        {
            if (selected)
                bindableElement.AddToClassList(k_GridViewSelectedItemStyleClassName);
            else
                bindableElement.RemoveFromClassList(k_GridViewSelectedItemStyleClassName);
        }
    }

    class ReusableGridViewRow
    {
        float m_ItemHeight;
        float m_ItemWidth;
        int m_MaxItemCount;
        List<ReusableGridViewItem> m_Items;

        public VisualElement bindableElement { get; private set; }
        public int index { get; }
        public int id { get; }

        public ReusableGridViewRow()
        {
            index = id = ReusableGridViewItem.UndefinedIndex;
        }

        public void Init(float itemWidth, float itemHeight, int itemCount)
        {
            m_ItemWidth = itemWidth;
            m_ItemHeight = itemHeight;
            m_MaxItemCount = itemCount;
            m_Items = new List<ReusableGridViewItem>();
            var row = CreateRow(itemHeight);
            bindableElement = row;
        }

        public void UpdateRow(float itemWidth, float itemHeight, int itemCount)
        {
            m_ItemWidth = itemWidth;
            m_ItemHeight = itemHeight;
            m_MaxItemCount = itemCount;
        }

        public VisualElement CreateRow(float itemHeight)
        {
            var row = new VisualElement
            {
                style =
                {
                    flexDirection = FlexDirection.Row,
                    flexShrink = 0,
                    height = itemHeight,
                    justifyContent = Justify.SpaceBetween
                }
            };

            return row;
        }

        public void AddItem(ReusableGridViewItem reusableItem)
        {
            if (bindableElement.childCount > m_MaxItemCount)
                return;

            reusableItem.Init(reusableItem.bindableElement, m_ItemWidth, m_ItemHeight);
            m_Items.Add(reusableItem);
            bindableElement.Add(reusableItem.bindableElement);
        }

        public void AddItem(VisualElement element)
        {
            if (bindableElement.childCount > m_MaxItemCount)
                return;

            var reusableItem = new ReusableGridViewItem();
            reusableItem.Init(element, m_ItemWidth, m_ItemHeight);
            m_Items.Add(reusableItem);
            bindableElement.Add(reusableItem.bindableElement);
        }

        public void RemoveItem(VisualElement element)
        {
            if (m_Items == null)
                return;

            foreach (var item in m_Items)
            {
                if (item.bindableElement == element)
                {
                    m_Items.Remove(item);
                    bindableElement.Remove(element);
                    return;
                }
            }
        }

        public void RemoveItemAt(int indexInRow)
        {
            if (m_Items == null)
                return;

            m_Items.RemoveAt(indexInRow);
            bindableElement.RemoveAt(indexInRow);
        }

        public void InsertItemAt(int indexInRow, ReusableGridViewItem item)
        {
            if (bindableElement.childCount > m_MaxItemCount)
                return;

            m_Items.Insert(indexInRow, item);
            bindableElement.Insert(indexInRow, item.bindableElement);
        }

        public bool IsEmpty()
        {
            if (m_Items == null)
                return true;

            if (m_Items.Count == 0 || ContainsUnboundItems())
                return true;

            return false;
        }

        bool ContainsUnboundItems()
        {
            if (m_Items == null)
                return true;

            foreach (var item in m_Items)
            {
                if (item.index == ReusableGridViewItem.UndefinedIndex)
                    continue;

                return false;
            }

            return true;
        }

        public void SetRowVisibility()
        {
            if (IsEmpty())
                bindableElement.style.display = DisplayStyle.None;
            else
                bindableElement.style.display = DisplayStyle.Flex;
        }

        public List<ReusableGridViewItem> GetItems()
        {
            return m_Items;
        }

        public ReusableGridViewItem GetItemAt(int indexInRow)
        {
            if (m_Items == null || m_Items.Count == 0)
                return null;

            return m_Items[indexInRow];
        }

        public ReusableGridViewItem GetLastItemInRow()
        {
            if (m_Items == null || m_Items.Count == 0)
                return null;

            return m_Items.Last();
        }

        public ReusableGridViewItem GetFirstItemInRow()
        {
            if (m_Items == null || m_Items.Count == 0)
                return null;

            return m_Items.First();
        }
    }
}
