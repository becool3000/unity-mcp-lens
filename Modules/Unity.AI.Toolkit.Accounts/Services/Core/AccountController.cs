using System;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.AI.Toolkit.Accounts.Services.States;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services
{
    static class AccountController
    {
        [InitializeOnLoadMethod]
        static void Init()
        {
            VerifyTermsOfServiceAcceptance();
            RefreshAccountInformation();
            Account.network.OnChange += RefreshAccountInformation;
            Account.signIn.OnChange += RefreshAccountInformation;
            Account.cloudConnected.OnChange += RefreshAccountInformation;
        }

        static void VerifyTermsOfServiceAcceptance()
        {
            // Ensure settings have been fetched and that we know the current terms of service setting
            if (Account.settings.Value == null)
            {
                Account.settings.OnChange += VerifyTermsOfServiceAcceptance;
                return;
            }

            Account.settings.OnChange -= VerifyTermsOfServiceAcceptance;

#if UNITY_6000_3_OR_NEWER
            // AIDropdownConfig.instance.termsAccepted will be true if the ai toolkit was not yet installed
            // and the user clicked "agree and install" with the editor ai dropdown dialog.
            // When it is the case, we need to send the fact that it was agreed to the server to be stored.
            if (AIDropdownConfig.instance.termsAccepted && !Account.legalAgreement.IsAgreed)
                _ = SetTermsOfService();
#endif
        }

        public static async Task SetTermsOfService()
        {
            var settings = await AccountApi.SetTermsOfServiceAcceptance(true);
            Account.settings.Value = new(settings);
        }

        /// <summary>
        /// Ensure account information gets fetched at least once during an editor session.
        /// </summary>
        static void RefreshAccountInformation()
        {
            if (!ApiAccessibleState.IsAccessible)
                Account.apiAccessible.OnChange += RefreshAccountInformation;
            Account.apiAccessible.OnChange -= RefreshAccountInformation;

            if (Account.settings.Value == null)
                Account.settings.Refresh();
            if (Account.pointsBalance.Value == null)
                Account.pointsBalance.Refresh();
        }
    }
}
