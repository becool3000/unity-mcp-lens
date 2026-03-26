using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using Unity.AI.Assistant.Utils;
using Unity.Relay.Editor.Acp;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    partial class AssistantTextField
    {
        const string k_ModelTag = "model";
        const string k_EmDash = "\u2014";

        Button m_ModeButton;
        Label m_ModeLabel;
        AssistantImage m_ModeIcon;
        AssistantDropdown m_ModeMenu;
        ModeProvider m_ModeProvider;

        Button m_ProviderButton;
        Label m_ProviderLabel;
        AssistantImage m_ProviderIcon;
        AssistantDropdown m_ProviderMenu;
        List<AssistantDropdownItemData> m_ProviderItems = new();

        Button m_CommandButton;
        AssistantDropdown m_CommandMenu;
        List<AssistantDropdownItemData> m_CommandMenuItems = new();

        void InitializeDropdowns(TemplateContainer view)
        {
            m_ModeButton = view.SetupButton("modeDropdown", _ => ToggleDropdown(m_ModeMenu, m_ModeButton));
            m_ModeLabel = m_ModeButton.Q<Label>("modeDropdownLabel");
            m_ModeIcon = new AssistantImage(m_ModeButton.Q<Image>("modeDropdownIcon"), autoHide: true);
            m_ModeProvider = new ModeProvider(Context.Blackboard);
            m_ModeProvider.ModeChanged += _ => RefreshModeDropdown();

            m_ProviderButton = view.SetupButton("providerSelector", _ => ToggleDropdown(m_ProviderMenu, m_ProviderButton));
            m_ProviderLabel = m_ProviderButton.Q<Label>("providerSelectorLabel");
            m_ProviderIcon = new AssistantImage(m_ProviderButton.Q<Image>("providerSelectorIcon"), autoHide: true);

            AcpProvidersRegistry.EnsureInitialized();

            m_CommandButton = view.SetupButton("commandSelector", _ => ToggleDropdown(m_CommandMenu, m_CommandButton));

            UpdateProviderSelectorVisibility();
            UpdateCommandSelectorVisibility();
            UpdateCommandSelectorEnabled();
        }

        void InitializeDropdownMenus(VisualElement popupRoot)
        {
            m_ModeMenu = CreateDropdownMenu(popupRoot, OnModeItemSelected);
            m_ProviderMenu = CreateDropdownMenu(popupRoot, OnProviderMenuItemSelected);
            m_CommandMenu = CreateDropdownMenu(popupRoot, OnCommandMenuItemSelected);

            RefreshModeDropdown();
            RefreshProviderItems();
        }

        AssistantDropdown CreateDropdownMenu(VisualElement popupRoot, Action<string> onSelected)
        {
            var menu = new AssistantDropdown();
            menu.Initialize(Context, autoShowControl: false);
            popupRoot.Add(menu);
            menu.ItemSelected += onSelected;
            return menu;
        }

        static void ToggleDropdown(AssistantDropdown menu, Button button)
        {
            if (menu == null)
            {
                return;
            }

            if (menu.IsShown)
            {
                menu.HideMenu();
            }
            else
            {
                menu.ShowAt(button, button);
            }
        }

        void RefreshModeDropdown()
        {
            var items = new List<AssistantDropdownItemData>();

            if (m_ModeProvider.AvailableModes.Count == 0)
            {
                m_ModeLabel.text = k_EmDash;
                m_ModeMenu.SetItems(items, null);
                return;
            }

            foreach (var mode in m_ModeProvider.AvailableModes)
            {
                items.Add(new AssistantDropdownItemData(mode.Id, mode.Name));
            }

            m_ModeMenu.SetItems(items, m_ModeProvider.CurrentModeId);

            var currentItem = items.FirstOrDefault(i => i.Id == m_ModeProvider.CurrentModeId);
            m_ModeLabel.text = currentItem.DisplayText ?? items.FirstOrDefault().DisplayText ?? k_EmDash;
            m_ModeIcon.SetIconClassName(currentItem.IconClass);
        }

        async void OnModeItemSelected(string modeId)
        {
            try
            {
                await m_ModeProvider.SetModeAsync(modeId);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[ModeDropdown] Failed to set mode: {ex.Message}");
            }
        }

        void UpdateProviderSelectorVisibility()
        {
            m_ProviderButton.SetDisplay(AssistantEditorPreferences.AiGatewayEnabled);
        }

        void RefreshProviderItems()
        {
            m_ProviderItems.Clear();

            var profiles = Context.AvailableUnityModelProfiles;
            if (profiles is { Count: > 0 })
            {
                foreach (var (providerId, profileName) in profiles)
                    m_ProviderItems.Add(new AssistantDropdownItemData(providerId, profileName ?? providerId));
            }
            else
            {
                m_ProviderItems.Add(new AssistantDropdownItemData(AssistantProviderFactory.DefaultProvider.ProfileId, "Unity"));
            }
            
            m_ProviderItems.AddRange(AcpProvidersRegistry.Providers.Select(p =>
                new AssistantDropdownItemData(p.Id, string.IsNullOrEmpty(p.DisplayName) ? p.Id : p.DisplayName)));

            m_ProviderMenu.SetItems(m_ProviderItems, m_SelectedProviderId);
            UpdateProviderLabel();
            UpdateProviderTooltip();
        }

        void UpdateProviderLabel()
        {
            var item = m_ProviderItems.FirstOrDefault(i => i.Id == m_SelectedProviderId);
            m_ProviderLabel.text = item.DisplayText ?? m_ProviderItems.FirstOrDefault().DisplayText ?? "";
            m_ProviderIcon.SetIconClassName(item.IconClass);
        }

        void UpdateProviderTooltip()
        {
            var displayName = GetProviderDisplayName(m_SelectedProviderId);
            var provider = AcpProvidersRegistry.Providers.FirstOrDefault(p => p.Id == m_SelectedProviderId);

            if (provider != null && !string.IsNullOrEmpty(provider.Version))
            {
                var tooltip = $"{displayName} v{provider.Version}";
                if (provider.IsCustom)
                {
                    tooltip += "  [Custom]";
                }
                m_ProviderButton.tooltip = tooltip;
            }
            else
            {
                m_ProviderButton.tooltip = displayName;
            }
        }

        void OnProviderMenuItemSelected(string id)
        {
            if (id == m_SelectedProviderId)
            {
                return;
            }

            var item = m_ProviderItems.FirstOrDefault(i => i.Id == id);
            if (item.Id == null)
            {
                return;
            }

            var oldProvider = m_SelectedProviderId;
            m_SelectedProviderId = id;
            UpdateProviderLabel();
            UpdateProviderTooltip();
            OnProviderChangedHandler(oldProvider, id);
        }

        void UpdateCommandSelectorVisibility()
        {
            var isThirdPartyProvider = !AssistantProviderFactory.IsUnityProvider(m_SelectedProviderId);
            m_CommandButton.SetDisplay(isThirdPartyProvider);
        }

        void UpdateCommandSelectorEnabled()
        {
            m_CommandButton.SetEnabled(m_CommandItems.Count > 0 || m_ModelItems.Count > 0);
        }

        void RefreshCommandSelectorItems()
        {
            m_CommandMenuItems.Clear();

            foreach (var model in m_ModelItems)
            {
                m_CommandMenuItems.Add(new AssistantDropdownItemData(model.id, model.displayName, tag: k_ModelTag));
            }

            foreach (var command in m_CommandItems)
            {
                m_CommandMenuItems.Add(new AssistantDropdownItemData(command.id, command.displayName));
            }

            m_CommandMenu.SetItems(m_CommandMenuItems, m_SelectedModelId);
            UpdateCommandSelectorEnabled();
        }

        void OnCommandMenuItemSelected(string id)
        {
            var item = m_CommandMenuItems.FirstOrDefault(i => i.Id == id);
            if (item.Id == null)
            {
                return;
            }

            if ((string)item.Tag == k_ModelTag)
            {
                m_SelectedModelId = id;
                OnModelSelected?.Invoke(id);
            }
            else
            {
                OnCommandSelected?.Invoke(id);
            }
        }
    }
}
