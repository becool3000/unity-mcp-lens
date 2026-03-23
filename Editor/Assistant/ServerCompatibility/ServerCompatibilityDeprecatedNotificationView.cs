using System;
using Unity.AI.Toolkit.Accounts.Components;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    class ServerCompatibilityDeprecatedNotificationView : BasicBannerContent
    {
        public ServerCompatibilityDeprecatedNotificationView(Action dismiss = null)
            : base(
                ServerCompatibilityText.DeprecatedMessage,
                new LabelLink(
                    ServerCompatibilityText.DeprecatedMessageLink,
                    () => UnityEditor.PackageManager.UI.Window.Open(AssistantConstants.PackageName))
            )
        {
            var dismissButton = new Button(() =>
            {
                NotificationsState.instance.hideCompatibility = true;
                dismiss?.Invoke();
            }) {text = "Dismiss"};
            dismissButton.AddToClassList("banner-right-action");
            Add(dismissButton);
        }
    }
}
