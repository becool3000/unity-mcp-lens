using System;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.ConversationSearch
{
    class AssistantViewSearchHelper : Manipulator
    {
        #region Constants

        const string k_NoResultsText = "No results";
        const string k_ResultsText = "{0}/{1}";
        const string k_SearchPlaceholderText = "Find in conversation";

        const string k_PreviousButtonTooltip = "Previous result";
        const string k_NextButtonTooltip = "Next result";
        const string k_CloseButtonTooltip = "Close search";

        const string k_SearchContainerName = "searchRow";
        const string k_SearchResultCountName = "searchResultCount";
        const string k_SearchNextButtonName = "searchNextButton";
        const string k_SearchPreviousButtonName = "searchPreviousButton";
        const string k_OpenSearchButtonName = "openSearchButton";

        const int k_LabelRectPadding = 20;

        const string k_SearchOpenClass = "mui-search-open";

        #endregion

        #region UI Elements

        ToolbarSearchField m_SearchField;
        TextField m_SearchTextField;

        Label m_SearchResultCountLabel;

        Button m_PreviousButton;
        Button m_NextButton;
        Button m_SearchButton;
        VisualElement m_SearchContainer;

        readonly ChatScrollView<MessageModel, ChatElementWrapper> m_MessageView;

        #endregion

        readonly AssistantUIContext Context;

        int m_CurrentSearchResultIndex;
        int TotalResultCount => m_ConversationSearcher.TotalResultCount;

        internal Action<AssistantMessageId> SearchResultHighlighted;

        bool m_ScrollToMainSearchResult;

        bool m_NeedToFindInitialSearchResult;

        SearchHighlighter m_SearchHighlighter;
        AssistantSearchMessageConverter m_MessageConverter;
        ConversationSearcher m_ConversationSearcher;

        internal AssistantViewSearchHelper(
            ChatScrollView<MessageModel, ChatElementWrapper> messageView,
            AssistantUIContext context)
        {
            Context = context;
            m_MessageView = messageView;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            m_MessageConverter = new AssistantSearchMessageConverter();
            m_SearchHighlighter = new SearchHighlighter(ScrollLabelIntoView, m_MessageConverter.GetRenderedMessage);
            m_ConversationSearcher = new ConversationSearcher(m_MessageConverter, Context);

            m_SearchContainer = target.Q<VisualElement>(k_SearchContainerName);

            m_SearchField = new ToolbarSearchField();
            m_SearchField.AddToClassList("mui-conversation-search-bar");
            m_SearchField.RegisterValueChangedCallback(SearchStringChanged);
            m_SearchField.placeholderText = k_SearchPlaceholderText;
            m_SearchField.RegisterCallback<KeyUpEvent>(OnSearchKeyUp);

            // Find textField inside ToolbarSearchField:
            m_SearchTextField = m_SearchField.Q<TextField>();
            m_SearchTextField.selectAllOnFocus = false;

            m_SearchContainer.Insert(0, m_SearchField);

            target.RegisterCallback<AttachToPanelEvent>(TargetAttached);
            target.RegisterCallback<DetachFromPanelEvent>(TargetDetached);

            m_SearchResultCountLabel = target.Q<Label>(k_SearchResultCountName);

            m_PreviousButton = target.SetupButton(k_SearchPreviousButtonName, PreviousResult);
            m_PreviousButton.tooltip = k_PreviousButtonTooltip;

            m_NextButton = target.SetupButton(k_SearchNextButtonName, NextResult);
            m_NextButton.tooltip = k_NextButtonTooltip;

            m_SearchButton = target.SetupButton(k_OpenSearchButtonName, _ => SearchButtonPressed());
            m_SearchButton.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider, enableOnProviderError: true);

            Context.Blackboard.ActiveConversationChanged += OnActiveConversationChanged;

            HideSearchBar();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            m_SearchField.UnregisterValueChangedCallback(SearchStringChanged);

            Context.Blackboard.ActiveConversationChanged -= OnActiveConversationChanged;
        }

        void TargetDetached(DetachFromPanelEvent evt)
        {
            target.panel.visualTree.UnregisterCallback<KeyDownEvent>(OnKeyDown);
        }

        void TargetAttached(AttachToPanelEvent evt)
        {
            target.panel.visualTree.RegisterCallback<KeyDownEvent>(OnKeyDown);
        }

        void OnActiveConversationChanged(AssistantConversationId previousConversationId,
            AssistantConversationId currentConversationId)
        {
            ClearForNewConversation();
        }

        void ClearForNewConversation()
        {
            m_SearchHighlighter.ClearHighlightableElements();
            m_MessageConverter.Clear();

            SearchResultHighlighted = null;
            m_CurrentSearchResultIndex = 0;
            m_ScrollToMainSearchResult = false;
        }

        void RefreshUI(bool highlight = true)
        {
            var showNextPreviousButtons= m_ConversationSearcher.TotalResultCount > 0;
            m_PreviousButton.SetDisplay(showNextPreviousButtons);
            m_NextButton.SetDisplay(showNextPreviousButtons);

            if (m_ConversationSearcher.TotalResultCount > 0)
            {
                m_SearchResultCountLabel.SetDisplay(true);
                m_SearchResultCountLabel.text =
                    string.Format(k_ResultsText, m_CurrentSearchResultIndex + 1, TotalResultCount);
            }
            else if (string.IsNullOrEmpty(m_ConversationSearcher.SearchString))
            {
                m_SearchResultCountLabel.SetDisplay(false);
            }
            else
            {
                m_SearchResultCountLabel.SetDisplay(true);
                m_SearchResultCountLabel.text = k_NoResultsText;
            }

            if (highlight)
            {
                HighlightSearchResults();
            }
        }

        void HighlightSearchResults()
        {
            int visibleResultsCounter = 0;

            if (m_ConversationSearcher.TotalResultCount > 0)
            {
                if (m_NeedToFindInitialSearchResult)
                {
                    // Find search result closest to the index closest to the top of the view:
                    var messageIndexInView =
                        m_MessageView.GetFirstItemInView();

                    var closestDistance = int.MaxValue;
                    var resultCounter = 0;

                    foreach (var searchResult in m_ConversationSearcher.SearchResults)
                    {
                        var distance = Math.Abs(searchResult.MessageIndex - messageIndexInView);
                        if (distance < closestDistance)
                        {
                            closestDistance = distance;
                            m_CurrentSearchResultIndex = resultCounter;

                            if (distance == 0)
                                break;
                        }

                        resultCounter += searchResult.MatchCount;
                    }

                    // We changed the current search result index, so refresh that part of the UI:
                    RefreshUI(false);
                }

                m_NeedToFindInitialSearchResult = false;

                var totalResultCounter = m_CurrentSearchResultIndex;
                visibleResultsCounter = m_CurrentSearchResultIndex;

                ConversationSearchResult mainSearchResult = null;

                foreach (var searchResult in m_ConversationSearcher.SearchResults)
                {
                    totalResultCounter -= searchResult.MatchCount;
                    mainSearchResult = searchResult;

                    // If the item does not have the UI created, the highlight will not find their labels,
                    // so do not count them towards the main highlight index:
                    if (!m_MessageView.IsItemPopulated(searchResult.MessageIndex))
                    {
                        visibleResultsCounter -= searchResult.MatchCount;
                    }

                    if (totalResultCounter < 0)
                        break;
                }

                if (m_ScrollToMainSearchResult)
                {
                    if (mainSearchResult != null)
                    {
                        m_MessageView.PopulateItem(mainSearchResult.MessageIndex);
                        SearchResultHighlighted?.Invoke(mainSearchResult.MessageId);
                    }
                }
            }

            var scrollToMain = m_ScrollToMainSearchResult;

            EditorApplication.delayCall += () =>
            {
                m_SearchHighlighter.Highlight(m_MessageView,
                    m_ConversationSearcher.SearchString,
                    scrollToMain,
                    visibleResultsCounter);
            };
        }

        void ScrollLabelIntoView(TextElement label)
        {
            var labelRect = SearchHighlighter.GetHighlightedLineNumberRect(label);

            var mainViewRect = m_MessageView.worldBound.center;

            // If the label is already in view, do not move:
            var mainViewRectCheck = m_MessageView.worldBound;
            mainViewRectCheck.yMin += k_LabelRectPadding;
            mainViewRectCheck.yMax -= k_LabelRectPadding;

            if (mainViewRectCheck.Overlaps(labelRect))
                return;

            // Bring main highlight into centre of view:
            var scrollTarget = labelRect.center.y - mainViewRect.y;

            // Avoid micro-movements:
            if (Mathf.Abs(scrollTarget) > 1)
            {
                m_MessageView.ScrollDownBy(scrollTarget);
            }
        }

        void DoSearch()
        {
            if (!m_ConversationSearcher.SearchActiveConversation())
            {
                RefreshUI();
                return;
            }

            if (m_ScrollToMainSearchResult)
            {
                m_CurrentSearchResultIndex =
                    Math.Clamp(m_CurrentSearchResultIndex, 0, Math.Max(0, TotalResultCount - 1));
            }

            RefreshUI();

            // Avoid auto-scrolling when new UI is created:
            m_ScrollToMainSearchResult = false;
        }

        void SearchButtonPressed()
        {
            if (m_SearchContainer.style.display == DisplayStyle.None)
            {
                ShowSearchBar();
            }
            else
            {
                HideSearchBar();
            }
        }

        void ShowSearchBar()
        {
            RefreshUI();
            m_SearchContainer.SetDisplay(true);
            m_SearchTextField.selectAllOnFocus = true;
            m_SearchField.Focus();

            m_SearchButton.tooltip = k_CloseButtonTooltip;
            m_SearchButton.EnableInClassList(k_SearchOpenClass, true);
        }

        void HideSearchBar()
        {
            m_SearchContainer.SetDisplay(false);

            m_SearchField.value = string.Empty;

            m_ConversationSearcher.Clear();
            m_SearchHighlighter.ClearAllHighlights();

            m_SearchButton.tooltip = k_SearchPlaceholderText;
            m_SearchButton.EnableInClassList(k_SearchOpenClass, false);
        }

        void RequestSearchNextFrame()
        {
            EditorApplication.delayCall -= DoSearch;
            EditorApplication.delayCall += DoSearch;
        }

        #region UI Element registration methods

        /// <summary>
        /// Registers additional text that should be considered when searching for a specific message.
        /// Used when a message has additional text in the UI that is not part of the message model's content.
        /// </summary>
        internal void RegisterAdditionalMessageText(
            AssistantMessageId messageId,
            VisualElement element,
            string additionalText)
        {
            m_MessageConverter.RegisterAdditionalMessageText(messageId, element, additionalText);

            RequestSearchNextFrame();
        }

        /// <summary>
        /// Remove entries registered with RegisterAdditionalMessageText.
        /// </summary>
        internal void UnregisterAdditionalMessageText(
            AssistantMessageId messageId,
            VisualElement element)
        {
            m_MessageConverter.UnregisterAdditionalMessageText(messageId, element);
            RequestSearchNextFrame();
        }

        internal void RegisterSearchableTextElement(TextElement textElement)
        {
            m_SearchHighlighter.RegisterHighlightableTextElement(textElement);
            RequestSearchNextFrame();
        }

        internal void UnregisterSearchableTextElement(TextElement textElement)
        {
            m_SearchHighlighter.UnregisterHighlightableTextElement(textElement);
        }

        #endregion

        #region Event Handlers

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.ctrlKey && evt.keyCode == KeyCode.F)
            {
                ShowSearchBar();
            }
        }

        void OnSearchKeyUp(KeyUpEvent evt)
        {
            if (evt.keyCode == KeyCode.Return)
            {
                NextResult(null);
                evt.StopImmediatePropagation();
                m_SearchTextField.selectAllOnFocus = false;
                m_SearchField.Focus();
            }
        }

        void PreviousResult(PointerUpEvent evt)
        {
            if (TotalResultCount > 0)
            {
                m_CurrentSearchResultIndex -= 1;
                if (m_CurrentSearchResultIndex < 0)
                    m_CurrentSearchResultIndex += TotalResultCount;
            }

            m_ScrollToMainSearchResult = true;
            RefreshUI();
        }

        void NextResult(PointerUpEvent evt)
        {
            if (TotalResultCount > 0)
            {
                m_CurrentSearchResultIndex = (m_CurrentSearchResultIndex + 1) % TotalResultCount;
            }

            m_ScrollToMainSearchResult = true;
            RefreshUI();
        }

        void SearchStringChanged(ChangeEvent<string> evt)
        {
            m_ConversationSearcher.SearchString = evt.newValue;

            m_ScrollToMainSearchResult = true;
            m_NeedToFindInitialSearchResult = true;

            DoSearch();
        }

        #endregion
    }
}
