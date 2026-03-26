using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Tools.Editor
{
    class UITools
    {
        [Serializable]
        public class PanelSettingsResult
        {
            [Description("GUID of the PanelSettings asset found or created.")]
            public string panelSettingsGuid = string.Empty;

            [Description("File path of the PanelSettings asset.")]
            public string panelSettingsPath = string.Empty;
        }

        [UsedImplicitly]
        [AgentTool("Find an existing PanelSettings asset in the project.", "Unity.FindPanelSettings",
            ToolCallEnvironment.EditMode, assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_UITag)]
        internal static async Task<PanelSettingsResult> FindPanelSettings(ToolExecutionContext context)
        {
            return await FindPanelSettingsInternal(context, false);
        }

        [UsedImplicitly]
        [AgentTool(
            "Find an existing PanelSettings asset in the project, or create a default one if none exists. ",
            "Unity.FindOrCreateDefaultPanelSettings",
            ToolCallEnvironment.EditMode,
            assistantMode: AssistantMode.Agent,
            tags: FunctionCallingUtilities.k_UITag)]
        internal static async Task<PanelSettingsResult> FindOrCreateDefaultPanelSettings(ToolExecutionContext context)
        {
            return await FindPanelSettingsInternal(context, true);
        }

        static async Task<PanelSettingsResult> FindPanelSettingsInternal(ToolExecutionContext context, bool createIfNotFound)
        {
            var result = new PanelSettingsResult();

            // Search for existing PanelSettings assets in the project
            var panelSettingsGuids = AssetDatabase.FindAssets("t:PanelSettings");

            if (panelSettingsGuids.Length > 0)
            {
                // Use the first found PanelSettings
                var firstGuid = panelSettingsGuids[0];
                var path = AssetDatabase.GUIDToAssetPath(firstGuid);

                result.panelSettingsGuid = firstGuid;
                result.panelSettingsPath = path;

                InternalLog.Log($"Found existing PanelSettings: {path} (GUID: {firstGuid})");
                return result;
            }

            if (!createIfNotFound)
            {
                InternalLog.Log("No existing PanelSettings found.");
                return result;
            }

            // No existing PanelSettings found, create a default one
            var assetsPath = "Assets";
            var panelSettingsDir = Path.Combine(assetsPath, "UI");

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, panelSettingsDir);

            if (!Directory.Exists(panelSettingsDir))
            {
                Directory.CreateDirectory(panelSettingsDir);
            }

            var panelSettingsPath = Path.Combine(panelSettingsDir, "DefaultPanelSettings.asset");
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, panelSettingsPath);

            // Create a new PanelSettings asset with sensible defaults
            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "DefaultPanelSettings";

            // Set ThemeStyleSheet if not already assigned (check for existing ones first)
            if (!panelSettings.themeStyleSheet)
            {
                var themeGuids = AssetDatabase.FindAssets("t:ThemeStyleSheet");
                if (themeGuids.Length > 0)
                {
                    var themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(
                        AssetDatabase.GUIDToAssetPath(themeGuids[0]));
                    if (themeStyleSheet)
                    {
                        panelSettings.themeStyleSheet = themeStyleSheet;
                        InternalLog.Log($"Assigned existing ThemeStyleSheet to PanelSettings");
                    }
                }
                else
                {
                    // Create a default ThemeStyleSheet if none exists
                    const string themeStyleSheetContent = "@import url(\"unity-theme://default\");";
                    var themePath = Path.Combine(panelSettingsDir, "UnityDefaultRuntimeTheme.tss");

                    await context.Permissions.CheckFileSystemAccess(
                        File.Exists(themePath)
                            ? IToolPermissions.ItemOperation.Modify
                            : IToolPermissions.ItemOperation.Create,
                        themePath);

                    await File.WriteAllTextAsync(themePath, themeStyleSheetContent);
                    AssetDatabase.ImportAsset(themePath, ImportAssetOptions.Default);
                    var themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(themePath);
                    panelSettings.themeStyleSheet = themeStyleSheet;
                    InternalLog.Log($"Created and assigned new default ThemeStyleSheet to PanelSettings");
                }
            }

            // Set PanelTextSettings if not already assigned (check for existing ones first)
            if (!panelSettings.textSettings)
            {
                var textSettingsGuids = AssetDatabase.FindAssets("t:PanelTextSettings");
                if (textSettingsGuids.Length > 0)
                {
                    var textSettings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(
                        AssetDatabase.GUIDToAssetPath(textSettingsGuids[0]));
                    if (textSettings)
                    {
                        panelSettings.textSettings = textSettings;
                        InternalLog.Log($"Assigned existing PanelTextSettings to PanelSettings");
                    }
                }
                else
                {
                    // Create a default PanelTextSettings if none exists
                    var textSettings = ScriptableObject.CreateInstance<PanelTextSettings>();
                    textSettings.name = "DefaultPanelTextSettings";
                    var textSettingsPath = Path.Combine(panelSettingsDir, "DefaultPanelTextSettings.asset");

                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, textSettingsPath);

                    AssetDatabase.CreateAsset(textSettings, textSettingsPath);
                    panelSettings.textSettings = textSettings;
                    InternalLog.Log($"Created and assigned new default PanelTextSettings");
                }
            }

            AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.Default);

            // Get the GUID of the newly created asset
            var newGuid = AssetDatabase.AssetPathToGUID(panelSettingsPath);

            result.panelSettingsGuid = newGuid;
            result.panelSettingsPath = panelSettingsPath;

            InternalLog.Log($"Created default PanelSettings: {panelSettingsPath} (GUID: {newGuid})");
            return result;
        }

        [UsedImplicitly]
        [AgentTool(
            "Generate UXML schema files (XSD files) required for validating UXML documents.",
            "Unity.GenerateUxmlSchemas",
            ToolCallEnvironment.EditMode,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_UITag)]
        internal static async Task<string> GenerateUxmlSchemas(ToolExecutionContext context)
        {
            return await GenerateUxmlSchemasWithoutPermissions();
        }

        [ToolPermissionIgnore]
        static Task<string> GenerateUxmlSchemasWithoutPermissions()
        {
            // Verify that the schema directory path will be accessible
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // Execute the menu item that generates UXML schemas
            EditorApplication.ExecuteMenuItem("Assets/Update UXML Schema");

            // Verify that the schema directory was created
            var schemaDir = Directory.GetDirectories(projectRoot, "UIElementsSchema", SearchOption.TopDirectoryOnly).FirstOrDefault();

            if (string.IsNullOrEmpty(schemaDir))
            {
                throw new InvalidOperationException(
                    "UXML schema generation did not create the UIElementsSchema directory. This may indicate an issue with the Unity Editor setup or permissions.");
            }

            // Verify that XSD files were created
            var xsdFiles = Directory.GetFiles(schemaDir, "*.xsd", SearchOption.TopDirectoryOnly);
            if (xsdFiles.Length == 0)
            {
                throw new InvalidOperationException($"UIElementsSchema directory was found at '{schemaDir}', but no XSD schema files were created. This may indicate an issue with the Unity Editor setup.");
            }

            return Task.FromResult($"Generated {xsdFiles.Length} UXML schema files");
        }

        const string k_TemplateExtension = ".uxml";
        const string k_StyleExtension = ".uss";
        const string k_ThemeExtension = ".tss";

        enum AssetType
        {
            Unknown,
            Uxml,
            Uss,
            Tss
        }

        [Serializable]
        public class UIAssetValidationResult
        {
            [Description("File path of the validated asset.")]
            public string filePath = string.Empty;

            [Description("Asset type (UXML, USS, etc.).")]
            public string assetType = string.Empty;
        }

        static AssetType GetAssetType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                k_TemplateExtension => AssetType.Uxml,
                k_StyleExtension => AssetType.Uss,
                k_ThemeExtension => AssetType.Tss,
                _ => AssetType.Unknown
            };
        }

        internal const string k_ValidateUIAssetFunctionId = "Unity.ValidateUIAsset";

        [UsedImplicitly]
        [AgentTool(
            "Validate only a UI document (UXML or USS file). If UXML validation fails due to missing schema files, ensure schemas are generated first, then retry validation.",
            k_ValidateUIAssetFunctionId, ToolCallEnvironment.EditMode,
            assistantMode: AssistantMode.Ask, tags: FunctionCallingUtilities.k_UITag)]
        internal static async Task<UIAssetValidationResult> ValidateUIAsset(ToolExecutionContext context,
            [Parameter("Full file path to the UI asset (e.g., Assets/UI/MyDocument.uxml or Assets/UI/Styles.uss).")]
            string filePath, [Parameter("Source code of the UI asset to save.")] string sourceCode)
        {
            return await ValidateUIAssetInternal(context, filePath, sourceCode, false);
        }

        internal const string k_SaveAndValidateFunctionId = "Unity.SaveAndValidateUIAsset";

        [UsedImplicitly]
        [AgentTool(
            "Validate and save a UI document (UXML or USS file). Save USS stylesheets before UXML documents that reference them. Ensure all referenced stylesheets and assets exist before calling this tool for UXML files.",
            k_SaveAndValidateFunctionId, ToolCallEnvironment.EditMode,
            assistantMode: AssistantMode.Agent, tags: FunctionCallingUtilities.k_UITag)]
        internal static async Task<UIAssetValidationResult> SaveAndValidateUIAsset(ToolExecutionContext context,
            [Parameter("Full file path to the UI asset (e.g., Assets/UI/MyDocument.uxml or Assets/UI/Styles.uss).")]
            string filePath, [Parameter("Source code of the UI asset to save.")] string sourceCode)
        {
            return await ValidateUIAssetInternal(context, filePath, sourceCode, true);
        }

        internal static async Task<UIAssetValidationResult> ValidateUIAssetInternal(
            ToolExecutionContext context,
            string filePath,
            string sourceCode,
            bool saveToDisk
        )
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("File path cannot be null or empty.");

            if (string.IsNullOrEmpty(sourceCode))
                throw new ArgumentException("Source code cannot be null or empty.");

            var assetType = GetAssetType(filePath);

            if (assetType == AssetType.Unknown)
            {
                throw new ArgumentException("Asset type cannot be unknown.");
            }

            var result = new UIAssetValidationResult
            {
                filePath = filePath,
                assetType = assetType.ToString()
            };

            var fileExtension = Path.GetExtension(filePath).ToLower();
            string validationError;
            var fixedSourceCode = string.Empty;
            var codeFormat = string.Empty;

            // === VALIDATION: Syntax checking and asset references ===
            if (fileExtension == k_TemplateExtension)
            {
                codeFormat = CodeFormat.Uxml;
                validationError = VisualTreeAssetUtils.ValidateUxml(sourceCode);

#if VALIDATE_UIDOCUMENT_ASSET_REFERENCES

                // Check for missing asset references
                string assetValidationError = null;
                var hasReplacements = false;
                string fixedContent = null;

                assetValidationError = VisualTreeAssetUtils.ValidateAndFixAssetReferences(
                    sourceCode,
                    out fixedContent,
                    out hasReplacements
                );

                if (!string.IsNullOrEmpty(assetValidationError))
                {
                    if (!string.IsNullOrEmpty(validationError))
                        validationError += "\n\n";
                    validationError += assetValidationError;
                }

                if (hasReplacements && !string.IsNullOrEmpty(fixedContent))
                    fixedSourceCode = fixedContent;
#endif
            }
            else if (fileExtension is k_ThemeExtension or k_StyleExtension)
            {
                codeFormat = CodeFormat.Uss;
                validationError = StyleSheetUtils.ValidateUss(sourceCode);

                // Check for missing asset references
                var assetValidationError = StyleSheetUtils.ValidateAndFixAssetReferences(
                    sourceCode,
                    out var fixedContent,
                    out var hasReplacements
                );

                if (!string.IsNullOrEmpty(assetValidationError))
                {
                    if (!string.IsNullOrEmpty(validationError))
                        validationError += "\n\n";
                    validationError += assetValidationError;
                }

                if (hasReplacements && !string.IsNullOrEmpty(fixedContent))
                    fixedSourceCode = fixedContent;
            }
            else
            {
                validationError = $"Unsupported file type: {fileExtension}. " +
                    $"Only {k_TemplateExtension}, {k_ThemeExtension} and {k_StyleExtension} files are supported.";
            }

            if (!string.IsNullOrEmpty(validationError))
            {
                throw new InvalidOperationException($"Validation failed: {validationError}");
            }

            if (!string.IsNullOrEmpty(fixedSourceCode))
            {
                InternalLog.Log($"SaveAndValidateUIAsset: Applied fixes for missing asset references in {filePath}");
                throw new InvalidOperationException(
                    "Validation found missing asset references and applied fixes:\n" + fixedSourceCode);
            }

            if (saveToDisk)
            {
                if (File.Exists(filePath))
                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Modify, filePath);
                else
                    await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Create, filePath);
                var fileSaved = await SaveUIDocumentToFile(filePath, sourceCode, codeFormat);
                if (!fileSaved)
                    throw new InvalidOperationException("Failed to save the UI asset after validation.");
            }

            InternalLog.Log($"The asset {filePath} was validated and saved successfully.");
            return result;
        }

        /// <summary>
        /// Saves the UI document to the specified file path.
        /// </summary>
        static async Task<bool> SaveUIDocumentToFile(string filePath, string sourceCode, string codeFormat)
        {
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(directory))
                    return false;

                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // Apply disclaimer header
                var disclaimerHeader = AssistantConstants.GetDisclaimerHeader(codeFormat);
                var contentWithHeader = disclaimerHeader + sourceCode;

                // Write the file
                await File.WriteAllTextAsync(filePath, contentWithHeader);

                // Refresh the asset database so Unity recognizes the new/modified file
                AssetDatabase.Refresh(ImportAssetOptions.Default);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        [UsedImplicitly]
        [AgentTool(
            "Visualize the validated UI by rendering it with stylesheets and panel settings. ",
            id: "Unity.GetUIAssetPreview",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_UITag)]
        internal static async Task<ImageOutput> GetUIAssetPreview(
            ToolExecutionContext context,
            [Parameter("File path of the saved UXML asset (e.g., Assets/UI/MyDocument.uxml). The asset must exist and be saved before calling this.")] string uxmlAssetPath,
            [Parameter("File path of an existing PanelSettings asset in the project. This must be a valid path. This is CANNOT be a placeholder or empty.")] string panelSettingsPath,
            [Parameter("The desired width of the preview image in pixels (default is 1920).")] int width = 1920,
            [Parameter("The desired height of the preview image in pixels (default is 1080).")] int height = 1080,
            [Parameter("Array of file paths for USS/TSS stylesheet assets to apply styling to the preview (optional). Example: [ \"Assets/Styles.uss\", \"Assets/Theme.tss\" ]")] string[] stylesheetPaths = null
        )
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive integer");

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive integer");

            if (string.IsNullOrEmpty(uxmlAssetPath))
                throw new ArgumentNullException(nameof(uxmlAssetPath), "UXML asset path cannot be null or empty");

            if (string.IsNullOrEmpty(panelSettingsPath))
                throw new ArgumentNullException(nameof(panelSettingsPath),  "PanelSettings path cannot be null or empty");

            // Verify the UXML asset exists
            if (!File.Exists(uxmlAssetPath))
                throw new ArgumentException($"UXML asset not found at path: {uxmlAssetPath}. Asset must be saved before generating preview.", nameof(uxmlAssetPath));

            return await GeneratePreviewInternal(panelSettingsPath, uxmlAssetPath, width, height, stylesheetPaths);
        }

        static async Task<ImageOutput> GeneratePreviewInternal(
            string panelSettingsPath,
            string uxmlAssetPath,
            int width,
            int height,
            params string[] stylesheetPaths)
        {
            // Load UXML asset
            var uxmlAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlAssetPath);
            if (!uxmlAsset)
                throw new ArgumentException("UXML asset not found", nameof(uxmlAssetPath));

            // Load PanelSettings asset
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);
            if (!panelSettings)
                throw new ArgumentException("PanelSettings not found", nameof(panelSettings));

            // Load stylesheet assets
            var styleSheets = new List<StyleSheet>();
            if (stylesheetPaths is {Length: > 0})
            {
                foreach (var sheetPath in stylesheetPaths)
                {
                    if (string.IsNullOrEmpty(sheetPath))
                        continue;

                    var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(sheetPath);

                    if (styleSheet == null)
                    {
                        InternalLog.LogWarning($"StyleSheet asset not found with path: {sheetPath}");
                        continue;
                    }

                    styleSheets.Add(styleSheet);
                }
            }

            var resultTexture = await GetUIDocumentPreview(
                uxmlAsset,
                width,
                height,
                panelSettings.themeStyleSheet,
                styleSheets.ToArray()
            );

            // Generate the output data
            var imageDescription = $"UI Preview {width}x{height} pixels";
            var previewImage = new ImageOutput(resultTexture, imageDescription, "UI Preview");

            InternalLog.Log($"UI Preview created successfully\n" +
                $"Resolution: {width}x{height}\n");

