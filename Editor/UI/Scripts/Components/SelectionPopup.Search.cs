using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Editor.Utils;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class SelectionPopup
    {
        const int k_MaxSearchResults = 50;

        static readonly Dictionary<string, SearchContextWrapper> k_SearchContextWrappers = new();
        string m_ActiveSearchFilter = string.Empty;
        int m_CurrentPage;
        class SearchContextWrapper
        {
            public string ProviderId;
            SearchContext m_Context;
            public List<Action<IList<Object>>> Callbacks = new();
            bool m_Active = true;

            public bool IsLoading => m_Active && m_Context is { searchInProgress: true };

            static List<Object> ConvertSearchItemsToObjects(IEnumerable<SearchItem> items)
            {
                return items.Select(item => item.ToObject()).Where(obj => obj).ToList();
            }

            public SearchContextWrapper(string providerId, string query)
            {
                ProviderId = providerId;
                m_Context = SearchService.CreateContext(providerId, query);
            }

            public void Stop()
            {
                m_Active = false;
                m_Context?.Dispose();
            }

            public void Start()
            {
                if (!m_Active) return;

                m_Context.asyncItemReceived += (_, items) =>
                {
                    if (!m_Active) return;
                    var itemsAsList = ConvertSearchItemsToObjects(items);
                    foreach (var callback in Callbacks)
                        callback.Invoke(itemsAsList);
                };

                m_Context.sessionEnded += _ =>
                {
                    if (!m_Active) return;
                    foreach (var callback in Callbacks)
                        callback.Invoke(null);
                };

                var initialResults = SearchService.GetItems(m_Context, SearchFlags.FirstBatchAsync);
                var itemsAsList = ConvertSearchItemsToObjects(initialResults);
                if (itemsAsList.Count > 0)
                {
                    foreach (var callback in Callbacks)
                        callback.Invoke(itemsAsList);
                }
            }
        }

        void Search()
        {
            m_CurrentPage = 0;
            SetupSearchProviders(m_ActiveSearchFilter);

            ClearAllTabResults();
            SetupSearchCallbacks();
            StartSearchers();
        }

        static void SetupSearchProviders(string query)
        {
            foreach (var wrapper in k_SearchContextWrappers.Values)
            {
                wrapper.Stop();
            }
            k_SearchContextWrappers.Clear();

            k_SearchContextWrappers["scene"] = new SearchContextWrapper("scene", query);
            k_SearchContextWrappers["asset"] = new SearchContextWrapper("asset", query);
        }

        void ClearAllTabResults()
        {
            ClearTabResults(m_AllTab);
            ClearTabResults(m_ProjectTab);
            ClearTabResults(m_HierarchyTab);
            ClearTabResults(m_ConsoleTab);
            ClearTabResults(m_SelectionTab);
        }

        void SetupSearchCallbacks()
        {
            if (k_SearchContextWrappers.TryGetValue("scene", out var sceneWrapper))
            {
                sceneWrapper.Callbacks.Add(items => { if (IsShown) OnTabResults(items, m_AllTab); });
                sceneWrapper.Callbacks.Add(items => { if (IsShown) OnTabResults(items, m_HierarchyTab); });
                sceneWrapper.Callbacks.Add(items => { if (IsShown) OnTabResults(items, m_SelectionTab); });
            }

            if (k_SearchContextWrappers.TryGetValue("asset", out var assetWrapper))
            {
                assetWrapper.Callbacks.Add(items => { if (IsShown) OnTabResults(items, m_AllTab); });
                assetWrapper.Callbacks.Add(items => { if (IsShown) OnTabResults(items, m_ProjectTab); });
                assetWrapper.Callbacks.Add(items => { if (IsShown) OnTabResults(items, m_SelectionTab); });
            }
        }

        static void StartSearchers()
        {
            foreach (var wrapper in k_SearchContextWrappers.Values)
            {
                wrapper.Start();
            }
        }

        static bool IsSupportedAsset(Object obj)
        {
            if (obj == null)
                return false;

            if (obj is DefaultAsset)
                return FolderContextUtils.IsFolderAsset(obj);

            var objType = obj.GetType();
            return AssetDatabase.Contains(obj) ||
                   typeof(Component).IsAssignableFrom(objType) ||
                   typeof(GameObject).IsAssignableFrom(objType) ||
                   typeof(Transform).IsAssignableFrom(objType);
        }


        void CheckAndRefilterSearchResults(bool force = false)
        {
            string newFilterValue = m_SearchField.value.Trim();
            if (newFilterValue == m_ActiveSearchFilter && !force)
            {
                return;
            }

            m_ActiveSearchFilter = newFilterValue;

            Search();

            if (string.IsNullOrEmpty(m_ActiveSearchFilter))
            {
                ScheduleSearchRefresh();
                RefreshSearchState();
                return;
            }

            ScheduleSearchRefresh();
        }

        internal void ScheduleSearchRefresh()
        {
            EditorApplication.delayCall -= PopulateSearchListView;
            EditorApplication.delayCall += PopulateSearchListView;
        }

    }
}
