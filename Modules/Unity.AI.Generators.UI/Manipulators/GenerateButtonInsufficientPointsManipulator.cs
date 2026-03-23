using System;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Accounts.Components;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class GenerateButtonInsufficientPointsManipulator : Manipulator
    {
        readonly Action m_OnPointsBalanceChanged;

        InsufficientPointsBanner m_InsufficientPointsBanner;
        Label m_PointsIndicator;

        EventCallback<AttachToPanelEvent> m_AttachToPanelCallback;
        EventCallback<DetachFromPanelEvent> m_DetachFromPanelCallback;

        public GenerateButtonInsufficientPointsManipulator(Action onPointsBalanceChanged)
        {
            m_OnPointsBalanceChanged = onPointsBalanceChanged;
            m_AttachToPanelCallback = _ => Account.pointsBalance.OnChange += m_OnPointsBalanceChanged;
            m_DetachFromPanelCallback = _ => Account.pointsBalance.OnChange -= m_OnPointsBalanceChanged;
        }

        public void OnGenerationValidationResultChanged(GenerationValidationResult result)
        {
            var showPointsIndicator = result.cost > 0;
            m_PointsIndicator?.SetShown(showPointsIndicator);
            if (m_PointsIndicator != null)
                m_PointsIndicator.text = result.cost.ToString();

            if (result.cost <= 0)
            {
                m_InsufficientPointsBanner?.SetShown(false);
                return;
            }

            var hasEnoughPoints = Account.pointsBalance.CanAfford(result.cost);
            m_InsufficientPointsBanner?.SetShown(!hasEnoughPoints && result.success);
        }

        protected override void RegisterCallbacksOnTarget()
        {
            m_InsufficientPointsBanner = target.Q<InsufficientPointsBanner>();
            m_PointsIndicator = target.Q<Label>("points-indicator");
            m_InsufficientPointsBanner?.SetShown(false);

            target.RegisterCallback(m_AttachToPanelCallback);
            target.RegisterCallback(m_DetachFromPanelCallback);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            target.UnregisterCallback(m_AttachToPanelCallback);
            target.UnregisterCallback(m_DetachFromPanelCallback);
        }
    }
}
