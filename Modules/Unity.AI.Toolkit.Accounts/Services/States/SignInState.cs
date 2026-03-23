using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.AI.Toolkit.Accounts.Services.Data;
using Unity.AI.Toolkit.Connect;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class SignInState
    {
        internal readonly Signal<SignInStatus> settings;

        public event Action OnChange;
        public SignInStatus Value { get => settings.Value; internal set => settings.Value = value; }
        public void Refresh() => settings.Refresh();

        public bool IsLikelySignedIn => Value is SignInStatus.SignedIn or SignInStatus.NotReady;
        public bool IsSignedIn => Value is SignInStatus.SignedIn;
        public bool IsSignedOut => Value is SignInStatus.SignedOut;

        public SignInState()
        {
            settings = new(AccountPersistence.SignInStatusProxy, RefreshInternal, () => OnChange?.Invoke());
            Refresh();
            AIDropdownBridge.ConnectStateChanged(Refresh);
            AIDropdownBridge.UserStateChanged(Refresh);
        }

        void RefreshInternal()
        {
            if (!AIDropdownBridge.IsUserInfoReady) Value = SignInStatus.NotReady;
            else if (AIDropdownBridge.LoggedIn) Value = SignInStatus.SignedIn;
            else Value = SignInStatus.SignedOut;
        }
    }
}
