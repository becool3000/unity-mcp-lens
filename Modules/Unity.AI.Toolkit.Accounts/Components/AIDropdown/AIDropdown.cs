using System;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Toolkit.Accounts.Services.Core;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class AIDropdown : VisualElement
    {
        VisualElement m_PointsContainer;
        PointsBeta m_PointsBeta;
        PointLoadFailedMessage m_PointLoadFailedMessage;
        VisualElement m_currentPointsUI;

        public AIDropdown()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/AIDropdown/AIDropdown.uxml");
            tree.CloneTree(this);

            m_PointsContainer = this.Q<VisualElement>("points-container");

            var menuExtensionsGeneral = this.Q<VisualElement>("menu-extensions-general");
            Extensions.OnExtendGeneral(menuExtensionsGeneral);

            var manageAccountSeparator = this.Q<VisualElement>("manage-account-separator");
            var menuExtensions = this.Q<VisualElement>("menu-extensions");

            if (DropdownExtension.onExtend.Count > 0)
                manageAccountSeparator.RemoveFromClassList("hidden");

            Extensions.OnExtend(menuExtensions);

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                Account.settings.OnChange += Refresh;
                Account.network.OnChange += Refresh;
                Account.pointsBalance.OnChange += Refresh;
                Refresh();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                Account.settings.OnChange -= Refresh;
                Account.network.OnChange -= Refresh;
                Account.pointsBalance.OnChange -= Refresh;
            });
        }

        void Refresh()
        {
            VisualElement currentPointsUI;
            if (!ShouldHidePoints && Account.pointsBalance.Value == null)
                currentPointsUI = m_PointLoadFailedMessage ??= new();
            else
                currentPointsUI = m_PointsBeta ??= new();

            if (m_PointsBeta != null)
                m_PointsBeta.style.display = ShouldHidePoints ? DisplayStyle.None : DisplayStyle.Flex;

            if (m_currentPointsUI != currentPointsUI)
            {
                m_currentPointsUI = currentPointsUI;
                m_PointsContainer.Clear();
                m_PointsContainer.Add(m_currentPointsUI);
            }

            Extensions.OnShow(this);
        }

        static bool ShouldHidePoints =>
            !Account.network.IsAvailable ||
            (!Account.settings.AiAssistantEnabled && !Account.settings.AiGeneratorsEnabled);
    }
}
