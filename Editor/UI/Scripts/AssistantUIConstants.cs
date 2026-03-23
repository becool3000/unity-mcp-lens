using Unity.AI.Assistant.Editor;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    internal static class AssistantUIConstants
    {
        internal const int CompactWindowThreshold = 600;
        internal const string CompactStyle = "mui-compact";
        internal const string IconStylePrefix = "mui-icon-";

        internal const char UnityPathSeparator = '/';
        internal const string TemplateExtension = ".uxml";
        internal const string StyleExtension = ".uss";

        internal const string ResourceFolderName = "Resources";

        internal const string PackageRoot = "";
        internal const string BasePath = "Packages/" + AssistantConstants.PackageName + PackageRoot + "/";
        internal const string UIEditorPath = "Editor/UI/";

        internal const string AssetFolder = "Assets/";
        internal const string ViewFolder = "Views/";
        internal const string StyleFolder = "Styles/";
        internal const string VideosFolder = "Videos/";

        internal const string UIModulePath = BasePath + UIEditorPath;
        internal const string UIStylePath = UIModulePath + StyleFolder;

        internal const string VideoPath = UIModulePath + AssetFolder + VideosFolder;
        internal const string VideoExtension = ".webm";

        internal const string AssistantBaseStyle = "Assistant.tss";
        internal const string AssistantSharedStyleDark = "AssistantSharedDark";
        internal const string AssistantSharedStyleLight = "AssistantSharedLight";

        internal const string ActiveActionButtonClass = "mui-action-button-active";

        internal const int UIPreviewFullHDWidth = 1920;
        internal const int UIPreviewFullHDHeight = 1080;

        internal const string CodeLineColorTransparency = "40";
        internal const string CodeLineAddedColor = "#a8e6a8" + CodeLineColorTransparency;
        internal const string CodeLineRemovedColor = "#e6a8a8" + CodeLineColorTransparency;
        internal const string CodeLineDefaultColor = "#606060" + CodeLineColorTransparency;

        internal const string FeedbackButtonDefaultTitle = "Send";
        internal const string FeedbackButtonSentTitle = "Feedback sent";
        internal const string FeedbackDownVotePlaceholder = "Tell us what went wrong";
        internal const string FeedbackUpVotePlaceholder = "Tell us what went well";

        internal const float UIAnalyticsDebounceInterval = 0.5f;
    }
}
