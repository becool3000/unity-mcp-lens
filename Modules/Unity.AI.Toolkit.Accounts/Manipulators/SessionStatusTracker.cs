using System;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Manipulators
{
    /// <summary>
    /// Set the target UI Element enabled or disabled depending on the session status.
    ///
    /// The session status is essentially "if the user can actually do anything".
    ///
    /// More precisely:
    ///     * If there is a user signed in
    ///     * If the project is cloud connected to an organization
    ///     * If the internet is reachable
    ///     * If the user has agreed to required legal terms
    ///     * If the user has any points remaining
    /// </summary>
    class SessionStatusTracker : Manipulator
    {
        readonly bool m_SetEnabled;
        readonly bool m_SetVisibility;
        readonly Action m_Callback;

        public SessionStatusTracker(bool setEnabled = true, bool setVisibility = false, Action callback = null)
        {
            m_SetEnabled = setEnabled;
            m_SetVisibility = setVisibility;
            m_Callback += callback;
        }

        protected override void RegisterCallbacksOnTarget()
        {
            Account.sessionStatus.OnChange += Refresh;
            Account.session.OnChange += Refresh;
            Refresh();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            Account.sessionStatus.OnChange -= Refresh;
            Account.session.OnChange -= Refresh;
        }

        void Refresh()
        {
            if (m_SetEnabled)
            {
                // Determine the specific enabled state based on the tracker type.
                bool shouldBeEnabled;
                switch (this)
                {
                    case AssistantSessionStatusTracker:
                        shouldBeEnabled = Account.sessionStatus.IsUsable && Account.settings.AiAssistantEnabled;
                        break;
                    case GeneratorsSessionStatusTracker:
                        shouldBeEnabled = Account.sessionStatus.IsUsable && Account.settings.AiGeneratorsEnabled;
                        break;
                    default:
                        shouldBeEnabled = Account.sessionStatus.IsUsable;
                        break;
                }

                // Schedule a one-time update to set the enabled state.
                EditorApplication.CallbackFunction updateCallback = null;
                updateCallback = () =>
                {
                    EditorApplication.update -= updateCallback;
                    target?.SetEnabled(shouldBeEnabled);
                };
                EditorApplication.update += updateCallback;
            }
            else if (m_SetVisibility)
            {
                // Schedule a one-time update to set the visibility style.
                EditorApplication.CallbackFunction updateCallback = null;
                updateCallback = () =>
                {
                    EditorApplication.update -= updateCallback;
                    if (target != null)
                    {
                        target.style.display = Account.sessionStatus.IsUsable ? DisplayStyle.Flex : DisplayStyle.None;
                    }
                };
                EditorApplication.update += updateCallback;
            }

            m_Callback?.Invoke();
        }
    }

    class AssistantSessionStatusTracker : SessionStatusTracker { }
    class GeneratorsSessionStatusTracker : SessionStatusTracker { }
}
