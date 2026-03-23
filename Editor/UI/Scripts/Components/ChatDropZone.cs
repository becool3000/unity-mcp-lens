using System;
using System.Collections.Generic;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class ChatDropZone : ManagedTemplate
    {
        VisualElement m_DropZoneContent;
        Action<IEnumerable<object>> m_DropCallback;

        public ChatDropZone()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        public void SetupDragDrop(VisualElement targetElement, Action<IEnumerable<object>> onDropped)
        {
            targetElement.RegisterCallback<DragEnterEvent>(OnDragEnter);
            targetElement.RegisterCallback<DragLeaveEvent>(OnDragLeave);
            targetElement.RegisterCallback<DragPerformEvent>(OnDragPerform);
            targetElement.RegisterCallback<DragUpdatedEvent>(OnDragUpdate);
            targetElement.RegisterCallback<DragExitedEvent>(OnDragExit);

            m_DropCallback = onDropped;
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_DropZoneContent = view.Q<VisualElement>("chatDropZoneContent");
        }

        public void SetDropZoneActive(bool active)
        {
            this.SetDisplay(active);
        }

        void OnDragEnter(DragEnterEvent evt)
        {
            UpdateDragState();
        }

        void OnDragLeave(DragLeaveEvent evt)
        {
        }

        void OnDragUpdate(DragUpdatedEvent evt)
        {
            UpdateDragState();
        }

        void OnDragExit(DragExitedEvent evt)
        {
        }

        void OnDragPerform(DragPerformEvent evt)
        {
            if (!IsDraggingObjects())
            {
                return;
            }

            if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
            {
                m_DropCallback?.Invoke(DragAndDrop.objectReferences);
                DragAndDrop.objectReferences = null;
            }
            else if (DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                List<object> filePaths = new List<object>();
                foreach (string path in DragAndDrop.paths)
                {
                    filePaths.Add(path);
                }

                m_DropCallback?.Invoke(filePaths);
                DragAndDrop.paths = null;
            }
        }

        bool IsDraggingObjects()
        {
            bool hasUnityObjects = DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0;
            bool hasExternalFiles = DragAndDrop.paths != null && DragAndDrop.paths.Length > 0;
            return hasUnityObjects || hasExternalFiles;
        }

        void UpdateDragState()
        {
            if (!IsDraggingObjects())
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Rejected;
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
        }
    }
}
