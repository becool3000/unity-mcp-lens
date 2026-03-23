using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.Checkpoint.Events;
using Unity.AI.Assistant.Editor.SessionBanner;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.History;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;
using Trace = Unity.AI.Tracing.Trace;
using TraceEventOptions = Unity.AI.Tracing.TraceEventOptions;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantView : ManagedTemplate
    {
        static readonly char[] k_MessageTrimChars = { ' ', '\n', '\r', '\t' };

        const string k_HistoryOpenClass = "mui-chat-history-open";
        const string k_ProviderUnity = "unity";

        readonly IAssistantHostWindow k_HostWindow;

        static CancellationTokenSource s_NewChatActiveTokenSource;

        VisualElement m_RootMain;
        VisualElement m_RootPanel;

        Button m_NewChatButton;
        Button m_HistoryButton;
        Button m_HideRevertedTimeStampButton;

        Label m_ConversationName;

        AssistantConversationPanel m_AssistantConversationPanel;

        VisualElement m_HistoryPanelRoot;
        VisualElement m_ProgressElementRoot;
        ProgressElement m_ProgressElement;

        HistoryPanel m_HistoryPanel;
        Rect m_HistoryPanelWorldBounds;

        VisualElement m_HeaderRow;
        VisualElement m_FooterRoot;
        VisualElement m_PopupAnchor;

        VisualElement m_ChatInputRoot;
        AssistantTextField m_ChatInput;

        VisualElement m_PopupRoot;
        SelectionPopup m_SelectionPopup;
        PopupTracker m_SelectionPopupTracker;
        PopupTracker m_ContextDropdownTracker;
        VisualElement m_ContextDropdownButton;
        Label m_DropdownToggleLabel;
        readonly string k_ContextDropdownToggleFocused = "mui-context-dropdown-toggle-focused";

        Button m_ClearContextButton;

        VisualElement m_SelectedContextRoot;
        ContextDropdown m_SelectedContextDropdown;

        Button m_WhatsNewButton;
        VisualElement m_EmptyStateRoot;
        VisualElement m_EmptyStateLoading;
        VisualElement m_EmptyStateContent;
        LoadingSpinner m_EmptyStateSpinner;
        GatewayDisclaimerView m_GatewayDisclaimerView;

        int m_SelectedConsoleMessageNum;
        string m_SelectedConsoleMessageContent;
        string m_SelectedGameObjectName;

        bool m_WaitingForConversationChange;

        BaseEventSubscriptionTicket m_ConversationSelectedEventTicket;
        BaseEventSubscriptionTicket m_RevertedTimeStampFilterRequestedEventTicket;
        BaseEventSubscriptionTicket m_GetRevertedTimeStampFilterEventTicket;
        BaseEventSubscriptionTicket m_CheckpointEnableStateChangedEventTicket;

        long m_RevertedTimeStampFilter;

        // Provider switching state
        bool m_IsSwitchingProvider;
        bool m_IsRecoveringFromCredentialError;

        /// <summary>
        /// Constructor for the MuseChatView.
        /// </summary>
        public AssistantView()
            : this(null)
        {
        }

        public AssistantView(IAssistantHostWindow hostWindow)
            : base(AssistantUIConstants.UIModulePath)
        {
            k_HostWindow = hostWindow;

            RegisterAttachEvents(OnAttachToPanel, OnDetachFromPanel);
        }

        public void InitializeThemeAndStyle()
        {
            LoadStyle(m_RootPanel, EditorGUIUtility.isProSkin ? AssistantUIConstants.AssistantSharedStyleDark : AssistantUIConstants.AssistantSharedStyleLight);
            LoadStyle(m_RootPanel, AssistantUIConstants.AssistantBaseStyle, true);
            LoadStyle(m_RootPanel, "AiGatewayDisclaimer");
        }

        public bool TryPushInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement userInteraction)
        {
            return m_AssistantConversationPanel.TryPushInteraction(callInfo, userInteraction);
        }

        public bool TryPopInteraction(ToolExecutionContext.CallInfo callInfo, VisualElement userInteraction)
        {
            return m_AssistantConversationPanel.TryPopInteraction(callInfo, userInteraction);
        }

        /// <summary>
        /// Provide access to the currently active context to Debug and Internal tools
        /// Note: Do not use this for public facing operations
        /// </summary>
        internal AssistantUIContext ActiveUIContext => Context;

        /// <summary>
        /// Initialize the view and its component, called by the managed template
        /// </summary>
        /// <param name="view">the template container of the current element</param>
        protected override void InitializeView(TemplateContainer view)
        {
            // Suspend any saving of state during initialization until the state was restored (RestoreState)
            Context.Blackboard.SuspendStateSave();

            style.flexGrow = 1;
            view.style.flexGrow = 1;

            m_HeaderRow = view.Q<VisualElement>("headerRow");

            m_RootMain = view.Q<VisualElement>("root-main");
            m_RootMain.RegisterCallback<DragEnterEvent>(OnMainDragEnter);
            m_RootMain.RegisterCallback<DragLeaveEvent>(OnMainDragLeave);
            m_RootMain.RegisterCallback<DragExitedEvent>(OnMainDragExit);

            m_RootPanel = view.Q<VisualElement>("root-panel");

            m_NewChatButton = view.SetupButton("newChatButton", OnNewChatClicked);
            m_NewChatButton.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider, enableOnProviderError: true);
            m_HistoryButton = view.SetupButton("historyButton", OnHistoryClicked);
            m_HistoryButton.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider, enableOnProviderError: true);
            m_HideRevertedTimeStampButton = view.SetupButton("hideRevertedTimeStampViewButton", OnHideRevertedTimeStampClicked);
            m_HideRevertedTimeStampButton.SetDisplay(false);

            m_ConversationName = view.Q<Label>("conversationNameLabel");
            m_ConversationName.enableRichText = false;

            var panelRoot = view.Q<VisualElement>("chatPanelRoot");
            m_AssistantConversationPanel = new AssistantConversationPanel();
            m_AssistantConversationPanel.Initialize(Context);
            m_AssistantConversationPanel.RegisterCallback<MouseUpEvent>(OnConversationPanelClicked);
            panelRoot.Add(m_AssistantConversationPanel);

            m_HistoryPanelRoot = view.Q<VisualElement>("historyPanelRoot");
            m_HistoryPanel = new HistoryPanel();
            m_HistoryPanel.Initialize(Context);
            m_HistoryPanelRoot.Add(m_HistoryPanel);
            RegisterCallback<ClickEvent>(CheckHistoryPanelClick);
            m_HistoryPanelRoot.style.display = AssistantUISessionState.instance.IsHistoryOpen ? DisplayStyle.Flex : DisplayStyle.None;

            m_ProgressElementRoot = view.Q<VisualElement>("progressElementContainer");
            m_ProgressElement = new ProgressElement();
            m_ProgressElement.Initialize(Context);
            m_ProgressElement.Hide();
            m_ProgressElementRoot.Add(m_ProgressElement);

            view.AddSessionRefreshManipulators(Context.API.Provider);

            m_FooterRoot = view.Q<VisualElement>("footerRoot");
            // Note: Status tracking is applied granularly to footer children in AssistantTextField
            // to keep ProviderSelector always enabled. contextRoot is tracked here.
            view.Q<VisualElement>("contextRoot")?.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider);

            m_PopupAnchor = view.Q<VisualElement>("popupAnchor");

            m_SelectedContextRoot = view.Q<VisualElement>("userSelectedContextRoot");
            m_ClearContextButton = view.Q<Button>("clearContextButton");

            m_ChatInputRoot = view.Q<VisualElement>("chatTextFieldRoot");

            m_PopupRoot = view.Q<VisualElement>("chatModalPopupRoot");
            InitializeSelectionPopup();

            m_ContextDropdownButton = view.Q<VisualElement>("dropdownToggle");
            InitializeContextDropdown();
            m_DropdownToggleLabel = view.Q<Label>("dropdownToggleLabel");

            m_ChatInput = new AssistantTextField();
            m_ChatInput.Initialize(Context);
            // Pre-seed provider UI from session state to avoid selector flicker on domain reload.
            var lastProviderId = AssistantUISessionState.instance.LastActiveProviderId;
            if (string.IsNullOrEmpty(lastProviderId))
            {
                lastProviderId = k_ProviderUnity;
            }
            m_ChatInput.SetProvider(lastProviderId, triggerEvent: false);
            m_ChatInput.SetHost(m_PopupRoot);
            m_ChatInput.SubmitRequest += OnRequestSubmit;
            m_ChatInput.CancelRequest += OnActiveProgressCancelRequested;
            m_ChatInput.OnProviderChanged += OnProviderChanged;
            m_ChatInput.OnCommandSelected += OnCommandSelected;
            m_ChatInput.ContextButton.RegisterCallback<PointerUpEvent>(_ => ToggleSelectionPopup());
            m_ContextDropdownButton.RegisterCallback<PointerUpEvent>(_ => ToggleContextDropdown());
            m_ChatInputRoot.Add(m_ChatInput);

            m_EmptyStateRoot = view.Q<VisualElement>("emptyStateRoot");
            m_EmptyStateLoading = view.Q<VisualElement>("emptyStateLoading");
            m_EmptyStateContent = view.Q<VisualElement>("emptyStateContent");
            m_EmptyStateSpinner = new LoadingSpinner();
            m_EmptyStateSpinner.Hide();
            m_EmptyStateLoading.Add(m_EmptyStateSpinner);
            m_WhatsNewButton = view.Q<Button>("museChatWhatsNewButton");
            m_WhatsNewButton.clicked += WhatsNewWindow.ShowWindow;

            m_GatewayDisclaimerView = new GatewayDisclaimerView();
            m_GatewayDisclaimerView.IsActiveChanged += isActive =>
            {
                if (isActive) m_ChatInput.Disable();
                else m_ChatInput.Enable();
            };
            view.Q<VisualElement>("chatContentInner").Add(m_GatewayDisclaimerView);

            UpdateAssistantEditorDriverContext();
            UpdateWarnings();

            EditorApplication.hierarchyChanged += OnHierarchChanged;

            ClearChat();

            m_DropZoneRoot = view.Q<VisualElement>("dropZoneRoot");
            m_DropZone = new ChatDropZone();
            m_DropZone.Initialize(Context);
            m_DropZoneRoot.Add(m_DropZone);
            m_DropZoneOverlay = view.Q<VisualElement>("dropZoneOverlay");
            m_DropZone.SetupDragDrop(m_DropZoneOverlay, OnDropped);

            m_DropZone.SetDropZoneActive(false);

            view.AddManipulator(Context.SearchHelper);
            view.AddManipulator(new PointsBalanceChanges(OnPointsBalanceChanged));

            view.RegisterCallback<GeometryChangedEvent>(OnViewGeometryChanged);

            Context.Initialize();

            UpdateContextSelectionElements();

            Context.API.ConversationReload += OnConversationReload;
            Context.API.ConversationChanged += OnConversationChanged;
            Context.API.ConversationDeleted += OnConversationDeleted;

            ScheduleConversationRefresh();

            Context.ConversationRenamed += OnConversationRenamed;
            Context.ProviderSwitched += OnProviderSwitched;
            GatewayPreferenceService.Instance.Preferences.OnChange += OnGatewayPreferencesChanged;
            // Subscribe to capability events (forwarded from any provider that supports them)
            Context.API.ModelsAvailable += OnModelsAvailable;
            Context.API.AvailableCommandsChanged += OnAvailableCommandsChanged;
            m_ChatInput.OnModelSelected += OnModelSelected;
            Context.API.ReplayCachedAvailableCommands();

            // Bind mode provider to current assistant provider
            m_ChatInput.BindModeProvider(Context.API.Provider);

            RegisterContextCallbacks();
        }

        private void OnPointsBalanceChanged()
        {
            if (!Account.pointsBalance.CanAfford(AssistantConstants.ChatPreAuthorizePoints))
                m_ChatInput.Disable();
            else
                m_ChatInput.Enable();
        }

        public void InitializeState()
        {
            RestoreConversationState();
        }

        void ScheduleConversationRefresh()
        {
            Context.API.RefreshConversations();

            // Schedule another history update in 5 minutes.
            schedule.Execute(ScheduleConversationRefresh).StartingIn(1000 * 60 * 5);
        }

        void CheckHistoryPanelClick(ClickEvent e)
        {
            var clickOfHistoryButton = m_HistoryButton.worldBound.Contains(e.position);
            var clickWithinHistoryPanel = m_HistoryPanel.worldBound.Contains(e.position);

            if (!clickWithinHistoryPanel && AssistantUISessionState.instance.IsHistoryOpen && !clickOfHistoryButton)
            {
                SetHistoryDisplay(false);
            }
        }

        void OnConversationPanelClicked(MouseUpEvent evt)
        {
            SetHistoryDisplay(false);
        }

        public void Deinit()
        {
            Context.Deinitialize();

            UnregisterContextCallbacks();

            // Unsubscribe from capability events
            Context.API.ModelsAvailable -= OnModelsAvailable;
            Context.API.AvailableCommandsChanged -= OnAvailableCommandsChanged;
            Context.ProviderSwitched -= OnProviderSwitched;
            m_ChatInput.OnModelSelected -= OnModelSelected;
        }

        async void RestoreConversationState()
        {
            var lastMode = AssistantUISessionState.instance.LastActiveMode;
            Context.Blackboard.ActiveMode = lastMode;

            var lastConvId = AssistantUISessionState.instance.LastActiveConversationId;
            if (string.IsNullOrEmpty(lastConvId))
            {
                // No conversation to restore - but ensure the provider is switched
                // (e.g., after domain reload with a non-Unity provider on a new conversation)
                await RestoreProviderIfNeeded();
                RestoreUIState(default);
                return;
            }

            m_WaitingForConversationChange = true;
            UpdateEmptyStateDisplay();

            var id = new AssistantConversationId(lastConvId);

            // Use ConversationReloadManager to load the conversation
            // This will automatically determine the provider, switch to it if needed,
            // and set the active conversation in the blackboard
            try
            {
                await Context.ConversationReloadManager.LoadConversationAsync(id);

                // After successful reload, update the UI to reflect the current provider
                // This is needed because ConversationReloadManager may have switched providers
                UpdateUIForCurrentProvider();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to restore conversation {lastConvId}: {ex.Message}");
                // Conversation load failed - but ensure the provider is switched
                // so the user can start a new conversation with the same provider
                m_WaitingForConversationChange = false;
                UpdateEmptyStateDisplay();
                await RestoreProviderIfNeeded();
                RestoreUIState(default);
                return;
            }

        }

        /// <summary>
        /// Ensures the saved provider is active after domain reload.
        /// When there's no conversation to restore (new conversation or failed load),
        /// we still need to switch to the saved provider so its session is created.
        /// Without this, the UI shows the provider name but no AcpProvider/session exists,
        /// leaving the relay with no session and models unavailable.
        /// </summary>
        async Task RestoreProviderIfNeeded()
        {
            var lastProviderId = AssistantUISessionState.instance.LastActiveProviderId;
            if (string.IsNullOrEmpty(lastProviderId) || lastProviderId == k_ProviderUnity)
                return;

            try
            {
                await Context.SwitchProviderAsync(lastProviderId);
                m_ChatInput.BindModeProvider(Context.API.Provider);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AssistantView] Failed to restore provider '{lastProviderId}': {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the UI components to reflect the current provider after a provider switch.
        /// </summary>
        void UpdateUIForCurrentProvider()
        {
            var currentProviderId = Context.CurrentProviderId;

            // Update the provider dropdown UI (without triggering event since provider is already switched)
            m_ChatInput.SetProvider(currentProviderId, triggerEvent: false);

            // Bind mode provider to update available modes
            m_ChatInput.BindModeProvider(Context.API.Provider);

            // Notify provider state observer
            ProviderStateObserver.SetProvider(currentProviderId);
        }

        void OnProviderSwitched()
        {
            // Update UI to reflect the newly switched provider
            UpdateUIForCurrentProvider();
            Context.API.ReplayCachedAvailableCommands();
        }

        void RestoreUIState(AssistantConversationId conversationId)
        {
            Trace.Event("ui.restore.begin", new TraceEventOptions
            {
                Level = "debug",
                Data = new { conversation = conversationId.Value, isWorking = Context.Blackboard.IsAPIWorking, isReady = Context.Blackboard.IsAPIReadyForPrompt }
            });

            if (m_WaitingForConversationChange)
            {
                if (Application.isBatchMode)
                {
                    EditorApplication.delayCall += () => RestoreUIState(conversationId);
                }
                // In non-batch mode, OnConversationReload will call RestoreUIState after Populate() completes
                return;
            }

            // Check for incomplete message to recover
            string incompleteId = AssistantUISessionState.instance.IncompleteMessageId;
            Trace.Event("ui.restore.incomplete", new TraceEventOptions
            {
                Level = "debug",
                Data = new { incompleteId = incompleteId ?? "(none)" }
            });
            if (conversationId != default && !string.IsNullOrEmpty(incompleteId))
            {
                // Set working state so UI shows stop button instead of send
                Context.API.SetWorkingState(true);
                // Recover incomplete message now that conversation is loaded
                Context.API.RecoverIncompleteMessage(conversationId);
            }

            m_ChatInput.SetText(AssistantUISessionState.instance.Prompt);
            var serializableContextList = JsonUtility.FromJson<AssistantContextList>(AssistantUISessionState.instance.Context);
            k_SelectedContext.Clear();
            if (serializableContextList?.m_ContextList.Count > 0)
            {
                RestoreContextSelection(serializableContextList.m_ContextList);
                UpdateContextSelectionElements();
            }

            Trace.Event("ui.restore.complete", new TraceEventOptions
            {
                Level = "debug",
                Data = new { isWorking = Context.Blackboard.IsAPIWorking, isReady = Context.Blackboard.IsAPIReadyForPrompt }
            });
            Context.Blackboard.ResumeStateSave();
            m_GatewayDisclaimerView.Refresh();
        }

        void OnConversationDeleted(AssistantConversationId conversationId)
        {
            if (!Context.Blackboard.ActiveConversationId.IsValid)
            {
                // Clear the chat, in case we deleted our active conversation
                ClearChat();
            }
        }

        void OnConversationRenamed(AssistantConversationId id)
        {
            if (Context.Blackboard.ActiveConversationId == id)
            {
                UpdateConversationTitle(id);
            }
        }

        void OnConversationChanged(AssistantConversationId conversationId)
        {
            UpdateConversationTitle(conversationId);

            // Hide empty state when conversation has content
            var conversation = Context.Blackboard.GetConversation(conversationId);
            if (conversation != null)
            {
                m_EmptyStateRoot.SetDisplay(conversation.Messages.Count == 0);
                UpdateEmptyStateDisplay();
            }
        }

        void UpdateConversationTitle(AssistantConversationId conversationId)
        {
            var conversation = Context.Blackboard.GetConversation(conversationId);
            if (conversation == null)
            {
                // We have not received this conversation data yet
                return;
            }

            m_ConversationName.text = conversation.Title;
        }

        void OnConversationReload(AssistantConversationId conversationId)
        {
            InternalLogUtils.PerformAndSetupDomainReloadLog(conversationId, Context);

            // If this conversation is not active, we don't display it
            if (Context.Blackboard.ActiveConversationId != conversationId)
                return;

            bool wasWaitingForRestore = m_WaitingForConversationChange;

            ClearChat(false);

            m_WaitingForConversationChange = false;
            UpdateEmptyStateDisplay();
            var conversation = Context.Blackboard.GetConversation(conversationId);
            if (conversation == null)
            {
                // We have not received this conversation data yet
                return;
            }

            var sw = new Stopwatch();
            sw.Start();
            try
            {
                m_ConversationName.text = conversation.Title;
                m_AssistantConversationPanel.Populate(conversation);
                m_EmptyStateRoot.SetDisplay(conversation.Messages.Count == 0);
                UpdateEmptyStateDisplay();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError("Failed to populate conversation panel: " + e.Message);
            }
            finally
            {
                sw.Stop();

                InternalLog.Log($"PopulateConversation took {sw.ElapsedMilliseconds}ms ({conversation.Messages.Count} Messages)");
            }

            // After Populate, trigger UI state restoration if this was a domain reload restore.
            // In non-batch mode, call RestoreUIState directly instead of using delayCall
            // which was vulnerable to being wiped by subsequent domain reloads.
            // In batch mode, use the original delayCall approach.
            if (wasWaitingForRestore)
            {
                if (Application.isBatchMode)
                {
                    EditorApplication.delayCall += () => RestoreUIState(conversationId);
                }
                else
                {
                    RestoreUIState(conversationId);
                }
            }
        }

        void ClearChat(bool clearInput = true)
        {
            m_ConversationName.text = "New conversation";

            if (clearInput)
            {
                m_ChatInput.ClearText();
            }

            m_AssistantConversationPanel.ClearConversation();
            m_EmptyStateRoot.SetDisplay(true);
            UpdateEmptyStateDisplay();
        }

        /// <summary>
        /// Updates the empty state root to show either a loading spinner (when waiting for conversation after domain reload)
        /// or the empty state content (See what's new + disclaimer) when loaded.
        /// </summary>
        void UpdateEmptyStateDisplay()
        {
            if (m_WaitingForConversationChange)
            {
                m_EmptyStateLoading.SetDisplay(true);
                m_EmptyStateContent.SetDisplay(false);
                m_EmptyStateSpinner.Show();
            }
            else
            {
                m_EmptyStateLoading.SetDisplay(false);
                m_EmptyStateContent.SetDisplay(true);
                m_EmptyStateSpinner.Hide();
            }
        }

        void OnHistoryClicked(PointerUpEvent evt)
        {
            Context.API.RefreshConversations();

            bool status = !(m_HistoryPanelRoot.style.display == DisplayStyle.Flex);
            SetHistoryDisplay(status);
        }

        void SetHistoryDisplay(bool isVisible)
        {
            m_HistoryPanelRoot.style.display = isVisible
                ? DisplayStyle.Flex
                : DisplayStyle.None;

            m_HistoryButton.EnableInClassList(k_HistoryOpenClass, isVisible);

            AssistantUISessionState.instance.IsHistoryOpen = isVisible;
        }

        void OnHistoryEntrySelected(EventHistoryConversationSelected eventData)
        {
            SetHistoryDisplay(false);
        }

        void OnRevertedTimeStampFilterRequested(EventRevertedTimeStampFilterRequested eventData)
        {
            SetRevertedTimeStampFilter(eventData.Timestamp);
        }

        void OnGetRevertedTimeStampFilter(EventGetRevertedTimeStampFilter eventData)
        {
            eventData.Timestamp = m_RevertedTimeStampFilter;
        }

        void ResetConversation(string providerId)
        {
            // Single path - API.CancelPrompt works for all providers
            Context.API.CancelPrompt();

            Context.Blackboard.ClearActiveConversation(true);
            AssistantUISessionState.instance.LastActiveConversationId = null;
            ClearChat();
            ClearContext(null);
            m_ProgressElement.Stop();

            Context.API.Reset();

            m_NewChatButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, true);
            TimerUtils.DelayedAction(ref s_NewChatActiveTokenSource, () =>
            {
                m_NewChatButton.EnableInClassList(AssistantUIConstants.ActiveActionButtonClass, false);
            });
        }

        /// <summary>
        /// When gateway preferences change while a non-Unity provider is in error state,
        /// auto-create a new conversation so updated credentials/settings take effect.
        /// </summary>
        async void OnGatewayPreferencesChanged()
        {
            if (m_IsRecoveringFromCredentialError)
                return;

            if (ProviderStateObserver.IsUnityProvider)
                return;

            if (ProviderStateObserver.ReadyState != ProviderStateObserver.ProviderReadyState.Error)
                return;

            m_IsRecoveringFromCredentialError = true;
            try
            {
                try
                {
                    await Context.API.EndActiveSessionAsync();
                }
                catch (Exception ex)
                {
                    InternalLog.LogWarning($"[AssistantView] End session failed during credential recovery: {ex.Message}");
                }

                var providerId = m_ChatInput.SelectedProviderId;
                ResetConversation(providerId);

                if (providerId != k_ProviderUnity)
                {
                    await Context.SwitchProviderAsync(providerId);
                    m_ChatInput.BindModeProvider(Context.API.Provider);
                }
            }
            finally
            {
                m_IsRecoveringFromCredentialError = false;
            }
        }

        internal async void OnNewChatClicked(PointerUpEvent evt)
        {
            InternalLog.Log("[AssistantView] New conversation button clicked");
            try
            {
                await Context.API.EndActiveSessionAsync();
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[AssistantView] End session failed: {ex.Message}");
            }

            ResetConversation(m_ChatInput.SelectedProviderId);

            // For non-Unity providers, create a new session by switching to the same provider
            if (m_ChatInput.SelectedProviderId != k_ProviderUnity)
            {
                await Context.SwitchProviderAsync(m_ChatInput.SelectedProviderId);
                m_ChatInput.BindModeProvider(Context.API.Provider);
            }

            AIAssistantAnalytics.ReportUITriggerBackendCreateNewConversationEvent();
        }

        void OnHierarchChanged()
        {
            UpdateContextSelectionElements();
        }

        void OnAssetDeletes(string[] paths)
        {
            CheckContextForDeletedAssets(paths);
        }

        async void OnProviderChanged(string oldProviderId, string newProviderId)
        {
            // Notify the provider state observer FIRST so status tracking updates immediately
            ProviderStateObserver.SetProvider(newProviderId);

            // Prevent re-entrancy when users toggle providers quickly
            if (m_IsSwitchingProvider)
                return;

            m_IsSwitchingProvider = true;
            try
            {
                await SwitchProviderCoreAsync(oldProviderId, newProviderId);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AssistantView] Provider switch failed: {ex.Message}");
            }
            finally
            {
                m_IsSwitchingProvider = false;
            }
        }

        async Task SwitchProviderCoreAsync(string oldProviderId, string newProviderId)
        {
            // End active session before switching — ensures cancel completes
            // and session is released before the old provider is disposed.
            try
            {
                await Context.API.EndActiveSessionAsync();
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[AssistantView] End session failed during provider switch: {ex.Message}");
            }

            // Clear conversation panel
            m_AssistantConversationPanel.ClearConversation();

            // Clear model selector - new provider will fire events if it supports them
            m_ChatInput.ClearModels();

            // Clear all conversations from the blackboard to prevent mixing provider histories
            Context.Blackboard.ClearConversations();

            ResetConversation(oldProviderId);

            if (oldProviderId == k_ProviderUnity && newProviderId != k_ProviderUnity)
            {
                Context.API.DisconnectWorkflow();
            }

            // Switch provider via the context (factory handles session lifecycle)
            await Context.SwitchProviderAsync(newProviderId);

            // Bind mode provider to the new assistant provider
            m_ChatInput.BindModeProvider(Context.API.Provider);
        }

        void OnModelsAvailable((string modelId, string name, string description)[] models, string currentModelId)
        {
            var providerId = m_ChatInput.SelectedProviderId;
            var preferredModelId = AssistantEditorPreferences.GetSelectedModel(providerId);
            var selectedModelId = currentModelId;

            if (!string.IsNullOrEmpty(preferredModelId) && models != null)
            {
                var preferredAvailable = false;
                foreach (var (modelId, _, _) in models)
                {
                    if (modelId == preferredModelId)
                    {
                        preferredAvailable = true;
                        break;
                    }
                }

                if (preferredAvailable)
                {
                    selectedModelId = preferredModelId;

                    if (!string.IsNullOrEmpty(currentModelId) && currentModelId != preferredModelId)
                    {
                        // Re-apply the preferred model after reload if session reports a different model.
                        Context.API.SetModel(preferredModelId);
                    }
                }
            }

            m_ChatInput.SetModels(models, selectedModelId);
        }

        void OnAvailableCommandsChanged((string name, string description)[] commands)
        {
            m_ChatInput.SetAvailableCommands(commands);
        }

        void OnCommandSelected(string commandName)
        {
            // Send the command as a user message with "/" prefix
            OnRequestSubmit("/" + commandName);
        }

        void OnModelSelected(string modelId)
        {
            // Save the model preference
            AssistantEditorPreferences.SetSelectedModel(m_ChatInput.SelectedProviderId, modelId);

            // Send the model change via the provider
            Context.API.SetModel(modelId);
        }

        void OnActiveProgressCancelRequested()
        {
            if (!Context.Blackboard.IsAPIWorking)
            {
                return;
            }

            AIAssistantAnalytics.ReportUITriggerBackendCancelRequestEvent(Context.Blackboard.ActiveConversationId);

            // Single path - works for all providers
            Context.API.CancelAssistant(Context.Blackboard.ActiveConversationId);
        }

        void OnRequestSubmit(string message)
        {
            message = message.Trim(k_MessageTrimChars);
            if (string.IsNullOrEmpty(message))
            {
                m_ChatInput.ClearText();
                return;
            }

            // Single code path for all providers
            if (Context.Blackboard.IsAPIWorking)
            {
                Context.API.CancelAssistant(Context.Blackboard.ActiveConversationId);
                m_ChatInput.ClearText();
                return;
            }

            Context.Blackboard.UnlockConversationChange();
            m_ChatInput.ClearText();
            m_EmptyStateRoot.SetDisplay(false);
            Context.API.SendPrompt(message, Context.Blackboard.ActiveMode);

            // Clear screenshot attachments after sending the prompt
            ClearScreenshotContextEntries();
            UpdateContextSelectionElements();
        }

        void OnViewGeometryChanged(GeometryChangedEvent evt)
        {
            bool isCompactView = evt.newRect.width < AssistantUIConstants.CompactWindowThreshold;

            m_HistoryButton.EnableInClassList(AssistantUIConstants.CompactStyle, isCompactView);
            m_NewChatButton.EnableInClassList(AssistantUIConstants.CompactStyle, isCompactView);

            m_ConversationName.EnableInClassList(AssistantUIConstants.CompactStyle, isCompactView);

            m_FooterRoot.EnableInClassList(AssistantUIConstants.CompactStyle, isCompactView);
        }

        void AddItemsNumberToLabel(int numItems)
        {
            m_DropdownToggleLabel.text = $"Attached items ({numItems})";
        }

        void ToggleContextDropdown()
        {
            if (m_SelectedContextDropdown.IsShown)
            {
                m_ContextDropdownButton.RemoveFromClassList(k_ContextDropdownToggleFocused);
                HideContextPopup();
            }
            else
            {
                m_ContextDropdownButton.AddToClassList(k_ContextDropdownToggleFocused);
                ShowContextPopup();
            }
        }

        void ToggleSelectionPopup()
        {
            if (m_SelectionPopup.IsShown)
            {
                HideSelectionPopup();
            }
            else
            {
                ShowSelectionPopup();
            }
        }

        void ShowContextPopup()
        {
            m_SelectedContextDropdown.Show();

            m_ContextDropdownTracker = new PopupTracker(
                m_SelectedContextDropdown,
                m_ContextDropdownButton,
                new Vector2Int(-1, 47),
                m_ContextDropdownButton
            );
            m_ContextDropdownTracker.Dismiss += HideContextPopup;
        }

        void HideContextPopup()
        {
            if (m_ContextDropdownTracker == null)
            {
                // Popup is not active
                return;
            }

            m_ContextDropdownTracker.Dismiss -= HideContextPopup;
            m_ContextDropdownTracker.Dispose();
            m_ContextDropdownTracker = null;

            m_SelectedContextDropdown.Hide();
        }

        void ShowSelectionPopup()
        {
            // Restore previous context selection
            m_SelectionPopup.SetSelectionFromContext(k_SelectedContext);

            m_ChatInput.ContextButton.EnableInClassList("mui-selected-context-button-open", true);

            m_SelectionPopup.ShowPopup();

            m_SelectionPopupTracker = new PopupTracker(m_SelectionPopup, m_ChatInput.ContextButton, m_PopupAnchor);
            m_SelectionPopupTracker.Dismiss += HideSelectionPopup;
        }

        void HideSelectionPopup()
        {
            if (m_SelectionPopupTracker == null)
            {
                // Popup is not active
                return;
            }

            m_SelectionPopupTracker.Dismiss -= HideSelectionPopup;
            m_SelectionPopupTracker.Dispose();
            m_SelectionPopupTracker = null;

            m_SelectionPopup.Hide();

            m_ChatInput.ContextButton.EnableInClassList("mui-selected-context-button-open", false);
            m_ChatInput.ContextButton.EnableInClassList("mui-selected-context-button-default-behavior", true);
        }

        void InitializeContextDropdown()
        {
            m_SelectedContextDropdown = new ContextDropdown();
            m_SelectedContextDropdown.Initialize(Context);
            m_SelectedContextDropdown.Hide();

            m_PopupRoot.Add(m_SelectedContextDropdown);

            if (k_HostWindow != null)
            {
                k_HostWindow.FocusLost += HideContextPopup;
            }
        }

        void InitializeSelectionPopup()
        {
            m_SelectionPopup = new SelectionPopup();
            m_SelectionPopup.Initialize(Context);
            m_SelectionPopup.Hide();
            m_SelectionPopup.OnSelectionChanged += () =>
            {
                // Memorize current context selection
                SyncContextSelection(m_SelectionPopup.ObjectSelection, m_SelectionPopup.ConsoleSelection);

                UpdateContextSelectionElements();
            };
            m_SelectionPopup.OnAnnotationRequested += OnAnnotationRequested;

            m_PopupRoot.Add(m_SelectionPopup);

            if (k_HostWindow != null)
            {
                k_HostWindow.FocusLost += HideSelectionPopup;
            }
        }

        void OnAnnotationRequested()
        {
            // Open the Edit Screen Capture window for annotations
            EditScreenCaptureWindow.Open();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            GatewayPreferenceService.Instance.Preferences.OnChange -= OnGatewayPreferencesChanged;
            AssistantAssetModificationDelegates.AssetDeletes -= OnAssetDeletes;

            AssistantEvents.Unsubscribe(ref m_ConversationSelectedEventTicket);
            AssistantEvents.Unsubscribe(ref m_RevertedTimeStampFilterRequestedEventTicket);
            AssistantEvents.Unsubscribe(ref m_GetRevertedTimeStampFilterEventTicket);
            AssistantEvents.Unsubscribe(ref m_CheckpointEnableStateChangedEventTicket);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            AssistantAssetModificationDelegates.AssetDeletes += OnAssetDeletes;

            m_ConversationSelectedEventTicket = AssistantEvents.Subscribe<EventHistoryConversationSelected>(OnHistoryEntrySelected);
            m_RevertedTimeStampFilterRequestedEventTicket = AssistantEvents.Subscribe<EventRevertedTimeStampFilterRequested>(OnRevertedTimeStampFilterRequested);
            m_GetRevertedTimeStampFilterEventTicket = AssistantEvents.Subscribe<EventGetRevertedTimeStampFilter>(OnGetRevertedTimeStampFilter);
            m_CheckpointEnableStateChangedEventTicket = AssistantEvents.Subscribe<EventCheckpointEnableStateChanged>(OnCheckpointEnabledChanged);
        }

        /// <summary>
        /// Ensures the specified provider is selected. Used by AssistantApi to reset state.
        /// </summary>
        /// <param name="providerId">The provider ID to ensure is active. Defaults to "unity".</param>
        public async Task EnsureProviderAsync(string providerId = k_ProviderUnity)
        {
            if (m_ChatInput.SelectedProviderId == providerId)
                return;

            var oldProviderId = m_ChatInput.SelectedProviderId;

            // Update the provider selector UI (without triggering event since we handle manually)
            m_ChatInput.SetProvider(providerId, triggerEvent: false);

            await SwitchProviderCoreAsync(oldProviderId, providerId);
        }

        public void SetRevertedTimeStampFilter(long timestamp)
        {
            if (m_RevertedTimeStampFilter == timestamp)
                return;
            
            m_RevertedTimeStampFilter = timestamp;

            var showRevertedMessages = (timestamp != 0);
            m_FooterRoot.SetDisplay(!showRevertedMessages);
            m_NewChatButton.SetDisplay(!showRevertedMessages);
            m_HistoryButton.SetDisplay(!showRevertedMessages);
            m_HideRevertedTimeStampButton.SetDisplay(showRevertedMessages);

            Context.API.RefreshConversation();
        }
        
        void OnHideRevertedTimeStampClicked(PointerUpEvent evt)
        {
            SetRevertedTimeStampFilter(0);
        }
        
        void OnCheckpointEnabledChanged(EventCheckpointEnableStateChanged eventData)
        {
            if (Context.Blackboard.ActiveConversationId != AssistantConversationId.Invalid)
            {
                OnConversationReload(Context.Blackboard.ActiveConversationId);
            }
        }
    }
}
