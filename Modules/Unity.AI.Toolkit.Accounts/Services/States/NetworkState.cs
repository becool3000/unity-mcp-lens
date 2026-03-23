using System;
using Unity.AI.Toolkit.Accounts.Services.Core;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class NetworkState
    {
        internal readonly Signal<bool> settings;

        public event Action OnChange;
        public bool Value { get => settings.Value; internal set => settings.Value = value; }
        public void Refresh() => settings.Refresh();

        public bool IsAvailable => Value;

        public NetworkState()
        {
            settings = new(AccountPersistence.NetworkAvailableProxy, RefreshInternal, () => OnChange?.Invoke());
            Refresh();
            NetworkAvailability.OnChanged += Refresh;
        }

        void RefreshInternal() => Value = Application.internetReachability != NetworkReachability.NotReachable;
    }
}
