using System;
using UnityEngine;

namespace Unity.AI.Toolkit.Connect
{
    static class AIDropdownBridge
    {
        internal static bool IsUserInfoReady => UnityConnectUtils.GetIsUserInfoReady();
        internal static bool LoggedIn => UnityConnectUtils.GetIsLoggedIn();
        internal static Action UserStateChanged(Action callback)
        {
            var unsubscribe = UnityConnectUtils.RegisterUserStateChangedEvent(_ => callback());
            return () => UnityConnectUtils.UnregisterUserStateChangedEvent(unsubscribe);
        }
        internal static Action ConnectStateChanged(Action callback)
        {
            var unsubscribe = UnityConnectUtils.RegisterConnectStateChangedEvent(_ => callback());
            return () => UnityConnectUtils.UnregisterConnectStateChangedEvent(unsubscribe);
        }
        internal static Action ConnectProjectStateChanged(Action callback)
        {
            var unsubscribe = UnityConnectUtils.RegisterProjectStateChangedEvent(_ => callback());
            return () => UnityConnectUtils.UnregisterProjectStateChangedEvent(unsubscribe);
        }
        internal static bool isProjectValid => UnityConnectUtils.GetIsProjectInfoValid();
    }
}
