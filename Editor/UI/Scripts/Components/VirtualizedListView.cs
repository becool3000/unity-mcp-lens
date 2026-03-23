using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class VirtualizedListView<TD, TV> : VisualElement where TV : VisualElement
    {
        readonly IList<TD> k_Data = new List<TD>();

        ListView m_ListView;
        AssistantUIContext m_Context;
        bool m_UpdateInProgress;

        public IList<TD> Data => k_Data;

        public event Action<int, TD> SelectionChanged;

        public void Initialize(AssistantUIContext context)
        {
            m_Context = context;

            m_ListView = new ListView();
            m_ListView.selectionType = SelectionType.None;
            m_ListView.reorderable = false;
            m_ListView.itemsSource = (IList)k_Data;
            m_ListView.makeItem = MakeItem;
            m_ListView.bindItem = BindItem;
            m_ListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;

            Add(m_ListView);
        }

        public void SetSelectionType(SelectionType type)
        {
            m_ListView.selectionType = type;
            
            m_ListView.selectionChanged -= OnSelectionChanged;

            switch (type)
            {
                case SelectionType.Single:
                case SelectionType.Multiple:
                {
                    m_ListView.selectionChanged += OnSelectionChanged;
                    break;
                }
            }
        }

        public void AddData(TD item)
        {
            k_Data.Add(item);

            if (!m_UpdateInProgress)
            {
                m_ListView.RefreshItems();
            }
        }

        public void ClearData()
        {
            k_Data.Clear();
            m_ListView.RefreshItems();
        }

        public void BeginUpdate()
        {
            m_UpdateInProgress = true;
        }

        public void EndUpdate()
        {
            m_UpdateInProgress = false;
            m_ListView.RefreshItems();
        }

        public void SetSelectionWithoutNotify(int index, bool scrollToSelection)
        {
            m_ListView.SetSelectionWithoutNotify(new[] { index });
            if (scrollToSelection)
            {
                m_ListView.ScrollToItem(index);
            }
        }

        public void ScrollToStart()
        {
            m_ListView.ScrollToItem(0);
        }

        public void ClearSelection()
        {
            m_ListView.ClearSelection();
        }

        VisualElement MakeItem()
        {
            var element = Activator.CreateInstance<TV>();
            if (element is ManagedTemplate managedTemplate)
            {
                managedTemplate.Initialize(m_Context);
            }
            return element;
        }

        void BindItem(VisualElement element, int index)
        {
            if (element is TV typedElement && index < k_Data.Count)
            {
                BindItemToData(typedElement, k_Data[index], index);
            }
        }

        protected virtual void BindItemToData(TV element, TD data, int index)
        {
            if (element is ManagedListEntry entry)
            {
                entry.SetData(index, data);
            }
        }

        void OnSelectionChanged(IEnumerable<object> selectedItems)
        {
            var selectedIndex = m_ListView.selectedIndex;
            if (selectedIndex >= 0 && selectedIndex < k_Data.Count)
            {
                SelectionChanged?.Invoke(selectedIndex, k_Data[selectedIndex]);
            }
        }
    }
}
