using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry; // For Response class
using Becool.UnityMcpLens.Editor.ToolRegistry.Parameters;

#if UNITY_6000_0_OR_NEWER
using PhysicsMaterialType = UnityEngine.PhysicsMaterial;
using PhysicsMaterialCombine = UnityEngine.PhysicsMaterialCombine;
#else
using PhysicsMaterialType = UnityEngine.PhysicMaterial;
using PhysicsMaterialCombine = UnityEngine.PhysicMaterialCombine;
#endif

namespace Becool.UnityMcpLens.Editor.Tools
{
    /// <summary>
    /// Handles asset management operations within the Unity project.
    /// </summary>
    public static class ManageAsset
    {
        /// <summary>
        /// Human-readable description of the Unity.ManageAsset tool functionality and usage.
        /// </summary>
        public const string Title = "Manage assets";

        public const string Description = @"Performs asset operations (import, create, modify, delete, etc.) in Unity.

Args:
    Action: Operation to perform (e.g., 'Import', 'Create', 'Modify', 'Delete', 'Duplicate', 'Move', 'Rename', 'Search', 'GetInfo', 'CreateFolder', 'GetComponents').
    Path: Asset path (e.g., ""Assets/Materials/MyMaterial.mat"") or search scope.
    AssetType: Asset type (e.g., 'Material', 'Folder') - required for 'create'.
    Properties: Dictionary of properties for 'create'/'modify'.
        example properties for Material: {""color"": [1, 0, 0, 1], ""shader"": ""Standard""}.
        example properties for Texture: {""width"": 1024, ""height"": 1024, ""format"": ""RGBA32""}.
        example properties for PhysicsMaterial: {""bounciness"": 1.0, ""staticFriction"": 0.5, ""dynamicFriction"": 0.5}.
    Destination: Target path for 'duplicate'/'move'.
    SearchPattern: Search pattern (e.g., '*.prefab').
    Filter*: Filters for search (Type, Date).
    Page: Pagination for search.

Returns:
    A dictionary with operation results ('success', 'data', 'error').";
        // --- Main Handler ---

        /// <summary>
        /// Demonstrates typed parameter handling with automatic schema generation.
        /// The schema is auto-generated from the ManageAssetParams record type.
        /// </summary>
        /// <param name="params">Parameters containing the action to perform and relevant asset information.</param>
        /// <returns>A response object indicating success or failure with relevant asset data.</returns>
        [McpTool("Unity.ManageAsset", Description, Title, Groups = new[] { "core", "assets" }, EnabledByDefault = true)]
        public static object HandleCommand(ManageAssetParams @params)
        {
            if (@params == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            // Common parameters - now type-safe!
            string path = @params.Path;

            try
            {
                switch (@params.Action)
                {
                    case AssetAction.Import:
                        // Note: Unity typically auto-imports. This might re-import or configure import settings.
                        return ReimportAsset(path, @params.Properties);
                    case AssetAction.Create:
                        return CreateAssetTyped(@params);
                    case AssetAction.Modify:
                        return ModifyAsset(path, @params.Properties);
                    case AssetAction.Delete:
                        return DeleteAsset(path);
                    case AssetAction.Duplicate:
                        return DuplicateAsset(path, @params.Destination);
                    case AssetAction.Move: // Often same as rename if within Assets/
                    case AssetAction.Rename:
                        return MoveOrRenameAsset(path, @params.Destination);
                    case AssetAction.Search:
                        return SearchAssetsTyped(@params);
                    case AssetAction.GetInfo:
                        return GetAssetInfo(path, @params.GeneratePreview);
                    case AssetAction.CreateFolder: // Added specific action for clarity
                        return CreateFolder(path);
                    case AssetAction.GetComponents:
                        return GetComponentsFromAsset(path);

                    default:
                        return Response.Error($"Unknown action: '{@params.Action}'. Valid actions are: {string.Join(", ", Enum.GetNames(typeof(AssetAction)))}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManageAsset] Action '{@params.Action}' failed for path '{path}': {e}");
                return Response.Error(
                    $"Internal error processing action '{@params.Action}' on '{path}': {e.Message}"
                );
            }
        }

        // --- Action Implementations ---

        static object ReimportAsset(string path, JObject properties)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for reimport.");
            string fullPath = SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return Response.Error($"Asset not found at path: {fullPath}");

            try
            {
                // TODO: Apply importer properties before reimporting?
                // This is complex as it requires getting the AssetImporter, casting it,
                // applying properties via reflection or specific methods, saving, then reimporting.
                if (properties != null && properties.HasValues)
                {
                    Debug.LogWarning(
                        "[ManageAsset.Reimport] Modifying importer properties before reimport is not fully implemented yet."
                    );
                    // AssetImporter importer = AssetImporter.GetAtPath(fullPath);
                    // if (importer != null) { /* Apply properties */ AssetDatabase.WriteImportSettingsIfDirty(fullPath); }
                }

                AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);
                // AssetDatabase.Refresh(); // Usually ImportAsset handles refresh
                return Response.Success($"Asset '{fullPath}' reimported.", GetAssetData(fullPath));
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to reimport asset '{fullPath}': {e.Message}");
            }
        }

        static object CreateAssetTyped(ManageAssetParams @params)
        {
            string path = @params.Path;
            string assetType = @params.AssetType;
            JObject properties = @params.Properties;

            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for create.");
            if (string.IsNullOrEmpty(assetType))
                return Response.Error("'assetType' is required for create.");

