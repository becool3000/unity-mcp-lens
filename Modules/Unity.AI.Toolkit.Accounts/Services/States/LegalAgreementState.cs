using System;
using Unity.AI.Toolkit.Accounts.Services.Core;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class LegalAgreementState
    {
        internal readonly Signal<bool> settings;

        public event Action OnChange;
        public bool Value { get => settings.Value; private set => settings.Value = value; }
        public void Refresh() => settings.Refresh();

        public bool IsAgreed => Value;

        public LegalAgreementState()
        {
            settings = new(AccountPersistence.LegalAgreementProxy, RefreshInternal, () => OnChange?.Invoke());
            Account.settings.OnChange += Refresh;
        }

        void RefreshInternal() => Value = Account.settings.IsTermsOfServiceAccepted;
    }
}
