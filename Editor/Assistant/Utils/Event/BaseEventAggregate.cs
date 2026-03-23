using System;
using System.Collections.Generic;

namespace Unity.AI.Assistant.Editor.Utils.Event
{
    class BaseEventAggregate<T>
        where T : class
    {
        readonly IDictionary<Type, BaseEventTargetCollection<T>> m_Subscribers;

        public BaseEventAggregate()
        {
            m_Subscribers = new Dictionary<Type, BaseEventTargetCollection<T>>();
        }

        public delegate void EventAction<in TSpecific>(TSpecific eventData)
            where TSpecific : T;

        public BaseEventSubscriptionTicket Subscribe<TSpecific>(EventAction<TSpecific> actionDelegate, Func<TSpecific, bool> filterDelegate = null)
            where TSpecific : T
        {
            var ticket = new BaseEventSubscriptionTicket(TypeDef<TSpecific>.Value, actionDelegate);
            if (filterDelegate != null)
            {
                ticket.FilterDelegate = x => filterDelegate((TSpecific)x);
            }

            DoSubscribe(ticket);
            return ticket;
        }

        public void Unsubscribe(ref BaseEventSubscriptionTicket ticket)
        {
            if (ticket == null)
            {
                return;
            }

            DoUnsubscribe(ticket);
            ticket = null;
        }

        public void Send<TSpecific>(TSpecific eventData)
            where TSpecific : T
        {
            DoSend(eventData);
        }

        protected void DoSubscribe(BaseEventSubscriptionTicket ticket)
        {
            lock (m_Subscribers)
            {
                if (!m_Subscribers.TryGetValue(ticket.TargetType, out var targets))
                {
                    targets = new BaseEventTargetCollection<T>();
                    m_Subscribers.Add(ticket.TargetType, targets);
                }

                targets.Add(ticket);
            }
        }

        protected void DoUnsubscribe(BaseEventSubscriptionTicket ticket)
        {
            lock (m_Subscribers)
            {
                if (m_Subscribers.TryGetValue(ticket.TargetType, out var targets)
                    && targets.Remove(ticket))
                {
                    return;
                }

                throw new InvalidOperationException("Unsubscribe could not find the given target");
            }
        }

        // NOTE: We snapshot the targets array reference outside the lock to prevent deadlocks when
        // callbacks unsubscribe during iteration. This means an unsubscribed handler might still
        // receive one in-flight call. If strict "no call after unsubscribe" is required, add an
        // IsValid flag to BaseEventSubscriptionTicket and check it before invoking.
        protected void DoSend<TSpecific>(TSpecific eventData)
            where TSpecific : T
        {
            BaseEventSubscriptionTicket[] pendingTargets;
            int occupied;

            lock (m_Subscribers)
            {
                if (!m_Subscribers.TryGetValue(TypeDef<TSpecific>.Value, out var targets) || targets.Occupied == 0)
                {
                    return;
                }

                pendingTargets = targets.Targets;
                occupied = targets.Occupied;
            }

            int sentCount = 0;
            for (var i = 0; i < pendingTargets.Length && sentCount < occupied; i++)
            {
                var target = pendingTargets[i];
                if (target == null)
                {
                    continue;
                }

                if (target.FilterDelegate == null || target.FilterDelegate(eventData))
                {
                    ((EventAction<TSpecific>)target.TargetDelegate).Invoke(eventData);
                }

                sentCount++;
            }
        }
    }
}
