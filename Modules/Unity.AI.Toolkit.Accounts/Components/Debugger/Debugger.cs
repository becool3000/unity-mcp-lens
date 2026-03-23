using System;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.AI.Toolkit.Accounts.Services.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class Debugger : VisualElement
    {
#if UNITY_6000_3_OR_NEWER
        void TrackDropdown()
        {
            var aiDropdownUsed = this.Q<Toggle>("is-used-by-ai-dropdown");
            AIDropdownController.dropdownContent.dropdown.RegisterCallback<AttachToPanelEvent>(_ => aiDropdownUsed.value = true);
            AIDropdownController.dropdownContent.dropdown.RegisterCallback<DetachFromPanelEvent>(_ => aiDropdownUsed.value = false);
        }
#endif

        public Debugger()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/Debugger/Debugger.uxml");
            tree.CloneTree(this);

#if UNITY_6000_3_OR_NEWER
            var reset = this.Q<Toggle>("reset-dropdown");
            reset.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    AIDropdownController.Init();
                else
                    AIDropdownController.Reset();
            });
            reset.SetValueWithoutNotify(AIDropdownController.dropdownContent != null);
#endif

            var session = this.Q<Toggle>("session");
            Account.sessionStatus.settings.Use(value => session.SetValueWithoutNotify(value));

#if UNITY_6000_3_OR_NEWER
            if (AIDropdownController.dropdownContent.dropdown == null)
                AIDropdownController.dropdownContent.OnCreated += TrackDropdown;
            else
                TrackDropdown();
