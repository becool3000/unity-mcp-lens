using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Editor.Utils.Event;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.UserInteraction;
using Unity.AI.Assistant.UI.Editor.Scripts.Events;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;

using Unity.Relay.Editor.Acp;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UIElements;
using TextField = UnityEngine.UIElements.TextField;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantTextField : ManagedTemplate
    {
        const string k_ChatFocusClass = "mui-mft-input-focused";
        const string k_ChatHoverClass = "mui-mft-input-hovered";
        const string k_ChatActionEnabledClass = "mui-submit-enabled";
        const string k_HasInteractionClass = "mui-mtf-has-interaction";

        const string k_SubmitImage = "arrow-up";
        const string k_StopImage = "stop-square";

        const string k_ActionButtonToolTipSend = "Send prompt";
        const string k_ActionButtonToolTipStop = "Stop response";
        const string k_ActionButtonToolTipNoPrompt = "No prompt entered";
        const string k_ActionButtonToolTipTotalImageSizeExceeded = "Total image size exceeds 5MB";

        const string k_PlaceholderAsk = "Ask about Unity";
        const string k_PlaceholderAgent = "Build with Unity";
        const string k_PlaceholderInitializing = "Initializing session...";
        const string k_PlaceholderError = "Session failed to initialize";

        VisualElement m_Root;
        UserInteractionBar m_InteractionBar;
        BaseEventSubscriptionTicket m_InteractionQueueSubscription;

        Button m_ActionButton;
        AssistantImage m_SubmitButtonImage;

        TextField m_ChatInput;
        Label m_ChatCharCount;
        Label m_Placeholder;
        VisualElement m_PlaceholderContent;
        VisualElement m_ActionRow;

        VisualElement m_ContextLimitWarning;
        VisualElement m_ImageSizeLimitWarning;

        Button m_AddContextButton;
        Button m_SettingsButton;
        SettingsPopup m_SettingsPopup;
        PopupTracker m_SettingPopupTracker;

        VisualElement m_PopupRoot;

        string m_SelectedProviderId = "unity";
        readonly List<(string id, string displayName)> m_CommandItems = new();
        readonly List<(string id, string displayName)> m_ModelItems = new();
        string m_SelectedModelId;

        bool m_TextHasFocus;
        bool m_ShowPlaceholder;
        bool m_HighlightFocus;
        bool m_EditContextEnabled;
        bool m_ImageSizeExceeded;

        public AssistantTextField()
            : base(AssistantUIConstants.UIModulePath)
        {
            m_EditContextEnabled = false;

            RegisterAttachEvents(OnAttachToPanel, OnDetachFromPanel);
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            AssistantEditorPreferences.AiGatewayEnabledChanged += OnAiGatewayEnabledChanged;
            AcpProvidersRegistry.OnProvidersChanged += RefreshProviderItems;
            Context.ConversationLoader.ConversationsLoaded += RefreshProviderItems;
            m_InteractionQueueSubscription = AssistantEvents.Subscribe<EventInteractionQueueChanged>(OnInteractionQueueChanged);
            UpdateInteractionBarStyle();
        }

        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            AssistantEditorPreferences.AiGatewayEnabledChanged -= OnAiGatewayEnabledChanged;
            AcpProvidersRegistry.OnProvidersChanged -= RefreshProviderItems;
            Context.ConversationLoader.ConversationsLoaded -= RefreshProviderItems;
            AssistantEvents.Unsubscribe(ref m_InteractionQueueSubscription);
        }

        void OnInteractionQueueChanged(EventInteractionQueueChanged evt)
        {
            UpdateInteractionBarStyle();
        }

        void UpdateInteractionBarStyle()
        {
            var hasInteraction = Context?.InteractionQueue?.HasPending ?? false;
            m_Root?.EnableInClassList(k_HasInteractionClass, hasInteraction);
        }

        public bool ShowPlaceholder
        {
            get => m_ShowPlaceholder;
            set
            {
                if (m_ShowPlaceholder == value)
                {
                    return;
                }

                m_ShowPlaceholder = value;
                RefreshUI();
            }
        }

        public bool HighlightFocus
        {
            get => m_HighlightFocus;
            set
            {
                if (m_HighlightFocus == value)
                {
                    return;
                }

                m_HighlightFocus = value;
                RefreshUI();
            }
        }

        internal string Text => m_ChatInput?.value ?? string.Empty;

        public event Action<string> SubmitRequest;
        public event Action CancelRequest;
        public event Action<string, string> OnProviderChanged;
        public event Action<string> OnCommandSelected;
        public event Action<string> OnModelSelected;

        public Button ContextButton => m_AddContextButton;
        public string SelectedProviderId => m_SelectedProviderId;

        /// <summary>
        /// Programmatically set the selected provider.
        /// </summary>
        /// <param name="providerId">The provider ID to select.</param>
        /// <param name="triggerEvent">Whether to fire the OnProviderChanged event.</param>
        public void SetProvider(string providerId, bool triggerEvent = true)
        {
            if (m_SelectedProviderId == providerId)
                return;

            var oldProvider = m_SelectedProviderId;
            m_SelectedProviderId = providerId;
            m_ProviderMenu?.SetSelectedId(providerId);
            UpdateProviderLabel();
            UpdateProviderTooltip();
            // Ensure UI state stays in sync even when we skip provider-change events (e.g., domain reload restore).
            UpdateCommandSelectorVisibility();
            UpdateCommandSelectorEnabled();

            if (triggerEvent)
            {
                OnProviderChangedHandler(oldProvider, providerId);
            }
        }

        public void SetHost(VisualElement popupRoot)
        {
            m_PopupRoot = popupRoot;
            m_EditContextEnabled = true;

            InitializeSettingsPopup();
            InitializeDropdownMenus(m_PopupRoot);

            m_AddContextButton.SetDisplay(m_EditContextEnabled);
        }

        public void ClearText()
        {
            m_ChatInput.value = "";
        }

        public void SetText(string text)
        {
            m_ChatInput.value = text;
            m_ChatInput.Focus();
        }

        public void Enable()
        {
            m_ChatInput.SetEnabled(true);
        }

        public void Disable(string reason = "")
        {
            m_Placeholder.text = reason;
            m_ChatInput.SetEnabled(false);
        }

        public void ToggleContextLimitWarning(bool enabled)
        {
            m_ContextLimitWarning.SetDisplay(enabled);
        }

        public void ToggleImageSizeLimitWarning(bool enabled)
        {
            m_ImageSizeExceeded = enabled;
            RefreshUI();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_Root = view.Q<VisualElement>("museTextFieldRoot");

            m_InteractionBar = new UserInteractionBar();
            m_InteractionBar.Initialize(Context, autoShowControl: false);
            view.Insert(view.IndexOf(m_Root), m_InteractionBar);

            m_AddContextButton = view.Q<Button>("addContextButton");
            m_AddContextButton.SetDisplay(m_EditContextEnabled);

            m_SettingsButton = view.Q<Button>("settingsButton");
            m_SettingsButton.clicked += OnSettingsButtonClicked;

            InitializeDropdowns(view);

            // Subscribe to mode changes for settings popup auto-run update
            m_ModeProvider.ModeChanged += OnModeChanged;

            UpdateSettingsPopupAutoRun();

            m_ActionButton = view.Q<Button>("actionButton");
            m_ActionButton.RegisterCallback<PointerUpEvent>(_ => OnSubmit());

            m_SubmitButtonImage = view.SetupImage("actionButtonImage", k_SubmitImage);

            m_PlaceholderContent = view.Q<VisualElement>("placeholderContent");
            m_Placeholder = view.Q<Label>("placeholderText");

            m_ChatInput = view.Q<TextField>("input");
            m_ChatInput.maxLength = AssistantMessageSizeConstraints.PromptLimit;
            m_ChatInput.multiline = true;
            m_ChatInput.verticalScrollerVisibility = ScrollerVisibility.Auto;
            m_ChatInput.selectAllOnFocus = false;
            m_ChatInput.selectAllOnMouseUp = false;
            m_ChatInput.RegisterCallback<ClickEvent>(_ => m_ChatInput.Focus());
            m_ChatInput.RegisterCallback<KeyUpEvent>(OnChatKeyUpEvent);
            // TrickleDown.TrickleDown is a workaround for registering KeyDownEvent type with Unity 6
            m_ChatInput.RegisterCallback<KeyDownEvent>(OnChatKeyDownEvent, TrickleDown.TrickleDown);
            m_ChatInput.RegisterValueChangedCallback(OnTextFieldValueChanged);
            m_PlaceholderContent.RegisterCallback<ClickEvent>(_ => m_ChatInput.Focus());
            m_ChatInput.RegisterCallback<FocusInEvent>(_ => SetTextFocused(true));
            m_ChatInput.RegisterCallback<FocusOutEvent>(_ => SetTextFocused(false));
            m_ChatInput.RegisterCallback<PointerLeaveEvent>(_ => m_ActionButton.RemoveFromClassList(k_ChatHoverClass));

            m_ActionRow = view.Q<VisualElement>("museTextFieldActionRow");

            m_Root.RegisterCallback<ClickEvent>(e =>
            {
                // Focus the input when clicking anywhere in the root, except on focusable elements
                // (focusable elements are interactive controls like buttons, textfields, dropdowns, etc.)
                if (e.target is VisualElement target && !target.focusable)
                {
                    m_ChatInput.Focus();
                }
            });

            m_ChatInput.Q<TextElement>().enableRichText = false;

            m_PlaceholderContent = view.Q<VisualElement>("placeholderContent");
            m_Placeholder = view.Q<Label>("placeholderText");

            UpdatePlaceholderText();

            m_ChatCharCount = view.Q<Label>("characterCount");

            m_ContextLimitWarning = view.Q<VisualElement>("contextLimitWarning");
            m_ImageSizeLimitWarning = view.Q<VisualElement>("imageSizeLimitWarning");

            Context.API.APIStateChanged += OnAPIStateChanged;

            ShowPlaceholder = true;
            HighlightFocus = true;

            m_ChatInput.value = AssistantUISessionState.instance.Prompt ?? "";
            UpdatePlaceholderText();
            RefreshUI();

            // Apply granular status tracking to disableable elements, keeping ProviderSelector always enabled
            view.Q<VisualElement>(className: "mui-mtf-warning-area")?.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider);
            view.Q<VisualElement>(className: "mui-mtf-input-root")?.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider);
            view.Q<VisualElement>(className: "mui-left-actions")?.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider);
            view.Q<Button>("actionButton")?.AddSessionAndCompatibilityStatusManipulators(Context.API.Provider);

            // Subscribe to provider ready state changes for placeholder updates
            ProviderStateObserver.OnReadyStateChanged += OnProviderReadyStateChanged;
        }

        void OnAiGatewayEnabledChanged()
        {
            UpdateProviderSelectorVisibility();
        }

        void OnAPIStateChanged()
        {
            RefreshUI();
        }

        void OnTextFieldValueChanged(ChangeEvent<string> evt)
        {
            OnChatValueChanged();
        }

        void OnSubmit()
        {
            if (Context.Blackboard.IsAPIWorking)
            {
                CancelRequest?.Invoke();
                return;
            }

            if (!Context.Blackboard.IsAPIReadyForPrompt)
            {
                return;
            }

            // if button is disabled do not submit
            if (!m_ActionButton.enabledSelf)
            {
                return;
            }

            SubmitRequest?.Invoke(AssistantUISessionState.instance.Prompt);
        }

        void SetTextFocused(bool state)
        {
            m_TextHasFocus = state;
            RefreshUI();
        }

        internal void RefreshUI()
        {
            RefreshChatCharCount();

            m_ImageSizeLimitWarning.SetDisplay(m_ImageSizeExceeded);

            var actionButtonEnabled = Context.Blackboard.IsAPIWorking ||
                                !string.IsNullOrEmpty(Text) &&
                                Context.Blackboard.IsAPIReadyForPrompt &&
                                !m_ImageSizeExceeded;

            m_ActionButton.EnableInClassList(k_ChatActionEnabledClass, actionButtonEnabled);

            var showPlaceholder = ShowPlaceholder && !m_TextHasFocus && string.IsNullOrEmpty(Text);
            m_PlaceholderContent.SetDisplay(showPlaceholder);

            m_Root.EnableInClassList(k_ChatFocusClass, m_TextHasFocus && m_HighlightFocus);

            m_SubmitButtonImage.SetIconClassName(Context.Blackboard.IsAPIWorking ? k_StopImage : k_SubmitImage);

            if (actionButtonEnabled)
            {
                m_ActionButton.tooltip =
                    Context.Blackboard.IsAPIWorking ? k_ActionButtonToolTipStop : k_ActionButtonToolTipSend;
            }
            else
            {
                m_ActionButton.tooltip =
                    m_ImageSizeExceeded ? k_ActionButtonToolTipTotalImageSizeExceeded : k_ActionButtonToolTipNoPrompt;
            }
            m_ActionButton.SetEnabled(actionButtonEnabled);
        }

        void OnChatValueChanged()
        {
            AssistantUISessionState.instance.Prompt = Text;
            RefreshUI();
        }

        void RefreshChatCharCount()
        {
            m_ChatCharCount.text = $"{Text.Length}/{AssistantMessageSizeConstraints.PromptLimit}";
        }

        void OnProviderChangedHandler(string oldProvider, string newProvider)
        {
            UpdateCommandSelectorVisibility();

            // Clear models and commands when switching providers
            m_ModelItems.Clear();
            m_CommandItems.Clear();
            m_SelectedModelId = null;
            RefreshCommandSelectorItems();

            OnProviderChanged?.Invoke(oldProvider, newProvider);
        }

        string GetProviderDisplayName(string providerId)
        {
            if (AssistantProviderFactory.IsUnityProvider(providerId))
            {
                var profiles = Context.AvailableUnityModelProfiles;
                if (profiles != null)
                {
                    foreach (var (id, displayName) in profiles)
                    {
                        if (id == providerId && !string.IsNullOrEmpty(displayName))
                            return displayName;
                    }
                }
                return providerId;
            }

            var match = AcpProvidersRegistry.Providers.FirstOrDefault(p => p.Id == providerId);
            if (match != null && !string.IsNullOrEmpty(match.DisplayName))
                return match.DisplayName;

            return providerId;
        }

        public void SetAvailableCommands(IReadOnlyList<(string name, string description)> commands)
        {
            m_CommandItems.Clear();
            if (commands != null)
            {
                m_CommandItems.AddRange(commands.Select(c => (c.name, "/" + c.name)));
            }

            RefreshCommandSelectorItems();
        }

        void OnChatKeyUpEvent(KeyUpEvent evt)
        {
            RefreshChatCharCount();
        }

        internal void OnChatKeyDownEvent(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.V)
            {
                bool isPasteShortcut;

                if (Application.platform == RuntimePlatform.OSXEditor)
                {
                    isPasteShortcut = evt.commandKey && !evt.altKey && !evt.shiftKey && !evt.ctrlKey;
                }
                else
                {
                    isPasteShortcut = evt.ctrlKey && !evt.altKey && !evt.shiftKey && !evt.commandKey;
                }

                if (isPasteShortcut)
                {
                    HandlePaste();
                    evt.StopPropagation();
#pragma warning disable CS0618 // Type or member is obsolete
                    evt.PreventDefault();
#pragma warning restore CS0618 // Type or member is obsolete
                    return;
                }
            }

            // Newline handling
            if (string.IsNullOrEmpty(Text) &&
                evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.UpArrow or KeyCode.DownArrow)
            {
                evt.StopImmediatePropagation();
            }

            if (evt.character == '\n')
            {
                if (evt.shiftKey)
                {
                    string previousText = m_ChatInput.value;
                    var isAtEnd = m_ChatInput.cursorIndex == previousText.Length;

                    string newText = m_ChatInput.value.Insert(m_ChatInput.cursorIndex, "\n");
                    SetText(newText);

                    m_ChatInput.cursorIndex++;

                    if (isAtEnd)
                    {
                        m_ChatInput.selectIndex = m_ChatInput.cursorIndex + 1;
                    }
                    else
                    {
                        m_ChatInput.selectIndex = m_ChatInput.cursorIndex;
                    }

                    evt.StopImmediatePropagation();
                    return;
                }

                evt.StopPropagation();
#if !UNITY_2023_1_OR_NEWER
                evt.PreventDefault();
#endif
            }

            switch (evt.keyCode)
            {
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (Text.Trim().Length == 0)
                        return;
                    break;

                default:
                    return;
            }

            if (evt.altKey || evt.shiftKey)
                return;

            bool useModifier = AssistantEditorPreferences.UseModifierKeyToSendPrompt;
            bool hasModifier = Application.platform == RuntimePlatform.OSXEditor ? evt.commandKey : evt.ctrlKey;
            if (hasModifier != useModifier)
                return;

            evt.StopPropagation();

            if (!Context.Blackboard.IsAPIWorking && Context.Blackboard.IsAPIReadyForPrompt)
                OnSubmit();
        }

        void InitializeSettingsPopup()
        {
            if (m_SettingsPopup != null)
                return;

            m_SettingsPopup = new SettingsPopup();
            m_SettingsPopup.Initialize(Context, autoShowControl: false);
            m_PopupRoot.Add(m_SettingsPopup);
        }

        void OnSettingsButtonClicked()
        {
            if (m_SettingsPopup.IsShown)
                HideSettingsPopup();
            else
                ShowSettingsPopup();
        }

        void OnModeChanged(string modeId)
        {
            UpdateSettingsPopupAutoRun();
            UpdatePlaceholderText();
        }

        void UpdatePlaceholderText()
        {
            if (!ProviderStateObserver.IsUnityProvider)
            {
                UpdatePlaceholderFromReadyState();
                return;
            }
            m_Placeholder.text = Context.Blackboard.ActiveMode == AssistantMode.Agent ? k_PlaceholderAgent : k_PlaceholderAsk;
        }

        void OnProviderReadyStateChanged(ProviderStateObserver.ProviderReadyState _, string __)
        {
            UpdatePlaceholderFromReadyState();
        }

        void UpdatePlaceholderFromReadyState()
        {
            if (ProviderStateObserver.IsUnityProvider)
            {
                m_Placeholder.text = Context.Blackboard.ActiveMode == AssistantMode.Agent
                    ? k_PlaceholderAgent : k_PlaceholderAsk;
                return;
            }

            switch (ProviderStateObserver.ReadyState)
            {
                case ProviderStateObserver.ProviderReadyState.Initializing:
                    m_Placeholder.text = k_PlaceholderInitializing;
                    break;
                case ProviderStateObserver.ProviderReadyState.Error:
                    // Show simple message - full error details are shown in chat area
                    m_Placeholder.text = k_PlaceholderError;
                    break;
                case ProviderStateObserver.ProviderReadyState.Ready:
                    m_Placeholder.text = k_PlaceholderAgent;
                    break;
            }
        }

        void UpdateSettingsPopupAutoRun() => m_SettingsPopup?.SetAutoRunEnabled(Context.Blackboard.ActiveMode == AssistantMode.Agent);

        /// <summary>
        /// Bind the mode provider to an IAssistantProvider.
        /// Call this when switching providers.
        /// </summary>
        public void BindModeProvider(IAssistantProvider provider)
        {
            m_ModeProvider.BindProvider(provider);
        }

        /// <summary>
        /// Set available models for the provider from session/initialized data.
        /// </summary>
        public void SetModels(IEnumerable<(string modelId, string name, string description)> models, string currentModelId)
        {
            m_ModelItems.Clear();

            if (models != null)
            {
                foreach (var (modelId, name, description) in models)
                {
                    m_ModelItems.Add((modelId, name));
                }
            }

            m_SelectedModelId = currentModelId;
            RefreshCommandSelectorItems();
        }

        /// <summary>
        /// Clear models when switching providers or resetting session.
        /// </summary>
        public void ClearModels()
        {
            m_ModelItems.Clear();
            m_SelectedModelId = null;
            RefreshCommandSelectorItems();
        }

        void ShowSettingsPopup()
        {
            using var listPoolHandle = ListPool<IToolPermissions.TemporaryPermission>.Get(out var permissions);
            Context.API.Provider.ToolPermissions.GetTemporaryPermissions(permissions);

            m_SettingsPopup.ShowWithPermissions(permissions);
            m_SettingPopupTracker = new PopupTracker(m_SettingsPopup, m_SettingsButton, new Vector2Int(0, 54), m_SettingsButton);
            m_SettingPopupTracker.Dismiss += HideSettingsPopup;
        }

        void HideSettingsPopup()
        {
            if (m_SettingPopupTracker == null)
                return;

            m_SettingPopupTracker.Dismiss -= HideSettingsPopup;
            m_SettingPopupTracker.Dispose();
            m_SettingPopupTracker = null;

            m_SettingsPopup.Hide();
        }
    }
}
