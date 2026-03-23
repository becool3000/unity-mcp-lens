namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    static class ServerCompatibilityText
    {
        public const string NotSupportedMessageLink = "openpackagemanager";
        public static string NotSupportedMessage = $"The {AssistantConstants.PackageName} package you currently have installed does not support the current server. Please update your package to the latest version. <link={NotSupportedMessageLink}><color=#7BAEFA>Update in Package Manager</color></link>";

        public const string DeprecatedMessageLink = "openpackagemanager";
        public static string DeprecatedMessage = $"This package is out of date and some features may not work as expected. Please update to the latest. <link={DeprecatedMessageLink}><color=#7BAEFA>Open Package Manager</color></link>";
    }
}
