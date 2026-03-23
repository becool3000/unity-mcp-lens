using System;

namespace Unity.AI.Assistant.Editor.Utils.Event
{
    class BaseEventTargetCollection<T>
        where T : class
    {
        const byte k_DefaultEventTargetSize = 100;

        int m_NextFreeIndex;

        public BaseEventTargetCollection()
        {
            Targets = Array.Empty<BaseEventSubscriptionTicket>();

            // Do one increase to set the initial size
            IncreaseSize();
        }

        public BaseEventSubscriptionTicket[] Targets;

        public int Capacity;
        public int Occupied;

        public void Add(BaseEventSubscriptionTicket newTarget)
        {
            if (Occupied == Capacity)
            {
                IncreaseSize();
            }

            Targets[m_NextFreeIndex] = newTarget;
            Occupied++;

            FindFreeIndex();
        }

        public bool Remove(BaseEventSubscriptionTicket target)
        {
            if (Occupied == 0)
            {
                return false;
            }

            int checkCount = 0;
            for (var i = 0; i < Targets.Length; i++)
            {
                if (Targets[i] == null)
                {
                    continue;
                }

                if (Targets[i] == target)
                {
                    ClearSlot(i);
                    return true;
                }

                checkCount++;
                if (checkCount == Occupied)
                {
                    return false;
                }
            }

            return false;
        }

        public void Send<TSpecific>(TSpecific eventData)
            where TSpecific : T
        {
            int sentCount = 0;
            for (var i = 0; i < Targets.Length; i++)
            {
                if (Targets[i] == null)
                {
                    continue;
                }

                BaseEventSubscriptionTicket target = Targets[i];
                if (target.FilterDelegate != null)
                {
                    if (target.FilterDelegate(eventData))
                    {
                        ((BaseEventAggregate<T>.EventAction<TSpecific>)target.TargetDelegate).Invoke(eventData);
                    }
                }
                else
                {
                    ((BaseEventAggregate<T>.EventAction<TSpecific>)target.TargetDelegate).Invoke(eventData);
                }

                sentCount++;
                if (sentCount == Occupied)
                {
                    // Avoid loop if we already covered all targets
                    break;
                }
            }
        }

        void ClearSlot(int index)
        {
            Targets[index] = null;
            Occupied--;
            if (m_NextFreeIndex > index)
            {
                m_NextFreeIndex = index;
            }
        }

        void IncreaseSize()
        {
            Array.Resize(ref Targets, Targets.Length + k_DefaultEventTargetSize);
            Capacity = Targets.Length;

            m_NextFreeIndex = 0;
            FindFreeIndex();
        }

        void FindFreeIndex()
        {
            for (var i = m_NextFreeIndex; i < Targets.Length; i++)
            {
                if (Targets[i] == null)
                {
                    m_NextFreeIndex = i;
                    break;
                }
            }
        }
    }
}
