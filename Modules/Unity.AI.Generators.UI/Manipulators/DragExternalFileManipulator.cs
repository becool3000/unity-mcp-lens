using System;
using System.IO;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class DragExternalFileManipulator : MouseManipulator
    {
        bool m_IsDragging;
        Vector2 m_MouseDownPos;
        public string externalFilePath { get; set; }
        public string newFileName { get; set; }

        public Func<CopyFunctionData, string> copyFunction { get; set; }
        public Func<MoveFunctionData, string> moveDependencies { get; set; }
        public Func<CompareFunctionData, bool> compareFunction { get; set; }

        const float k_DragThreshold = 5f;

        public DragExternalFileManipulator() => activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });

        protected override void RegisterCallbacksOnTarget()
        {
            target.RegisterCallback<MouseDownEvent>(OnMouseDown);
            target.RegisterCallback<MouseMoveEvent>(OnMouseMove);
            target.RegisterCallback<MouseUpEvent>(OnMouseUp);
            target.RegisterCallback<MouseLeaveEvent>(OnMouseLeave);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown);
            target.UnregisterCallback<MouseMoveEvent>(OnMouseMove);
            target.UnregisterCallback<MouseUpEvent>(OnMouseUp);
            target.UnregisterCallback<MouseLeaveEvent>(OnMouseLeave);
        }

        void OnMouseLeave(MouseLeaveEvent evt) => m_IsDragging = false;

        void OnMouseDown(MouseDownEvent evt)
        {
            if (!CanStartManipulation(evt))
                return;

            m_MouseDownPos = evt.mousePosition;
            m_IsDragging = !string.IsNullOrEmpty(externalFilePath) && File.Exists(externalFilePath);
        }

        void OnMouseMove(MouseMoveEvent evt)
        {
            if (!m_IsDragging)
                return;

            if (!((evt.mousePosition - m_MouseDownPos).magnitude > k_DragThreshold))
                return;

            ExternalFileDragDrop.StartDragFromExternalPath(externalFilePath, newFileName, copyFunction, moveDependencies, compareFunction);
            m_IsDragging = false;
        }

        void OnMouseUp(MouseUpEvent evt)
        {
            if (!CanStopManipulation(evt))
                return;

            m_IsDragging = false;
        }
    }
}
