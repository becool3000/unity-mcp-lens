using System;
using System.Collections.Generic;

namespace Unity.AI.Search.Editor.Utilities
{
    // Custom PriorityQueue for .NET Standard 2.1 compatibility.
    // System.Collections.Generic.PriorityQueue requires .NET 6+.
    class PriorityQueue<TElement, TPriority>
    {
        struct Node
        {
            public TElement Element;
            public TPriority Priority;
        }

        readonly List<Node> m_Heap = new List<Node>();
        readonly IComparer<TPriority> m_Comparer;

        public PriorityQueue() : this(null) { }

        public PriorityQueue(IComparer<TPriority> comparer)
        {
            m_Comparer = comparer ?? Comparer<TPriority>.Default;
        }

        public int Count => m_Heap.Count;

        public void Enqueue(TElement element, TPriority priority)
        {
            m_Heap.Add(new Node { Element = element, Priority = priority });
            SiftUp(m_Heap.Count - 1);
        }

        public TElement Dequeue()
        {
            if (m_Heap.Count == 0)
                throw new InvalidOperationException("The priority queue is empty.");

            var root = m_Heap[0];
            var lastIndex = m_Heap.Count - 1;
            var last = m_Heap[lastIndex];
            m_Heap.RemoveAt(lastIndex);
            if (m_Heap.Count > 0)
            {
                m_Heap[0] = last;
                SiftDown(0);
            }
            return root.Element;
        }

        public bool TryPeek(out TElement element, out TPriority priority)
        {
            if (m_Heap.Count == 0)
            {
                element = default;
                priority = default;
                return false;
            }
            var node = m_Heap[0];
            element = node.Element;
            priority = node.Priority;
            return true;
        }

        void SiftUp(int index)
        {
            while (index > 0)
            {
                var parent = (index - 1) / 2;
                if (m_Comparer.Compare(m_Heap[index].Priority, m_Heap[parent].Priority) >= 0)
                    break;
                Swap(index, parent);
                index = parent;
            }
        }

        void SiftDown(int index)
        {
            var count = m_Heap.Count;
            while (true)
            {
                var left = 2 * index + 1;
                var right = left + 1;
                var smallest = index;

                if (left < count && m_Comparer.Compare(m_Heap[left].Priority, m_Heap[smallest].Priority) < 0)
                    smallest = left;
                if (right < count && m_Comparer.Compare(m_Heap[right].Priority, m_Heap[smallest].Priority) < 0)
                    smallest = right;
                if (smallest == index)
                    break;
                Swap(index, smallest);
                index = smallest;
            }
        }

        void Swap(int i, int j)
        {
            var temp = m_Heap[i];
            m_Heap[i] = m_Heap[j];
            m_Heap[j] = temp;
        }
    }
}


