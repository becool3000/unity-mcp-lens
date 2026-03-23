using System;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    /// <summary>
    /// Anything that can affect "can something make the session unusable".
    /// </summary>
    class SessionState
    {
        public event Action OnChange
        {
            add
            {
                Account.network.OnChange += value;
                Account.signIn.OnChange += value;
                Account.cloudConnected.OnChange += value;
                Account.settings.OnChange += value;
                Account.legalAgreement.OnChange += value;
                Account.pointsBalance.OnChange += value;
            }
            remove
            {
                Account.network.OnChange -= value;
                Account.signIn.OnChange -= value;
                Account.cloudConnected.OnChange -= value;
                Account.settings.OnChange -= value;
                Account.legalAgreement.OnChange -= value;
                Account.pointsBalance.OnChange -= value;
            }
        }
    }
}
