using System;
using System.IO;
using System.Text.RegularExpressions;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Context;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class ContextUtils
    {
        public static string GetObjectTooltip(Object obj)
        {
            var type = obj.GetType().ToString();
            var idx = type.LastIndexOf('.');

            if (idx != -1)
                type = type.Substring(idx + 1);

            return $"{obj.name} ({AddSpacesBeforeCapitals(type)})";
        }

        public static string GetObjectTooltipByName(string objName, string typeName)
        {
            var type = typeName;
            var idx = type.LastIndexOf('.');

            if (idx != -1)
                type = type.Substring(idx + 1);

            return $"{objName} ({AddSpacesBeforeCapitals(type)})";
        }

        static string AddSpacesBeforeCapitals(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            var newText = Regex.Replace(text, "(?<=[a-z])([A-Z])", " $1", RegexOptions.Compiled).Trim();
            return newText;
        }

        public static string GetShortTypeName(string fullTypeName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return null;

            if (fullTypeName.Contains('.'))
            {
                string[] parts = fullTypeName.Split('.');
                return parts[^1];
            }

            return fullTypeName;
        }

        public static bool IsImageFile(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return false;

            foreach (string supportedExtension in AssistantConstants.SupportedImageExtensions)
            {
                if (supportedExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public static VirtualAttachment ProcessImageFileForContext(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                var fileInfo = new FileInfo(filePath);
                string fileName = fileInfo.Name;
                string fileExtension = fileInfo.Extension.ToLowerInvariant();

                // Check if it's a supported image format
                if (!IsImageFile(fileExtension))
                    return null;

                // Read and process the image
                byte[] imageData = File.ReadAllBytes(filePath);
                var sourceFormat = fileExtension.TrimStart('.');
                var attachment = ScreenContextUtility.GetAttachment(imageData, ImageContextCategory.Image, sourceFormat);

                if (attachment != null)
                {
                    attachment.DisplayName = fileName;
                    attachment.Type = "Image";
                }

                return attachment;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing image file '{filePath}': {ex.Message}");
                return null;
            }
        }
    }
}
