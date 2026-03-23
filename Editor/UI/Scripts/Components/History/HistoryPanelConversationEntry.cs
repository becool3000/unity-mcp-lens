using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.History
{
    class HistoryPanelConversationEntry : ManagedTemplate
    {
        const string k_SelectedClass = "mui-history-panel-entry-selected";

        const string k_Edit = "Edit";
        const string k_Delete = "Delete";
        int m_Index;
        ConversationModel m_Data;

        VisualElement m_ConversationElementRoot;
        Button m_FavoriteToggle;
        AssistantImage m_FavoriteStateIcon;
        Label m_ConversationText;
        Image m_ProviderIcon;
        TextField m_ConversationEditText;

        string m_CurrentFavoriteClassName;

        bool m_EditModeActive;
        bool m_IsSelected;
        bool m_IsButtonClick;
        bool m_IsFavorited;

        public ConversationModel Data => m_Data;

        public HistoryPanelConversationEntry()
            : base(AssistantUIConstants.UIModulePath)
        {
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ConversationElementRoot = view.Q<VisualElement>("historyPanelElementConversationRoot");
            m_ConversationElementRoot.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.button == (int)MouseButton.RightMouse)
                {
                    OnConversationRightClicked(evt);
                }
            });

            m_FavoriteToggle = view.SetupButton("historyPanelFavoriteStateButton", OnToggleFavorite);
            m_FavoriteStateIcon = view.SetupImage("historyPanelFavoriteStateIcon");

            m_ConversationText = view.Q<Label>("historyPanelElementConversationText");
            m_ConversationText.enableRichText = false;

            m_ProviderIcon = view.Q<Image>("historyPanelProviderIcon");

            m_ConversationEditText = view.Q<TextField>("historyPanelElementConversationEditText");
            m_ConversationEditText.isDelayed = true;
            m_ConversationEditText.RegisterCallback<FocusOutEvent>(OnEditFocusLost);
            m_ConversationEditText.RegisterValueChangedCallback(OnEditComplete);

            RegisterCallback<PointerUpEvent>(OnSelectEntry);

            UpdateEditUI();
        }

        void OnSelectEntry(PointerUpEvent evt)
        {
            if (m_IsSelected || m_IsButtonClick)
            {
                m_IsButtonClick = false;
                return;
            }

            AssistantEvents.Send(new EventHistoryConversationSelected(m_Data.Id));
            
            SetSelected(true);
            
            AIAssistantAnalytics.ReportUITriggerBackendLoadConversationEvent(m_Data.Id, m_Data.Title);
        }

        void OnToggleFavorite(PointerUpEvent evt)
        {
            m_IsButtonClick = true;

            m_IsFavorited = !m_IsFavorited;

            Context.API.SetFavorite(m_Data.Id, m_IsFavorited);

            RefreshFavoriteDisplay();

            AIAssistantAnalytics.ReportUITriggerBackendFavoriteConversationEvent(m_Data.Id, m_Data.Title, m_IsFavorited);
        }

        void OnConversationRightClicked(PointerUpEvent evt)
        {
            m_IsButtonClick = true;

            // Create the menu and add items to it
            var menu = new GenericMenu();

            // Add menu items
            menu.AddItem(new GUIContent(k_Edit), false, OnEditClicked);
            menu.AddItem(new GUIContent(k_Delete), false, OnDeleteClicked);
            // Add more items here

            // Show the menu at the current mouse position
            menu.ShowAsContext();

            // Use the event
            evt.StopPropagation();
            evt.StopImmediatePropagation();
            Event.current.Use();
        }

        void OnEditClicked()
        {
            BeginEdit();
            m_ConversationEditText.SetValueWithoutNotify(m_Data.Title);
        }

        void OnDeleteClicked()
        {
            Context.API.ConversationDelete(m_Data.Id);

            AIAssistantAnalytics.ReportUITriggerBackendDeleteConversationEvent(m_Data.Id, m_Data.Title);
        }

        public void SetData(int index, ConversationModel data, bool isSelected = false)
        {
            EndEdit(false);
            m_Index = index;
            m_Data = data;

            // TODO: Enable if design wants a log to indicate contextual conversations
            // m_ConversationIcon.style.display = data.IsContextAware ? DisplayStyle.Flex : DisplayStyle.None;
            m_ConversationText.text = data.Title == null ? "New conversation" : data.Title.Replace("\n", " ");
            m_ConversationText.tooltip = data.Title;

            // This field is fetched via cache, which gets invalidated on a full reload
            // Until then we persist our local state since changing this value can take longer than a conversation refresh
            m_IsFavorited = Context.Blackboard.GetFavorite(data.Id);

            // Update provider badge
            UpdateProviderBadge();

            RefreshFavoriteDisplay();
            SetSelected(Context.Blackboard.ActiveConversationId == m_Data.Id);
        }

        void UpdateProviderBadge()
        {
            if (m_ProviderIcon == null || m_Data == null)
                return;

            // Determine provider ID for icon lookup
            bool isUnityProvider = string.IsNullOrEmpty(m_Data.ProviderId) ||
                                   m_Data.ProviderId == Unity.AI.Assistant.Editor.Acp.AssistantProviderFactory.UnityProviderId;

            string providerIdForIcon = isUnityProvider ? "unity" : m_Data.ProviderId;

            m_ProviderIcon.image = null;
            m_ProviderIcon.style.display = DisplayStyle.None;
        }

        static string GetProviderDisplayName(string providerId) =>
            Unity.Relay.Editor.Acp.AcpProvidersRegistry.GetDisplayName(providerId);

        void RefreshFavoriteDisplay()
        {
            string newClassName = m_IsFavorited ? "star-filled-white" : "star-filled-grey";
            if (m_CurrentFavoriteClassName == newClassName)
            {
                // No change
                return;
            }

            m_CurrentFavoriteClassName = newClassName;
            m_FavoriteStateIcon.SetIconClassName(m_CurrentFavoriteClassName);
        }

        void OnEditFocusLost(FocusOutEvent evt)
        {
            EndEdit();
        }

        void OnEditComplete(ChangeEvent<string> evt)
        {
            EndEdit();

            if (string.IsNullOrWhiteSpace(evt.newValue))
                return;

            if (evt.newValue == m_Data.Title)
            {
                return;
            }

            // Set the conversation title directly, we don't wait for the server to respond
            m_Data.Title = evt.newValue;
            m_ConversationText.text = m_Data.Title;

            Context.API.ConversationRename(m_Data.Id, evt.newValue);
            AIAssistantAnalytics.ReportUITriggerBackendRenameConversationEvent(m_Data.Id, evt.newValue);

            Context.SendConversationRenamed(m_Data.Id);
        }

        void BeginEdit()
        {
            m_EditModeActive = true;
            Context.API.SuspendConversationRefresh();
            UpdateEditUI();
        }

        void EndEdit(bool refresh = true)
        {
            if (!m_EditModeActive)
            {
                return;
            }

            Context.API.ResumeConversationRefresh();
            m_EditModeActive = false;

            if (refresh)
            {
                UpdateEditUI();
            }
        }

        void UpdateEditUI()
        {
            if (m_EditModeActive)
            {
                m_ConversationEditText.style.display = DisplayStyle.Flex;
                m_ConversationText.style.display = DisplayStyle.None;
                m_ConversationEditText.Focus();
            }
            else
            {
                m_ConversationEditText.style.display = DisplayStyle.None;
                m_ConversationText.style.display = DisplayStyle.Flex;
            }
        }
        
        void SetSelected(bool selected)
        {
            if (m_IsSelected == selected)
            {
                return;
            }

            m_IsSelected = selected;
            EnableInClassList(k_SelectedClass, selected);
            m_ConversationText.EnableInClassList(k_SelectedClass, selected);
        }
    }
}
