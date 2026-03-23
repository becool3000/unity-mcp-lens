using System;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    /// <summary>
    /// This manipulator triggers a manual refresh of account settings on mouse down but only when account was last unavailable.
    /// Note: This class is possibly no longer necessary because we now correctly monitor network adapter status
    /// in the AccountAPI and refresh account statuses properly. It is kept for safety and redundancy.
    /// </summary>
    class AccountRefreshManipulator : Manipulator
    {
        static Task s_RefreshTask;

        protected override void RegisterCallbacksOnTarget() =>
            target.RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);

        protected override void UnregisterCallbacksFromTarget() =>
            target.UnregisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);

        static void OnMouseDown(MouseDownEvent evt)
        {
            if (AssistantStatusTracker.disableAIToolkitAccountCheck)
                return;

            // Debounce: If a refresh task is already running, do not start a new one.
            if (s_RefreshTask is { IsCompleted: false })
                return;

            // Start the refresh and store the returned task to track its status.
            if (!Account.sessionStatus.IsUsable || !Account.settings.AiAssistantEnabled)
                s_RefreshTask = Account.settings.RefreshSettings();
        }
    }
}
