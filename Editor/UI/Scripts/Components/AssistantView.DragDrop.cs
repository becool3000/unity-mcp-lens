using System.Collections.Generic;
using System.IO;
using Unity.AI.Assistant.Editor.Utils;
using UnityEditor;
using UnityEngine.UIElements;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantView
    {
        VisualElement m_DropZoneRoot;
        ChatDropZone m_DropZone;
        VisualElement m_DropZoneOverlay;

        void OnDropped(IEnumerable<object> obj)
        {
            bool anyAdded = false;

            foreach (object droppedObject in obj)
            {
                if (droppedObject is string filePath)
                {
                    if (AddFilePathToContext(filePath))
                    {
                        anyAdded = true;
                    }
                }
                else if (AddObjectToContext(droppedObject))
                {
                    anyAdded = true;
                }
            }

            if (anyAdded)
            {
                UpdateContextSelectionElements(true);
            }

            m_DropZone.SetDropZoneActive(false);
            ResetDropZoneOverlay();
        }

        bool AddFilePathToContext(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            try
            {
                // Try to process as image first using shared utility
                var imageAttachment = ContextUtils.ProcessImageFileForContext(filePath);
                if (imageAttachment != null)
                {
                    Context.Blackboard.AddVirtualAttachment(imageAttachment);
                    Context.VirtualAttachmentAdded?.Invoke(imageAttachment);
                    return true;
                }

                // Handle non-image files
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                string fileExtension = fileInfo.Extension.ToLowerInvariant();

                if (fileInfo.Length > AssistantConstants.MaxImageFileSize)
                    return false;

                string placeholderContent = $"[File: {fileName}]\nFile type: {fileExtension}\nSize: {fileInfo.Length} bytes";
                var attachment = new VirtualAttachment(placeholderContent, "File", fileName, string.Empty);
                Context.Blackboard.AddVirtualAttachment(attachment);
                Context.VirtualAttachmentAdded?.Invoke(attachment);

                return true;
            }
            catch
            {
                return false;
            }
        }

        bool IsSupportedAsset(UnityEngine.Object unityObject) => unityObject is not DefaultAsset || FolderContextUtils.IsFolderAsset(unityObject);

        void OnMainDragEnter(DragEnterEvent evt)
        {
#if UNITY_EDITOR_OSX
            if (evt.pressedButtons == 0)
                return;
#endif

            bool hasSupportedContent = false;

            if (DragAndDrop.objectReferences != null && DragAndDrop.objectReferences.Length > 0)
            {
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (IsSupportedAsset(obj))
                    {
                        hasSupportedContent = true;
                        break;
                    }
                }
            }

            if (!hasSupportedContent && DragAndDrop.paths != null && DragAndDrop.paths.Length > 0)
            {
                foreach (string path in DragAndDrop.paths)
                {
                    if (IsSupportedFilePath(path))
                    {
                        hasSupportedContent = true;
                        break;
                    }
                }
            }

            if (hasSupportedContent)
            {
                m_DropZoneOverlay.pickingMode = PickingMode.Position;
                m_DropZone.SetDropZoneActive(true);
            }
        }

        bool IsSupportedFilePath(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > AssistantConstants.MaxImageFileSize)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        void OnMainDragLeave(DragLeaveEvent evt)
        {
            ResetDropZoneOverlay();
        }

        void OnMainDragExit(DragExitedEvent evt)
        {
            ResetDropZoneOverlay();
        }

        void ResetDropZoneOverlay()
        {
            m_DropZoneOverlay.pickingMode = PickingMode.Ignore;
            m_DropZone.SetDropZoneActive(false);
        }
    }
}
