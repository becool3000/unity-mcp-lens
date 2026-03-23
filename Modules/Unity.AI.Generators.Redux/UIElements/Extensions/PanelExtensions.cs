using System;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UIElements.Core;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UIElements.Extensions
{
    static class PanelExtensions
    {
        public static Unsubscribe OnPanel(this VisualElement element, EventCallback<AttachToPanelEvent> onAttach, EventCallback<DetachFromPanelEvent> onDetach)
        {
            element.RegisterCallback(onAttach);
            element.RegisterCallback(onDetach);

            return () =>
            {
                element.UnregisterCallback(onAttach);
                element.UnregisterCallback(onDetach);
                return true;
            };
        }

        public static Unsubscribe OnPanel(this VisualElement element, Action onAttach, Action onDetach) =>
            element.OnPanel(_ => onAttach(), _ => onDetach());

        /// <summary>
        /// Calls method whenever the element is present in a panel.
        /// </summary>
        public static Unsubscribe OnLive(this VisualElement element, Action onAttach, Action onDetach)
        {
            bool isAttached = false;

            void AttachHandler(AttachToPanelEvent _)
            {
                if (!isAttached)
                {
                    onAttach();
                    isAttached = true;
                }
            }

            void DetachHandler(DetachFromPanelEvent _)
            {
                if (isAttached)
                {
                    onDetach();
                    isAttached = false;
                }
            }

            var unsubscribe = element.OnPanel(
                AttachHandler,
                DetachHandler
            );

            // Panel is live
            if (element.panel != null)
                AttachHandler(null);

            return () =>
            {
                DetachHandler(null);
                return unsubscribe();
            };
        }

        public static Unsubscribe OnLive(this VisualElement element, LifecycleAsync lifecycle) =>
            element.OnLive(lifecycle.Subscribe, lifecycle.UnsubscribeAction);

        public static Unsubscribe OnLive(this VisualElement element, SubscribeAsync subscribe) =>
            element.OnLive(new LifecycleAsync(subscribe));

        public static Unsubscribe OnLive(this VisualElement element, Subscribe subscribe) =>
            element.OnLive(new Lifecycle(subscribe));
    }
}
