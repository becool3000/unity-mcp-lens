using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using UnityEditor;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.UIElements.StyleSheets;
#endif
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Bridge.Editor
{
#if UNITY_6000_3_OR_NEWER
    class UssImporter : StyleSheetImporterImpl
    {
        StringBuilder m_ErrorCollector;

        public UssImporter(StringBuilder errorCollector)
        {
            m_ErrorCollector = errorCollector;
        }

        protected override void OnImportError(StyleSheetImportErrors errors)
        {
            foreach (var error in errors)
            {
                m_ErrorCollector.AppendLine($"USS import error: {error}");
            }
        }
    }

    static partial class UssValidator
    {
        internal static string ValidateUss(string content)
        {
            var errorCollector = new StringBuilder();
            var importer = new UssImporter(errorCollector);
            var stylesheet = ScriptableObject.CreateInstance<StyleSheet>();

            try
            {
                importer.Import(stylesheet, content);
                return errorCollector.ToString();
            }
            catch (System.Exception ex)
            {
                return $"Exception during import: {ex.Message}\n{ex.StackTrace}";
            }
            finally
            {
                Object.DestroyImmediate(stylesheet);
            }
        }
    }
#endif

    static class UxmlValidator
    {
        // Regex to detect USS variables (var(...)) in style attribute values
        static readonly Regex k_UssVariablePattern = new Regex(@"var\s*\(\s*--[a-zA-Z0-9\-]+\s*(?:,\s*[^)]+)?\s*\)", RegexOptions.Compiled);

        internal static string ValidateUxml(string content)
        {
            var errorCollector = new StringBuilder();

            // Check for USS variables in style attributes (not supported in UXML inline styles)
            var ussVarErrors = ValidateUssVariablesInStyleAttributes(content);
            if (!string.IsNullOrEmpty(ussVarErrors))
            {
                errorCollector.AppendLine(ussVarErrors);
            }

            // Search for the schema directory from the project root.
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var schemaPath = Directory.GetDirectories(projectRoot, "UIElementsSchema", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (string.IsNullOrEmpty(schemaPath))
            {
                return "UIElementsSchema directory not found. UXML schema files are required for validation. ";
            }

            try
            {
                // Load all XSD files from UIElementsSchema directory
                var xsdFiles = Directory.GetFiles(schemaPath, "*.xsd", SearchOption.TopDirectoryOnly);
                if (xsdFiles.Length == 0)
                {
                    return "No XSD schema files found in UIElementsSchema directory at " + schemaPath + ". " +
                        "UXML validation requires schema files to be generated. ";
                }

                var settings = new XmlReaderSettings();

                // There is a bug in Unity 6.4+ related to XSD generation. The provided schemas are not valid.
                // For now we just bypass validation via schemas: case IN-123563
#if !DISABLE_SCHEMA_VALIDATION
                settings.ValidationType = ValidationType.Schema;
                settings.ValidationFlags |= XmlSchemaValidationFlags.ProcessInlineSchema;
                settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;

                foreach (var xsdFile in xsdFiles)
                {
                    using var xsdReader = XmlReader.Create(xsdFile);
                    settings.Schemas.Add(null, xsdReader);
                }
#else
                settings.ValidationType = ValidationType.None;
#endif

                settings.ValidationEventHandler += (sender, args) =>
                {
                    errorCollector.AppendLine($"UXML validation {args.Severity.ToString().ToLower()}: Line {args.Exception.LineNumber}, Position {args.Exception.LinePosition}: {args.Message}");
                };

                // Validate the UXML content
                using (var stringReader = new StringReader(content))
                using (var xmlReader = XmlReader.Create(stringReader, settings))
                {
                    while (xmlReader.Read()) { }
                }

                return errorCollector.ToString();
            }
            catch (XmlException ex)
            {
                return $"XML parsing error at Line {ex.LineNumber}, Position {ex.LinePosition}: {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Exception during validation: {ex.Message}";
            }
        }

        /// <summary>
        /// Validates that UXML does not contain USS variables (var(...)) in style attributes.
        /// UIToolkit only supports USS variables in USS files, not in inline style attributes.
        /// </summary>
        static string ValidateUssVariablesInStyleAttributes(string content)
        {
            var errors = new StringBuilder();

            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(content);

                CheckNodeForUssVariables(xmlDoc.DocumentElement, errors);

                return errors.ToString();
            }
            catch (XmlException)
            {
                // XML parsing will be caught by the main validation, skip this check
                return string.Empty;
            }
            catch (Exception ex)
            {
                return $"Exception during USS variable check: {ex.Message}";
            }
        }

        static void CheckNodeForUssVariables(XmlNode node, StringBuilder errors)
        {
            if (node == null) return;

            // Check all attributes for style attribute with USS variables
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                {
                    if (attr.Name == "style")
                    {
                        var styleValue = attr.Value;
                        if (k_UssVariablePattern.IsMatch(styleValue))
                        {
                            var matches = k_UssVariablePattern.Matches(styleValue);
                            foreach (Match match in matches)
                            {
                                errors.AppendLine($"UXML validation error: USS variable '{match.Value}' found in style attribute. " +
                                    "USS variables (var(...)) are not supported in inline style attributes in UXML. " +
                                    "Please move this styling to a USS stylesheet or use static values instead.");
                            }
                        }
                    }
                }
            }

            // Recursively check child nodes
            foreach (XmlNode child in node.ChildNodes)
            {
                CheckNodeForUssVariables(child, errors);
            }
        }
    }

    record AssetRef
    {
        /// <summary>
        /// Path of the asset (for Unity project:// URLs, this is the Assets/... path).
        /// </summary>
        public string path;

        /// <summary>
        /// Original URL as it appears in the USS file (for replacement purposes).
        /// </summary>
        public string originalUrl;

        /// <summary>
        /// True if the asset is referenced via a resource() function, false if via url().
        /// </summary>
        public bool isResource;

        /// <summary>
        /// True if the original URL was in Unity project:// format.
        /// </summary>
        public bool isProjectUrl;

        /// <summary>
        /// GUID of the asset, if known.
        /// </summary>
        public string guid;

        /// <summary>
        /// Type of the asset (which importer used), if known.
        /// </summary>
        public int fileType;

        /// <summary>
        /// Name of the asset, if known.
        /// </summary>
        public string name;

        /// <summary>
        /// Implicit conversion from string to AssetRef for convenience.
        /// </summary>
        /// <param name="path"> Path of the asset. </param>
        /// <returns> New AssetRef instance. </returns>
        public static implicit operator AssetRef(string path) => new AssetRef { path = path, originalUrl = path };
    }

    static class AssetReferenceValidator
    {
        static readonly Regex k_UssUrlPattern = new Regex(@"url\s*\(\s*['""]?([^'""()]+)['""]?\s*\)", RegexOptions.Compiled);

        static readonly Regex k_UssResourcePattern = new Regex(@"resource\s*\(\s*['""]?([^'""()]+)['""]?\s*\)", RegexOptions.Compiled);

        // Regex to parse Unity project:// URLs with GUID (supports both Assets/ and Packages/)
        static readonly Regex k_UnityProjectUrlPattern = new Regex(@"project://database/((?:Assets|Packages)/[^?]+)\?.*?guid=([0-9a-fA-F]+)", RegexOptions.Compiled);

        // Regex to parse Unity project:/ URLs without database prefix (supports both Assets/ and Packages/)
        static readonly Regex k_UnityProjectShortUrlPattern = new Regex(@"project:/((?:Assets|Packages)/[^""')\s]+)", RegexOptions.Compiled);

        static readonly string[] k_ValidImageExtensions = new[] { ".png", ".jpg", ".jpeg", ".tga", ".gif", ".bmp", ".psd", ".tif", ".tiff", ".webp" };

        const string k_PlaceholderTextureFolder = "Assets/AI_Assistant/Placeholders";

        const string k_PlaceholderTextureName = "PlaceholderTexture.png";

        internal static string ValidateAndFixUxmlAssetReferences(string uxmlContent, out string fixedContent, out bool hasReplacements)
        {
            var errorCollector = new StringBuilder();
            fixedContent = uxmlContent;
            hasReplacements = false;

            try
            {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(uxmlContent);

                // Track which assets to replace (mapping from node+attribute to replacement URL)
                var replacements = new List<(XmlAttribute attribute, string originalUrl, string replacementUrl)>();

                // Check all elements for asset references and collect replacements
                CollectAssetReplacements(xmlDoc.DocumentElement, replacements, errorCollector);

                if (replacements.Count > 0)
                {
                    // Ensure placeholder texture exists
                    var placeholderPath = EnsurePlaceholderTexture();
                    var placeholderGuid = AssetDatabase.AssetPathToGUID(placeholderPath);

                    // Apply replacements directly to the XML document
                    foreach (var (attribute, originalUrl, _) in replacements)
                    {
                        string replacementUrl;

                        // Determine if original was a Unity project:// URL
                        bool isProjectUrl = originalUrl.StartsWith("project://");

                        // If original was a Unity project:// URL, use the same format
                        if (isProjectUrl)
                        {
                            replacementUrl = $"project://database/{placeholderPath}?fileID=2800000&guid={placeholderGuid}&type=3#{Path.GetFileNameWithoutExtension(placeholderPath)}";
                        }
                        else
                        {
                            // Otherwise use simple path
                            replacementUrl = $"/{placeholderPath.Replace("Assets/", "").Replace("\\", "/")}";
                        }

                        // Modify the attribute value directly in the XmlDocument
                        attribute.Value = attribute.Value.Replace(originalUrl, replacementUrl);

                        errorCollector.AppendLine($"Missing asset reference '{originalUrl}' replaced with placeholder texture.");
                        hasReplacements = true;
                    }

                    // Serialize the modified XML back to string
                    fixedContent = xmlDoc.OuterXml;
                }
            }
            catch (XmlException ex)
            {
                return $"Failed to parse UXML for asset validation: {ex.Message}";
            }

            return errorCollector.ToString();
        }

        static void CollectAssetReplacements(XmlNode node, List<(XmlAttribute attribute, string originalUrl, string replacementUrl)> replacements, StringBuilder errorCollector)
        {
            if (node == null) return;

            // Check all attributes for asset references
            if (node.Attributes != null)
            {
                foreach (XmlAttribute attr in node.Attributes)
                {
                    // Check for src attribute (Style, Image, etc.)
                    if (attr.Name == "src")
                    {
                        var assetRef = ParseAssetUrl(attr.Value);
                        if (!AssetExists(assetRef))
                        {
                            var extension = Path.GetExtension(assetRef.path).ToLower();

                            // Only replace image assets with placeholders
                            if (!k_ValidImageExtensions.Contains(extension))
                            {
                                errorCollector.AppendLine($"Warning: Missing asset reference '{assetRef.originalUrl}'.");
                            }
                            else
                            {
                                replacements.Add((attr, assetRef.originalUrl, null));
                            }
                        }
                    }
                    // Check for style attribute with inline USS
                    else if (attr.Name == "style")
                    {
                        CollectInlineStyleReplacements(attr, replacements, errorCollector);
                    }
                }
            }

            // Recursively check child nodes
            foreach (XmlNode child in node.ChildNodes)
            {
                CollectAssetReplacements(child, replacements, errorCollector);
            }
        }

        static void CollectInlineStyleReplacements(XmlAttribute styleAttr, List<(XmlAttribute attribute, string originalUrl, string replacementUrl)> replacements, StringBuilder errorCollector)
        {
            // In inline styles, URLs are HTML-encoded: url(&quot;...&quot;)
            // First decode HTML entities
            var decodedStyle = System.Net.WebUtility.HtmlDecode(styleAttr.Value);

            // Check url() functions
            var urlMatches = k_UssUrlPattern.Matches(decodedStyle);
            foreach (Match match in urlMatches)
            {
                var fullUrl = match.Groups[1].Value;
                var assetRef = ParseAssetUrl(fullUrl);

                if (!AssetExists(assetRef))
                {
                    var extension = Path.GetExtension(assetRef.path).ToLower();

                    // Only replace image assets with placeholders
                    if (!k_ValidImageExtensions.Contains(extension))
                    {
                        errorCollector.AppendLine($"Warning: Missing asset reference '{assetRef.originalUrl}'.");
                    }
                    else
                    {
                        replacements.Add((styleAttr, assetRef.originalUrl, null));
                    }
                }
            }

            // Check resource() functions
            var resourceMatches = k_UssResourcePattern.Matches(decodedStyle);
            foreach (Match match in resourceMatches)
            {
                var resourcePath = match.Groups[1].Value;
                var assetRef = new AssetRef
                {
                    path = resourcePath,
                    originalUrl = resourcePath,
                    isResource = true
                };
                if (!ResourceExists(resourcePath))
                {
                    errorCollector.AppendLine($"Warning: Missing asset reference '{assetRef.originalUrl}'.");
                }
            }
        }

        internal static string ValidateAndFixUssAssetReferences(string ussContent, out string fixedContent, out bool hasReplacements)
        {
            var errorCollector = new StringBuilder();
            fixedContent = ussContent;
            hasReplacements = false;

            var missingAssets = new Dictionary<string, AssetRef>();

            // Find all url() and resource() references
            var urlMatches = k_UssUrlPattern.Matches(ussContent);
            var resourceMatches = k_UssResourcePattern.Matches(ussContent);

            foreach (Match match in urlMatches)
            {
                var fullUrl = match.Groups[1].Value;
                AssetRef assetRef = ParseAssetUrl(fullUrl);

                if (!AssetExists(assetRef))
                {
                    // Use originalUrl as key to avoid duplicates
                    missingAssets[assetRef.originalUrl] = assetRef;
                }
            }

            foreach (Match match in resourceMatches)
            {
                var resourcePath = match.Groups[1].Value;
                var assetRef = new AssetRef
                {
                    path = resourcePath,
                    originalUrl = resourcePath,
                    isResource = true
                };
                if (!ResourceExists(resourcePath))
                {
                    missingAssets[assetRef.originalUrl] = assetRef;
                }
            }

            if (missingAssets.Count > 0)
            {
                // Ensure placeholder texture exists
                var placeholderPath = EnsurePlaceholderTexture();
                var placeholderGuid = AssetDatabase.AssetPathToGUID(placeholderPath);

                // Build replacement map
                var replacementMap = new Dictionary<string, string>();

                foreach (var missingAsset in missingAssets.Values)
                {
                    // if the missing asset is from a url(),
                    // ensure the replacement path is in the correct format
                    if (!missingAsset.isResource && !k_ValidImageExtensions.Contains(Path.GetExtension(missingAsset.path).ToLower()))
                    {
                        errorCollector.AppendLine($"Warning: Missing asset reference '{missingAsset.path}' which is not an image file.");
                        continue;
                    }

                    string replacementUrl;

                    // If original was a Unity project:// URL, use the same format
                    if (missingAsset.isProjectUrl)
                    {
                        replacementUrl = $"project://database/{placeholderPath}?fileID=2800000&guid={placeholderGuid}&type=3#{Path.GetFileNameWithoutExtension(placeholderPath)}";
                    }
                    else
                    {
                        // Otherwise use simple path
                        replacementUrl = $"/{placeholderPath.Replace("Assets/", "").Replace("\\", "/")}";
                    }

                    replacementMap[missingAsset.originalUrl] = replacementUrl;
                    errorCollector.AppendLine($"Missing asset reference '{missingAsset.originalUrl}' replaced with placeholder texture.");
                    hasReplacements = true;
                }

                // Use regex replacement with MatchEvaluator to ensure exact matches
                if (replacementMap.Count > 0)
                {
                    fixedContent = k_UssUrlPattern.Replace(ussContent, match =>
                    {
                        var url = match.Groups[1].Value;
                        return replacementMap.TryGetValue(url, out var replacement)
                            ? match.Value.Replace(url, replacement)
                            : match.Value;
                    });

                    fixedContent = k_UssResourcePattern.Replace(fixedContent, match =>
                    {
                        var resourcePath = match.Groups[1].Value;
                        return replacementMap.TryGetValue(resourcePath, out var replacement)
                            ? match.Value.Replace(resourcePath, replacement)
                            : match.Value;
                    });
                }
            }

            return errorCollector.ToString();
        }

        static AssetRef ParseAssetUrl(string url)
        {
            // Check if it's a Unity project:// URL with GUID
            var projectMatch = k_UnityProjectUrlPattern.Match(url);
            if (projectMatch.Success)
            {
                var assetPath = projectMatch.Groups[1].Value;
                var guid = projectMatch.Groups[2].Value;
                return new AssetRef
                {
                    path = assetPath,
                    originalUrl = url,
                    guid = guid,
                    isResource = false,
                    isProjectUrl = true
                };
            }

            // Check if it's a Unity project:/ URL without GUID (short format)
            var shortProjectMatch = k_UnityProjectShortUrlPattern.Match(url);
            if (shortProjectMatch.Success)
            {
                var assetPath = shortProjectMatch.Groups[1].Value;
                return new AssetRef
                {
                    path = assetPath,
                    originalUrl = url,
                    isResource = false,
                    isProjectUrl = true
                };
            }

            // Otherwise, treat as simple path
            return new AssetRef
            {
                path = url,
                originalUrl = url,
                isResource = false,
                isProjectUrl = false
            };
        }

        static bool AssetExists(AssetRef assetRef)
        {
            // First try by GUID if available
            if (!string.IsNullOrEmpty(assetRef.guid))
            {
                var pathFromGuid = AssetDatabase.GUIDToAssetPath(assetRef.guid);
                if (!string.IsNullOrEmpty(pathFromGuid) && File.Exists(pathFromGuid))
                {
                    return true;
                }
                // If GUID is provided but doesn't resolve, consider it missing
                return false;
            }

            // Fall back to path-based check
            var assetPath = assetRef.path;

            // Relative paths (e.g., ../Image.png, ./icon.png, subfolder/image.png) cannot be validated
            // because we don't know the context (where the UXML/USS file will be saved).
            // Assume they exist to avoid false positives.
            if (!assetPath.StartsWith("/") && !assetPath.StartsWith("Assets/") && !assetPath.StartsWith("Packages/"))
            {
                return true;
            }

            // Check if it's already a valid Assets/ or Packages/ path
            if (assetPath.StartsWith("Assets/") || assetPath.StartsWith("Packages/"))
            {
                return File.Exists(assetPath);
            }

            // Try as absolute path from Assets root
            if (assetPath.StartsWith("/"))
            {
                assetPath = "Assets" + assetPath;
            }

            return File.Exists(assetPath);
        }

        static bool ResourceExists(string resourcePath)
        {
            // Find assets by name to avoid loading them with Resources.Load
            var assetName = Path.GetFileName(resourcePath);
            var guids = AssetDatabase.FindAssets($"{assetName} t:Object");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var resourceIndex = path.IndexOf("/Resources/");
                if (resourceIndex == -1) continue;
                var pathInResources = path.Substring(resourceIndex + 10); // Length of "/Resources/"
                var pathWithoutExt = Path.ChangeExtension(pathInResources, null);
                if (pathWithoutExt?.Replace('\\', '/') == resourcePath)
                    return true;
            }
            return false;
        }

        static string EnsurePlaceholderTexture()
        {
            var fullPath = Path.Combine(k_PlaceholderTextureFolder, k_PlaceholderTextureName);

            if (!File.Exists(fullPath))
            {
                // Create directory if it doesn't exist
                Directory.CreateDirectory(k_PlaceholderTextureFolder);

                // Create a simple 32x32 magenta texture (easy to spot)
                var texture = new Texture2D(32, 32);
                try
                {
                    var pixels = new Color32[32 * 32];
                    for (var i = 0; i < pixels.Length; i++)
                    {
                        pixels[i] = new Color32(255, 0, 255, 255); // Magenta
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply();

                    // Save as PNG
                    var pngData = texture.EncodeToPNG();
                    File.WriteAllBytes(fullPath, pngData);
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }

                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);
                // Set texture import settings
                var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.mipmapEnabled = false;
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }

            return fullPath;
        }
    }

    static class UIPanelUtils
    {
        static readonly PropertyInfo k_PanelProperty = typeof(PanelSettings)
            .GetProperty("panel", BindingFlags.Instance | BindingFlags.NonPublic);

        static MethodInfo s_RepaintMethod;

        static MethodInfo s_RenderMethod;

        internal static IPanel GetPanel(PanelSettings panelSettings)
        {
            return k_PanelProperty!.GetValue(panelSettings) as IPanel;
        }

        internal static void RepaintPanel(IPanel panel)
        {
            var panelType = panel.GetType();
            s_RepaintMethod ??= panelType.GetMethod("Repaint", BindingFlags.Public | BindingFlags.Instance);
            s_RepaintMethod!.Invoke(panel, new object[] {Event.current});
        }

        internal static void RenderPanel(IPanel panel)
        {
            var panelType = panel.GetType();
            s_RenderMethod ??= panelType.GetMethod("Render", BindingFlags.Public | BindingFlags.Instance);
            s_RenderMethod!.Invoke(panel, null);
        }

        internal static VisualElement GetVisualTree(IPanel panel)
        {
            var panelType = panel.GetType();
            var visualTreeProperty =
                panelType.GetProperty("visualTree", BindingFlags.Instance | BindingFlags.Public);
            var root = (VisualElement) visualTreeProperty!.GetValue(panel);
            return root;
        }
    }
}