#endif

            var cloudConnected = this.Q<EnumField>("cloud-connected");
            cloudConnected.Init(Account.cloudConnected.Value);
            cloudConnected.RegisterValueChangedCallback(evt => Account.cloudConnected.Value = (ProjectStatus)evt.newValue);
            Account.cloudConnected.settings.Use(value => cloudConnected.SetValueWithoutNotify(value));

            var network = this.Q<Toggle>("network");
            network.RegisterValueChangedCallback(evt => Account.network.Value = evt.newValue);
            Account.network.settings.Use(value => network.SetValueWithoutNotify(value));

            var signin = this.Q<EnumField>("signin");
            signin.Init(Account.signIn.Value);
            signin.RegisterValueChangedCallback(evt => Account.signIn.settings.Value = (SignInStatus)evt.newValue);
            Account.signIn.settings.Use(value => signin.SetValueWithoutNotify(value));

            var broken = this.Q<Toggle>("broken");
            broken.RegisterValueChangedCallback(evt => Account.cloudConnected.SimulateBroken = evt.newValue);
            Account.cloudConnected.settings.Use(_ => broken.SetValueWithoutNotify(Account.cloudConnected.SimulateBroken));

            var settings = this.Q<VisualElement>("settings-group");
            this.Q<Button>("fetch-settings").clicked += Account.settings.Refresh;
            this.Q<Button>("clear-settings").clicked += () => Account.settings.Value = null;
            var settingsOrgId = this.Q<TextField>("settings-orgid");
            var aiAssistantEnabled = this.Q<Toggle>("ai-assistant-enabled");
            var aiGeneratorsEnabled = this.Q<Toggle>("ai-generators-enabled");
            aiAssistantEnabled.RegisterValueChangedCallback(evt => Account.settings.Value = Account.settings.Value with {IsAiAssistantEnabled = evt.newValue});
            aiGeneratorsEnabled.RegisterValueChangedCallback(evt => Account.settings.Value = Account.settings.Value with {IsAiGeneratorsEnabled = evt.newValue});
            Account.settings.settings.Use(value =>
            {
                settings.SetEnabled(value != null);
                if (value == null)
                    return;
                settingsOrgId.SetValueWithoutNotify(value.OrgId);
                aiAssistantEnabled.SetValueWithoutNotify(value.IsAiAssistantEnabled);
                aiGeneratorsEnabled.SetValueWithoutNotify(value.IsAiGeneratorsEnabled);
            });
            var failFetchSettings = this.Q<Toggle>("fail-fetch-settings");
            var originalFetchSettingsDelegate = AccountApi.GetSettingsDelegate;
            failFetchSettings.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    AccountApi.GetSettingsDelegate = async () =>
                    {
                        await EditorTask.Delay(3000);
                        return null;
                    };
                else
                    AccountApi.GetSettingsDelegate = originalFetchSettingsDelegate;
            });

            var legalAgreement = this.Q<Toggle>("legal-agreement");
            legalAgreement.RegisterValueChangedCallback(evt => Account.settings.Value = Account.settings.Value with {IsTermsOfServiceAccepted = evt.newValue});
            Account.legalAgreement.settings.Use(value => legalAgreement.SetValueWithoutNotify(value));

            var dataSharing = this.Q<Toggle>("data-sharing");
            dataSharing.RegisterValueChangedCallback(evt => Account.settings.Value = Account.settings.Value with {IsDataSharingEnabled = evt.newValue});
            Account.settings.settings.Use(value =>
            {
                if (value == null)
                    return;
                dataSharing.SetValueWithoutNotify(value.IsDataSharingEnabled);
            });

            var regionAvailable = this.Q<Toggle>("region-available");
            regionAvailable.RegisterValueChangedCallback(evt => Account.settings.RegionAvailable = evt.newValue);
            Account.settings.regionAvailability.Use(value => regionAvailable.SetValueWithoutNotify(value));

            var packagesSupported = this.Q<Toggle>("packages-supported");
            packagesSupported.RegisterValueChangedCallback(evt => Account.settings.PackagesSupported = evt.newValue);
            Account.settings.packagesSupported.Use(value => packagesSupported.SetValueWithoutNotify(value));

            var mcpProEnabled = this.Q<Toggle>("mcp-pro-enabled");
            mcpProEnabled.RegisterValueChangedCallback(evt => Account.settings.Value = Account.settings.Value with {IsMcpProEnabled = evt.newValue});
            Account.settings.settings.Use(value =>
            {
                if (value == null)
                    return;
                mcpProEnabled.SetValueWithoutNotify(value.IsMcpProEnabled);
            });

            var points = this.Q<VisualElement>("points-group");
            this.Q<Button>("fetch-points").clicked += Account.pointsBalance.Refresh;
            this.Q<Button>("clear-points").clicked += () => Account.pointsBalance.Value = null;
            var pointsOrgId = this.Q<TextField>("points-orgid");
            var pointsAvailable = this.Q<Slider>("points-available");
            var pointsAllocated = this.Q<Slider>("points-allocated");
            pointsAvailable.RegisterValueChangedCallback(evt => Account.pointsBalance.Value = Account.pointsBalance.Value with {PointsAvailable = Convert.ToInt32(evt.newValue)});
            pointsAllocated.RegisterValueChangedCallback(evt => Account.pointsBalance.Value = Account.pointsBalance.Value with {PointsAllocated = Convert.ToInt32(evt.newValue)});
            Account.pointsBalance.settings.Use(value =>
            {
                points.SetEnabled(value != null);
                if (value == null)
                    return;
                pointsOrgId.SetValueWithoutNotify(value.OrgId);
                pointsAvailable.SetValueWithoutNotify(value.PointsAvailable);
                pointsAllocated.SetValueWithoutNotify(value.PointsAllocated);
            });
            var failFetchPoints = this.Q<Toggle>("fail-fetch-points");
            var originalFetchPointsDelegate = AccountApi.GetPointsDelegate;
            failFetchPoints.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue)
                    AccountApi.GetPointsDelegate = async () =>
                    {
                        await EditorTask.Delay(3000);
                        return null;
                    };
                else
                    AccountApi.GetPointsDelegate = originalFetchPointsDelegate;
            });

        }
    }
}
