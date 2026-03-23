using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.History
{
    class HistoryPanel : ManagedTemplate
    {
        static readonly List<ConversationModel> k_ConversationCache = new();
        static readonly IDictionary<string, List<ConversationModel>> k_GroupCache = new Dictionary<string, List<ConversationModel>>();

        readonly IList<object> k_TempList = new List<object>();

        ToolbarSearchField m_SearchBar;
        VisualElement m_ContentRoot;
        VirtualizedListView<object, HistoryPanelEntryWrapper> m_ContentList;

        AssistantConversationId m_SelectedConversation;

        BaseEventSubscriptionTicket m_ConversationSelectedEventTicket;

        string m_SearchFilter;

        bool m_LastDockingState;

        public HistoryPanel(): base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ContentRoot = view.Q<VisualElement>("historyContentRoot");
            m_ContentList = new VirtualizedListView<object, HistoryPanelEntryWrapper>();
            m_ContentList.Initialize(Context);
            m_ContentRoot.Add(m_ContentList);

            m_SearchBar = new ToolbarSearchField();
            m_SearchBar.AddToClassList("mui-history-panel-search-bar");
            view.Q<VisualElement>("historySearchBarRoot").Add(m_SearchBar);
            m_SearchBar.RegisterCallback<KeyUpEvent>(OnSearchTextChanged);
            m_SearchBar.RegisterValueChangedCallback(OnSearchValueChanged);

            Context.API.ConversationsRefreshed += OnReloadRequired;
            Context.API.ConversationDeleted += OnConversationDeleted;

            // Subscribe to storage cleared event to refresh history when chat is cleared
            // This works regardless of which provider is currently active
            AcpConversationStorage.OnStorageCleared += OnStorageCleared;

            view.RegisterCallback<GeometryChangedEvent>(OnViewGeometryChanged);

            m_LastDockingState = GetWindowDockingState();
            
            RegisterAttachEvents(OnAttach, OnDetach);
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            AssistantEvents.Unsubscribe(ref m_ConversationSelectedEventTicket);
            AcpConversationStorage.OnStorageCleared -= OnStorageCleared;
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            m_ConversationSelectedEventTicket = AssistantEvents.Subscribe<EventHistoryConversationSelected>(SelectionChanged);
        }

        bool GetWindowDockingState() => Context.WindowDockingState?.Invoke() ?? false;

        void OnConversationDeleted(AssistantConversationId obj)
        {
            Reload(fullReload: true);
        }

        void LoadData(IList<object> result, long nowRaw, string searchFilter = null)
        {
            bool searchActive = !string.IsNullOrEmpty(searchFilter);
            k_GroupCache.Clear();
            result.Clear();
            foreach (var conversationInfo in k_ConversationCache)
            {
                if (searchActive && conversationInfo.Title.IndexOf(searchFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string groupKey;
                if (Context.Blackboard.GetFavorite(conversationInfo.Id))
                {
                    groupKey = "000000#Favorites";
                }
                else
                {
                    groupKey = MessageUtils.GetMessageTimestampGroup(conversationInfo.LastMessageTimestamp, nowRaw);
                }

                if (!k_GroupCache.TryGetValue(groupKey, out var groupInfos))
                {
                    groupInfos = new List<ConversationModel>();
                    k_GroupCache.Add(groupKey, groupInfos);
                }

                groupInfos.Add(conversationInfo);
            }

            var orderedKeys = k_GroupCache.Keys.OrderBy(x => x).ToArray();
            for (var i = 0; i < orderedKeys.Length; i++)
            {
                var title = orderedKeys[i].Split('#')[1];
                result.Add(title);

                var groupContent = k_GroupCache[orderedKeys[i]];
                groupContent.Sort((e1, e2) => DateTimeOffset.Compare(DateTimeOffset.FromUnixTimeMilliseconds(e2.LastMessageTimestamp), DateTimeOffset.FromUnixTimeMilliseconds(e1.LastMessageTimestamp)));
                foreach (var info in groupContent)
                {
                    result.Add(info);
                }
            }
        }

        void Reload(bool fullReload = true, bool resetScrollPosition = false)
        {
            if (fullReload)
            {
                // Full reload let's get a fresh list of conversations from the driver
                k_ConversationCache.Clear();
                k_ConversationCache.AddRange(Context.Blackboard.Conversations);

                // Update the cache
                foreach (var conversationInfo in k_ConversationCache)
                {
                    Context.Blackboard.SetFavorite(conversationInfo.Id, conversationInfo.IsFavorite);
                }
            }

            var nowRaw = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var activeConversation = Context.Blackboard.ActiveConversation;

            m_ContentList.BeginUpdate();
            m_ContentList.ClearData();
            m_ContentList.ClearSelection();

            k_TempList.Clear();
            LoadData(k_TempList, nowRaw, m_SearchFilter);

            for (var i = 0; i < k_TempList.Count; i++)
            {
                m_ContentList.AddData(k_TempList[i]);
            }

            m_ContentList.EndUpdate();

            m_SelectedConversation = activeConversation?.Id ?? default;

            m_ContentList.SetDisplay(m_ContentList.Data.Count != 0);

            if (resetScrollPosition)
            {
                m_ContentList.ScrollToStart();
            }
        }

        void OnReloadRequired()
        {
            Reload(fullReload: true);
        }

        void OnStorageCleared()
        {
            // When storage is cleared, clear our local cache and reload with empty data
            k_ConversationCache.Clear();
            Reload(fullReload: true);
        }

        void OnSearchTextChanged(KeyUpEvent evt)
        {
            SetSearchFilter(m_SearchBar.value);
        }

        void OnViewGeometryChanged(GeometryChangedEvent evt)
        {
            var dockingState = GetWindowDockingState();
            if (m_LastDockingState == dockingState)
                return;

            m_LastDockingState = dockingState;

            Reload(fullReload: false);
        }

        async void SelectionChanged(EventHistoryConversationSelected eventData)
        {
            m_ContentList.BeginUpdate();
            try
            {
                // End active session before loading — ensures prompt stops cleanly
                try
                {
                    await Context.API.EndActiveSessionAsync();
                }
                catch (System.Exception ex)
                {
                    UnityEngine.Debug.LogWarning($"[HistoryPanel] End session failed: {ex.Message}");
                }

                m_SelectedConversation = eventData.Id;
                await Context.ConversationReloadManager.LoadConversationAsync(m_SelectedConversation);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to load conversation {m_SelectedConversation}: {ex.Message}");
            }
            finally
            {
                m_ContentList.EndUpdate();
            }
        }

        void OnSearchValueChanged(ChangeEvent<string> evt)
        {
            SetSearchFilter(evt.newValue);
        }

        void SetSearchFilter(string filter)
        {
            if (m_SearchFilter == filter)
            {
                return;
            }

            m_SearchFilter = filter;
            Reload(false, true);
        }
    }
}
