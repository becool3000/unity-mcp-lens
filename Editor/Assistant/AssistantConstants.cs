using System;
using Unity.AI.Assistant.Editor.Utils;
using UnityEditor;

namespace Unity.AI.Assistant.Editor
{
    static class AssistantConstants
    {
        internal const string PackageName = "com.unity.ai.assistant";

        internal const int MaxConversationHistory = 1000;

        internal const string TextCutoffSuffix = "...";

        internal static readonly string SourceReferenceColor = EditorGUIUtility.isProSkin ? "4c7effff" : "055b9fff";
        internal static readonly string SourceReferencePrefix = "REF:";

        internal static readonly string InlineCodeTextColor = EditorGUIUtility.isProSkin ? "#E6E6E6" : "#141414";
        internal static readonly string ChatElementLineHeight = "18px";

        internal static readonly string UpdateButtonAccentColor = EditorGUIUtility.isProSkin ? "#2c5d87" : "#3a72b0";

        internal const string NewLineCRLF = "\r\n";
        internal const string NewLineLF = "\n";

        internal const string ProjectIdTagPrefix = "projId:";

        internal const string ContextTag = "#PROJECTCONTEXT#";
        internal static readonly string ContextTagEscaped = ContextTag.Replace("#", @"\#");

        internal const int AttachedContextDisplayLimit = 8;

        internal const long MaxImageFileSize = 10 * 1024 * 1024;
        internal const long MaxTotalImageSize = 5 * 1024 * 1024; // 5MB total limit for all embedded images

        internal const int ChatPreAuthorizePoints = 25; // Defined backend side: Estimated points preauthorized per request. Set to cover P99 plus buffer
        
#if AI_TEMPORARY_ASSET_IMPORTERS_PRESENT
        internal static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".tga", ".tif", ".tiff", ".psd", ".exr", ".hdr", ".iff", ".pct" };
#else
        internal static readonly string[] SupportedImageExtensions = { ".png", ".jpg", ".jpeg", ".exr" };
#endif
        internal const int UserGuidelineCharacterLimit = 16384;

        internal static string GetDisclaimerHeader(string codeFormat = CodeFormat.CSharp)
        {
            const string disclaimerText = @"{0} AI-Tag
This was created with the help of Assistant, a Unity Artificial Intelligence product.";

            return CodeUtils.GetCommentedLines(string.Format(disclaimerText, DateTime.UtcNow.ToString("yyyy-MM-dd")), codeFormat);
        }

        internal const string DefaultCodeBlockCsharpFilename = "Code";
        internal const string DefaultCodeBlockCsharpExtension = "cs";
        internal const string DefaultCodeBlockShaderFilename = "NewShader";
        internal const string DefaultCodeBlockShaderExtension = "shader";
        internal const string DefaultCodeBlockTextFilename = "Output";
        internal const string DefaultCodeBlockTextExtension = "txt";

        internal static readonly string[] ShaderCodeBlockTypes = new string[] { "glsl", "hlsl", "shader" };

        internal const string CodeBlockCsharpFiletype = "cs";
        internal const string CodeBlockCsharpValidateFiletype = "csharp_validate";

        internal const string UxmlExtension = ".uxml";
        internal const string UssExtension = ".uss";

        internal const string DefaultConversationTitle = "New conversation";
    }
}
