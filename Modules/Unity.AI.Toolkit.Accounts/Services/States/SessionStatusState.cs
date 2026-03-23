using System;
using Unity.AI.Toolkit.Accounts.Services.Core;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class SessionStatusState
    {
        internal readonly Signal<bool> settings;
        bool m_Value;

        public bool Value { get => settings.Value; private set => settings.Value = value; }
        public event Action OnChange;

        public bool IsUsable => Value;

        public SessionStatusState()
        {
            settings = new(
                new Proxy<bool>(() => m_Value, value => m_Value = value),
                () => Value = ApiAccessibleState.IsAccessible && Account.settings.RegionAvailable && (Account.settings.AiAssistantEnabled || Account.settings.AiGeneratorsEnabled) && Account.legalAgreement.IsAgreed && Account.pointsBalance.HasAny,
                () => OnChange?.Invoke());

            settings.Refresh();
            Account.session.OnChange += settings.Refresh;
        }
    }
}
