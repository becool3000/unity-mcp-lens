using System;

namespace Unity.AI.Assistant.Editor.Utils.Event
{
    class BaseEventSubscriptionTicket
    {
        public BaseEventSubscriptionTicket(Type targetType, object targetDelegate)
        {
            TargetType = targetType;
            TargetDelegate = targetDelegate;
        }

        public object TargetDelegate { get; private set; }

        public Type TargetType { get; private set; }

        public Func<object, bool> FilterDelegate { get; set; }
    }
}