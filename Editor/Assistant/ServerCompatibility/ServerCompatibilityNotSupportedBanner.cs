using Unity.AI.Toolkit.Accounts.Components;

namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    class ServerCompatibilityNotSupportedBanner : BasicBannerContent
    {
        public ServerCompatibilityNotSupportedBanner() : base(
                ServerCompatibilityText.NotSupportedMessage,
                new LabelLink(
                    ServerCompatibilityText.NotSupportedMessageLink,
                    () => UnityEditor.PackageManager.UI.Window.Open(AssistantConstants.PackageName))
            ) { }
    }
}
