using System;

namespace Unity.AI.Assistant.Editor.Utils.Event
{
    static class AssistantEvents
    {
        static BaseEventAggregate<IAssistantEvent> s_Aggregate = new();

        public static BaseEventSubscriptionTicket Subscribe<TSpecific>(BaseEventAggregate<IAssistantEvent>.EventAction<TSpecific> actionDelegate, Func<TSpecific, bool> filterDelegate = null)
            where TSpecific : IAssistantEvent
        {
            return s_Aggregate.Subscribe(actionDelegate, filterDelegate);
        }

        public static void Unsubscribe(ref BaseEventSubscriptionTicket ticket)
        {
            s_Aggregate.Unsubscribe(ref ticket);
        }

        public static void Send<TSpecific>(TSpecific eventData)
            where TSpecific : IAssistantEvent
        {
            s_Aggregate.Send(eventData);
        }
    }
}
