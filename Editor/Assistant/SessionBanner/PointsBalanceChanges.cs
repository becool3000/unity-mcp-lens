using System;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    /// <summary>
    /// Updates elements based on changes to the points balance.
    /// </summary>
    class PointsBalanceChanges : Manipulator
    {
        readonly Action m_Callback;

        public PointsBalanceChanges(Action callback, bool callImmediately = true)
        {
            m_Callback = callback;
            if (callImmediately)
                Refresh();
        }

        protected override void RegisterCallbacksOnTarget() => Account.pointsBalance.OnChange += Refresh;
        protected override void UnregisterCallbacksFromTarget() => Account.pointsBalance.OnChange -= Refresh;

        void Refresh() => m_Callback?.Invoke();
    }
}

