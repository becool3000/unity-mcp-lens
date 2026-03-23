using System;
using Unity.AI.Toolkit.Accounts.Services.States;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services
{
    static class Account
    {
        public static SignInState signIn = new();
        public static NetworkState network = new();
        public static CloudConnectedState cloudConnected = new();

        public static ApiAccessibleState apiAccessible = new();

        public static SettingsState settings = new();
        public static PointsBalanceState pointsBalance = new();
        public static LegalAgreementState legalAgreement = new();

        public static SessionState session = new();
        public static SessionStatusState sessionStatus = new();
    }
}
