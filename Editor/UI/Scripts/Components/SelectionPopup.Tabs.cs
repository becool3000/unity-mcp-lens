using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Bridge.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class SelectionPopup
    {
        class TabData
        {
            public Tab Tab;
            public List<Object> SearchResults = new();
            public Label NumberOfResultsLabel;

            int m_NumberOfResults;
            public int NumberOfResults
            {
                get => m_NumberOfResults;
                set
                {
                    m_NumberOfResults = value;
                    NumberOfResultsLabel.text = m_NumberOfResults > 0 ? m_NumberOfResults.ToString() : string.Empty;
                }
            }
        }

        readonly string k_PopupTabClass = "mui-selection-popup-tab";
        void InitializeTabs(TabView tabView)
        {
            m_AllTab = CreateTab("All");
            m_ProjectTab = CreateTab("Project");
            m_HierarchyTab = CreateTab("Hierarchy");
            m_ConsoleTab = CreateTab("Console");
            m_SelectionTab = CreateTab("Selection");

            m_AllTabs = new[] { m_AllTab, m_ProjectTab, m_HierarchyTab, m_ConsoleTab, m_SelectionTab };

            foreach (var tabData in m_AllTabs)
            {
                tabData.Tab.selected += _ => OnTabSelected(tabData);
                tabData.Tab.tabHeader.AddToClassList(k_PopupTabClass);
                tabData.Tab.tabHeader.Add(tabData.NumberOfResultsLabel);
                tabView.Add(tabData.Tab);
            }

            tabView.selectedTabIndex = m_SelectedTabIndex;
            OnTabSelected(m_AllTabs[m_SelectedTabIndex]);
            PopulateSearchListView();
            RefreshSelectionCount();
        }

        TabData CreateTab(string label)
        {
            var tab = new Tab(label);
            var resultsLabel = new Label();
            resultsLabel.AddToClassList("mui-tab-results-label");

            return new TabData
            {
                Tab = tab,
                NumberOfResultsLabel = resultsLabel
            };
        }

        void SetSelectedTab(TabData selectedTab)
        {
            m_SelectedTab = selectedTab;
            m_SelectedTabIndex = Array.IndexOf(m_AllTabs, m_SelectedTab);
        }

        void OnTabSelected(TabData tabData)
        {
            SetSelectedTab(tabData);
            ScheduleSearchRefresh();
        }

        void ClearTabResults(TabData tabData)
        {
            tabData.SearchResults.Clear();

            if (tabData == m_SelectionTab && string.IsNullOrEmpty(m_ActiveSearchFilter))
            {
                if (Selection.objects != null)
                {
                    tabData.SearchResults.AddRange(Selection.objects.Where(IsSupportedAsset));
                }
            }

            RefreshTabResults(tabData);
        }

        void RefreshTabResults(TabData tabData)
        {
            var resultCount = tabData.SearchResults.Count;

            if (tabData == m_ConsoleTab)
                resultCount += ConsoleUtils.GetConsoleLogCount(m_ActiveSearchFilter);
            else if (tabData == m_SelectionTab)
                resultCount += ConsoleUtils.GetSelectedConsoleLogCount(m_ActiveSearchFilter);

            tabData.NumberOfResults = resultCount;
        }

        void OnTabResults(IEnumerable<Object> items, TabData tabData)
        {
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (IsSupportedAsset(item) && !tabData.SearchResults.Contains(item))
                    {
                        if (tabData == m_SelectionTab && (Selection.objects == null || !Selection.objects.Contains(item)))
                            continue;

                        tabData.SearchResults.Add(item);
                    }
                }
            }

            RefreshTabResults(tabData);

            if (tabData == m_SelectedTab)
            {
                ScheduleSearchRefresh();
            }
        }

        void RefreshSelectionCount()
        {
            ValidateObjectSelection();

            foreach (var tab in m_AllTabs)
            {
                RefreshTabResults(tab);
            }

            if (m_SelectedTab != null)
            {
                RefreshSearchState();
            }
        }

        void SetInstructionMessages()
        {
            if (m_SelectedTab == m_ConsoleTab)
            {
                m_Instruction1Message.text = "No console messages exist.";
                m_Instruction2Message.text = "When errors, warnings, or messages exist in the Console they will appear here.";
            }
            else if (m_SelectedTab == m_SelectionTab)
            {
                m_Instruction1Message.text = "Nothing selected.";
                m_Instruction2Message.text = "Select items from the hierarchy, or assets to add them as an attachment.";
            }
            else
            {
                m_Instruction1Message.text = "Search sources inside of Unity and attach them your prompt for additional context.";
                m_Instruction2Message.text = "Or drag and drop them directly below.";
            }
        }

        bool HasSearchProviders()
        {
            return m_SelectedTab != m_ConsoleTab;
        }
    }
}