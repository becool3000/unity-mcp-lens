using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class ManagedScrollView<TData, TElement> where TElement : ManagedListEntry, new()
    {
        readonly List<TData> m_Data = new();
        readonly List<TElement> m_Elements = new();
        readonly ScrollView m_ScrollView;

        AssistantUIContext m_Context;

        public IReadOnlyList<TData> Data => m_Data;

        public int Count => m_Data.Count;

        public ManagedScrollView(ScrollView scrollView)
        {
            m_ScrollView = scrollView ?? throw new ArgumentNullException(nameof(scrollView));
        }

        public void Initialize(AssistantUIContext context)
        {
            m_Context = context;
        }

        public void ClearData()
        {
            m_Data.Clear();
            foreach (var element in m_Elements)
            {
                m_ScrollView.Remove(element);
            }
            m_Elements.Clear();
        }

        public void AddData(TData data)
        {
            m_Data.Add(data);

            var element = new TElement();
            element.Initialize(m_Context);
            element.SetData(m_Data.Count - 1, data);

            m_Elements.Add(element);
            m_ScrollView.Add(element);
        }

        public void SetData(IList<TData> newData)
        {
            ClearData();
            foreach (var data in newData)
            {
                AddData(data);
            }
        }
    }
}
