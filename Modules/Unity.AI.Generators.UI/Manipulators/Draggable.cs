using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class Draggable : Manipulator
    {
        readonly Action<Vector3> m_OnDrag;

        readonly Action m_OnDragStart;

        readonly Action m_OnDragEnd;

        public Draggable(Action onDragStart, Action<Vector3> onDrag, Action onDragEnd)
        {
            m_OnDragStart = onDragStart;
            m_OnDrag = onDrag;
            m_OnDragEnd = onDragEnd;
        }

        public Vector3 worldPosition { get; private set; }

        public Vector3 deltaPosition { get; private set; }

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<PointerDownEvent>(OnPointerDown);
            target.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            target.RegisterCallback<PointerUpEvent>(OnPointerUp);
            target.RegisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            target.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
            target.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            target.UnregisterCallback<PointerCancelEvent>(OnPointerCancel);
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            target.CapturePointer(evt.pointerId);
            worldPosition = evt.position;
            deltaPosition = Vector3.zero;
            evt.StopPropagation();
            m_OnDragStart?.Invoke();
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (target.HasPointerCapture(evt.pointerId))
            {
                deltaPosition = evt.position - worldPosition;
                worldPosition = evt.position;
                evt.StopPropagation();
                m_OnDrag?.Invoke(deltaPosition);
            }
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (target.HasPointerCapture(evt.pointerId))
            {
                target.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
                m_OnDragEnd?.Invoke();
            }
        }

        void OnPointerCancel(PointerCancelEvent evt)
        {
            if (target.HasPointerCapture(evt.pointerId))
            {
                target.ReleasePointer(evt.pointerId);
                evt.StopPropagation();
                m_OnDragEnd?.Invoke();
            }
        }
    }
}
