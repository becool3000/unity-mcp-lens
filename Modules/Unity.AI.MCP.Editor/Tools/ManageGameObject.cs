#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json; // Added for JsonSerializationException
using Newtonsoft.Json.Linq;
using UnityEditor;

// For CompilationPipeline
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry; // For Response class
using Unity.AI.MCP.Editor.ToolRegistry.Parameters;
using Unity.AI.MCP.Runtime.Serialization;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles GameObject manipulation within the current scene (CRUD, find, components).
    /// </summary>
    public static class ManageGameObject
    {
        /// <summary>
        /// Description of the ManageGameObject tool functionality and parameters.
        /// </summary>
        public const string Description = @"Manages GameObjects: create, modify, delete, find, and component operations.

Args:
    action: Operation (e.g., 'create', 'modify', 'find', 'add_component', 'remove_component', 'set_component_property', 'get_components', 'get_component').
    target: GameObject identifier (name or path string) for modify/delete/component actions.
    search_method: How to find objects ('by_name', 'by_id', 'by_path', etc.). Used with 'find' and some 'target' lookups.
    name: GameObject name - used for both 'create' (initial name) and 'modify' (rename).
    tag: Tag name - used for both 'create' (initial tag) and 'modify' (change tag).
    parent: Parent GameObject reference - used for both 'create' (initial parent) and 'modify' (change parent).
    layer: Layer name - used for both 'create' (initial layer) and 'modify' (change layer).
    component_properties: Dict mapping Component names to their properties to set.
                          Example: {""Rigidbody"": {""mass"": 10.0, ""useGravity"": True}},
                          To set references:
                          - Use asset path string for Prefabs/Materials, e.g., {""MeshRenderer"": {""material"": ""Assets/Materials/MyMat.mat""}}
                          - Use a dict for scene objects/components, e.g.:
                            {""MyScript"": {""otherObject"": {""find"": ""Player"", ""method"": ""by_name""}}} (assigns GameObject)
                            {""MyScript"": {""playerHealth"": {""find"": ""Player"", ""component"": ""HealthComponent""}}} (assigns Component)
                          Example set nested property:
                          - Access shared material: {""MeshRenderer"": {""sharedMaterial.color"": [1, 0, 0, 1]}}
    components_to_add: List of component names to add.
    Action-specific arguments (e.g., position, rotation, scale for create/modify;
             component_name for component actions;
             search_term, find_all for 'find').
    include_non_public_serialized: If True, includes private fields marked [SerializeField] in component data.

    Action-specific details:
    - For 'get_components':
        Required: target, search_method
        Optional: includeNonPublicSerialized (defaults to True)
        Returns all components on the target GameObject with their serialized data.
        The search_method parameter determines how to find the target ('by_name', 'by_id', 'by_path').
    - For 'get_component', specify 'component_name' to retrieve only that component's serialized data.

Returns:
    Dictionary with operation results ('success', 'message', 'data').";
        // Shared JsonSerializer to avoid per-call allocation overhead
        static readonly JsonSerializer InputSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new UnityEngineObjectConverter()
            }
        });

        // --- Main Handler ---

        /// <summary>
        /// Returns the input schema for this tool.
        /// </summary>
        /// <returns>The JSON schema object describing the tool's input structure.</returns>
        [McpSchema("Unity.ManageGameObject")]
        public static object GetInputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    action = new
                    {
                        type = "string",
                        description = "Operation to perform",
                        @enum = new[]
                        {
                            "create", "modify", "delete", "find", "get_components", "get_component",
                            "add_component", "remove_component", "set_component_property"
                        }
                    },
                    // Targeting and search
                    target = new
                    {
                        description = "GameObject identifier (name/path or instance ID)",
                        anyOf = new object[] { new { type = "string" }, new { type = "integer" } }
                    },
                    search_method = new
                    {
                        type = "string",
                        description = "How to find objects ('by_name','by_id','by_path')",
                        @enum = new[] { "by_name", "by_id", "by_path" }
                    },

                    // Common fields for create/modify
                    name = new { type = "string", description = "GameObject name" },
                    tag = new { type = "string", description = "Tag name" },
                    layer = new { type = "string", description = "Layer name" },
                    parent = new
                    {
                        description = "Parent GameObject (name/path or instance ID)",
                        anyOf = new object[] { new { type = "string" }, new { type = "integer" } }
                    },
                    position = new
                    {
                        type = "array",
                        description = "Local position [x,y,z]",
                        items = new { type = "number" },
                        min_items = 3,
                        max_items = 3
                    },
                    rotation = new
                    {
                        type = "array",
                        description = "Local rotation euler [x,y,z]",
                        items = new { type = "number" },
                        min_items = 3,
                        max_items = 3
                    },
                    scale = new
                    {
                        type = "array",
                        description = "Local scale [x,y,z]",
                        items = new { type = "number" },
                        min_items = 3,
                        max_items = 3
                    },

                    // Creation helpers
                    primitive_type = new { type = "string", description = "Unity primitive type to create (e.g., Cube, Sphere)" },
                    save_as_prefab = new { type = "boolean", description = "If true, save created object as prefab" },
                    prefab_path = new { type = "string", description = "Prefab path (Assets/... .prefab) when saving prefab" },
                    prefab_folder = new { type = "string", description = "Folder for prefab creation (defaults to Assets/Prefabs)" },

                    // Modify toggles
                    set_active = new { type = "boolean", description = "Set GameObject active state" },

                    // Component operations
                    components_to_add = new { type = "array", items = new { type = "string" }, description = "List of component type names to add" },
                    components_to_remove = new { type = "array", items = new { type = "string" }, description = "List of component type names to remove" },
                    component_name = new { type = "string", description = "Single component type name for add/remove/set operations" },
                    component_properties = new
                    {
                        type = "object",
                        description = "Map of component names to property dictionaries",
                        additional_properties = new { type = "object" }
                    },

                    // Find parameters
                    search_term = new { type = "string", description = "Search term for 'find'" },
                    find_all = new { type = "boolean", description = "If true, return all matching objects" },
                    search_in_children = new { type = "boolean", description = "Search within children" },
                    search_inactive = new { type = "boolean", description = "Include inactive objects in search" },

                    // Serialization controls
                    include_non_public_serialized = new { type = "boolean", description = "Include [SerializeField] private fields in component data" }
                },
                required = new[] { "action" }
            };
        }
        /// <summary>
        /// Main handler for GameObject management actions.
        /// </summary>
        /// <param name="params">The JObject containing action and parameters for GameObject operations.</param>
        /// <returns>A response object containing success status, message, and optional data.</returns>
        [McpTool("Unity.ManageGameObject", Description, Groups = new string[] { "core", "scene" })]
        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            string action = @params["action"]?.ToString().ToLower();
            if (string.IsNullOrEmpty(action))
            {
                return Response.Error("Action parameter is required.");
            }

            // Parameters used by various actions
            JToken targetToken = @params["target"]; // Can be string (name/path) or int (instanceID)
            string searchMethod = @params["search_method"]?.ToString().ToLower();

            // Get common parameters (consolidated)
            string name = @params["name"]?.ToString();
            string tag = @params["tag"]?.ToString();
            string layer = @params["layer"]?.ToString();
            JToken parentToken = @params["parent"];

            // --- Add parameter for controlling non-public field inclusion ---
            bool includeNonPublicSerialized = @params["include_non_public_serialized"]?.ToObject<bool>() ?? false;
            // --- End add parameter ---

            // --- Prefab Redirection Check ---
            string targetPath =
                targetToken?.Type == JTokenType.String ? targetToken.ToString() : null;
            if (
                !string.IsNullOrEmpty(targetPath)
                && targetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
            )
            {
                // Allow 'create' (instantiate), 'find' (?), 'get_components' (?)
                if (action == "modify" || action == "set_component_property")
                {
                    Debug.Log(
                        $"[ManageGameObject->ManageAsset] Redirecting action '{action}' for prefab '{targetPath}' to ManageAsset."
                    );
                    // Prepare params for ManageAsset.ModifyAsset
                    var assetParams = new ManageAssetParams
                    {
                        Action = AssetAction.Modify,
                        Path = targetPath
                    };

                    // Extract properties.
                    // For 'set_component_property', combine componentName and componentProperties.
                    // For 'modify', directly use componentProperties.
                    JObject properties = null;
                    if (action == "set_component_property")
                    {
                        string compName = @params["component_name"]?.ToString();
                        JObject compProps = @params["component_properties"]?[compName] as JObject; // Handle potential nesting
                        if (string.IsNullOrEmpty(compName))
                            return Response.Error(
                                "Missing 'componentName' for 'set_component_property' on prefab."
                            );
                        if (compProps == null)
                            return Response.Error(
                                $"Missing or invalid 'componentProperties' for component '{compName}' for 'set_component_property' on prefab."
                            );

                        properties = new JObject();
                        properties[compName] = compProps;
                    }
                    else // action == "modify"
                    {
                        properties = @params["component_properties"] as JObject;
                        if (properties == null)
                            return Response.Error(
                                "Missing 'componentProperties' for 'modify' action on prefab."
                            );
                    }

                    assetParams.Properties = properties;

                    // Call ManageAsset handler
                    return ManageAsset.HandleCommand(assetParams);
                }
                else if (
                    action == "delete"
                    || action == "add_component"
                    || action == "remove_component"
                    || action == "get_components"
                ) // Added get_components here too
                {
                    // Explicitly block other modifications on the prefab asset itself via Unity.ManageGameObject
                    return Response.Error(
                        $"Action '{action}' on a prefab asset ('{targetPath}') should be performed using the 'Unity.ManageAsset' command."
                    );
                }
                // Allow 'create' (instantiation) and 'find' to proceed, although finding a prefab asset by path might be less common via Unity.ManageGameObject.
                // No specific handling needed here, the code below will run.
            }
            // --- End Prefab Redirection Check ---

            try
            {
                switch (action)
                {
                    case "create":
                        return CreateGameObject(@params);
                    case "modify":
                        return ModifyGameObject(@params, targetToken, searchMethod);
                    case "delete":
                        return DeleteGameObject(targetToken, searchMethod);
                    case "find":
                        return FindGameObjects(@params, targetToken, searchMethod);
                    case "get_components":
                        string getCompTarget = targetToken?.ToString(); // Expect name, path, or ID string
                        if (getCompTarget == null)
                            return Response.Error(
                                "'target' parameter required for get_components."
                            );
                        // Pass the includeNonPublicSerialized flag here
                        return GetComponentsFromTarget(getCompTarget, searchMethod, includeNonPublicSerialized);
                    case "get_component":
                        string getSingleCompTarget = targetToken?.ToString();
                        if (getSingleCompTarget == null)
                            return Response.Error(
                                "'target' parameter required for get_component."
                            );
                        string componentName = @params["component_name"]?.ToString() ?? @params["componentName"]?.ToString();
                        if (string.IsNullOrEmpty(componentName))
                            return Response.Error(
                                "'component_name' parameter required for get_component."
                            );
                        return GetSingleComponentFromTarget(getSingleCompTarget, searchMethod, componentName, includeNonPublicSerialized);
                    case "add_component":
                        return AddComponentToTarget(@params, targetToken, searchMethod);
                    case "remove_component":
                        return RemoveComponentFromTarget(@params, targetToken, searchMethod);
                    case "set_component_property":
                        return SetComponentPropertyOnTarget(@params, targetToken, searchMethod);

                    default:
                        return Response.Error($"Unknown action: '{action}'.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ManageGameObject] Action '{action}' failed: {e}");
                return Response.Error($"Internal error processing action '{action}': {e.Message}");
            }
        }

        // --- Action Implementations ---

        static object CreateGameObject(JObject @params)
        {
            string name = @params["name"]?.ToString();
            if (string.IsNullOrEmpty(name))
            {
                return Response.Error("'name' parameter is required for 'create' action.");
            }

            // Get prefab creation parameters
            bool saveAsPrefab = @params["save_as_prefab"]?.ToObject<bool>() ?? false;
            string prefabPath = @params["prefab_path"]?.ToString();
            string prefabFolder = @params["prefab_folder"]?.ToString() ?? "Assets/Prefabs";
            string tag = @params["tag"]?.ToString(); // Get tag for creation
            string primitiveType = @params["primitive_type"]?.ToString(); // Keep primitiveType check

            // --- Handle Prefab Path Logic (Python server parity) ---
            if (saveAsPrefab)
            {
                if (string.IsNullOrEmpty(prefabPath))
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return Response.Error("Cannot create default prefab path: 'name' parameter is missing.");
                    }
                    // Construct path using prefab_folder and name
                    string constructedPath = $"{prefabFolder}/{name}.prefab";
                    // Ensure clean path separators (Unity prefers '/')
                    prefabPath = constructedPath.Replace("\\", "/");
                    Debug.Log($"[ManageGameObject.Create] Constructed prefab path: '{prefabPath}'");
                }
                else if (!prefabPath.ToLower().EndsWith(".prefab"))
                {
                    return Response.Error($"Invalid prefab_path: '{prefabPath}' must end with .prefab");
                }
            }
            // --- End Prefab Path Logic ---

            GameObject newGo = null; // Initialize as null

            // --- Try Instantiating Prefab First ---
            string originalPrefabPath = prefabPath; // Keep original for messages
            if (!string.IsNullOrEmpty(prefabPath))
            {
                // If no extension, search for the prefab by name
                if (
                    !prefabPath.Contains("/")
                    && !prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                )
                {
                    string prefabNameOnly = prefabPath;
                    Debug.Log(
                        $"[ManageGameObject.Create] Searching for prefab named: '{prefabNameOnly}'"
                    );
                    string[] guids = AssetDatabase.FindAssets($"t:Prefab {prefabNameOnly}");
                    if (guids.Length == 0)
                    {
                        return Response.Error(
                            $"Prefab named '{prefabNameOnly}' not found anywhere in the project."
                        );
                    }
                    else if (guids.Length > 1)
                    {
                        string foundPaths = string.Join(
                            ", ",
                            guids.Select(g => AssetDatabase.GUIDToAssetPath(g))
                        );
                        return Response.Error(
                            $"Multiple prefabs found matching name '{prefabNameOnly}': {foundPaths}. Please provide a more specific path."
                        );
                    }
                    else // Exactly one found
                    {
                        prefabPath = AssetDatabase.GUIDToAssetPath(guids[0]); // Update prefabPath with the full path
                        Debug.Log(
                            $"[ManageGameObject.Create] Found unique prefab at path: '{prefabPath}'"
                        );
                    }
                }
                else if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    // If it looks like a path but doesn't end with .prefab, assume user forgot it and append it.
                    Debug.LogWarning(
                        $"[ManageGameObject.Create] Provided prefabPath '{prefabPath}' does not end with .prefab. Assuming it's missing and appending."
                    );
                    prefabPath += ".prefab";
                    // Note: This path might still not exist, AssetDatabase.LoadAssetAtPath will handle that.
                }
                // The logic above now handles finding or assuming the .prefab extension.

                GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefabAsset != null)
                {
                    try
                    {
                        // Instantiate the prefab, initially place it at the root
                        // Parent will be set later if specified
                        newGo = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;

                        if (newGo == null)
                        {
                            // This might happen if the asset exists but isn't a valid GameObject prefab somehow
                            Debug.LogError(
                                $"[ManageGameObject.Create] Failed to instantiate prefab at '{prefabPath}', asset might be corrupted or not a GameObject."
                            );
                            return Response.Error(
                                $"Failed to instantiate prefab at '{prefabPath}'."
                            );
                        }
                        // Name the instance based on the 'name' parameter, not the prefab's default name
                        if (!string.IsNullOrEmpty(name))
                        {
                            newGo.name = name;
                        }
                        // Register Undo for prefab instantiation
                        Undo.RegisterCreatedObjectUndo(
                            newGo,
                            $"Instantiate Prefab '{prefabAsset.name}' as '{newGo.name}'"
                        );
                        Debug.Log(
                            $"[ManageGameObject.Create] Instantiated prefab '{prefabAsset.name}' from path '{prefabPath}' as '{newGo.name}'."
                        );
                    }
                    catch (Exception e)
                    {
                        return Response.Error(
                            $"Error instantiating prefab '{prefabPath}': {e.Message}"
                        );
                    }
                }
                else
                {
                    // Only return error if prefabPath was specified but not found.
                    // If prefabPath was empty/null, we proceed to create primitive/empty.
                    Debug.LogWarning(
                        $"[ManageGameObject.Create] Prefab asset not found at path: '{prefabPath}'. Will proceed to create new object if specified."
                    );
                    // Do not return error here, allow fallback to primitive/empty creation
                }
            }

            // --- Fallback: Create Primitive or Empty GameObject ---
            bool createdNewObject = false; // Flag to track if we created (not instantiated)
            if (newGo == null) // Only proceed if prefab instantiation didn't happen
            {
                if (!string.IsNullOrEmpty(primitiveType))
                {
                    try
                    {
                        PrimitiveType type = (PrimitiveType)
                            Enum.Parse(typeof(PrimitiveType), primitiveType, true);
                        newGo = GameObject.CreatePrimitive(type);
                        // Set name *after* creation for primitives
                        if (!string.IsNullOrEmpty(name))
                        {
                            newGo.name = name;
                        }
                        else
                        {
                            UnityEngine.Object.DestroyImmediate(newGo); // cleanup leak
                            return Response.Error(
                                "'name' parameter is required when creating a primitive."
                            ); // Name is essential
                        }
                        createdNewObject = true;
                    }
                    catch (ArgumentException)
                    {
                        return Response.Error(
                            $"Invalid primitive type: '{primitiveType}'. Valid types: {string.Join(", ", Enum.GetNames(typeof(PrimitiveType)))}"
                        );
                    }
                    catch (Exception e)
                    {
                        return Response.Error(
                            $"Failed to create primitive '{primitiveType}': {e.Message}"
                        );
                    }
                }
                else // Create empty GameObject
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return Response.Error(
                            "'name' parameter is required for 'create' action when not instantiating a prefab or creating a primitive."
                        );
                    }
                    newGo = new GameObject(name);
                    createdNewObject = true;
                }
                // Record creation for Undo *only* if we created a new object
                if (createdNewObject)
                {
                    Undo.RegisterCreatedObjectUndo(newGo, $"Create GameObject '{newGo.name}'");
                }
            }
            // --- Common Setup (Parent, Transform, Tag, Components) - Applied AFTER object exists ---
            if (newGo == null)
            {
                // Should theoretically not happen if logic above is correct, but safety check.
                return Response.Error("Failed to create or instantiate the GameObject.");
            }

            // Record potential changes to the existing prefab instance or the new GO
            // Record transform separately in case parent changes affect it
            Undo.RecordObject(newGo.transform, "Set GameObject Transform");
            Undo.RecordObject(newGo, "Set GameObject Properties");

            // Set Transform
            Vector3? position = ParseVector3(@params["position"] as JArray);
            Vector3? rotation = ParseVector3(@params["rotation"] as JArray);
            Vector3? scale = ParseVector3(@params["scale"] as JArray);

            if (position.HasValue)
                newGo.transform.localPosition = position.Value;
            if (rotation.HasValue)
                newGo.transform.localEulerAngles = rotation.Value;
            if (scale.HasValue)
                newGo.transform.localScale = scale.Value;

            // Set Parent
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject parentGo = ObjectsHelper.FindObject(parentToken, "by_id_or_name_or_path"); // Flexible parent finding
                if (parentGo == null)
                {
                    UnityEngine.Object.DestroyImmediate(newGo); // Clean up created object
                    return Response.Error($"Parent specified ('{parentToken}') but not found.");
                }
                newGo.transform.SetParent(parentGo.transform, true); // worldPositionStays = true
            }

            // Set Tag (added for create action)
            if (!string.IsNullOrEmpty(tag))
            {
                // Similar logic as in ModifyGameObject for setting/creating tags
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;
                try
                {
                    newGo.tag = tagToSet;
                }
                catch (UnityException ex)
                {
                    if (ex.Message.Contains("is not defined"))
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject.Create] Tag '{tagToSet}' not found. Attempting to create it."
                        );
                        try
                        {
                            InternalEditorUtility.AddTag(tagToSet);
                            newGo.tag = tagToSet; // Retry
                            Debug.Log(
                                $"[ManageGameObject.Create] Tag '{tagToSet}' created and assigned successfully."
                            );
                        }
                        catch (Exception innerEx)
                        {
                            UnityEngine.Object.DestroyImmediate(newGo); // Clean up
                            return Response.Error(
                                $"Failed to create or assign tag '{tagToSet}' during creation: {innerEx.Message}."
                            );
                        }
                    }
                    else
                    {
                        UnityEngine.Object.DestroyImmediate(newGo); // Clean up
                        return Response.Error(
                            $"Failed to set tag to '{tagToSet}' during creation: {ex.Message}."
                        );
                    }
                }
            }

            // Set Layer (new for create action)
            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId != -1)
                {
                    newGo.layer = layerId;
                }
                else
                {
                    Debug.LogWarning(
                        $"[ManageGameObject.Create] Layer '{layerName}' not found. Using default layer."
                    );
                }
            }

            // Add Components
            if (@params["components_to_add"] is JArray componentsToAddArray)
            {
                foreach (var compToken in componentsToAddArray)
                {
                    string typeName = null;
                    JObject properties = null;

                    if (compToken.Type == JTokenType.String)
                    {
                        typeName = compToken.ToString();
                    }
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString();
                        properties = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = AddComponentInternal(newGo, typeName, properties);
                        if (addResult != null) // Check if AddComponentInternal returned an error object
                        {
                            UnityEngine.Object.DestroyImmediate(newGo); // Clean up
                            return addResult; // Return the error response
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject] Invalid component format in components_to_add: {compToken}"
                        );
                    }
                }
            }

            // Save as Prefab ONLY if we *created* a new object AND saveAsPrefab is true
            GameObject finalInstance = newGo; // Use this for selection and return data
            if (createdNewObject && saveAsPrefab)
            {
                string finalPrefabPath = prefabPath; // Use a separate variable for saving path
                // This check should now happen *before* attempting to save
                if (string.IsNullOrEmpty(finalPrefabPath))
                {
                    // Clean up the created object before returning error
                    UnityEngine.Object.DestroyImmediate(newGo);
                    return Response.Error(
                        "'prefabPath' is required when 'saveAsPrefab' is true and creating a new object."
                    );
                }
                // Ensure the *saving* path ends with .prefab
                if (!finalPrefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log(
                        $"[ManageGameObject.Create] Appending .prefab extension to save path: '{finalPrefabPath}' -> '{finalPrefabPath}.prefab'"
                    );
                    finalPrefabPath += ".prefab";
                }

                try
                {
                    // Ensure directory exists using the final saving path
                    string directoryPath = System.IO.Path.GetDirectoryName(finalPrefabPath);
                    if (
                        !string.IsNullOrEmpty(directoryPath)
                        && !System.IO.Directory.Exists(directoryPath)
                    )
                    {
                        System.IO.Directory.CreateDirectory(directoryPath);
                        AssetDatabase.Refresh(); // Refresh asset database to recognize the new folder
                        Debug.Log(
                            $"[ManageGameObject.Create] Created directory for prefab: {directoryPath}"
                        );
                    }
                    // Use SaveAsPrefabAssetAndConnect with the final saving path
                    finalInstance = PrefabUtility.SaveAsPrefabAssetAndConnect(
                        newGo,
                        finalPrefabPath,
                        InteractionMode.UserAction
                    );

                    if (finalInstance == null)
                    {
                        // Destroy the original if saving failed somehow (shouldn't usually happen if path is valid)
                        UnityEngine.Object.DestroyImmediate(newGo);
                        return Response.Error(
                            $"Failed to save GameObject '{name}' as prefab at '{finalPrefabPath}'. Check path and permissions."
                        );
                    }
                    Debug.Log(
                        $"[ManageGameObject.Create] GameObject '{name}' saved as prefab to '{finalPrefabPath}' and instance connected."
                    );
                    // Mark the new prefab asset as dirty? Not usually necessary, SaveAsPrefabAsset handles it.
                    // EditorUtility.SetDirty(finalInstance); // Instance is handled by SaveAsPrefabAssetAndConnect
                }
                catch (Exception e)
                {
                    // Clean up the instance if prefab saving fails
                    UnityEngine.Object.DestroyImmediate(newGo); // Destroy the original attempt
                    return Response.Error($"Error saving prefab '{finalPrefabPath}': {e.Message}");
                }
            }

            // Select the instance in the scene (either prefab instance or newly created/saved one)
            Selection.activeGameObject = finalInstance;

            // Determine appropriate success message using the potentially updated or original path
            string messagePrefabPath =
                finalInstance == null
                    ? originalPrefabPath
                    : AssetDatabase.GetAssetPath(
                        PrefabUtility.GetCorrespondingObjectFromSource(finalInstance)
                            ?? (UnityEngine.Object)finalInstance
                    );
            string successMessage;
            if (!createdNewObject && !string.IsNullOrEmpty(messagePrefabPath)) // Instantiated existing prefab
            {
                successMessage =
                    $"Prefab '{messagePrefabPath}' instantiated successfully as '{finalInstance.name}'.";
            }
            else if (createdNewObject && saveAsPrefab && !string.IsNullOrEmpty(messagePrefabPath)) // Created new and saved as prefab
            {
                successMessage =
                    $"GameObject '{finalInstance.name}' created and saved as prefab to '{messagePrefabPath}'.";
            }
            else // Created new primitive or empty GO, didn't save as prefab
            {
                successMessage =
                    $"GameObject '{finalInstance.name}' created successfully in scene.";
            }

            // Use the new serializer helper
            //return Response.Success(successMessage, GetGameObjectData(finalInstance));
            return Response.Success(successMessage, GameObjectSerializer.GetGameObjectData(finalInstance));
        }

        static object ModifyGameObject(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = ObjectsHelper.FindObject(targetToken, searchMethod);
            if (targetGo == null)
            {
                return Response.Error(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            // Record state for Undo *before* modifications
            Undo.RecordObject(targetGo.transform, "Modify GameObject Transform");
            Undo.RecordObject(targetGo, "Modify GameObject Properties");

            bool modified = false;

            // Rename (using consolidated 'name' parameter)
            string name = @params["name"]?.ToString();
            if (!string.IsNullOrEmpty(name) && targetGo.name != name)
            {
                targetGo.name = name;
                modified = true;
            }

            // Set Active State
            bool? setActive = @params["set_active"]?.ToObject<bool?>();
            if (setActive.HasValue && targetGo.activeSelf != setActive.Value)
            {
                targetGo.SetActive(setActive.Value);
                modified = true;
            }

            // Change Tag (using consolidated 'tag' parameter)
            string tag = @params["tag"]?.ToString();
            // Only attempt to change tag if a non-null tag is provided and it's different from the current one.
            // Allow setting an empty string to remove the tag (Unity uses "Untagged").
            if (tag != null && targetGo.tag != tag)
            {
                // Ensure the tag is not empty, if empty, it means "Untagged" implicitly
                string tagToSet = string.IsNullOrEmpty(tag) ? "Untagged" : tag;
                try
                {
                    targetGo.tag = tagToSet;
                    modified = true;
                }
                catch (UnityException ex)
                {
                    // Check if the error is specifically because the tag doesn't exist
                    if (ex.Message.Contains("is not defined"))
                    {
                        Debug.LogWarning(
                            $"[ManageGameObject] Tag '{tagToSet}' not found. Attempting to create it."
                        );
                        try
                        {
                            // Attempt to create the tag using internal utility
                            InternalEditorUtility.AddTag(tagToSet);
                            // Wait a frame maybe? Not strictly necessary but sometimes helps editor updates.
                            // yield return null; // Cannot yield here, editor script limitation

                            // Retry setting the tag immediately after creation
                            targetGo.tag = tagToSet;
                            modified = true;
                            Debug.Log(
                                $"[ManageGameObject] Tag '{tagToSet}' created and assigned successfully."
                            );
                        }
                        catch (Exception innerEx)
                        {
                            // Handle failure during tag creation or the second assignment attempt
                            Debug.LogError(
                                $"[ManageGameObject] Failed to create or assign tag '{tagToSet}' after attempting creation: {innerEx.Message}"
                            );
                            return Response.Error(
                                $"Failed to create or assign tag '{tagToSet}': {innerEx.Message}. Check Tag Manager and permissions."
                            );
                        }
                    }
                    else
                    {
                        // If the exception was for a different reason, return the original error
                        return Response.Error($"Failed to set tag to '{tagToSet}': {ex.Message}.");
                    }
                }
            }

            // Change Layer (using consolidated 'layer' parameter)
            string layerName = @params["layer"]?.ToString();
            if (!string.IsNullOrEmpty(layerName))
            {
                int layerId = LayerMask.NameToLayer(layerName);
                if (layerId == -1 && layerName != "Default")
                {
                    return Response.Error(
                        $"Invalid layer specified: '{layerName}'. Use a valid layer name."
                    );
                }
                if (layerId != -1 && targetGo.layer != layerId)
                {
                    targetGo.layer = layerId;
                    modified = true;
                }
            }

            // Transform Modifications
            Vector3? position = ParseVector3(@params["position"] as JArray);
            Vector3? rotation = ParseVector3(@params["rotation"] as JArray);
            Vector3? scale = ParseVector3(@params["scale"] as JArray);

            if (position.HasValue && targetGo.transform.localPosition != position.Value)
            {
                string positionType = @params["positionType"]?.ToString().ToLower();
                if (string.IsNullOrEmpty(positionType))
                {
                    positionType = "center";
                }

                var positionToSet = position.Value;
                switch (positionType)
                {
                    case "center":
                        var center = ComponentResolver.GetObjectWorldCenter(targetGo);
                        var delta = center - targetGo.transform.position;
                        positionToSet -= delta;
                        break;
                    case "pivot":
                        // no changes
                        break;
                }
                targetGo.transform.localPosition = positionToSet;
                modified = true;
            }
            if (rotation.HasValue && targetGo.transform.localEulerAngles != rotation.Value)
            {
                targetGo.transform.localEulerAngles = rotation.Value;
                modified = true;
            }
            if (scale.HasValue && targetGo.transform.localScale != scale.Value)
            {
                targetGo.transform.localScale = scale.Value;
                modified = true;
            }

            // Change Parent (using consolidated 'parent' parameter)
            JToken parentToken = @params["parent"];
            if (parentToken != null)
            {
                GameObject newParentGo = ObjectsHelper.FindObject(parentToken, "by_id_or_name_or_path");
                // Check for hierarchy loops
                if (
                    newParentGo == null
                    && !(
                        parentToken.Type == JTokenType.Null
                        || (
                            parentToken.Type == JTokenType.String
                            && string.IsNullOrEmpty(parentToken.ToString())
                        )
                    )
                )
                {
                    return Response.Error($"New parent ('{parentToken}') not found.");
                }
                if (newParentGo != null && newParentGo.transform.IsChildOf(targetGo.transform))
                {
                    return Response.Error(
                        $"Cannot parent '{targetGo.name}' to '{newParentGo.name}', as it would create a hierarchy loop."
                    );
                }
                if (targetGo.transform.parent != (newParentGo?.transform))
                {
                    targetGo.transform.SetParent(newParentGo?.transform, true); // worldPositionStays = true
                    modified = true;
                }
            }

            // --- Component Modifications ---
            // Note: These might need more specific Undo recording per component

            // Remove Components
            if (@params["components_to_remove"] is JArray componentsToRemoveArray)
            {
                foreach (var compToken in componentsToRemoveArray)
                {
                    // ... (parsing logic as in CreateGameObject) ...
                    string typeName = compToken.ToString();
                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var removeResult = RemoveComponentInternal(targetGo, typeName);
                        if (removeResult != null)
                            return removeResult; // Return error if removal failed
                        modified = true;
                    }
                }
            }

            // Add Components (similar to create)
            if (@params["components_to_add"] is JArray componentsToAddArrayModify)
            {
                foreach (var compToken in componentsToAddArrayModify)
                {
                    string typeName = null;
                    JObject properties = null;
                    if (compToken.Type == JTokenType.String)
                        typeName = compToken.ToString();
                    else if (compToken is JObject compObj)
                    {
                        typeName = compObj["typeName"]?.ToString();
                        properties = compObj["properties"] as JObject;
                    }

                    if (!string.IsNullOrEmpty(typeName))
                    {
                        var addResult = AddComponentInternal(targetGo, typeName, properties);
                        if (addResult != null)
                            return addResult;
                        modified = true;
                    }
                }
            }

            // Set Component Properties
            var componentErrors = new List<object>();
            if (@params["component_properties"] is JObject componentPropertiesObj)
            {
                foreach (var prop in componentPropertiesObj.Properties())
                {
                    string compName = prop.Name;
                    JObject propertiesToSet = prop.Value as JObject;
                    if (propertiesToSet != null)
                    {
                        var setResult = SetComponentPropertiesInternal(
                            targetGo,
                            compName,
                            propertiesToSet
                        );
                        if (setResult != null)
                        {
                            componentErrors.Add(setResult);
                        }
                        else
                        {
                            modified = true;
                        }
                    }
                }
            }

            // Return component errors if any occurred (after processing all components)
            if (componentErrors.Count > 0)
            {
                // Aggregate flattened error strings to make tests/API assertions simpler
                var aggregatedErrors = new List<string>();
                foreach (var errorObj in componentErrors)
                {
                    try
                    {
                        var dataProp = errorObj?.GetType().GetProperty("data");
                        var dataVal = dataProp?.GetValue(errorObj);
                        if (dataVal != null)
                        {
                            var errorsProp = dataVal.GetType().GetProperty("errors");
                            var errorsEnum = errorsProp?.GetValue(dataVal) as System.Collections.IEnumerable;
                            if (errorsEnum != null)
                            {
                                foreach (var item in errorsEnum)
                                {
                                    var s = item?.ToString();
                                    if (!string.IsNullOrEmpty(s)) aggregatedErrors.Add(s);
                                }
                            }
                        }
                    }
                    catch { }
                }

                return Response.Error(
                    $"One or more component property operations failed on '{targetGo.name}'.",
                    new { componentErrors = componentErrors, errors = aggregatedErrors }
                );
            }

            if (!modified)
            {
                // Use the new serializer helper
                // return Response.Success(
                //     $"No modifications applied to GameObject '{targetGo.name}'.",
                //     GetGameObjectData(targetGo));

                return Response.Success(
                    $"No modifications applied to GameObject '{targetGo.name}'.",
                    GameObjectSerializer.GetGameObjectData(targetGo)
                );
            }

            EditorUtility.SetDirty(targetGo); // Mark scene as dirty
            // Use the new serializer helper
            return Response.Success(
                $"GameObject '{targetGo.name}' modified successfully.",
                GameObjectSerializer.GetGameObjectData(targetGo)
            );
            // return Response.Success(
            //     $"GameObject '{targetGo.name}' modified successfully.",
            //     GetGameObjectData(targetGo));

        }

        static object DeleteGameObject(JToken targetToken, string searchMethod)
        {
            // Find potentially multiple objects if name/tag search is used without find_all=false implicitly
            List<GameObject> targets = ObjectsHelper.FindObjects(targetToken, searchMethod, true); // find_all=true for delete safety

            if (targets.Count == 0)
            {
                return Response.Error(
                    $"Target GameObject(s) ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            List<object> deletedObjects = new List<object>();
            foreach (var targetGo in targets)
            {
                if (targetGo != null)
                {
                    string goName = targetGo.name;
                    int goId = targetGo.GetInstanceID();
                    // Use Undo.DestroyObjectImmediate for undo support
                    Undo.DestroyObjectImmediate(targetGo);
                    deletedObjects.Add(new { name = goName, instanceID = goId });
                }
            }

            if (deletedObjects.Count > 0)
            {
                string message =
                    targets.Count == 1
                        ? $"GameObject '{deletedObjects[0].GetType().GetProperty("name").GetValue(deletedObjects[0])}' deleted successfully."
                        : $"{deletedObjects.Count} GameObjects deleted successfully.";
                return Response.Success(message, deletedObjects);
            }
            else
            {
                // Should not happen if targets.Count > 0 initially, but defensive check
                return Response.Error("Failed to delete target GameObject(s).");
            }
        }

        static object FindGameObjects(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            bool findAll = @params["find_all"]?.ToObject<bool>() ?? @params["findAll"]?.ToObject<bool>() ?? false;
            List<GameObject> foundObjects = ObjectsHelper.FindObjects(
                targetToken,
                searchMethod,
                findAll,
                @params
            );

            if (foundObjects.Count == 0)
            {
                return Response.Success("No matching GameObjects found.", new List<object>());
            }

            // Use the new serializer helper
            //var results = foundObjects.Select(go => GetGameObjectData(go)).ToList();
            var results = foundObjects.Select(go => GameObjectSerializer.GetGameObjectData(go)).ToList();
            return Response.Success($"Found {results.Count} GameObject(s).", results);
        }

        static object GetComponentsFromTarget(string target, string searchMethod, bool includeNonPublicSerialized = true)
        {
            GameObject targetGo = ObjectsHelper.FindObject(target, searchMethod);
            if (targetGo == null)
            {
                return Response.Error(
                    $"Target GameObject ('{target}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            try
            {
                // --- Get components, immediately copy to list, and null original array ---
                Component[] originalComponents = targetGo.GetComponents<Component>();
                List<Component> componentsToIterate = new List<Component>(originalComponents ?? Array.Empty<Component>()); // Copy immediately, handle null case
                int componentCount = componentsToIterate.Count;
                originalComponents = null; // Null the original reference
                // Debug.Log($"[GetComponentsFromTarget] Found {componentCount} components on {targetGo.name}. Copied to list, nulled original. Starting REVERSE for loop...");
                // --- End Copy and Null ---

                var componentData = new List<object>();

                for (int i = componentCount - 1; i >= 0; i--) // Iterate backwards over the COPY
                {
                    Component c = componentsToIterate[i]; // Use the copy
                    if (c == null)
                    {
                        // Debug.LogWarning($"[GetComponentsFromTarget REVERSE for] Encountered a null component at index {i} on {targetGo.name}. Skipping.");
                        continue; // Safety check
                    }
                    // Debug.Log($"[GetComponentsFromTarget REVERSE for] Processing component: {c.GetType()?.FullName ?? "null"} (ID: {c.GetInstanceID()}) at index {i} on {targetGo.name}");
                    try
                    {
                        var data = ComponentSummarySerializer.GetSafeComponentData(c, includeNonPublicSerialized);
                        if (data != null) // Ensure GetComponentData didn't return null
                        {
                            componentData.Insert(0, data); // Insert at beginning to maintain original order in final list
                        }
                        // else
                        // {
                        //     Debug.LogWarning($"[GetComponentsFromTarget REVERSE for] GetComponentData returned null for component {c.GetType().FullName} (ID: {c.GetInstanceID()}) on {targetGo.name}. Skipping addition.");
                        // }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[GetComponentsFromTarget REVERSE for] Error processing component {c.GetType().FullName} (ID: {c.GetInstanceID()}) on {targetGo.name}: {ex.Message}\n{ex.StackTrace}");
                        // Optionally add placeholder data or just skip
                        componentData.Insert(0, new JObject( // Insert error marker at beginning
                            new JProperty("typeName", c.GetType().FullName + " (Serialization Error)"),
                            new JProperty("instanceID", c.GetInstanceID()),
                            new JProperty("error", ex.Message)
                        ));
                    }
                }
                // Debug.Log($"[GetComponentsFromTarget] Finished REVERSE for loop.");

                // Cleanup the list we created
                componentsToIterate.Clear();
                componentsToIterate = null;

                return Response.Success(
                    $"Retrieved {componentData.Count} components from '{targetGo.name}'.",
                    ShapeComponentPayload(
                        componentData,
                        $"Retrieved {componentData.Count} components from '{targetGo.name}'.",
                        new
                        {
                            action = "get_components",
                            target,
                            search_method = searchMethod,
                            include_non_public_serialized = includeNonPublicSerialized
                        })
                );
            }
            catch (Exception e)
            {
                return Response.Error(
                    $"Error getting components from '{targetGo.name}': {e.Message}"
                );
            }
        }

        /// <summary>
        /// Gets a single component from the target GameObject and returns its serialized data.
        /// </summary>
        static object GetSingleComponentFromTarget(string target, string searchMethod, string componentName, bool includeNonPublicSerialized = true)
        {
            GameObject targetGo = ObjectsHelper.FindObject(target, searchMethod);
            if (targetGo == null)
            {
                return Response.Error(
                    $"Target GameObject ('{target}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            try
            {
                // Try to find the component by name using ComponentResolver first
                Component targetComponent = null;
                if (ComponentResolver.TryResolve(componentName, out var compType, out var compError))
                {
                    targetComponent = targetGo.GetComponent(compType);
                }

                // Fallback: search all components for name/type match
                if (targetComponent == null)
                {
                    Component[] allComponents = targetGo.GetComponents<Component>();
                    foreach (Component comp in allComponents)
                    {
                        if (comp != null)
                        {
                            string typeName = comp.GetType().Name;
                            string fullTypeName = comp.GetType().FullName;

                            if (typeName.Equals(componentName, StringComparison.OrdinalIgnoreCase) ||
                                fullTypeName.Equals(componentName, StringComparison.OrdinalIgnoreCase))
                            {
                                targetComponent = comp;
                                break;
                            }
                        }
                    }
                }

                if (targetComponent == null)
                {
                    return Response.Error(
                        $"Component '{componentName}' not found on GameObject '{targetGo.name}'."
                    );
                }

                var componentData = ComponentSummarySerializer.GetSafeComponentData(targetComponent, includeNonPublicSerialized);

                if (componentData == null)
                {
                    return Response.Error(
                        $"Failed to serialize component '{componentName}' on GameObject '{targetGo.name}'."
                    );
                }

                return Response.Success(
                    $"Retrieved component '{componentName}' from '{targetGo.name}'.",
                    ShapeComponentPayload(
                        componentData,
                        $"Retrieved component '{componentName}' from '{targetGo.name}'.",
                        new
                        {
                            action = "get_component",
                            target,
                            search_method = searchMethod,
                            component_name = componentName,
                            include_non_public_serialized = includeNonPublicSerialized
                        })
                );
            }
            catch (Exception e)
            {
                return Response.Error(
                    $"Error getting component '{componentName}' from '{targetGo.name}': {e.Message}"
                );
            }
        }

        static object ShapeComponentPayload(object data, string summary, object detailRef)
        {
            var serialized = JsonConvert.SerializeObject(data, Formatting.None);
            var rawBytes = PayloadBudgeting.GetUtf8ByteCount(serialized);
            if (rawBytes <= PayloadBudgetPolicy.MaxToolResultBytes)
            {
                PayloadStats.Record("tool_result", "Unity.ManageGameObject", rawBytes, rawBytes, PayloadBudgeting.EstimateTokensFromBytes(rawBytes), PayloadBudgeting.ComputeSha256(serialized));
                return data;
            }

            var budgeted = PayloadBudgeting.CreateTextResult(
                summary,
                new { rawBytes },
                serialized,
                detailRef,
                maxPreviewLines: 40,
                maxPreviewBytes: PayloadBudgetPolicy.MaxToolResultBytes);
            PayloadStats.Record("tool_result", "Unity.ManageGameObject", rawBytes, PayloadBudgeting.GetUtf8ByteCount(budgeted.Preview), PayloadBudgeting.EstimateTokensFromBytes(PayloadBudgeting.GetUtf8ByteCount(budgeted.Preview)), budgeted.Sha256);
            return budgeted;
        }

        static object AddComponentToTarget(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = ObjectsHelper.FindObject(targetToken, searchMethod);
            if (targetGo == null)
            {
                return Response.Error(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            string typeName = null;
            JObject properties = null;

            // Allow adding component specified directly or via componentsToAdd array (take first)
            if (@params["component_name"] != null)
            {
                typeName = @params["component_name"]?.ToString();
                properties = @params["component_properties"]?[typeName] as JObject; // Check if props are nested under name
            }
            else if (
                @params["components_to_add"] is JArray componentsToAddArray
                && componentsToAddArray.Count > 0
            )
            {
                var compToken = componentsToAddArray.First;
                if (compToken.Type == JTokenType.String)
                    typeName = compToken.ToString();
                else if (compToken is JObject compObj)
                {
                    typeName = compObj["typeName"]?.ToString();
                    properties = compObj["properties"] as JObject;
                }
            }

            if (string.IsNullOrEmpty(typeName))
            {
                return Response.Error(
                    "Component type name ('componentName' or first element in 'components_to_add') is required."
                );
            }

            var addResult = AddComponentInternal(targetGo, typeName, properties);
            if (addResult != null)
                return addResult; // Return error

            EditorUtility.SetDirty(targetGo);
            // Use the new serializer helper
            return Response.Success(
                $"Component '{typeName}' added to '{targetGo.name}'.",
                GameObjectSerializer.GetGameObjectData(targetGo)
            ); // Return updated GO data
        }

        static object RemoveComponentFromTarget(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = ObjectsHelper.FindObject(targetToken, searchMethod);
            if (targetGo == null)
            {
                return Response.Error(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            string typeName = null;
            // Allow removing component specified directly or via componentsToRemove array (take first)
            if (@params["component_name"] != null)
            {
                typeName = @params["component_name"]?.ToString();
            }
            else if (
                @params["components_to_remove"] is JArray componentsToRemoveArray
                && componentsToRemoveArray.Count > 0
            )
            {
                typeName = componentsToRemoveArray.First?.ToString();
            }

            if (string.IsNullOrEmpty(typeName))
            {
                return Response.Error(
                    "Component type name ('componentName' or first element in 'componentsToRemove') is required."
                );
            }

            var removeResult = RemoveComponentInternal(targetGo, typeName);
            if (removeResult != null)
                return removeResult; // Return error

            EditorUtility.SetDirty(targetGo);
             // Use the new serializer helper
            return Response.Success(
                $"Component '{typeName}' removed from '{targetGo.name}'.",
                GameObjectSerializer.GetGameObjectData(targetGo)
            );
        }

        static object SetComponentPropertyOnTarget(
            JObject @params,
            JToken targetToken,
            string searchMethod
        )
        {
            GameObject targetGo = ObjectsHelper.FindObject(targetToken, searchMethod);
            if (targetGo == null)
            {
                return Response.Error(
                    $"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'."
                );
            }

            string compName = @params["component_name"]?.ToString();
            JObject propertiesToSet = null;

            if (!string.IsNullOrEmpty(compName))
            {
                // Properties might be directly under componentProperties or nested under the component name
                if (@params["component_properties"] is JObject compProps)
                {
                    propertiesToSet = compProps[compName] as JObject ?? compProps; // Allow flat or nested structure
                }
            }
            else
            {
                return Response.Error("'componentName' parameter is required.");
            }

            if (propertiesToSet == null || !propertiesToSet.HasValues)
            {
                return Response.Error(
                    "'componentProperties' dictionary for the specified component is required and cannot be empty."
                );
            }

            var setResult = SetComponentPropertiesInternal(targetGo, compName, propertiesToSet);
            if (setResult != null)
                return setResult; // Return error

            EditorUtility.SetDirty(targetGo);
             // Use the new serializer helper
            return Response.Success(
                $"Properties set for component '{compName}' on '{targetGo.name}'.",
                GameObjectSerializer.GetGameObjectData(targetGo)
            );
        }

        // --- Internal Helpers ---

        /// <summary>
        /// Parses a JArray like [x, y, z] into a Vector3.
        /// </summary>
        static Vector3? ParseVector3(JArray array)
        {
            if (array != null && array.Count == 3)
            {
                try
                {
                    return new Vector3(
                        array[0].ToObject<float>(),
                        array[1].ToObject<float>(),
                        array[2].ToObject<float>()
                    );
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to parse JArray as Vector3: {array}. Error: {ex.Message}");
                }
            }
            return null;
        }

        // Helper to get all scene objects efficiently
        internal static IEnumerable<GameObject> GetAllSceneObjects(bool includeInactive)
        {
            // SceneManager.GetActiveScene().GetRootGameObjects() is faster than FindObjectsOfType<GameObject>()
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();
            var allObjects = new List<GameObject>();
            foreach (var root in rootObjects)
            {
                allObjects.AddRange(
                    root.GetComponentsInChildren<Transform>(includeInactive)
                        .Select(t => t.gameObject)
                );
            }
            return allObjects;
        }

        /// <summary>
        /// Adds a component by type name and optionally sets properties.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        static object AddComponentInternal(
            GameObject targetGo,
            string typeName,
            JObject properties
        )
        {
            Type componentType = FindType(typeName);
            if (componentType == null)
            {
                return Response.Error(
                    $"Component type '{typeName}' not found or is not a valid Component."
                );
            }
            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                return Response.Error($"Type '{typeName}' is not a Component.");
            }

            // Prevent adding Transform again
            if (componentType == typeof(Transform))
            {
                return Response.Error("Cannot add another Transform component.");
            }

            // Check for 2D/3D physics component conflicts
            bool isAdding2DPhysics =
                typeof(Rigidbody2D).IsAssignableFrom(componentType)
                || typeof(Collider2D).IsAssignableFrom(componentType);
            bool isAdding3DPhysics =
                typeof(Rigidbody).IsAssignableFrom(componentType)
                || typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics)
            {
                // Check if the GameObject already has any 3D Rigidbody or Collider
                if (
                    targetGo.GetComponent<Rigidbody>() != null
                    || targetGo.GetComponent<Collider>() != null
                )
                {
                    return Response.Error(
                        $"Cannot add 2D physics component '{typeName}' because the GameObject '{targetGo.name}' already has a 3D Rigidbody or Collider."
                    );
                }
            }
            else if (isAdding3DPhysics)
            {
                // Check if the GameObject already has any 2D Rigidbody or Collider
                if (
                    targetGo.GetComponent<Rigidbody2D>() != null
                    || targetGo.GetComponent<Collider2D>() != null
                )
                {
                    return Response.Error(
                        $"Cannot add 3D physics component '{typeName}' because the GameObject '{targetGo.name}' already has a 2D Rigidbody or Collider."
                    );
                }
            }

            try
            {
                // Use Undo.AddComponent for undo support
                Component newComponent = Undo.AddComponent(targetGo, componentType);
                if (newComponent == null)
                {
                    return Response.Error(
                        $"Failed to add component '{typeName}' to '{targetGo.name}'. It might be disallowed (e.g., adding script twice)."
                    );
                }

                // Set default values for specific component types
                if (newComponent is Light light)
                {
                    // Default newly added lights to directional
                    light.type = LightType.Directional;
                }

                // Set properties if provided
                if (properties != null)
                {
                    var setResult = SetComponentPropertiesInternal(
                        targetGo,
                        typeName,
                        properties,
                        newComponent
                    ); // Pass the new component instance
                    if (setResult != null)
                    {
                        // If setting properties failed, maybe remove the added component?
                        Undo.DestroyObjectImmediate(newComponent);
                        return setResult; // Return the error from setting properties
                    }
                }

                return null; // Success
            }
            catch (Exception e)
            {
                return Response.Error(
                    $"Error adding component '{typeName}' to '{targetGo.name}': {e.Message}"
                );
            }
        }

        /// <summary>
        /// Removes a component by type name.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        static object RemoveComponentInternal(GameObject targetGo, string typeName)
        {
            Type componentType = FindType(typeName);
            if (componentType == null)
            {
                return Response.Error($"Component type '{typeName}' not found for removal.");
            }

            // Prevent removing essential components
            if (componentType == typeof(Transform))
            {
                return Response.Error("Cannot remove the Transform component.");
            }

            Component componentToRemove = targetGo.GetComponent(componentType);
            if (componentToRemove == null)
            {
                return Response.Error(
                    $"Component '{typeName}' not found on '{targetGo.name}' to remove."
                );
            }

            try
            {
                // Use Undo.DestroyObjectImmediate for undo support
                Undo.DestroyObjectImmediate(componentToRemove);
                return null; // Success
            }
            catch (Exception e)
            {
                return Response.Error(
                    $"Error removing component '{typeName}' from '{targetGo.name}': {e.Message}"
                );
            }
        }

        /// <summary>
        /// Sets properties on a component.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        static object SetComponentPropertiesInternal(
            GameObject targetGo,
            string compName,
            JObject propertiesToSet,
            Component targetComponentInstance = null
        )
        {
            Component targetComponent = targetComponentInstance;
            if (targetComponent == null)
            {
                if (ComponentResolver.TryResolve(compName, out var compType, out var compError))
                {
                    targetComponent = targetGo.GetComponent(compType);
                }
                else
                {
                    targetComponent = targetGo.GetComponent(compName); // fallback to string-based lookup
                }
            }
            if (targetComponent == null)
            {
                return Response.Error(
                    $"Component '{compName}' not found on '{targetGo.name}' to set properties."
                );
            }

            Undo.RecordObject(targetComponent, "Set Component Properties");

            var failures = new List<string>();
            foreach (var prop in propertiesToSet.Properties())
            {
                string propName = prop.Name;
                JToken propValue = prop.Value;

                try
                {
                    bool setResult = SetProperty(targetComponent, propName, propValue);
                    if (!setResult)
                    {
                        var availableProperties = ComponentResolver.GetAllComponentProperties(targetComponent.GetType());
                        var suggestions = ComponentResolver.GetAIPropertySuggestions(propName, availableProperties);
                        var msg = suggestions.Any()
                            ? $"Property '{propName}' not found. Did you mean: {string.Join(", ", suggestions)}? Available: [{string.Join(", ", availableProperties)}]"
                            : $"Property '{propName}' not found. Available: [{string.Join(", ", availableProperties)}]";
                        Debug.LogWarning($"[ManageGameObject] {msg}");
                        failures.Add(msg);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(
                        $"[ManageGameObject] Error setting property '{propName}' on '{compName}': {e.Message}"
                    );
                    failures.Add($"Error setting '{propName}': {e.Message}");
                }
            }
            EditorUtility.SetDirty(targetComponent);
            return failures.Count == 0
                ? null
                : Response.Error($"One or more properties failed on '{compName}'.", new { errors = failures });
        }

        /// <summary>
        /// Helper to set a property or field via reflection, handling basic types.
        /// </summary>
        static bool SetProperty(object target, string memberName, JToken value)
        {
            Type type = target.GetType();
            BindingFlags flags =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            // Use shared serializer to avoid per-call allocation
            var inputSerializer = InputSerializer;

            try
            {
                // Handle special case for materials with dot notation (material.property)
                // Examples: material.color, sharedMaterial.color, materials[0].color
                if (memberName.Contains('.') || memberName.Contains('['))
                {
                    // Pass the inputSerializer down for nested conversions
                    return SetNestedProperty(target, memberName, value, inputSerializer);
                }

                PropertyInfo propInfo = type.GetProperty(memberName, flags);
                if (propInfo != null && propInfo.CanWrite)
                {
                    // Use the inputSerializer for conversion
                    object convertedValue = ConvertJTokenToType(value, propInfo.PropertyType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null) // Allow setting null
                    {
                        propInfo.SetValue(target, convertedValue);
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SetProperty] Conversion failed for property '{memberName}' (Type: {propInfo.PropertyType.Name}) from token: {value.ToString(Formatting.None)}");
                    }
                }
                else
                {
                    FieldInfo fieldInfo = type.GetField(memberName, flags);
                    if (fieldInfo != null) // Check if !IsLiteral?
                    {
                        // Use the inputSerializer for conversion
                        object convertedValue = ConvertJTokenToType(value, fieldInfo.FieldType, inputSerializer);
                        if (convertedValue != null || value.Type == JTokenType.Null) // Allow setting null
                        {
                            fieldInfo.SetValue(target, convertedValue);
                            return true;
                        }
                        else
                        {
                            Debug.LogWarning($"[SetProperty] Conversion failed for field '{memberName}' (Type: {fieldInfo.FieldType.Name}) from token: {value.ToString(Formatting.None)}");
                        }
                    }
                    else
                    {
                        // Try NonPublic [SerializeField] fields
                        var npField = type.GetField(memberName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (npField != null && npField.GetCustomAttribute<SerializeField>() != null)
                        {
                            object convertedValue = ConvertJTokenToType(value, npField.FieldType, inputSerializer);
                            if (convertedValue != null || value.Type == JTokenType.Null)
                            {
                                npField.SetValue(target, convertedValue);
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[SetProperty] Failed to set '{memberName}' on {type.Name}: {ex.Message}\nToken: {value.ToString(Formatting.None)}"
                );
            }
            return false;
        }

        /// <summary>
        /// Sets a nested property using dot notation (e.g., "material.color") or array access (e.g., "materials[0]")
        /// </summary>
        // Pass the input serializer for conversions
        //Using the serializer helper
        static bool SetNestedProperty(object target, string path, JToken value, JsonSerializer inputSerializer)
        {
            try
            {
                // Split the path into parts (handling both dot notation and array indexing)
                string[] pathParts = SplitPropertyPath(path);
                if (pathParts.Length == 0)
                    return false;

                object currentObject = target;
                Type currentType = currentObject.GetType();
                BindingFlags flags =
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

                // Traverse the path until we reach the final property
                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    string part = pathParts[i];
                    bool isArray = false;
                    int arrayIndex = -1;

                    // Check if this part contains array indexing
                    if (part.Contains("["))
                    {
                        int startBracket = part.IndexOf('[');
                        int endBracket = part.IndexOf(']');
                        if (startBracket > 0 && endBracket > startBracket)
                        {
                            string indexStr = part.Substring(
                                startBracket + 1,
                                endBracket - startBracket - 1
                            );
                            if (int.TryParse(indexStr, out arrayIndex))
                            {
                                isArray = true;
                                part = part.Substring(0, startBracket);
                            }
                        }
                    }
                    // Get the property/field
                    PropertyInfo propInfo = currentType.GetProperty(part, flags);
                    FieldInfo fieldInfo = null;
                    if (propInfo == null)
                    {
                        fieldInfo = currentType.GetField(part, flags);
                        if (fieldInfo == null)
                        {
                            Debug.LogWarning(
                                $"[SetNestedProperty] Could not find property or field '{part}' on type '{currentType.Name}'"
                            );
                            return false;
                        }
                    }

                    // Get the value
                    currentObject =
                        propInfo != null
                            ? propInfo.GetValue(currentObject)
                            : fieldInfo.GetValue(currentObject);
                    //Need to stop if current property is null
                    if (currentObject == null)
                    {
                        Debug.LogWarning(
                            $"[SetNestedProperty] Property '{part}' is null, cannot access nested properties."
                        );
                        return false;
                    }
                    // If this part was an array or list, access the specific index
                    if (isArray)
                    {
                        if (currentObject is Material[])
                        {
                            var materials = currentObject as Material[];
                            if (arrayIndex < 0 || arrayIndex >= materials.Length)
                            {
                                Debug.LogWarning(
                                    $"[SetNestedProperty] Material index {arrayIndex} out of range (0-{materials.Length - 1})"
                                );
                                return false;
                            }
                            currentObject = materials[arrayIndex];
                        }
                        else if (currentObject is System.Collections.IList)
                        {
                            var list = currentObject as System.Collections.IList;
                            if (arrayIndex < 0 || arrayIndex >= list.Count)
                            {
                                Debug.LogWarning(
                                    $"[SetNestedProperty] Index {arrayIndex} out of range (0-{list.Count - 1})"
                                );
                                return false;
                            }
                            currentObject = list[arrayIndex];
                        }
                        else
                        {
                            Debug.LogWarning(
                                $"[SetNestedProperty] Property '{part}' is not an array or list, cannot access by index."
                            );
                            return false;
                        }
                    }
                    currentType = currentObject.GetType();
                }

                // Set the final property
                string finalPart = pathParts[pathParts.Length - 1];

                // Special handling for Material properties (shader properties)
                if (currentObject is Material material && finalPart.StartsWith("_"))
                {
                    // Use the serializer to convert the JToken value first
                    if (value is JArray jArray)
                    {
                        // Try converting to known types that SetColor/SetVector accept
                        if (jArray.Count == 4) {
                            try { Color color = value.ToObject<Color>(inputSerializer); material.SetColor(finalPart, color); return true; } catch { }
                            try { Vector4 vec = value.ToObject<Vector4>(inputSerializer); material.SetVector(finalPart, vec); return true; } catch { }
                        } else if (jArray.Count == 3) {
                            try { Color color = value.ToObject<Color>(inputSerializer); material.SetColor(finalPart, color); return true; } catch { } // ToObject handles conversion to Color
                        } else if (jArray.Count == 2) {
                            try { Vector2 vec = value.ToObject<Vector2>(inputSerializer); material.SetVector(finalPart, vec); return true; } catch { }
                        }
                    }
                    else if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                    {
                        try { material.SetFloat(finalPart, value.ToObject<float>(inputSerializer)); return true; } catch { }
                    }
                    else if (value.Type == JTokenType.Boolean)
                    {
                        try { material.SetFloat(finalPart, value.ToObject<bool>(inputSerializer) ? 1f : 0f); return true; } catch { }
                    }
                    else if (value.Type == JTokenType.String)
                    {
                        // Try converting to Texture using the serializer/converter
                        try {
                            Texture texture = value.ToObject<Texture>(inputSerializer);
                            if (texture != null) {
                                material.SetTexture(finalPart, texture);
                                return true;
                            }
                        } catch { }
                    }

                    Debug.LogWarning(
                        $"[SetNestedProperty] Unsupported or failed conversion for material property '{finalPart}' from value: {value.ToString(Formatting.None)}"
                    );
                    return false;
                }

                // For standard properties (not shader specific)
                PropertyInfo finalPropInfo = currentType.GetProperty(finalPart, flags);
                if (finalPropInfo != null && finalPropInfo.CanWrite)
                {
                    // Use the inputSerializer for conversion
                    object convertedValue = ConvertJTokenToType(value, finalPropInfo.PropertyType, inputSerializer);
                    if (convertedValue != null || value.Type == JTokenType.Null)
                    {
                        finalPropInfo.SetValue(currentObject, convertedValue);
                        return true;
                    }
                    else
                    {
                        Debug.LogWarning($"[SetNestedProperty] Final conversion failed for property '{finalPart}' (Type: {finalPropInfo.PropertyType.Name}) from token: {value.ToString(Formatting.None)}");
                    }
                }
                else
                {
                    FieldInfo finalFieldInfo = currentType.GetField(finalPart, flags);
                    if (finalFieldInfo != null)
                    {
                        // Use the inputSerializer for conversion
                        object convertedValue = ConvertJTokenToType(value, finalFieldInfo.FieldType, inputSerializer);
                        if (convertedValue != null || value.Type == JTokenType.Null)
                        {
                            finalFieldInfo.SetValue(currentObject, convertedValue);
                            return true;
                        }
                        else
                        {
                            Debug.LogWarning($"[SetNestedProperty] Final conversion failed for field '{finalPart}' (Type: {finalFieldInfo.FieldType.Name}) from token: {value.ToString(Formatting.None)}");
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[SetNestedProperty] Could not find final writable property or field '{finalPart}' on type '{currentType.Name}'"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"[SetNestedProperty] Error setting nested property '{path}': {ex.Message}\nToken: {value.ToString(Formatting.None)}"
                );
            }

            return false;
        }


        /// <summary>
        /// Split a property path into parts, handling both dot notation and array indexers
        /// </summary>
        static string[] SplitPropertyPath(string path)
        {
            // Handle complex paths with both dots and array indexers
            List<string> parts = new List<string>();
            int startIndex = 0;
            bool inBrackets = false;

            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];

                if (c == '[')
                {
                    inBrackets = true;
                }
                else if (c == ']')
                {
                    inBrackets = false;
                }
                else if (c == '.' && !inBrackets)
                {
                    // Found a dot separator outside of brackets
                    parts.Add(path.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }
            if (startIndex < path.Length)
            {
                parts.Add(path.Substring(startIndex));
            }
            return parts.ToArray();
        }

        /// <summary>
        /// Simple JToken to Type conversion for common Unity types, using JsonSerializer.
        /// </summary>
        // Pass the input serializer
        static object ConvertJTokenToType(JToken token, Type targetType, JsonSerializer inputSerializer)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                {
                    Debug.LogWarning($"Cannot assign null to non-nullable value type {targetType.Name}. Returning default value.");
                    return Activator.CreateInstance(targetType);
                }
                return null;
            }

            try
            {
                // Use the provided serializer instance which includes our custom converters
                return token.ToObject(targetType, inputSerializer);
            }
            catch (JsonSerializationException jsonEx)
            {
                 Debug.LogError($"JSON Deserialization Error converting token to {targetType.FullName}: {jsonEx.Message}\nToken: {token.ToString(Formatting.None)}");
                 // Optionally re-throw or return null/default
                 // return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
                 throw; // Re-throw to indicate failure higher up
            }
            catch (ArgumentException argEx)
            {
                Debug.LogError($"Argument Error converting token to {targetType.FullName}: {argEx.Message}\nToken: {token.ToString(Formatting.None)}");
                 throw;
            }
            catch (Exception ex)
            {
                 Debug.LogError($"Unexpected error converting token to {targetType.FullName}: {ex}\nToken: {token.ToString(Formatting.None)}");
                 throw;
            }
            // If ToObject succeeded, it would have returned. If it threw, we wouldn't reach here.
             // This fallback logic is likely unreachable if ToObject covers all cases or throws on failure.
             // Debug.LogWarning($"Conversion failed for token to {targetType.FullName}. Token: {token.ToString(Formatting.None)}");
             // return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;
        }

        // --- ParseJTokenTo... helpers are likely redundant now with the serializer approach ---
        // Keep them temporarily for reference or if specific fallback logic is ever needed.

        static Vector3 ParseJTokenToVector3(JToken token)
        {
            // ... (implementation - likely replaced by Vector3Converter) ...
            // Consider removing these if the serializer handles them reliably.
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z"))
            {
                return new Vector3(obj["x"].ToObject<float>(), obj["y"].ToObject<float>(), obj["z"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 3)
            {
                 return new Vector3(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Vector3 using fallback. Returning Vector3.zero.");
            return Vector3.zero;

        }

        static Vector2 ParseJTokenToVector2(JToken token)
        {
            // ... (implementation - likely replaced by Vector2Converter) ...
             if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y"))
            {
                return new Vector2(obj["x"].ToObject<float>(), obj["y"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 2)
            {
                 return new Vector2(arr[0].ToObject<float>(), arr[1].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Vector2 using fallback. Returning Vector2.zero.");
            return Vector2.zero;
        }

        static Quaternion ParseJTokenToQuaternion(JToken token)
        {
            // ... (implementation - likely replaced by QuaternionConverter) ...
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("z") && obj.ContainsKey("w"))
            {
                return new Quaternion(obj["x"].ToObject<float>(), obj["y"].ToObject<float>(), obj["z"].ToObject<float>(), obj["w"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 4)
            {
                 return new Quaternion(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Quaternion using fallback. Returning Quaternion.identity.");
            return Quaternion.identity;
        }

        static Color ParseJTokenToColor(JToken token)
        {
             // ... (implementation - likely replaced by ColorConverter) ...
            if (token is JObject obj && obj.ContainsKey("r") && obj.ContainsKey("g") && obj.ContainsKey("b") && obj.ContainsKey("a"))
            {
                return new Color(obj["r"].ToObject<float>(), obj["g"].ToObject<float>(), obj["b"].ToObject<float>(), obj["a"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 4)
            {
                 return new Color(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Color using fallback. Returning Color.white.");
            return Color.white;
        }

        static Rect ParseJTokenToRect(JToken token)
        {
             // ... (implementation - likely replaced by RectConverter) ...
            if (token is JObject obj && obj.ContainsKey("x") && obj.ContainsKey("y") && obj.ContainsKey("width") && obj.ContainsKey("height"))
            {
                return new Rect(obj["x"].ToObject<float>(), obj["y"].ToObject<float>(), obj["width"].ToObject<float>(), obj["height"].ToObject<float>());
            }
            if (token is JArray arr && arr.Count >= 4)
            {
                 return new Rect(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>(), arr[3].ToObject<float>());
            }
            Debug.LogWarning($"Could not parse JToken '{token}' as Rect using fallback. Returning Rect.zero.");
            return Rect.zero;
        }

        static Bounds ParseJTokenToBounds(JToken token)
        {
             // ... (implementation - likely replaced by BoundsConverter) ...
            if (token is JObject obj && obj.ContainsKey("center") && obj.ContainsKey("size"))
            {
                // Requires Vector3 conversion, which should ideally use the serializer too
                Vector3 center = ParseJTokenToVector3(obj["center"]); // Or use obj["center"].ToObject<Vector3>(inputSerializer)
                Vector3 size = ParseJTokenToVector3(obj["size"]);     // Or use obj["size"].ToObject<Vector3>(inputSerializer)
                return new Bounds(center, size);
            }
            // Array fallback for Bounds is less intuitive, maybe remove?
            // if (token is JArray arr && arr.Count >= 6)
            // {
            //      return new Bounds(new Vector3(arr[0].ToObject<float>(), arr[1].ToObject<float>(), arr[2].ToObject<float>()), new Vector3(arr[3].ToObject<float>(), arr[4].ToObject<float>(), arr[5].ToObject<float>()));
            // }
            Debug.LogWarning($"Could not parse JToken '{token}' as Bounds using fallback. Returning new Bounds(Vector3.zero, Vector3.zero).");
            return new Bounds(Vector3.zero, Vector3.zero);
        }
        // --- End Redundant Parse Helpers ---

        /// <summary>
        /// Finds a specific UnityEngine.Object based on a find instruction JObject.
        /// Primarily used by UnityEngineObjectConverter during deserialization.
        /// </summary>
        /// <param name="instruction">The JObject containing find instructions (find term, method, component).</param>
        /// <param name="targetType">The type of Unity Object to find.</param>
        /// <returns>The found UnityEngine.Object or null if not found.</returns>
        // Made public static so UnityEngineObjectConverter can call it. Moved from ConvertJTokenToType.
        public static UnityEngine.Object FindObjectByInstruction(JObject instruction, Type targetType)
        {
            string findTerm = instruction["find"]?.ToString();
            string method = instruction["method"]?.ToString()?.ToLower();
            string componentName = instruction["component"]?.ToString(); // Specific component to get

            if (string.IsNullOrEmpty(findTerm))
            {
                Debug.LogWarning("Find instruction missing 'find' term.");
                return null;
            }

            // Use a flexible default search method if none provided
            string searchMethodToUse = string.IsNullOrEmpty(method) ? "by_id_or_name_or_path" : method;

            // If the target is an asset (Material, Texture, ScriptableObject etc.) try AssetDatabase first
            if (typeof(Material).IsAssignableFrom(targetType) ||
                typeof(Texture).IsAssignableFrom(targetType) ||
                typeof(ScriptableObject).IsAssignableFrom(targetType) ||
                targetType.FullName.StartsWith("UnityEngine.U2D") || // Sprites etc.
                typeof(AudioClip).IsAssignableFrom(targetType) ||
                typeof(AnimationClip).IsAssignableFrom(targetType) ||
                typeof(Font).IsAssignableFrom(targetType) ||
                typeof(Shader).IsAssignableFrom(targetType) ||
                typeof(ComputeShader).IsAssignableFrom(targetType) ||
                typeof(GameObject).IsAssignableFrom(targetType) && findTerm.StartsWith("Assets/")) // Prefab check
            {
                // Try loading directly by path/GUID first
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(findTerm, targetType);
                if (asset != null) return asset;
                asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(findTerm); // Try generic if type specific failed
                if (asset != null && targetType.IsAssignableFrom(asset.GetType())) return asset;


                // If direct path failed, try finding by name/type using FindAssets
                string searchFilter = $"t:{targetType.Name} {System.IO.Path.GetFileNameWithoutExtension(findTerm)}"; // Search by type and name
                string[] guids = AssetDatabase.FindAssets(searchFilter);

                if (guids.Length == 1)
                {
                    asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), targetType);
                    if (asset != null) return asset;
                }
                else if (guids.Length > 1)
                {
                    Debug.LogWarning($"[FindObjectByInstruction] Ambiguous asset find: Found {guids.Length} assets matching filter '{searchFilter}'. Provide a full path or unique name.");
                    // Optionally return the first one? Or null? Returning null is safer.
                    return null;
                }
                // If still not found, fall through to scene search (though unlikely for assets)
            }


            // --- Scene Object Search ---
            // Find the GameObject using the internal finder
            GameObject foundGo = ObjectsHelper.FindObject(new JValue(findTerm), searchMethodToUse);

            if (foundGo == null)
            {
                // Don't warn yet, could still be an asset not found above
                // Debug.LogWarning($"Could not find GameObject using instruction: {instruction}");
                return null;
            }

            // Now, get the target object/component from the found GameObject
            if (targetType == typeof(GameObject))
            {
                return foundGo; // We were looking for a GameObject
            }
            else if (typeof(Component).IsAssignableFrom(targetType))
            {
                Type componentToGetType = targetType;
                if (!string.IsNullOrEmpty(componentName))
                {
                    Type specificCompType = FindType(componentName);
                    if (specificCompType != null && typeof(Component).IsAssignableFrom(specificCompType))
                    {
                        componentToGetType = specificCompType;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find component type '{componentName}' specified in find instruction. Falling back to target type '{targetType.Name}'.");
                    }
                }

                Component foundComp = foundGo.GetComponent(componentToGetType);
                if (foundComp == null)
                {
                    Debug.LogWarning($"Found GameObject '{foundGo.name}' but could not find component of type '{componentToGetType.Name}'.");
                }
                return foundComp;
            }
            else
            {
                Debug.LogWarning($"Find instruction handling not implemented for target type: {targetType.Name}");
                return null;
            }
        }


        /// <summary>
        /// Robust component resolver that avoids Assembly.LoadFrom and works with asmdefs.
        /// Searches already-loaded assemblies, prioritizing runtime script assemblies.
        /// </summary>
        internal static Type FindType(string typeName)
        {
            if (ComponentResolver.TryResolve(typeName, out Type resolvedType, out string error))
            {
                return resolvedType;
            }

            // Log the resolver error if type wasn't found
            if (!string.IsNullOrEmpty(error))
            {
                Debug.LogWarning($"[FindType] {error}");
            }

            return null;
        }
    }

    /// <summary>
    /// Robust component resolver that avoids Assembly.LoadFrom and supports assembly definitions.
    /// Prioritizes runtime (Player) assemblies over Editor assemblies.
    /// </summary>
    static class ComponentResolver
    {
        static readonly Dictionary<string, Type> CacheByFqn = new(StringComparer.Ordinal);
        static readonly Dictionary<string, Type> CacheByName = new(StringComparer.Ordinal);

        /// <summary>
        /// Resolve a Component/MonoBehaviour type by short or fully-qualified name.
        /// Prefers runtime (Player) script assemblies; falls back to Editor assemblies.
        /// Never uses Assembly.LoadFrom.
        /// </summary>
        public static bool TryResolve(string nameOrFullName, out Type type, out string error)
        {
            error = string.Empty;
            type = null!;

            // Handle null/empty input
            if (string.IsNullOrWhiteSpace(nameOrFullName))
            {
                error = "Component name cannot be null or empty";
                return false;
            }

            // 1) Exact cache hits
            if (CacheByFqn.TryGetValue(nameOrFullName, out type)) return true;
            if (!nameOrFullName.Contains(".") && CacheByName.TryGetValue(nameOrFullName, out type)) return true;
            type = Type.GetType(nameOrFullName, throwOnError: false);
            if (IsValidComponent(type)) { Cache(type); return true; }

            // 2) Search loaded assemblies (prefer Player assemblies)
            var candidates = FindCandidates(nameOrFullName);
            if (candidates.Count == 1) { type = candidates[0]; Cache(type); return true; }
            if (candidates.Count > 1) { error = Ambiguity(nameOrFullName, candidates); type = null!; return false; }

#if UNITY_EDITOR
            // 3) Last resort: Editor-only TypeCache (fast index)
            var tc = TypeCache.GetTypesDerivedFrom<Component>()
                              .Where(t => NamesMatch(t, nameOrFullName));
            candidates = PreferPlayer(tc).ToList();
            if (candidates.Count == 1) { type = candidates[0]; Cache(type); return true; }
            if (candidates.Count > 1) { error = Ambiguity(nameOrFullName, candidates); type = null!; return false; }
#endif

            error = $"Component type '{nameOrFullName}' not found in loaded runtime assemblies. " +
                    "Use a fully-qualified name (Namespace.TypeName) and ensure the script compiled.";
            type = null!;
            return false;
        }

        static bool NamesMatch(Type t, string q) =>
            t.Name.Equals(q, StringComparison.Ordinal) ||
            (t.FullName?.Equals(q, StringComparison.Ordinal) ?? false);

        static bool IsValidComponent(Type t) =>
            t != null && typeof(Component).IsAssignableFrom(t);

        static void Cache(Type t)
        {
            if (t.FullName != null) CacheByFqn[t.FullName] = t;
            CacheByName[t.Name] = t;
        }

        static List<Type> FindCandidates(string query)
        {
            bool isShort = !query.Contains('.');
            var loaded = AppDomain.CurrentDomain.GetAssemblies();

#if UNITY_EDITOR
            // Names of Player (runtime) script assemblies (asmdefs + Assembly-CSharp)
            var playerAsmNames = new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player).Select(a => a.name),
                StringComparer.Ordinal);

            IEnumerable<Assembly> playerAsms = loaded.Where(a => playerAsmNames.Contains(a.GetName().Name));
            IEnumerable<Assembly> editorAsms = loaded.Except(playerAsms);
#else
            IEnumerable<System.Reflection.Assembly> playerAsms = loaded;
            IEnumerable<System.Reflection.Assembly> editorAsms = Array.Empty<System.Reflection.Assembly>();
#endif
            static IEnumerable<Type> SafeGetTypes(Assembly a)
            {
                try { return a.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { return rtle.Types.Where(t => t != null)!; }
            }

            Func<Type, bool> match = isShort
                ? (t => t.Name.Equals(query, StringComparison.Ordinal))
                : (t => t.FullName!.Equals(query, StringComparison.Ordinal));

            var fromPlayer = playerAsms.SelectMany(SafeGetTypes)
                                       .Where(IsValidComponent)
                                       .Where(match);
            var fromEditor = editorAsms.SelectMany(SafeGetTypes)
                                       .Where(IsValidComponent)
                                       .Where(match);

            var list = new List<Type>(fromPlayer);
            if (list.Count == 0) list.AddRange(fromEditor);
            return list;
        }

#if UNITY_EDITOR
        static IEnumerable<Type> PreferPlayer(IEnumerable<Type> seq)
        {
            var player = new HashSet<string>(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Player).Select(a => a.name),
                StringComparer.Ordinal);

            return seq.OrderBy(t => player.Contains(t.Assembly.GetName().Name) ? 0 : 1);
        }
#endif

        static string Ambiguity(string query, IEnumerable<Type> cands)
        {
            var lines = cands.Select(t => $"{t.FullName} (assembly {t.Assembly.GetName().Name})");
            return $"Multiple component types matched '{query}':\n - " + string.Join("\n - ", lines) +
                   "\nProvide a fully qualified type name to disambiguate.";
        }

        /// <summary>
        /// Gets all accessible property and field names from a component type.
        /// </summary>
        public static List<string> GetAllComponentProperties(Type componentType)
        {
            if (componentType == null) return new List<string>();

            var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                         .Where(p => p.CanRead && p.CanWrite)
                                         .Select(p => p.Name);

            var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                                     .Where(f => !f.IsInitOnly && !f.IsLiteral)
                                     .Select(f => f.Name);

            // Also include SerializeField private fields (common in Unity)
            var serializeFields = componentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                                              .Where(f => f.GetCustomAttribute<SerializeField>() != null)
                                              .Select(f => f.Name);

            return properties.Concat(fields).Concat(serializeFields).Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Uses AI to suggest the most likely property matches for a user's input.
        /// </summary>
        public static List<string> GetAIPropertySuggestions(string userInput, List<string> availableProperties)
        {
            if (string.IsNullOrWhiteSpace(userInput) || !availableProperties.Any())
                return new List<string>();

            // Simple caching to avoid repeated AI calls for the same input
            var cacheKey = $"{userInput.ToLowerInvariant()}:{string.Join(",", availableProperties)}";
            if (PropertySuggestionCache.TryGetValue(cacheKey, out var cached))
                return cached;

            try
            {
                var prompt = $"A Unity developer is trying to set a component property but used an incorrect name.\n\n" +
                             $"User requested: \"{userInput}\"\n" +
                             $"Available properties: [{string.Join(", ", availableProperties)}]\n\n" +
                             $"Find 1-3 most likely matches considering:\n" +
                             $"- Unity Inspector display names vs actual field names (e.g., \"Max Reach Distance\" → \"maxReachDistance\")\n" +
                             $"- camelCase vs PascalCase vs spaces\n" +
                             $"- Similar meaning/semantics\n" +
                             $"- Common Unity naming patterns\n\n" +
                             $"Return ONLY the matching property names, comma-separated, no quotes or explanation.\n" +
                             $"If confidence is low (<70%), return empty string.\n\n" +
                             $"Examples:\n" +
                             $"- \"Max Reach Distance\" → \"maxReachDistance\"\n" +
                             $"- \"Health Points\" → \"healthPoints, hp\"\n" +
                             $"- \"Move Speed\" → \"moveSpeed, movementSpeed\"";

                // For now, we'll use a simple rule-based approach that mimics AI behavior
                // This can be replaced with actual AI calls later
                var suggestions = GetRuleBasedSuggestions(userInput, availableProperties);

                PropertySuggestionCache[cacheKey] = suggestions;
                return suggestions;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AI Property Matching] Error getting suggestions for '{userInput}': {ex.Message}");
                return new List<string>();
            }
        }

        static readonly Dictionary<string, List<string>> PropertySuggestionCache = new();

        /// <summary>
        /// Rule-based suggestions that mimic AI behavior for property matching.
        /// This provides immediate value while we could add real AI integration later.
        /// </summary>
        static List<string> GetRuleBasedSuggestions(string userInput, List<string> availableProperties)
        {
            var suggestions = new List<string>();
            var cleanedInput = userInput.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

            foreach (var property in availableProperties)
            {
                var cleanedProperty = property.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");

                // Exact match after cleaning
                if (cleanedProperty == cleanedInput)
                {
                    suggestions.Add(property);
                    continue;
                }

                // Check if property contains all words from input
                var inputWords = userInput.ToLowerInvariant().Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (inputWords.All(word => cleanedProperty.Contains(word.ToLowerInvariant())))
                {
                    suggestions.Add(property);
                    continue;
                }

                // Levenshtein distance for close matches
                if (LevenshteinDistance(cleanedInput, cleanedProperty) <= Math.Max(2, cleanedInput.Length / 4))
                {
                    suggestions.Add(property);
                }
            }

            // Prioritize exact matches, then by similarity
            return suggestions.OrderBy(s => LevenshteinDistance(cleanedInput, s.ToLowerInvariant().Replace(" ", "")))
                             .Take(3)
                             .ToList();
        }

        /// <summary>
        /// Calculates Levenshtein distance between two strings for similarity matching.
        /// </summary>
        static int LevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            var matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s2[j - 1] == s1[i - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(Math.Min(
                        matrix[i - 1, j] + 1,      // deletion
                        matrix[i, j - 1] + 1),     // insertion
                        matrix[i - 1, j - 1] + cost); // substitution
                }
            }

            return matrix[s1.Length, s2.Length];
        }

        /// <summary>
        /// Get the center of the gameobject based on collider or meshrenderer bounds
        /// </summary>
        /// <param name="targetGo">The gameobject to target</param>
        /// <returns>Vector3 world position of the center</returns>
        public static Vector3 GetObjectWorldCenter(GameObject targetGo)
        {
            if (targetGo.TryGetComponent<Collider>(out var collider))
            {
                return collider.bounds.center;
            }
            if (targetGo.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                return meshRenderer.bounds.center;
            }

            return targetGo.transform.position;
        }
    }
}