            string fullPath = SanitizeAssetPath(path);
            string directory = Path.GetDirectoryName(fullPath);

            // Ensure directory exists
            if (!Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), directory)))
            {
                Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), directory));
                AssetDatabase.Refresh(); // Make sure Unity knows about the new folder
            }

            if (AssetExists(fullPath))
                return Response.Error($"Asset already exists at path: {fullPath}");

            try
            {
                UnityEngine.Object newAsset = null;
                string lowerAssetType = assetType.ToLowerInvariant();

                // Handle common asset types
                if (lowerAssetType == "folder")
                {
                    return CreateFolder(path); // Use dedicated method
                }
                else if (lowerAssetType == "material")
                {
                    // Prefer provided shader; fall back to common pipelines
                    var requested = properties?["shader"]?.ToString();
                    Shader shader =
                        (!string.IsNullOrEmpty(requested) ? Shader.Find(requested) : null)
                        ?? Shader.Find("Universal Render Pipeline/Lit")
                        ?? Shader.Find("HDRP/Lit")
                        ?? Shader.Find("Standard")
                        ?? Shader.Find("Unlit/Color");
                    if (shader == null)
                        return Response.Error($"Could not find a suitable shader (requested: '{requested ?? "none"}').");

                    var mat = new Material(shader);
                    if (properties != null)
                        ApplyMaterialProperties(mat, properties);
                    AssetDatabase.CreateAsset(mat, fullPath);
                    newAsset = mat;
                }
                else if (lowerAssetType == "physicsmaterial")
                {
                    PhysicsMaterialType pmat = new PhysicsMaterialType();
                    if (properties != null)
                        ApplyPhysicsMaterialProperties(pmat, properties);
                    AssetDatabase.CreateAsset(pmat, fullPath);
                    newAsset = pmat;
                }
                else if (lowerAssetType == "scriptableobject")
                {
                    string scriptClassName = properties?["scriptClass"]?.ToString();
                    if (string.IsNullOrEmpty(scriptClassName))
                        return Response.Error(
                            "'scriptClass' property required when creating ScriptableObject asset."
                        );

                    Type scriptType = ComponentResolver.TryResolve(scriptClassName, out var resolvedType, out var error) ? resolvedType : null;
                    if (
                        scriptType == null
                        || !typeof(ScriptableObject).IsAssignableFrom(scriptType)
                    )
                    {
                        var reason = scriptType == null
                            ? (string.IsNullOrEmpty(error) ? "Type not found." : error)
                            : "Type found but does not inherit from ScriptableObject.";
                        return Response.Error($"Script class '{scriptClassName}' invalid: {reason}");
                    }

                    ScriptableObject so = ScriptableObject.CreateInstance(scriptType);
                    // TODO: Apply properties from JObject to the ScriptableObject instance?
                    AssetDatabase.CreateAsset(so, fullPath);
                    newAsset = so;
                }
                else if (lowerAssetType == "prefab")
                {
                    // Creating prefabs usually involves saving an existing GameObject hierarchy.
                    // A common pattern is to create an empty GameObject, configure it, and then save it.
                    return Response.Error(
                        "Creating prefabs programmatically usually requires a source GameObject. Use Unity.ManageGameObject to create/configure, then save as prefab via a separate mechanism or future enhancement."
                    );
                    // Example (conceptual):
                    // GameObject source = GameObject.Find(properties["sourceGameObject"].ToString());
                    // if(source != null) PrefabUtility.SaveAsPrefabAsset(source, fullPath);
                }
                // TODO: Add more asset types (Animation Controller, Scene, etc.)
                else
                {
                    // Generic creation attempt (might fail or create empty files)
                    // For some types, just creating the file might be enough if Unity imports it.
                    // File.Create(Path.Combine(Directory.GetCurrentDirectory(), fullPath)).Close();
                    // AssetDatabase.ImportAsset(fullPath); // Let Unity try to import it
                    // newAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath);
                    return Response.Error(
                        $"Creation for asset type '{assetType}' is not explicitly supported yet. Supported: Folder, Material, ScriptableObject."
                    );
                }

                if (
                    newAsset == null
                    && !Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), fullPath))
                ) // Check if it wasn't a folder and asset wasn't created
                {
                    return Response.Error(
                        $"Failed to create asset '{assetType}' at '{fullPath}'. See logs for details."
                    );
                }

                AssetDatabase.SaveAssets();
                // AssetDatabase.Refresh(); // CreateAsset often handles refresh
                return Response.Success(
                    $"Asset '{fullPath}' created successfully.",
                    GetAssetData(fullPath)
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to create asset at '{fullPath}': {e.Message}");
            }
        }

        static object CreateFolder(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for create_folder.");
            string fullPath = SanitizeAssetPath(path);
            string parentDir = Path.GetDirectoryName(fullPath);
            string folderName = Path.GetFileName(fullPath);

            if (AssetExists(fullPath))
            {
                // Check if it's actually a folder already
                if (AssetDatabase.IsValidFolder(fullPath))
                {
                    return Response.Success(
                        $"Folder already exists at path: {fullPath}",
                        GetAssetData(fullPath)
                    );
                }
                else
                {
                    return Response.Error(
                        $"An asset (not a folder) already exists at path: {fullPath}"
                    );
                }
            }

            try
            {
                // Ensure parent exists
                if (!string.IsNullOrEmpty(parentDir) && !AssetDatabase.IsValidFolder(parentDir))
                {
                    // Recursively create parent folders if needed (AssetDatabase handles this internally)
                    // Or we can do it manually: Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), parentDir)); AssetDatabase.Refresh();
                }

                string guid = AssetDatabase.CreateFolder(parentDir, folderName);
                if (string.IsNullOrEmpty(guid))
                {
                    return Response.Error(
                        $"Failed to create folder '{fullPath}'. Check logs and permissions."
                    );
                }

                // AssetDatabase.Refresh(); // CreateFolder usually handles refresh
                return Response.Success(
                    $"Folder '{fullPath}' created successfully.",
                    GetAssetData(fullPath)
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Failed to create folder '{fullPath}': {e.Message}");
            }
        }

        static object ModifyAsset(string path, JObject properties)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for modify.");
            if (properties == null || !properties.HasValues)
                return Response.Error("'properties' are required for modify.");

            string fullPath = SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return Response.Error($"Asset not found at path: {fullPath}");

            try
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    fullPath
                );
                if (asset == null)
                    return Response.Error($"Failed to load asset at path: {fullPath}");

                bool modified = false; // Flag to track if any changes were made

                // --- NEW: Handle GameObject / Prefab Component Modification ---
                if (asset is GameObject gameObject)
                {
                    // Iterate through the properties JSON: keys are component names, values are properties objects for that component
                    foreach (var prop in properties.Properties())
                    {
                        string componentName = prop.Name; // e.g., "Collectible"
                        // Check if the value associated with the component name is actually an object containing properties
                        if (
                            prop.Value is JObject componentProperties
                            && componentProperties.HasValues
                        ) // e.g., {"bobSpeed": 2.0}
                        {
                            // Resolve component type via ComponentResolver, then fetch by Type
                            Component targetComponent = null;
                            bool resolved = ComponentResolver.TryResolve(componentName, out var compType, out var compError);
                            if (resolved)
                            {
                                targetComponent = gameObject.GetComponent(compType);
                            }

                            // Only warn about resolution failure if component also not found
                            if (targetComponent == null && !resolved)
                            {
                                Debug.LogWarning(
                                    $"[ManageAsset.ModifyAsset] Failed to resolve component '{componentName}' on '{gameObject.name}': {compError}"
                                );
                            }

                            if (targetComponent != null)
                            {
                                // Apply the nested properties (e.g., bobSpeed) to the found component instance
                                // Use |= to ensure 'modified' becomes true if any component is successfully modified
                                modified |= ApplyObjectProperties(
                                    targetComponent,
                                    componentProperties
                                );
                            }
                            else
                            {
                                // Log a warning if a specified component couldn't be found
                                Debug.LogWarning(
                                    $"[ManageAsset.ModifyAsset] Component '{componentName}' not found on GameObject '{gameObject.name}' in asset '{fullPath}'. Skipping modification for this component."
                                );
                            }
                        }
                        else
                        {
                            // Log a warning if the structure isn't {"ComponentName": {"prop": value}}
                            // We could potentially try to apply this property directly to the GameObject here if needed,
                            // but the primary goal is component modification.
                            Debug.LogWarning(
                                $"[ManageAsset.ModifyAsset] Property '{prop.Name}' for GameObject modification should have a JSON object value containing component properties. Value was: {prop.Value.Type}. Skipping."
                            );
                        }
                    }
                    // Note: 'modified' is now true if ANY component property was successfully changed.
                }
                // --- End NEW ---

                // --- Existing logic for other asset types (now as else-if) ---
                // Example: Modifying a Material
                else if (asset is Material material)
                {
                    // Apply properties directly to the material. If this modifies, it sets modified=true.
                    // Use |= in case the asset was already marked modified by previous logic (though unlikely here)
                    modified |= ApplyMaterialProperties(material, properties);
                }
                // Example: Modifying a ScriptableObject
                else if (asset is ScriptableObject so)
                {
                    // Apply properties directly to the ScriptableObject.
                    modified |= ApplyObjectProperties(so, properties); // General helper
                }
                // Example: Modifying TextureImporter settings
                else if (asset is Texture)
                {
                    AssetImporter importer = AssetImporter.GetAtPath(fullPath);
                    if (importer is TextureImporter textureImporter)
                    {
                        bool importerModified = ApplyObjectProperties(textureImporter, properties);
                        if (importerModified)
                        {
                            // Importer settings need saving and reimporting
                            AssetDatabase.WriteImportSettingsIfDirty(fullPath);
                            AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate); // Reimport to apply changes
                            modified = true; // Mark overall operation as modified
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Could not get TextureImporter for {fullPath}.");
                    }
                }
                // TODO: Add modification logic for other common asset types (Models, AudioClips importers, etc.)
                else // Fallback for other asset types OR direct properties on non-GameObject assets
                {
                    // This block handles non-GameObject/Material/ScriptableObject/Texture assets.
                    // Attempts to apply properties directly to the asset itself.
                    Debug.LogWarning(
                        $"[ManageAsset.ModifyAsset] Asset type '{asset.GetType().Name}' at '{fullPath}' is not explicitly handled for component modification. Attempting generic property setting on the asset itself."
                    );
                    modified |= ApplyObjectProperties(asset, properties);
                }
                // --- End Existing Logic ---

                // Check if any modification happened (either component or direct asset modification)
                if (modified)
                {
                    // Mark the asset as dirty (important for prefabs/SOs) so Unity knows to save it.
                    EditorUtility.SetDirty(asset);
                    // Save all modified assets to disk.
                    AssetDatabase.SaveAssets();
                    // Refresh might be needed in some edge cases, but SaveAssets usually covers it.
                    // AssetDatabase.Refresh();
                    return Response.Success(
                        $"Asset '{fullPath}' modified successfully.",
                        GetAssetData(fullPath)
                    );
                }
                else
                {
                    // If no changes were made (e.g., component not found, property names incorrect, value unchanged), return a success message indicating nothing changed.
                    return Response.Success(
                        $"No applicable or modifiable properties found for asset '{fullPath}'. Check component names, property names, and values.",
                        GetAssetData(fullPath)
                    );
                    // Previous message: return Response.Success($"No applicable properties found to modify for asset '{fullPath}'.", GetAssetData(fullPath));
                }
            }
            catch (Exception e)
            {
                // Log the detailed error internally
                Debug.LogError($"[ManageAsset] Action 'modify' failed for path '{path}': {e}");
                // Return a user-friendly error message
                return Response.Error($"Failed to modify asset '{fullPath}': {e.Message}");
            }
        }

        static object DeleteAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for delete.");
            string fullPath = SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return Response.Error($"Asset not found at path: {fullPath}");

            try
            {
                bool success = AssetDatabase.DeleteAsset(fullPath);
                if (success)
                {
                    // AssetDatabase.Refresh(); // DeleteAsset usually handles refresh
                    return Response.Success($"Asset '{fullPath}' deleted successfully.");
                }
                else
                {
                    // This might happen if the file couldn't be deleted (e.g., locked)
                    return Response.Error(
                        $"Failed to delete asset '{fullPath}'. Check logs or if the file is locked."
                    );
                }
            }
            catch (Exception e)
            {
                return Response.Error($"Error deleting asset '{fullPath}': {e.Message}");
            }
        }

        static object DuplicateAsset(string path, string destinationPath)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for duplicate.");

            string sourcePath = SanitizeAssetPath(path);
            if (!AssetExists(sourcePath))
                return Response.Error($"Source asset not found at path: {sourcePath}");

            string destPath;
            if (string.IsNullOrEmpty(destinationPath))
            {
                // Generate a unique path if destination is not provided
                destPath = AssetDatabase.GenerateUniqueAssetPath(sourcePath);
            }
            else
            {
                destPath = SanitizeAssetPath(destinationPath);
                if (AssetExists(destPath))
                    return Response.Error($"Asset already exists at destination path: {destPath}");
                // Ensure destination directory exists
                EnsureDirectoryExists(Path.GetDirectoryName(destPath));
            }

            try
            {
                bool success = AssetDatabase.CopyAsset(sourcePath, destPath);
                if (success)
                {
                    // AssetDatabase.Refresh();
                    return Response.Success(
                        $"Asset '{sourcePath}' duplicated to '{destPath}'.",
                        GetAssetData(destPath)
                    );
                }
                else
                {
                    return Response.Error(
                        $"Failed to duplicate asset from '{sourcePath}' to '{destPath}'."
                    );
                }
            }
            catch (Exception e)
            {
                return Response.Error($"Error duplicating asset '{sourcePath}': {e.Message}");
            }
        }

        static object MoveOrRenameAsset(string path, string destinationPath)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for move/rename.");
            if (string.IsNullOrEmpty(destinationPath))
                return Response.Error("'destination' path is required for move/rename.");

            string sourcePath = SanitizeAssetPath(path);
            string destPath = SanitizeAssetPath(destinationPath);

            if (!AssetExists(sourcePath))
                return Response.Error($"Source asset not found at path: {sourcePath}");
            if (AssetExists(destPath))
                return Response.Error(
                    $"An asset already exists at the destination path: {destPath}"
                );

            // Ensure destination directory exists
            EnsureDirectoryExists(Path.GetDirectoryName(destPath));

            try
            {
                // Validate will return an error string if failed, null if successful
                string error = AssetDatabase.ValidateMoveAsset(sourcePath, destPath);
                if (!string.IsNullOrEmpty(error))
                {
                    return Response.Error(
                        $"Failed to move/rename asset from '{sourcePath}' to '{destPath}': {error}"
                    );
                }

                string guid = AssetDatabase.MoveAsset(sourcePath, destPath);
                if (!string.IsNullOrEmpty(guid)) // MoveAsset returns the new GUID on success
                {
                    // AssetDatabase.Refresh(); // MoveAsset usually handles refresh
                    return Response.Success(
                        $"Asset moved/renamed from '{sourcePath}' to '{destPath}'.",
                        GetAssetData(destPath)
                    );
                }
                else
                {
                    // This case might not be reachable if ValidateMoveAsset passes, but good to have
                    return Response.Error(
                        $"MoveAsset call failed unexpectedly for '{sourcePath}' to '{destPath}'."
                    );
                }
            }
            catch (Exception e)
            {
                return Response.Error($"Error moving/renaming asset '{sourcePath}': {e.Message}");
            }
        }

        static object SearchAssetsTyped(ManageAssetParams @params)
        {
            string searchPattern = @params.SearchPattern;
            string filterType = @params.FilterType;
            string pathScope = @params.Path; // Use path as folder scope
            string filterDateAfterStr = @params.FilterDate;
            int pageSize = @params.PageSize; // Default set in record
            int pageNumber = @params.PageNumber; // Default set in record
            bool generatePreview = @params.GeneratePreview;

            List<string> searchFilters = new List<string>();
            if (!string.IsNullOrEmpty(searchPattern))
                searchFilters.Add(searchPattern);
            if (!string.IsNullOrEmpty(filterType))
                searchFilters.Add($"t:{filterType}");

            string[] folderScope = null;
            if (!string.IsNullOrEmpty(pathScope))
            {
                folderScope = new string[] { SanitizeAssetPath(pathScope) };
                if (!AssetDatabase.IsValidFolder(folderScope[0]))
                {
                    // Maybe the user provided a file path instead of a folder?
                    // We could search in the containing folder, or return an error.
                    Debug.LogWarning(
                        $"Search path '{folderScope[0]}' is not a valid folder. Searching entire project."
                    );
                    folderScope = null; // Search everywhere if path isn't a folder
                }
            }

            DateTime? filterDateAfter = null;
            if (!string.IsNullOrEmpty(filterDateAfterStr))
            {
                if (
                    DateTime.TryParse(
                        filterDateAfterStr,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out DateTime parsedDate
                    )
                )
                {
                    filterDateAfter = parsedDate;
                }
                else
                {
                    Debug.LogWarning(
                        $"Could not parse filterDateAfter: '{filterDateAfterStr}'. Expected ISO 8601 format."
                    );
                }
            }

            try
            {
                string[] guids = AssetDatabase.FindAssets(
                    string.Join(" ", searchFilters),
                    folderScope
                );
                List<object> results = new List<object>();
                int totalFound = 0;

                foreach (string guid in guids)
                {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(assetPath))
                        continue;

                    // Apply date filter if present
                    if (filterDateAfter.HasValue)
                    {
                        DateTime lastWriteTime = File.GetLastWriteTimeUtc(
                            Path.Combine(Directory.GetCurrentDirectory(), assetPath)
                        );
                        if (lastWriteTime <= filterDateAfter.Value)
                        {
                            continue; // Skip assets older than or equal to the filter date
                        }
                    }

                    totalFound++; // Count matching assets before pagination
                    results.Add(GetAssetData(assetPath, generatePreview));
                }

                // Apply pagination
                int startIndex = (pageNumber - 1) * pageSize;
                var pagedResults = results.Skip(startIndex).Take(pageSize).ToList();

                return Response.Success(
                    $"Found {totalFound} asset(s). Returning page {pageNumber} ({pagedResults.Count} assets).",
                    new
                    {
                        totalAssets = totalFound,
                        pageSize = pageSize,
                        pageNumber = pageNumber,
                        assets = pagedResults,
                    }
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Error searching assets: {e.Message}");
            }
        }

        static object GetAssetInfo(string path, bool generatePreview)
        {
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for get_info.");
            string fullPath = SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return Response.Error($"Asset not found at path: {fullPath}");

            try
            {
                return Response.Success(
                    "Asset info retrieved.",
                    GetAssetData(fullPath, generatePreview)
                );
            }
            catch (Exception e)
            {
                return Response.Error($"Error getting info for asset '{fullPath}': {e.Message}");
            }
        }

        /// <summary>
        /// Retrieves components attached to a GameObject asset (like a Prefab).
        /// </summary>
        /// <param name="path">The asset path of the GameObject or Prefab.</param>
        /// <returns>A response object containing a list of component type names or an error.</returns>
        static object GetComponentsFromAsset(string path)
        {
            // 1. Validate input path
            if (string.IsNullOrEmpty(path))
                return Response.Error("'path' is required for get_components.");

            // 2. Sanitize and check existence
            string fullPath = SanitizeAssetPath(path);
            if (!AssetExists(fullPath))
                return Response.Error($"Asset not found at path: {fullPath}");

            try
            {
                // 3. Load the asset
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                    fullPath
                );
                if (asset == null)
                    return Response.Error($"Failed to load asset at path: {fullPath}");

                // 4. Check if it's a GameObject (Prefabs load as GameObjects)
                GameObject gameObject = asset as GameObject;
                if (gameObject == null)
                {
                    // Also check if it's *directly* a Component type (less common for primary assets)
                    Component componentAsset = asset as Component;
                    if (componentAsset != null)
                    {
                        // If the asset itself *is* a component, maybe return just its info?
                        // This is an edge case. Let's stick to GameObjects for now.
                        return Response.Error(
                            $"Asset at '{fullPath}' is a Component ({asset.GetType().FullName}), not a GameObject. Components are typically retrieved *from* a GameObject."
                        );
                    }
                    return Response.Error(
                        $"Asset at '{fullPath}' is not a GameObject (Type: {asset.GetType().FullName}). Cannot get components from this asset type."
                    );
                }

                // 5. Get components
                Component[] components = gameObject.GetComponents<Component>();

                // 6. Format component data
                List<object> componentList = components
                    .Select(comp => new
                    {
                        typeName = comp.GetType().FullName,
                        instanceID = UnityApiAdapter.GetObjectId(comp),
                        // TODO: Add more component-specific details here if needed in the future?
                        //       Requires reflection or specific handling per component type.
                    })
                    .ToList<object>(); // Explicit cast for clarity if needed

                // 7. Return success response
                return Response.Success(
                    $"Found {componentList.Count} component(s) on asset '{fullPath}'.",
                    componentList
                );
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[ManageAsset.GetComponentsFromAsset] Error getting components for '{fullPath}': {e}"
                );
                return Response.Error(
                    $"Error getting components for asset '{fullPath}': {e.Message}"
                );
            }
        }

        // --- Internal Helpers ---

        /// <summary>
        /// Ensures the asset path starts with "Assets/".
        /// </summary>
        static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            path = path.Replace('\\', '/'); // Normalize separators
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                return "Assets/" + path.TrimStart('/');
            }
            return path;
        }

        /// <summary>
        /// Checks if an asset exists at the specified path.
        /// </summary>
        /// <param name="assetPath">The asset path to check.</param>
        /// <returns>True if the asset exists; otherwise, false.</returns>
        public static bool AssetPathExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;

            // Works for both "Assets/..." and "Packages/..."
            if (AssetDatabase.IsValidFolder(assetPath)) return true;              // folder
            return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)); // file/asset
        }

        /// <summary>
        /// Checks if an asset exists at the given path (file or folder).
        /// </summary>
        static bool AssetExists(string sanitizedPath)
        {
            // AssetDatabase APIs are generally preferred over raw File/Directory checks for assets.
            // Check if it's a known asset GUID.
#if UNITY_2022_0_OR_NEWER
            var assetPathExists = AssetDatabase.AssetPathExists(sanitizedPath);
#else
            var assetPathExists = AssetPathExists(sanitizedPath);
#endif
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sanitizedPath)) && assetPathExists)
            {
                return true;
            }
            // AssetPathToGUID might not work for newly created folders not yet refreshed.
            // Check directory explicitly for folders.
            if (Directory.Exists(Path.Combine(Directory.GetCurrentDirectory(), sanitizedPath)))
            {
                // Check if it's considered a *valid* folder by Unity
                return AssetDatabase.IsValidFolder(sanitizedPath);
            }
            // Check file existence for non-folder assets.
            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), sanitizedPath)))
            {
                return true; // Assume if file exists, it's an asset or will be imported
            }

            return false;
            // Alternative: return !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(sanitizedPath));
        }

        /// <summary>
        /// Ensures the directory for a given asset path exists, creating it if necessary.
        /// </summary>
        static void EnsureDirectoryExists(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return;
            string fullDirPath = Path.Combine(Directory.GetCurrentDirectory(), directoryPath);
            if (!Directory.Exists(fullDirPath))
            {
                Directory.CreateDirectory(fullDirPath);
                AssetDatabase.Refresh(); // Let Unity know about the new folder
            }
        }

        /// <summary>
        /// Applies properties from JObject to a Material.
        /// </summary>
        static bool ApplyMaterialProperties(Material mat, JObject properties)
        {
            if (mat == null || properties == null)
                return false;
            bool modified = false;

            // Example: Set shader
            if (properties["shader"]?.Type == JTokenType.String)
            {
                Shader newShader = Shader.Find(properties["shader"].ToString());
                if (newShader != null && mat.shader != newShader)
                {
                    mat.shader = newShader;
                    modified = true;
                }
            }
            // Example: Set color property
            if (properties["color"] is JObject colorProps)
            {
                string propName = colorProps["name"]?.ToString() ?? "_Color"; // Default main color
                if (colorProps["value"] is JArray colArr && colArr.Count >= 3)
                {
                    try
                    {
                        Color newColor = new Color(
                            colArr[0].ToObject<float>(),
                            colArr[1].ToObject<float>(),
                            colArr[2].ToObject<float>(),
                            colArr.Count > 3 ? colArr[3].ToObject<float>() : 1.0f
                        );
                        if (mat.HasProperty(propName) && mat.GetColor(propName) != newColor)
                        {
                            mat.SetColor(propName, newColor);
                            modified = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"Error parsing color property '{propName}': {ex.Message}"
                        );
                    }
                }
            } else if (properties["color"] is JArray colorArr) //Use color now with examples set in Unity.ManageAsset.py
            {
                string propName =  "_Color";
                try {
                    if (colorArr.Count >= 3)
                    {
                        Color newColor = new Color(
                            colorArr[0].ToObject<float>(),
                            colorArr[1].ToObject<float>(),
                            colorArr[2].ToObject<float>(),
                            colorArr.Count > 3 ? colorArr[3].ToObject<float>() : 1.0f
                        );
                        if (mat.HasProperty(propName) && mat.GetColor(propName) != newColor)
                        {
                            mat.SetColor(propName, newColor);
                            modified = true;
                        }
                    }
                }
                catch (Exception ex) {
                    Debug.LogWarning(
                        $"Error parsing color property '{propName}': {ex.Message}"
                    );
                }
            }
            // Example: Set float property
            if (properties["float"] is JObject floatProps)
            {
                string propName = floatProps["name"]?.ToString();
                if (
                    !string.IsNullOrEmpty(propName) &&
                    (floatProps["value"]?.Type == JTokenType.Float || floatProps["value"]?.Type == JTokenType.Integer)
                )
                {
                    try
                    {
                        float newVal = floatProps["value"].ToObject<float>();
                        if (mat.HasProperty(propName) && mat.GetFloat(propName) != newVal)
                        {
                            mat.SetFloat(propName, newVal);
                            modified = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"Error parsing float property '{propName}': {ex.Message}"
                        );
                    }
                }
            }
            // Example: Set texture property
            if (properties["texture"] is JObject texProps)
            {
                string propName = texProps["name"]?.ToString() ?? "_MainTex"; // Default main texture
                string texPath = texProps["path"]?.ToString();
                if (!string.IsNullOrEmpty(texPath))
                {
                    Texture newTex = AssetDatabase.LoadAssetAtPath<Texture>(
                        SanitizeAssetPath(texPath)
                    );
                    if (
                        newTex != null
                        && mat.HasProperty(propName)
                        && mat.GetTexture(propName) != newTex
                    )
                    {
                        mat.SetTexture(propName, newTex);
                        modified = true;
                    }
                    else if (newTex == null)
                    {
                        Debug.LogWarning($"Texture not found at path: {texPath}");
                    }
                }
            }

            // TODO: Add handlers for other property types (Vectors, Ints, Keywords, RenderQueue, etc.)
            return modified;
        }

        /// <summary>
        ///  Applies properties from JObject to a PhysicsMaterial.
        /// </summary>
        static bool ApplyPhysicsMaterialProperties(PhysicsMaterialType pmat, JObject properties)
        {
            if (pmat == null || properties == null)
                return false;
            bool modified = false;

            // Example: Set dynamic friction
            if (properties["dynamicFriction"]?.Type == JTokenType.Float)
            {
                float dynamicFriction = properties["dynamicFriction"].ToObject<float>();
                pmat.dynamicFriction = dynamicFriction;
                modified = true;
            }

            // Example: Set static friction
            if (properties["staticFriction"]?.Type == JTokenType.Float)
            {
                float staticFriction = properties["staticFriction"].ToObject<float>();
                pmat.staticFriction = staticFriction;
                modified = true;
            }

            // Example: Set bounciness
            if (properties["bounciness"]?.Type == JTokenType.Float)
            {
                float bounciness = properties["bounciness"].ToObject<float>();
                pmat.bounciness = bounciness;
                modified = true;
            }

            List<String> averageList = new List<String> { "ave", "Ave", "average", "Average" };
            List<String> multiplyList = new List<String> { "mul", "Mul", "mult", "Mult", "multiply", "Multiply" };
            List<String> minimumList = new List<String> { "min", "Min", "minimum", "Minimum" };
            List<String> maximumList = new List<String> { "max", "Max", "maximum", "Maximum" };

            // Example: Set friction combine
            if (properties["frictionCombine"]?.Type == JTokenType.String)
            {
                string frictionCombine = properties["frictionCombine"].ToString();
                if (averageList.Contains(frictionCombine))
                    pmat.frictionCombine = PhysicsMaterialCombine.Average;
                else if (multiplyList.Contains(frictionCombine))
                    pmat.frictionCombine = PhysicsMaterialCombine.Multiply;
                else if (minimumList.Contains(frictionCombine))
                    pmat.frictionCombine = PhysicsMaterialCombine.Minimum;
                else if (maximumList.Contains(frictionCombine))
                    pmat.frictionCombine = PhysicsMaterialCombine.Maximum;
                modified = true;
            }

            // Example: Set bounce combine
            if (properties["bounceCombine"]?.Type == JTokenType.String)
            {
                string bounceCombine = properties["bounceCombine"].ToString();
                if (averageList.Contains(bounceCombine))
                    pmat.bounceCombine = PhysicsMaterialCombine.Average;
                else if (multiplyList.Contains(bounceCombine))
                    pmat.bounceCombine = PhysicsMaterialCombine.Multiply;
                else if (minimumList.Contains(bounceCombine))
                    pmat.bounceCombine = PhysicsMaterialCombine.Minimum;
                else if (maximumList.Contains(bounceCombine))
                    pmat.bounceCombine = PhysicsMaterialCombine.Maximum;
                modified = true;
            }

            return modified;
        }

        /// <summary>
        /// Generic helper to set properties on any UnityEngine.Object using reflection.
        /// </summary>
        static bool ApplyObjectProperties(UnityEngine.Object target, JObject properties)
        {
            if (target == null || properties == null)
                return false;
            bool modified = false;
            Type type = target.GetType();

            foreach (var prop in properties.Properties())
            {
                string propName = prop.Name;
                JToken propValue = prop.Value;
                if (SetPropertyOrField(target, propName, propValue, type))
                {
                    modified = true;
                }
            }
            return modified;
        }

        /// <summary>
        /// Helper to set a property or field via reflection, handling basic types and Unity objects.
        /// </summary>
        static bool SetPropertyOrField(
            object target,
            string memberName,
            JToken value,
            Type type = null
        )
        {
            type = type ?? target.GetType();
            System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.IgnoreCase;

            try
            {
                System.Reflection.PropertyInfo propInfo = type.GetProperty(memberName, flags);
                if (propInfo != null && propInfo.CanWrite)
                {
                    object convertedValue = ConvertJTokenToType(value, propInfo.PropertyType);
                    if (
                        convertedValue != null
                        && !Equals(propInfo.GetValue(target), convertedValue)
                    )
                    {
                        propInfo.SetValue(target, convertedValue);
                        return true;
                    }
                }
                else
                {
                    System.Reflection.FieldInfo fieldInfo = type.GetField(memberName, flags);
                    if (fieldInfo != null)
                    {
                        object convertedValue = ConvertJTokenToType(value, fieldInfo.FieldType);
                        if (
                            convertedValue != null
                            && !Equals(fieldInfo.GetValue(target), convertedValue)
                        )
                        {
                            fieldInfo.SetValue(target, convertedValue);
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SetPropertyOrField] Failed to set '{memberName}' on {type.Name}: {ex.Message}"
                );
            }
            return false;
        }

        /// <summary>
        /// Simple JToken to Type conversion for common Unity types and primitives.
        /// </summary>
        static object ConvertJTokenToType(JToken token, Type targetType)
        {
            try
            {
                if (token == null || token.Type == JTokenType.Null)
                    return null;

                if (targetType == typeof(string))
                    return token.ToObject<string>();
                if (targetType == typeof(int))
                    return token.ToObject<int>();
                if (targetType == typeof(float))
                    return token.ToObject<float>();
                if (targetType == typeof(bool))
                    return token.ToObject<bool>();
                if (targetType == typeof(Vector2) && token is JArray arrV2 && arrV2.Count == 2)
                    return new Vector2(arrV2[0].ToObject<float>(), arrV2[1].ToObject<float>());
                if (targetType == typeof(Vector3) && token is JArray arrV3 && arrV3.Count == 3)
                    return new Vector3(
                        arrV3[0].ToObject<float>(),
                        arrV3[1].ToObject<float>(),
                        arrV3[2].ToObject<float>()
                    );
                if (targetType == typeof(Vector4) && token is JArray arrV4 && arrV4.Count == 4)
                    return new Vector4(
                        arrV4[0].ToObject<float>(),
                        arrV4[1].ToObject<float>(),
                        arrV4[2].ToObject<float>(),
                        arrV4[3].ToObject<float>()
                    );
                if (targetType == typeof(Quaternion) && token is JArray arrQ && arrQ.Count == 4)
                    return new Quaternion(
                        arrQ[0].ToObject<float>(),
                        arrQ[1].ToObject<float>(),
                        arrQ[2].ToObject<float>(),
                        arrQ[3].ToObject<float>()
                    );
                if (targetType == typeof(Color) && token is JArray arrC && arrC.Count >= 3) // Allow RGB or RGBA
                    return new Color(
                        arrC[0].ToObject<float>(),
                        arrC[1].ToObject<float>(),
                        arrC[2].ToObject<float>(),
                        arrC.Count > 3 ? arrC[3].ToObject<float>() : 1.0f
                    );
                if (targetType.IsEnum)
                    return Enum.Parse(targetType, token.ToString(), true); // Case-insensitive enum parsing

                // Handle loading Unity Objects (Materials, Textures, etc.) by path
                if (
                    typeof(UnityEngine.Object).IsAssignableFrom(targetType)
                    && token.Type == JTokenType.String
                )
                {
                    string assetPath = SanitizeAssetPath(token.ToString());
                    UnityEngine.Object loadedAsset = AssetDatabase.LoadAssetAtPath(
                        assetPath,
                        targetType
                    );
                    if (loadedAsset == null)
                    {
                        Debug.LogWarning(
                            $"[ConvertJTokenToType] Could not load asset of type {targetType.Name} from path: {assetPath}"
                        );
                    }
                    return loadedAsset;
                }

                // Fallback: Try direct conversion (might work for other simple value types)
                return token.ToObject(targetType);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[ConvertJTokenToType] Could not convert JToken '{token}' (type {token.Type}) to type '{targetType.Name}': {ex.Message}"
                );
                return null;
            }
        }


        // --- Data Serialization ---

        /// <summary>
        /// Creates a serializable representation of an asset.
        /// </summary>
        static object GetAssetData(string path, bool generatePreview = false)
        {
            if (string.IsNullOrEmpty(path) || !AssetExists(path))
                return null;

            string guid = AssetDatabase.AssetPathToGUID(path);
            Type assetType = AssetDatabase.GetMainAssetTypeAtPath(path);
            UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            string previewBase64 = null;
            int previewWidth = 0;
            int previewHeight = 0;

            if (generatePreview && asset != null)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(asset);

                if (preview != null)
                {
                    try
                    {
                        // Ensure texture is readable for EncodeToPNG
                        // Creating a temporary readable copy is safer
                        RenderTexture rt = null;
                        Texture2D readablePreview = null;
                        RenderTexture previous = RenderTexture.active;
                        try
                        {
                            rt = RenderTexture.GetTemporary(preview.width, preview.height);
                            Graphics.Blit(preview, rt);
                            RenderTexture.active = rt;
                            readablePreview = new Texture2D(preview.width, preview.height, TextureFormat.RGB24, false);
                            readablePreview.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                            readablePreview.Apply();

                            var pngData = readablePreview.EncodeToPNG();
                            if (pngData != null && pngData.Length > 0)
                            {
                                previewBase64 = Convert.ToBase64String(pngData);
                                previewWidth = readablePreview.width;
                                previewHeight = readablePreview.height;
                            }
                        }
                        finally
                        {
                            RenderTexture.active = previous;
                            if (rt != null) RenderTexture.ReleaseTemporary(rt);
                            if (readablePreview != null) UnityEngine.Object.DestroyImmediate(readablePreview);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning(
                            $"Failed to generate readable preview for '{path}': {ex.Message}. Preview might not be readable."
                        );
                        // Fallback: Try getting static preview if available?
                        // Texture2D staticPreview = AssetPreview.GetMiniThumbnail(asset);
                    }
                }
                else
                {
                    Debug.LogWarning(
                        $"Could not get asset preview for {path} (Type: {assetType?.Name}). Is it supported?"
                    );
                }
            }

            return new
            {
                path = path,
                guid = guid,
                assetType = assetType?.FullName ?? "Unknown",
                name = Path.GetFileNameWithoutExtension(path),
                fileName = Path.GetFileName(path),
                isFolder = AssetDatabase.IsValidFolder(path),
                instanceID = UnityApiAdapter.GetObjectIdOrZero(asset),
                lastWriteTimeUtc = File.GetLastWriteTimeUtc(
                        Path.Combine(Directory.GetCurrentDirectory(), path)
                    )
                    .ToString("o"), // ISO 8601
                // --- Preview Data ---
                previewBase64 = previewBase64, // PNG data as Base64 string
                previewWidth = previewWidth,
                previewHeight = previewHeight,
                // TODO: Add more metadata? Importer settings? Dependencies?
            };
        }
    }
}
