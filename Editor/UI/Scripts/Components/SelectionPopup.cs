using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class SelectionPopup : ManagedTemplate
    {
        const int k_ScreenshotButtonCooldownTime = 100;

        internal class ListEntry
        {
            public Object Object;
            public SelectionPopup Owner;
            public LogData? LogData;
            public bool IsSelected;
        }

        enum SearchState
        {
            NoSearchTerm,
            NoResults,
            Loading,
            HasResults
        }

        VisualElement m_ListViewContainer;
        ToolbarSearchField m_SearchField;
        ManagedScrollView<ListEntry, SelectionElement> m_ManagedScrollView;
        VisualElement m_InitialSelection;
        VisualElement m_NoResultsContainer;
        Label m_SearchStringDisplay;
        Label m_Instruction1Message;
        Label m_Instruction2Message;
        VisualElement m_LoadingIndicator;
        LoadingSpinner m_LoadingSpinner;
        VisualElement m_PagingContainer;
        Label m_PagingLabel;
        Button m_UploadCustomButton;
        Button m_TakeScreenshotButton;
        Button m_AnnotateButton;

        TabView m_SelectionTabView;
        TabData m_AllTab, m_ProjectTab, m_HierarchyTab, m_ConsoleTab, m_SelectionTab;
        TabData m_SelectedTab;
        int m_SelectedTabIndex;
        TabData[] m_AllTabs;

        double m_LastConsoleCheckTime;
        const float k_ConsoleCheckInterval = 0.2f;

        readonly List<LogData> m_LastUpdatedLogReferences = new();
        readonly HashSet<LogData> m_LastUpdatedLogHashSet = new();
        readonly HashSet<LogData> m_CurrentLogHashSet = new();
        readonly List<LogData> m_LastUpdatedSelectedLogReferences = new();

        readonly List<Object> m_ObjectSelection = new();
        readonly List<LogData> m_ConsoleSelection = new();

        public IReadOnlyList<Object> ObjectSelection => m_ObjectSelection;
        public IReadOnlyList<LogData> ConsoleSelection => m_ConsoleSelection;

        public Action OnSelectionChanged;
        public Action<Object> OnContextObjectAdded;
        public Action<LogData> OnContextLogAdded;
        public Action OnAnnotationRequested;

        static bool s_LastConsoleTimestampFlag;
        const string k_PagingLabelText = "Showing {0} - {1} results";

        public SelectionPopup()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        public void SetSelectionFromContext(List<AssistantContextEntry> context, bool notify = true)
        {
            m_ObjectSelection.Clear();
            m_ConsoleSelection.Clear();

            for (var i = 0; i < context.Count; i++)
            {
                var entry = context[i];
                switch (entry.EntryType)
                {
                    case AssistantContextType.HierarchyObject:
                    case AssistantContextType.SubAsset:
                    case AssistantContextType.SceneObject:
                    {
                        var target = entry.GetTargetObject();
                        if (target != null)
                        {
                            m_ObjectSelection.Add(target);
                        }

                        break;
                    }

                    case AssistantContextType.ConsoleMessage:
                    {
                        var logEntry = new LogData
                        {
                            Message = entry.Value,
                            Type = Enum.Parse<LogDataType>(entry.ValueType)
                        };

                        m_ConsoleSelection.Add(logEntry);
                        break;
                    }
                }
            }

            if (notify)
            {
                OnSelectionChanged?.Invoke();
            }
        }

        void RefreshSearchState()
        {
            if (m_SelectedTab == null)
            {
                SetSearchState(SearchState.NoSearchTerm);
                return;
            }

            var results = m_SelectedTab.NumberOfResults;

            if (results > 0)
                SetSearchState(SearchState.HasResults);
            else if (IsTabLoading())
                SetSearchState(SearchState.Loading);
            else if (!string.IsNullOrEmpty(m_ActiveSearchFilter))
                SetSearchState(SearchState.NoResults);
            else
                SetSearchState(SearchState.NoSearchTerm);
        }

        bool IsTabLoading()
        {
            if (m_SelectedTab == m_ConsoleTab) return false;

            var relevantProviders = GetSearchProviders(m_SelectedTab);
            return k_SearchContextWrappers.Values
                .Where(w => relevantProviders.Contains(w.ProviderId))
                .Any(w => w.IsLoading);
        }

        string[] GetSearchProviders(TabData tabData)
        {
            if (tabData == m_AllTab) return new[] { "scene", "asset" };
            if (tabData == m_ProjectTab) return new[] { "asset" };
            if (tabData == m_HierarchyTab) return new[] { "scene" };
            if (tabData == m_SelectionTab) return new[] { "scene", "asset" };
            return new string[0];
        }

        void SetSearchState(SearchState state)
        {
            m_NoResultsContainer.style.display = DisplayStyle.None;
            m_InitialSelection.style.display = DisplayStyle.None;
            m_ListViewContainer.style.display = DisplayStyle.None;
            m_LoadingIndicator.style.display = DisplayStyle.None;
            m_PagingContainer.style.display = DisplayStyle.None;

            // Hide spinner by default
            m_LoadingSpinner.Hide();

            if (m_SelectedTab != null)
            {
                if (string.IsNullOrEmpty(m_ActiveSearchFilter))
                {
                    SetInstructionMessages();
                }
                else
                {
                    m_Instruction1Message.text = string.Empty;
                    m_Instruction2Message.text = string.Empty;
                }
            }

            switch (state)
            {
                case SearchState.NoSearchTerm:
                    m_InitialSelection.style.display = DisplayStyle.Flex;
                    break;
                case SearchState.NoResults:
                    m_InitialSelection.style.display = DisplayStyle.Flex;

                    if (HasSearchProviders())
                    {
                        m_NoResultsContainer.style.display = DisplayStyle.Flex;
                    }

                    m_SearchStringDisplay.text = m_ActiveSearchFilter;
                    break;
                case SearchState.HasResults:
                    m_ListViewContainer.style.display = DisplayStyle.Flex;

                    var numberOfResults = m_SelectedTab.NumberOfResults;
                    if (numberOfResults > k_MaxSearchResults)
                    {
                        m_PagingContainer.style.display = DisplayStyle.Flex;
                        m_PagingLabel.text = string.Format(k_PagingLabelText, m_CurrentPage * k_MaxSearchResults + 1,
                            Math.Min(m_CurrentPage * k_MaxSearchResults + k_MaxSearchResults, numberOfResults));
                    }

                    break;
                case SearchState.Loading:
                    m_LoadingIndicator.style.display = DisplayStyle.Flex;
                    m_LoadingSpinner.Show();
                    break;
            }
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_SelectionTabView = view.Q<TabView>("selectionTabView");

            m_ListViewContainer = view.Q<VisualElement>("listViewContainer");
            m_ListViewContainer.style.display = DisplayStyle.None;
            m_NoResultsContainer = view.Q<VisualElement>("noResultsMessage");
            m_SearchStringDisplay = view.Q<Label>("noResultsSearchDisplay");
            m_Instruction1Message = view.Q<Label>("instruction1Message");
            m_Instruction2Message = view.Q<Label>("instruction2Message");
            m_InitialSelection = view.Q<VisualElement>("initialSelectionPopupMessage");
            m_LoadingIndicator = view.Q<VisualElement>("loadingIndicator");

            // Create and add the new LoadingSpinner
            m_LoadingSpinner = new LoadingSpinner();
            m_LoadingSpinner.name = "selectionPopupLoadingSpinner";
            m_LoadingSpinner.Hide(); // Start hidden
            m_LoadingIndicator.Add(m_LoadingSpinner);

            m_PagingContainer = view.Q<VisualElement>("pagingContainer");
            m_PagingLabel = view.Q<Label>("pagingLabel");
            view.SetupButton("previousPageButton", PreviousPage);
            view.SetupButton("nextPageButton", NextPage);

            var searchFieldContainer = view.Q<VisualElement>("searchFieldContainer");
            m_SearchField = new ToolbarSearchField();
            m_SearchField.AddToClassList("mui-selection-search-bar");
            m_SearchField.RegisterValueChangedCallback(_ => CheckAndRefilterSearchResults());
            searchFieldContainer.Add(m_SearchField);

            m_TakeScreenshotButton = view.SetupButton("takeScreenshotButton", TakeScreenshot);
            m_AnnotateButton = view.SetupButton("annotateButton", StartAnnotation);

            // Disable annotate button on Linux as the feature is not supported
            if (Application.platform == RuntimePlatform.LinuxEditor)
            {
                m_AnnotateButton.SetEnabled(false);
                m_AnnotateButton.tooltip = "Annotation feature is not supported on Linux";
            }

            m_UploadCustomButton = view.SetupButton("uploadCustomButton", UploadCustom);

            // Set dynamic tooltip with supported file types
            var supportedTypes = string.Join(", ", AssistantConstants.SupportedImageExtensions);
            m_UploadCustomButton.tooltip = $"Upload supported file types:\n{supportedTypes}";

            var scrollView = new ScrollView();
            scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            m_ListViewContainer.Add(scrollView);

            m_ManagedScrollView = new ManagedScrollView<ListEntry, SelectionElement>(scrollView);
            m_ManagedScrollView.Initialize(Context);

            InitializeTabs(m_SelectionTabView);
            RefreshSearchState();

            ScheduleSearchRefresh();

            m_LastConsoleCheckTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += DetectLogChanges;
        }

        public void ShowPopup()
        {
            Show();

            RefreshSelectionCount();

            m_SelectionTabView.Clear();

            InitializeTabs(m_SelectionTabView);

            CheckAndRefilterSearchResults(true);

            m_SearchField.Focus();
        }

        void PopulateSearchListView()
        {
            RefreshSelectionCount();

            if (m_SelectedTab == null)
            {
                m_ManagedScrollView.ClearData();
                return;
            }

            // When search results come in or tabs change, we need to rebuild the list.
            // This is expensive and when only later pages change, it's not needed.
            // Only rebuild the list if the actual data has changed:
            if (GetEntriesToShow(out var entriesToShow))
            {
                m_ManagedScrollView.SetData(entriesToShow);
            }

            RefreshSearchState();
        }

        int GetNumberOfPages()
        {
            var tab = m_SelectedTab;

            var objectsToShow = tab.SearchResults;
            var totalEntryCount = objectsToShow.Count;

            if (tab == m_ConsoleTab)
            {
                totalEntryCount += ConsoleUtils.GetConsoleLogCount(m_ActiveSearchFilter);
            }
            else if (tab == m_SelectionTab)
            {
                totalEntryCount += ConsoleUtils.GetSelectedConsoleLogCount(m_ActiveSearchFilter);
            }

            return totalEntryCount / k_MaxSearchResults;
        }

        bool GetEntriesToShow(out List<ListEntry> results)
        {
            results = new List<ListEntry>();

            var changed = false;

            var tab = m_SelectedTab;

            // Make sure we don't go past the last page when tabs are switched on later pages:
            m_CurrentPage = Math.Min(m_CurrentPage, GetNumberOfPages());

            var objectsToShow = tab.SearchResults;
            var totalEntryCount = objectsToShow.Count;

            int startIndex, endIndex, returnedEntryCount = 0, logEntryCount = 0;

            if (tab == m_ConsoleTab || tab == m_SelectionTab)
            {
                List<LogData> consoleLogs;

                if (tab == m_ConsoleTab)
                {
                    ConsoleUtils.GetConsoleLogs(m_LastUpdatedLogReferences, m_ActiveSearchFilter);
                    consoleLogs = m_LastUpdatedLogReferences;
                }
                else
                {
                    ConsoleUtils.GetSelectedConsoleLogs(m_LastUpdatedSelectedLogReferences, m_ActiveSearchFilter);
                    consoleLogs = m_LastUpdatedSelectedLogReferences;
                }

                logEntryCount = consoleLogs.Count;
                totalEntryCount += logEntryCount;

                startIndex = m_CurrentPage * k_MaxSearchResults;
                endIndex = Math.Min(consoleLogs.Count,
                    Math.Min(
                        startIndex + k_MaxSearchResults,
                        totalEntryCount));

                for (var i = startIndex; i < endIndex; i++, returnedEntryCount++)
                {
                    var logRef = consoleLogs[i];

                    logRef.IsSelectedInConsole = tab == m_SelectionTab;

                    var entry = new ListEntry
                    {
                        Object = null,
                        Owner = this,
                        LogData = logRef,
                        IsSelected = ConsoleUtils.FindLogEntry(m_ConsoleSelection, logRef) >= 0
                    };

                    if (!changed && !DoEntriesMatch(i, entry))
                    {
                        changed = true;
                    }

                    results.Add(entry);
                }

                var consoleTimestampFlag = ConsoleUtils.IsConsoleShowingTimestamps();
                if (s_LastConsoleTimestampFlag != consoleTimestampFlag)
                {
                    changed = true;
                }

                s_LastConsoleTimestampFlag = consoleTimestampFlag;

                if (returnedEntryCount >= k_MaxSearchResults)
                {
                    return HasDataChanged(results);
                }
            }

            startIndex = Math.Max(0, m_CurrentPage * k_MaxSearchResults - logEntryCount);
            endIndex = Math.Min(startIndex + k_MaxSearchResults, objectsToShow.Count);

            for (var i = startIndex;
                 i < endIndex && returnedEntryCount < k_MaxSearchResults;
                 i++, returnedEntryCount++)
            {
                var obj = objectsToShow[i];

                var entry = new ListEntry { Object = obj, Owner = this, IsSelected = m_ObjectSelection.Contains(obj) };

                if (!changed && !DoEntriesMatch(i, entry))
                {
                    changed = true;
                }

                results.Add(entry);
            }

            return HasDataChanged(results);

            bool HasDataChanged(List<ListEntry> results)
            {
                return changed || m_ManagedScrollView.Count != results.Count;
            }

            bool DoEntriesMatch(int index, ListEntry entry)
            {
                var currentEntries = m_ManagedScrollView.Data;

                if (currentEntries.Count <= index)
                    return false;

                var currentEntry = currentEntries[index];
                if (currentEntry == null)
                    return false;

                if (currentEntry.LogData.HasValue != entry.LogData.HasValue)
                    return false;

                if (currentEntry.LogData.HasValue && !currentEntry.LogData.Value.Equals(entry.LogData.Value))
                    return false;

                return currentEntry.Object == entry.Object
                       && currentEntry.IsSelected == entry.IsSelected;
            }
        }

        internal void PingObject(Object obj)
        {
            EditorGUIUtility.PingObject(obj);
        }

        internal void SelectedObject(Object obj, SelectionElement e)
        {
            if (!m_ObjectSelection.Contains(obj))
            {
                AddObjectToSelection(obj);
                e.SetSelected(true);
            }
            else
            {
                m_ObjectSelection.Remove(obj);
                e.SetSelected(false);
            }

            OnSelectionChanged?.Invoke();

            RefreshSelectionCount();
        }

        void AddObjectToSelection(Object obj, bool notifySelectionChanged = false)
        {
            m_ObjectSelection.Add(obj);
            OnContextObjectAdded?.Invoke(obj);

            if (notifySelectionChanged)
                OnSelectionChanged?.Invoke();
        }

        internal void SelectedLogReference(LogData logRef, SelectionElement e)
        {
            var existingEntryIndex = ConsoleUtils.FindLogEntry(m_ConsoleSelection, logRef);
            if (existingEntryIndex < 0)
            {
                AddLogReferenceToSelection(logRef);
                e.SetSelected(true);
            }
            else
            {
                m_ConsoleSelection.RemoveAt(existingEntryIndex);
                e.SetSelected(false);
            }

            OnSelectionChanged?.Invoke();

            RefreshSelectionCount();
        }

        void AddLogReferenceToSelection(LogData logRef, bool notifySelectionChanged = false)
        {
            m_ConsoleSelection.Add(logRef);
            OnContextLogAdded?.Invoke(logRef);

            if (notifySelectionChanged)
                OnSelectionChanged?.Invoke();
        }

        void DetectLogChanges()
        {
            if (!IsShown || EditorApplication.timeSinceStartup < m_LastConsoleCheckTime + k_ConsoleCheckInterval)
                return;

            List<LogData> logs = new();
            ConsoleUtils.GetConsoleLogs(logs, m_ActiveSearchFilter);

            ConsoleUtils.HashEntries(m_LastUpdatedLogReferences, m_LastUpdatedLogHashSet);
            ConsoleUtils.HashEntries(logs, m_CurrentLogHashSet);

            if (m_LastUpdatedLogReferences.Count != logs.Count
                || !ConsoleUtils.HasEqualLogEntries(m_LastUpdatedLogHashSet, m_CurrentLogHashSet)
                || !ConsoleUtils.HasEqualLogEntries(m_CurrentLogHashSet, m_LastUpdatedLogHashSet))
            {
                m_LastUpdatedLogReferences.Clear();
                m_LastUpdatedLogReferences.AddRange(logs);

                PopulateSearchListView();
            }

            m_LastConsoleCheckTime = EditorApplication.timeSinceStartup;
        }

        void ValidateObjectSelection()
        {
            for (var i = m_ObjectSelection.Count - 1; i >= 0; i--)
            {
                if (m_ObjectSelection[i] == null)
                {
                    m_ObjectSelection.RemoveAt(i);
                }
            }
        }

        void PreviousPage(PointerUpEvent evt)
        {
            if (m_CurrentPage > 0)
            {
                m_CurrentPage -= 1;
                ScheduleSearchRefresh();
            }
        }

        void NextPage(PointerUpEvent evt)
        {
            if (m_CurrentPage < GetNumberOfPages())
            {
                m_CurrentPage += 1;
                ScheduleSearchRefresh();
            }
        }

        void TakeScreenshot(PointerUpEvent evt)
        {
            m_TakeScreenshotButton.SetEnabled(false);

            schedule.Execute(() =>
            {
                m_TakeScreenshotButton.SetEnabled(true);
            }).StartingIn(k_ScreenshotButtonCooldownTime);

            // Hide popup before capturing screenshot
            Hide();

            // Also hide EditScreenCaptureWindow if it's open
            var editWindow = EditorWindow.GetWindow<EditScreenCaptureWindow>(false);
            if (editWindow != null)
            {
                editWindow.Close();
            }

            // Schedule multiple frame delays to ensure UI is fully hidden before capturing
            EditorTask.delayCall += () =>
            {
                EditorTask.delayCall += () =>
                {
                    // Capture screenshot with all popups hidden
                    byte[] screenshotBytes = EditScreenCaptureWindow.CaptureScreenToBytes();
                    var attachment = ScreenContextUtility.GetAttachment(screenshotBytes, ImageContextCategory.Screenshot, "png");
                    if (attachment != null)
                    {
                        attachment.DisplayName = "Screenshot";
                        attachment.Type = "Image";
                        Context.Blackboard.AddVirtualAttachment(attachment);
                        Context.VirtualAttachmentAdded?.Invoke(attachment);
                    }
                };
            };
        }

        void StartAnnotation(PointerUpEvent evt)
        {
            Hide();

            // Show privacy warning before capturing full screen (unless user disabled it)
            const string warningTitle = "Annotation Privacy Notice";
            const string warningMessage = "The annotation feature captures your entire desktop, which may include:\n" +
                "• Other open applications\n" +
                "• Taskbars and system trays\n" +
                "• Sensitive information from other windows\n\n" +
                "Please ensure no sensitive information is visible on your screen before proceeding.\n\n" +
                "Captured annotations will be sent to the AI Assistant backend.";

            if (!AssistantEditorPreferences.AnnotationPrivacyNoticeAcknowledged)
            {
                int result = EditorUtility.DisplayDialogComplex(
                    warningTitle,
                    warningMessage,
                    "Proceed",
                    "Cancel",
                    "Don't Ask Again");

                // result: 0 = Proceed, 1 = Cancel, 2 = Don't Ask Again
                if (result == 1) // Cancel
                {
                    return;
                }

                if (result == 2) // Don't Ask Again
                {
                    AssistantEditorPreferences.AnnotationPrivacyNoticeAcknowledged = true;
                    // Continue with annotation after setting preference
                }
            }

            // Delay screenshot capture to ensure privacy notice dialog and UI are completely hidden before capturing
            EditorTask.delayCall += () =>
            {
                // Add another frame delay to ensure dialog is fully closed and not visible in screenshot
                EditorTask.delayCall += () =>
                {
                    // Capture full screen for annotation (including taskbar and everything)
                    byte[] screenshotBytes = EditScreenCaptureWindow.CaptureFullScreenToBytes();
                    if (screenshotBytes != null && screenshotBytes.Length > 0)
                    {
                        // Attach screenshot to context
                        var attachment = ScreenContextUtility.GetAttachment(screenshotBytes, ImageContextCategory.Screenshot, "png");
                        if (attachment != null)
                        {
                            attachment.DisplayName = "Screenshot";
                            attachment.Type = "Image";
                            Context.Blackboard.AddVirtualAttachment(attachment);
                            Context.VirtualAttachmentAdded?.Invoke(attachment);

                            // Open annotation window with the captured screenshot and pass the attachment
                            // so it can be updated in place when the user clicks Done
                            EditScreenCaptureWindow.OpenWithScreenshot(screenshotBytes, attachment);
                        }
                    }
                    else
                    {
                        InternalLog.LogWarning("[SelectionPopup] Failed to capture full screen for annotation");
                        // Fallback: open window without screenshot
                        OnAnnotationRequested?.Invoke();
                    }
                };
            };
        }

        void UploadCustom(PointerUpEvent evt)
        {
            var extensions = string.Join(",", AssistantConstants.SupportedImageExtensions).Replace(".", "");
            var path = EditorUtility.OpenFilePanel("Select Custom File", "", extensions);
            if (!string.IsNullOrEmpty(path))
            {
                var attachment = ContextUtils.ProcessImageFileForContext(path);
                if (attachment != null)
                {
                    // Set proper display name based on file context
                    var displayName = GetProperDisplayName(path);
                    attachment.DisplayName = displayName;

                    Context.Blackboard.AddVirtualAttachment(attachment);
                    Context.VirtualAttachmentAdded?.Invoke(attachment);
                }
                else if (!string.IsNullOrEmpty(path))
                {
                    EditorUtility.DisplayDialog("File Not Attached", "The selected file could not be attached. It might be an unsupported file type.", "OK");
                }
            }
        }

        static string GetProperDisplayName(string filePath)
        {
            try
            {
                // Try to load as Unity asset directly - this only works for files within the project
                if (filePath.StartsWith(Application.dataPath))
                {
                    var relativePath = "Assets" + filePath.Substring(Application.dataPath.Length);
                    var unityAsset = AssetDatabase.LoadAssetAtPath<Object>(relativePath);
                    if (unityAsset != null)
                    {
                        return unityAsset.name; // Unity's display name without extension
                    }
                }

                // For external files (not Unity assets), use filename without extension
                return Path.GetFileNameWithoutExtension(filePath);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to determine proper display name for file '{filePath}': {ex.Message}");
                return Path.GetFileNameWithoutExtension(filePath);
            }
        }
    }
}