#if ASSISTANT_INTERNAL
            await SaveDebugTextureToDisk(resultTexture);
#endif
            
            // Clean up the temporary texture
            Object.DestroyImmediate(resultTexture);

            return previewImage;
        }
#pragma warning disable CS1998
#if ASSISTANT_INTERNAL
        [ToolPermissionIgnore]
        static async Task SaveDebugTextureToDisk(Texture2D texture)
        {
            // Save PNG to disk for debugging with timestamp
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            var projectTempDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp"));
            if (!Directory.Exists(projectTempDir))
                Directory.CreateDirectory(projectTempDir);
            var debugPreviewPath = Path.Combine(projectTempDir, $"UIPreview_Debug_{timestamp}.png");
            await File.WriteAllBytesAsync(debugPreviewPath, texture.EncodeToPNG());
            InternalLog.Log($"Debug PNG: {debugPreviewPath}");
        }
#endif
#pragma warning restore CS1998

        /// <summary>
        /// Renders a UIDocument to a Texture2D using internal UITK rendering APIs.
        /// </summary>
        /// <param name="visualTreeAsset"> The UXML VisualTreeAsset to render.</param>
        /// <param name="width"> The width of the resulting texture. Default is 1920.</param>
        /// <param name="height"> The height of the resulting texture. Default is 1080.</param>
        /// <param name="themeStyleSheet"> Optional ThemeStyleSheet to apply.</param>
        /// <param name="stylesheets"> Optional set of StyleSheets to apply.
        /// Do not include any StyleSheets that are already part of the ThemeStyleSheet or the VisualTreeAsset.</param>
        /// <returns> A Texture2D containing the rendered UI.</returns>
        internal static async Task<Texture2D> GetUIDocumentPreview(
            VisualTreeAsset visualTreeAsset,
            int width = 1920,
            int height = 1080,
            ThemeStyleSheet themeStyleSheet = null,
            params StyleSheet[] stylesheets)
        {
            RenderTexture rt = null;
            var oldActive = RenderTexture.active;
            var desc = new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0) {
                sRGB = QualitySettings.activeColorSpace == ColorSpace.Linear
            };

            rt = RenderTexture.GetTemporary(desc);
            RenderTexture.active = rt;

            var panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.clearColor = true;
            panelSettings.clearDepthStencil = true;
            panelSettings.targetTexture = rt;

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(width, height);

            if (themeStyleSheet)
                panelSettings.themeStyleSheet = themeStyleSheet;

            await Task.Yield(); // Ensure asynchronous context

            UIElementsUtils.RenderPanel(panelSettings, visualTreeAsset, stylesheets);

            Object.DestroyImmediate(panelSettings);

            var resultTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            resultTexture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            resultTexture.Apply();

            RenderTexture.active = oldActive;
            RenderTexture.ReleaseTemporary(rt);

            return resultTexture;
        }
    }
}
