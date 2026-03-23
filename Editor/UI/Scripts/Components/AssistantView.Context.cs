using System.Collections.Generic;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using Unity.Assistant.UI.Components;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantView
    {
        readonly List<AssistantContextEntry> k_SelectedContext = new();

        bool m_DelayedUpdateContextElements;
        float m_LastReportTime;

        void RegisterContextCallbacks()
        {
            ContextMenuUtility.ObjectsAttached += OnObjectsAttached;
            ConsoleUI.s_OnLogsAdded += OnLogAdded;
            Context.VirtualAttachmentAdded += OnVirtualAttachmentAdded;
        }

        void UnregisterContextCallbacks()
        {
            ContextMenuUtility.ObjectsAttached -= OnObjectsAttached;
            ConsoleUI.s_OnLogsAdded -= OnLogAdded;
            Context.VirtualAttachmentAdded -= OnVirtualAttachmentAdded;
        }

        void OnVirtualAttachmentAdded(VirtualAttachment attachment)
        {
            k_SelectedContext.Add(attachment.ToContextEntry());
            UpdateContextSelectionElements(true);
        }

        private void OnObjectsAttached(IEnumerable<Object> objects)
        {
            bool anyAdded = false;

            foreach (var obj in objects)
            {
                if (AddObjectToContext(obj))
                {
                    anyAdded = true;
                }
            }

            if (anyAdded)
            {
                UpdateContextSelectionElements(true);
            }
        }

        bool AddObjectToContext(object droppedObject)
        {
            if (droppedObject is not UnityEngine.Object unityObject)
            {
                return false;
            }

            if (unityObject == null)
            {
                return false;
            }

            if (!IsSupportedAsset(unityObject))
            {
                var currentTime = Time.time;
                if (currentTime - m_LastReportTime > AssistantUIConstants.UIAnalyticsDebounceInterval)
                {
                    AIAssistantAnalytics.ReportContextDragDropAttachedContextEvent(unityObject);
                    m_LastReportTime = currentTime;
                }
                return false;
            }

            var contextEntry = unityObject.GetContextEntry();
            if (k_SelectedContext.Contains(contextEntry))
            {
                return false;
            }

            AIAssistantAnalytics.ReportContextDragDropAttachedContextEvent(contextEntry);
            k_SelectedContext.Add(contextEntry);
            return true;
        }

        void CheckContextForDeletedAssets(string[] paths)
        {
            if (m_DelayedUpdateContextElements)
            {
                return;
            }

            var pathHash = new HashSet<string>(paths);
            for (var i = 0; i < k_SelectedContext.Count; i++)
            {
                var entry = k_SelectedContext[i];
                switch (entry.EntryType)
                {
                    case AssistantContextType.HierarchyObject:
                    case AssistantContextType.SubAsset:
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(entry.Value);
                        if (pathHash.Contains(assetPath))
                        {
                            m_DelayedUpdateContextElements = true;
                            EditorApplication.delayCall += () => UpdateContextSelectionElements(true);
                            return;
                        }

                        break;
                    }
                }
            }
        }

        void RestoreContextSelection(List<AssistantContextEntry> contextEntries)
        {
            k_SelectedContext.AddRange(contextEntries);
        }

        void SyncContextSelection(IReadOnlyList<UnityEngine.Object> objectList, IReadOnlyList<LogData> consoleList)
        {
            var deleteList = new List<AssistantContextEntry>();
            foreach (AssistantContextEntry contextEntry in k_SelectedContext)
            {
                switch (contextEntry.EntryType)
                {
                    case AssistantContextType.Virtual:
                    {
                        break;
                    }

                    default:
                    {
                        deleteList.Add(contextEntry);
                        break;
                    }
                }
            }

            foreach (var contextEntry in deleteList)
            {
                k_SelectedContext.Remove(contextEntry);
            }

            if (objectList != null)
            {
                for (var i = 0; i < objectList.Count; i++)
                {
                    var entry = objectList[i].GetContextEntry();
                    if (k_SelectedContext.Contains(entry))
                    {
                        continue;
                    }

                    k_SelectedContext.Add(entry);
                }
            }

            if (consoleList != null)
            {
                for (var i = 0; i < consoleList.Count; i++)
                {
                    var entry = consoleList[i].GetContextEntry();
                    if (k_SelectedContext.Contains(entry))
                    {
                        continue;
                    }

                    k_SelectedContext.Add(entry);
                }
            }
        }

        void OnLogAdded(IEnumerable<LogData> logDatas)
        {
            bool anyAdded = false;

            foreach (var data in logDatas)
            {
                var entry = data.GetContextEntry();
                if (!k_SelectedContext.Contains(entry))
                {
                    k_SelectedContext.Add(entry);
                    anyAdded = true;
                }
            }

            if (anyAdded)
            {
                UpdateContextSelectionElements();
            }
        }

        void RemoveInvalidContextEntries()
        {
            IList<AssistantContextEntry> deleteList = new List<AssistantContextEntry>();
            for (var i = 0; i < k_SelectedContext.Count; i++)
            {
                var entry = k_SelectedContext[i];
                switch (entry.EntryType)
                {
                    case AssistantContextType.HierarchyObject:
                    case AssistantContextType.SubAsset:
                    case AssistantContextType.SceneObject:
                    {
                        if (entry.GetTargetObject() == null)
                        {
                            deleteList.Add(entry);
                        }

                        break;
                    }

                    case AssistantContextType.Component:
                    {
                        if (entry.GetComponent() == null)
                        {
                            deleteList.Add(entry);
                        }

                        break;
                    }
                }
            }

            for (var i = 0; i < deleteList.Count; i++)
            {
                k_SelectedContext.Remove(deleteList[i]);
            }
        }

        void UpdateContextSelectionElements(bool updatePopup = false)
        {
            if (updatePopup && m_SelectionPopup.visible)
            {
                m_SelectionPopup.ScheduleSearchRefresh();
            }

            RemoveInvalidContextEntries();

            m_ClearContextButton?.UnregisterCallback<PointerUpEvent>(ClearContext);
            m_SelectedContextRoot.Clear();
            m_SelectedContextDropdown.ClearData();
            m_ContextDropdownButton.SetDisplay(false);
            m_ClearContextButton.SetDisplay(false);

            if (k_SelectedContext.Count > 0)
            {
                m_SelectedContextRoot.SetDisplay(true);
                m_SelectedContextDropdown.AddChoicesToDropdown(k_SelectedContext, this);
                if (k_SelectedContext.Count >= AssistantConstants.AttachedContextDisplayLimit)
                {
                    m_ContextDropdownButton.SetDisplay(true);
                    AddItemsNumberToLabel(k_SelectedContext.Count);
                }
                else
                {
                    if (m_SelectedContextDropdown.IsShown)
                    {
                        ToggleContextDropdown();
                    }

                    var entries = m_SelectedContextDropdown.GetEntries();
                    for (var i = 0; i < k_SelectedContext.Count; i++)
                    {
                        var newElement = new ContextElement();
                        newElement.Initialize(Context);
                        newElement.SetData(i, entries[i]);
                        newElement.SetOwner(this);
                        newElement.RemoveListStyles(i);
                        m_SelectedContextRoot.Add(newElement);
                    }
                }

                m_ClearContextButton.SetDisplay(true);
                m_ClearContextButton?.RegisterCallback<PointerUpEvent>(ClearContext);

                m_SelectedContextRoot.MarkDirtyRepaint();

            }
            else
            {
                m_SelectedContextRoot.SetDisplay(false);
            }

            UpdateAssistantEditorDriverContext();
            UpdateWarnings();
        }

        public void OnRemoveContextEntry(AssistantContextEntry entry)
        {
            k_SelectedContext.Remove(entry);
            UpdateContextSelectionElements(true);
        }

        internal void ReplaceContextScreenshot(VirtualAttachment originalAttachment, VirtualAttachment updatedAttachment)
        {
            if (originalAttachment == null || updatedAttachment == null)
            {
                InternalLog.LogWarning("[AssistantView] Cannot replace screenshot: attachment is null");
                return;
            }

            // Find the context entry that corresponds to the original attachment
            // Match by Payload (unique PNG data) since DisplayName is the same for all screenshots
            var indexToReplace = -1;

            for (int i = 0; i < k_SelectedContext.Count; i++)
            {
                var entry = k_SelectedContext[i];
                // Match by Payload (the base64 PNG data is unique for each original screenshot)
                if (entry.EntryType == AssistantContextType.Virtual &&
                    entry.Value == originalAttachment.Payload)
                {
                    indexToReplace = i;
                    break;
                }
            }

            if (indexToReplace >= 0)
            {
                // Replace the old context entry with the new one
                var newContextEntry = updatedAttachment.ToContextEntry();
                k_SelectedContext[indexToReplace] = newContextEntry;
                UpdateContextSelectionElements(false);
            }
            else
            {
                // Could not find original entry, create a new screenshot instead
                if (!string.IsNullOrEmpty(updatedAttachment.Payload))
                {
                    k_SelectedContext.Add(updatedAttachment.ToContextEntry());
                    UpdateContextSelectionElements(false);
                }
                else
                {
                    InternalLog.LogWarning("[AssistantView] Could not create new screenshot: updatedAttachment has no payload data");
                }
            }
        }

        void UpdateWarnings()
        {
            m_ChatInput.ToggleContextLimitWarning(Context.API.GetAttachedContextLength() > AssistantMessageSizeConstraints.ContextLimit);
            var totalImageSize = ImageUtils.GetTotalImageSize(Context.Blackboard.VirtualAttachments, Context.Blackboard.ObjectAttachments);
            m_ChatInput.ToggleImageSizeLimitWarning(totalImageSize > AssistantConstants.MaxTotalImageSize);
        }

        void ClearContext(PointerUpEvent _)
        {
            k_SelectedContext.Clear();

            Context.Blackboard.ClearAttachments();

            UpdateContextSelectionElements();

            AIAssistantAnalytics.ReportContextClearAllAttachedContextEvent();
        }

        internal void ClearScreenshotContextEntries()
        {
            var entriesToRemove = new List<AssistantContextEntry>();

            // First, collect all screenshot entries to remove
            foreach (var entry in k_SelectedContext)
            {
                if (entry.EntryType == AssistantContextType.Virtual && entry.Metadata is ImageContextMetaData metadata)
                {
                    if (metadata.Category == ImageContextCategory.Screenshot)
                    {
                        entriesToRemove.Add(entry);
                    }
                }
            }

            // Then remove them and notify EditScreenCaptureWindow for each one
            foreach (var entry in entriesToRemove)
            {
                k_SelectedContext.Remove(entry);
                // Notify EditScreenCaptureWindow that this screenshot was sent
                var attachment = VirtualAttachment.FromContextEntry(entry);
                if (attachment != null)
                {
                    EditScreenCaptureWindow.NotifyScreenshotSent(attachment);
                }
            }
        }

        void UpdateAssistantEditorDriverContext()
        {
            Context.Blackboard.ClearAttachments();

            for (var i = 0; i < k_SelectedContext.Count; i++)
            {
                var entry = k_SelectedContext[i];
                switch (entry.EntryType)
                {
                    case AssistantContextType.ConsoleMessage:
                    {
                        Context.Blackboard.AddConsoleAttachment(entry.GetLogData());
                        break;
                    }

                    case AssistantContextType.Component:
                    {
                        Context.Blackboard.AddObjectAttachment(entry.GetComponent());
                        break;
                    }

                    case AssistantContextType.Virtual:
                    {
                        Context.Blackboard.AddVirtualAttachment(VirtualAttachment.FromContextEntry(entry));
                        break;
                    }

                    case AssistantContextType.HierarchyObject:
                    case AssistantContextType.SubAsset:
                    case AssistantContextType.SceneObject:
                    {
                        Context.Blackboard.AddObjectAttachment(entry.GetTargetObject());
                        break;
                    }
                }
            }
        }
    }
}
