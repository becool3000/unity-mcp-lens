using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Accounts.Services.Core;

namespace Unity.AI.Toolkit.Accounts.Components
{
    [UxmlElement]
    partial class Points : VisualElement
    {
        readonly Label m_Points;
        Action m_Unsubscribe;

        public Points()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/Points/Points.uxml");
            tree.CloneTree(this);

            m_Points = this.Q<Label>(className: "points-label");
            this.Q<Button>("get-points").clicked += AccountLinks.ViewBundles;

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
            if (Account.pointsBalance.Value != null)
            {
                m_Points.text = PrettyFormatSimple(Account.pointsBalance.Value.PointsAvailable);
                m_Points.tooltip = TooltipText(Account.pointsBalance.Value.PointsAvailable);
            }
        }

        /// <summary>
        /// Transform the points in a compact format to display in the UI.
        /// 3,000 points
        /// 0 points
        /// 28.1k points
        /// 2.1m points
        /// Everything above 4 digits needs abbreviation.
        /// </summary>
        /// <param name="number"></param>
        /// <returns></returns>
        internal static string PrettyFormatSimple(long number)
        {
            if (number < 0)
                return "0";

            if (number > 999999)
            {
                return (number / 100000 * 100000).ToString("0,,.#m", CultureInfo.CurrentCulture.NumberFormat);
            }
            if (number > 9999)
            {
                return (number / 100 * 100).ToString("0,.#k", CultureInfo.CurrentCulture.NumberFormat);
            }

            return number.ToString("N0", CultureInfo.CurrentCulture.NumberFormat);
        }

        internal static string TooltipText(long number)
        {
            return number.ToString("N0", CultureInfo.CurrentCulture.NumberFormat) + " Points";
        }
    }
}