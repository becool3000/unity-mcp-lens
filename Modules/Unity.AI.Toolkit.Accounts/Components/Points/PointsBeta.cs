using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class PointsBeta : VisualElement
    {
        readonly Label m_Points;
        Action m_Unsubscribe;
        readonly PointLoadFailedMessage m_LoadFailedMessage;

        public PointsBeta()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/Points/PointsBeta.uxml");
            tree.CloneTree(this);

            m_LoadFailedMessage = this.Q<PointLoadFailedMessage>();
            m_Points = this.Q<Label>(className: "points-label");

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                m_Unsubscribe = Account.pointsBalance.settings.Use(_ => RefreshPoints());
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                m_Unsubscribe?.Invoke();
                m_Unsubscribe = null;
            });
        }

        void RefreshPoints()
        {
            if (Account.pointsBalance.Value == null)
                return;
            var available = Account.pointsBalance.Value.PointsAvailable;
            var allocated = Account.pointsBalance.Value.PointsAllocated;
            m_Points.text = $"{Points.PrettyFormatSimple(available)} / {Points.PrettyFormatSimple(allocated)}";
            m_Points.tooltip = Points.TooltipText(available);
        }
    }
}