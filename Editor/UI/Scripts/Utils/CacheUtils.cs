namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class CacheUtils
    {
        static readonly char[] k_PathTrimChars = {' ', '\t', '\n', '/'};

        public static string GetCachePath(string basePath, string subPath, string resourceFolderName)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                basePath = AssistantUIConstants.UIEditorPath;
            }

            string result = basePath + resourceFolderName;

            if (!string.IsNullOrEmpty(subPath))
            {
                result = result + subPath.Trim(k_PathTrimChars) + AssistantUIConstants.UnityPathSeparator;
            }

            return result;
        }

        public static string GetCacheKey(string basePath, string subPath)
        {
            return string.Concat(basePath, "_", subPath ?? string.Empty);
        }
    }
}
