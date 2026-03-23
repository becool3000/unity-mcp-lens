using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    class PackageUpdateStateChanges : Manipulator
    {
        readonly Action m_Callback;

        public PackageUpdateStateChanges(Action callback)
        {
            m_Callback = callback;
        }

        protected override void RegisterCallbacksOnTarget() => PackageUpdateState.instance.OnChange += Refresh;
        protected override void UnregisterCallbacksFromTarget() => PackageUpdateState.instance.OnChange -= Refresh;

        void Refresh() => m_Callback?.Invoke();
    }
}
