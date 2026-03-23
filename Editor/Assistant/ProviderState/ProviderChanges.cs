using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Manipulator that invokes a callback when the provider changes.
    /// Used by SessionBanner and other UI components that need to react to provider switches.
    /// </summary>
    class ProviderChanges : Manipulator
    {
        readonly Action m_Callback;

        public ProviderChanges(Action callback)
        {
            m_Callback = callback;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            ProviderStateObserver.OnProviderChanged += OnProviderChanged;
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            ProviderStateObserver.OnProviderChanged -= OnProviderChanged;
        }

        void OnProviderChanged(string providerId)
        {
            m_Callback?.Invoke();
        }
    }
}
