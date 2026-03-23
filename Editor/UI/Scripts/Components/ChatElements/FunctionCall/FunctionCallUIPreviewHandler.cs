using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Preview;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    static class FunctionCallUIPreviewHandler
    {
        const string k_PreviewBorderClass = "mui-ui-preview-bordered";
        const int k_MinPreviewHeight = 400;

        static readonly Dictionary<string, UxmlCacheEntry> k_UxmlCache = new();
        static readonly UIPreviewSystem k_PreviewSystem = new();
        static readonly Dictionary<string, string> k_UnassociatedUssFiles = new();

        static string s_CurrentUxmlPath;
        static bool s_IsSubscribedToConversationChanges;

        class UxmlCacheEntry
        {
            public string UxmlContent;
            public Dictionary<string, string> AssociatedUssFiles = new();
        }

        static bool IsUxmlFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) && filePath.EndsWith(AssistantUIConstants.TemplateExtension, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsUssFile(string filePath)
        {
            return !string.IsNullOrEmpty(filePath) && filePath.EndsWith(AssistantUIConstants.StyleExtension, StringComparison.OrdinalIgnoreCase);
        }

        static UIPreviewContainer CreatePreview(string uxmlContent, string[] ussContents)
        {
            var preview = k_PreviewSystem.CreatePreviewFromMemory(uxmlContent, ussContents);
            if (preview != null)
            {
                preview.SetupForDisplay();
                preview.AddToClassList(k_PreviewBorderClass);
                preview.style.minHeight = k_MinPreviewHeight;
            }
            return preview;
        }

        static void UpdateUxmlCache(string filePath, string uxmlContent)
        {
            s_CurrentUxmlPath = filePath;

            if (!k_UxmlCache.TryGetValue(filePath, out var entry))
            {
                entry = new UxmlCacheEntry();
                k_UxmlCache[filePath] = entry;
            }

            entry.UxmlContent = uxmlContent;

            foreach (var ussFilePath in k_UnassociatedUssFiles.Keys)
            {
                entry.AssociatedUssFiles[ussFilePath] = k_UnassociatedUssFiles[ussFilePath];
            }
            k_UnassociatedUssFiles.Clear();
        }

        static void UpdateUssCache(string uxmlPath, string ussFilePath, string ussContent)
        {
            if (!string.IsNullOrEmpty(uxmlPath) && k_UxmlCache.TryGetValue(uxmlPath, out var entry))
            {
                entry.AssociatedUssFiles[ussFilePath] = ussContent;
            }
            else
            {
                k_UnassociatedUssFiles[ussFilePath] = ussContent;
            }
        }

        static string[] GetCachedUssContents(string uxmlPath)
        {
            if (!k_UxmlCache.TryGetValue(uxmlPath, out var entry) || entry.AssociatedUssFiles.Count == 0)
            {
                return null;
            }

            return entry.AssociatedUssFiles.Values.ToArray();
        }

        static string[] GetUssContentsWithOverride(string uxmlPath, string overrideFilePath, string overrideContent)
        {
            if (!k_UxmlCache.TryGetValue(uxmlPath, out var entry))
                return null;

            var ussContents = new List<string>();

            foreach (var kvp in entry.AssociatedUssFiles)
            {
                if (string.Equals(kvp.Key, overrideFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(overrideContent))
                        ussContents.Add(overrideContent);
                }
                else
                {
                    ussContents.Add(kvp.Value);
                }
            }

            if (!string.IsNullOrEmpty(overrideContent) && !entry.AssociatedUssFiles.ContainsKey(overrideFilePath))
            {
                ussContents.Add(overrideContent);
            }

            return ussContents.Count > 0 ? ussContents.ToArray() : null;
        }

        static string GetCurrentUxmlPath() => s_CurrentUxmlPath;

        static string GetCachedUxmlContent(string uxmlPath)
        {
            return k_UxmlCache.TryGetValue(uxmlPath, out var entry) ? entry.UxmlContent : null;
        }

        public static UIPreviewContainer ProcessUIAsset(AssistantUIContext context, string filePath, string sourceCode)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(sourceCode))
                return null;

            EnsureSubscribedToConversationChanges(context);

            if (IsUssFile(filePath))
            {
                var currentUxmlPath = GetCurrentUxmlPath();
                UpdateUssCache(currentUxmlPath, filePath, sourceCode);
                return null;
            }

            if (IsUxmlFile(filePath))
            {
                UpdateUxmlCache(filePath, sourceCode);
                var ussContents = GetCachedUssContents(filePath);
                return CreatePreview(sourceCode, ussContents);
            }

            return null;
        }

        public static void ClearCache()
        {
            k_UxmlCache.Clear();
            k_UnassociatedUssFiles.Clear();
            s_CurrentUxmlPath = null;
        }

        static void EnsureSubscribedToConversationChanges(AssistantUIContext context)
        {
            if (s_IsSubscribedToConversationChanges || context?.Blackboard == null)
                return;

            context.Blackboard.ActiveConversationChanged += OnActiveConversationChanged;
            s_IsSubscribedToConversationChanges = true;
        }

        static void OnActiveConversationChanged(AssistantConversationId previousId, AssistantConversationId currentId)
        {
            ClearCache();
        }

        public static UIPreviewContainer TryCreatePreview(AssistantUIContext context, string filePath, string newContent, string oldContent = null)
        {
            if (string.IsNullOrEmpty(filePath) || string.IsNullOrEmpty(newContent))
                return null;

            EnsureSubscribedToConversationChanges(context);

            if (IsUxmlFile(filePath))
            {
                var fullContent = ApplyEditToContent(GetCachedUxmlContent(filePath), oldContent, newContent);
                UpdateUxmlCache(filePath, fullContent);
                var ussContents = GetCachedUssContents(filePath);
                return CreatePreview(fullContent, ussContents);
            }

            if (IsUssFile(filePath))
            {
                var currentUxmlPath = GetCurrentUxmlPath();
                if (string.IsNullOrEmpty(currentUxmlPath))
                    return null;

                var uxmlContent = GetCachedUxmlContent(currentUxmlPath);
                if (string.IsNullOrEmpty(uxmlContent))
                    return null;

                var cachedUssContent = GetCachedUssContent(currentUxmlPath, filePath);
                var fullUssContent = ApplyEditToContent(cachedUssContent, oldContent, newContent);

                var ussContents = GetUssContentsWithOverride(currentUxmlPath, filePath, fullUssContent);
                var preview = CreatePreview(uxmlContent, ussContents);

                if (preview != null)
                    UpdateUssCache(currentUxmlPath, filePath, fullUssContent);

                return preview;
            }

            return null;
        }

        static string ApplyEditToContent(string cachedContent, string oldContent, string newContent)
        {
            if (string.IsNullOrEmpty(oldContent))
                return newContent;

            if (string.IsNullOrEmpty(cachedContent))
                return newContent;

            var index = cachedContent.IndexOf(oldContent, StringComparison.Ordinal);
            if (index < 0)
                return newContent;

            return cachedContent.Substring(0, index) + newContent + cachedContent.Substring(index + oldContent.Length);
        }

        static string GetCachedUssContent(string uxmlPath, string ussFilePath)
        {
            if (k_UxmlCache.TryGetValue(uxmlPath, out var entry) && entry.AssociatedUssFiles.TryGetValue(ussFilePath, out var content))
                return content;

            if (k_UnassociatedUssFiles.TryGetValue(ussFilePath, out var unassociatedContent))
                return unassociatedContent;

            return null;
        }
    }
}
