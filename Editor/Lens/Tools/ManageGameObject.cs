#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using Becool.UnityMcpLens.Editor.Services.GameObjects;
using Becool.UnityMcpLens.Editor.ToolRegistry; // For Response class
using Becool.UnityMcpLens.Editor.ToolRegistry.Parameters;
using Becool.UnityMcpLens.Editor.Lens;

namespace Becool.UnityMcpLens.Editor.Tools
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
        static readonly UnityGameObjectAdapter TsamGameObjectAdapter = new UnityGameObjectAdapter();
        static readonly UnityComponentMutationAdapter TsamComponentMutationAdapter = new UnityComponentMutationAdapter(TsamGameObjectAdapter);
        static readonly GameObjectRequestNormalizer TsamRequestNormalizer = new GameObjectRequestNormalizer();
        static readonly GameObjectQueryService TsamQueryService = new GameObjectQueryService(TsamGameObjectAdapter);
        static readonly GameObjectMutationService TsamMutationService = new GameObjectMutationService(TsamGameObjectAdapter);
        static readonly GameObjectComponentReadService TsamComponentReadService = new GameObjectComponentReadService(TsamGameObjectAdapter);
        static readonly GameObjectComponentMutationService TsamComponentMutationService = new GameObjectComponentMutationService(TsamGameObjectAdapter, TsamComponentMutationAdapter);

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
                            "create", "modify", "delete", "find", "get_selection", "get_bounds", "get_builtin_assets",
                            "get_components", "get_component", "add_component", "remove_component", "set_component_property"
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
        [McpTool("Unity.ManageGameObject", Description, Groups = new string[] { "core", "scene" }, EnabledByDefault = true)]
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
                        if (IsSimpleModifyRequest(@params))
                            return HandleTsamSimpleModify(@params, targetToken, searchMethod);
                        return ModifyGameObject(@params, targetToken, searchMethod);
                    case "delete":
                        return DeleteGameObject(targetToken, searchMethod);
                    case "find":
                        return HandleTsamFind(@params, targetToken, searchMethod);
                    case "get_selection":
                        return HandleTsamGetSelection(@params);
                    case "get_bounds":
                        return HandleTsamGetBounds(@params, targetToken, searchMethod);
                    case "get_builtin_assets":
                        return GetBuiltinAssets();
                    case "get_components":
                        return HandleTsamGetComponents(@params, targetToken, searchMethod);
                    case "get_component":
                        return HandleTsamGetComponent(@params, targetToken, searchMethod);
                    case "add_component":
                        return HandleTsamAddComponent(@params, targetToken, searchMethod);
                    case "remove_component":
                        return HandleTsamRemoveComponent(@params, targetToken, searchMethod);
                    case "set_component_property":
                        return HandleTsamSetComponentProperty(@params, targetToken, searchMethod);

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

        static object HandleTsamFind(JObject @params, JToken targetToken, string searchMethod)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", "find", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectQueryRequest request;
                using (timing.Measure("normalization"))
                {
                    request = TsamRequestNormalizer.NormalizeQuery(@params, targetToken, searchMethod);
                }

                using (timing.Measure("service"))
                {
                    result = TsamQueryService.Find(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] find failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing action 'find': {ex.Message}", errorKind);
            }

            return ShapeTsamResponse(result, timing, errorKind);
        }

        static object HandleTsamGetSelection(JObject @params)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", "get_selection", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                    // No action-specific parameters to normalize.
                }

                using (timing.Measure("service"))
                {
                    result = TsamQueryService.GetSelection(timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] get_selection failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing action 'get_selection': {ex.Message}", errorKind);
            }

            return ShapeTsamResponse(result, timing, errorKind);
        }

        static object HandleTsamGetBounds(JObject @params, JToken targetToken, string searchMethod)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", "get_bounds", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectBoundsRequest request;
                using (timing.Measure("normalization"))
                {
                    request = TsamRequestNormalizer.NormalizeBounds(targetToken, searchMethod);
                }

                using (timing.Measure("service"))
                {
                    result = TsamQueryService.GetBounds(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] get_bounds failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing action 'get_bounds': {ex.Message}", errorKind);
            }

            return ShapeTsamResponse(result, timing, errorKind);
        }

        static object HandleTsamSimpleModify(JObject @params, JToken targetToken, string searchMethod)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", "modify", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectSimpleModifyRequest request;
                using (timing.Measure("normalization"))
                {
                    request = TsamRequestNormalizer.NormalizeSimpleModify(@params, targetToken, searchMethod);
                }

                using (timing.Measure("service"))
                {
                    result = TsamMutationService.ModifySimple(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] modify failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing action 'modify': {ex.Message}", errorKind);
            }

            return ShapeTsamResponse(result, timing, errorKind);
        }

        static object HandleTsamGetComponents(JObject @params, JToken targetToken, string searchMethod)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", "get_components", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectComponentGetRequest request;
                using (timing.Measure("normalization"))
                {
                    request = TsamRequestNormalizer.NormalizeComponentGet(@params, targetToken, searchMethod);
                }

                using (timing.Measure("service"))
                {
                    result = request?.target == null || request.target.isNull || request.target.isEmptyString
                        ? GameObjectOperationResult.Error(
                            "'target' parameter required for get_components.",
                            "missing_target",
                            new { errorKind = "missing_target", code = "missing_target" })
                        : TsamComponentReadService.GetComponentsLegacy(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] get_components failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing action 'get_components': {ex.Message}", errorKind);
            }

            return ShapeLegacyComponentReadResponse(result, timing, errorKind, @params, "get_components");
        }

        static object HandleTsamGetComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", "get_component", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectComponentGetRequest request;
                using (timing.Measure("normalization"))
                {
                    request = TsamRequestNormalizer.NormalizeComponentGet(@params, targetToken, searchMethod);
                }

                using (timing.Measure("service"))
                {
                    if (request?.target == null || request.target.isNull || request.target.isEmptyString)
                    {
                        result = GameObjectOperationResult.Error(
                            "'target' parameter required for get_component.",
                            "missing_target",
                            new { errorKind = "missing_target", code = "missing_target" });
                    }
                    else if (string.IsNullOrWhiteSpace(request.componentName))
                    {
                        result = GameObjectOperationResult.Error(
                            "'component_name' parameter required for get_component.",
                            "missing_component_name",
                            new { errorKind = "missing_component_name", code = "missing_component_name" });
                    }
                    else
                    {
                        result = TsamComponentReadService.GetComponentLegacy(request, timing);
                    }
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] get_component failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing action 'get_component': {ex.Message}", errorKind);
            }

            return ShapeLegacyComponentReadResponse(result, timing, errorKind, @params, "get_component");
        }

        static object HandleTsamAddComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            return HandleTsamComponentMutation(@params, targetToken, searchMethod, "add");
        }

        static object HandleTsamRemoveComponent(JObject @params, JToken targetToken, string searchMethod)
        {
            return HandleTsamComponentMutation(@params, targetToken, searchMethod, "remove");
        }

        static object HandleTsamSetComponentProperty(JObject @params, JToken targetToken, string searchMethod)
        {
            return HandleTsamComponentMutation(@params, targetToken, searchMethod, "setProperties");
        }

        static object HandleTsamComponentMutation(JObject @params, JToken targetToken, string searchMethod, string operation)
        {
            var timing = new GameObjectToolTiming("Unity.ManageGameObject", operation == "setProperties" ? "set_component_property" : $"{operation}_component", GetUtf8ByteCount(@params?.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectComponentMutationRequest request;
                using (timing.Measure("normalization"))
                {
                    request = TsamRequestNormalizer.NormalizeComponentMutation(@params, targetToken, searchMethod, operation);
                }

                using (timing.Measure("service"))
                {
                    result = TsamComponentMutationService.ApplyLegacy(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                Debug.LogError($"[ManageGameObject.TSAM] component mutation '{operation}' failed: {ex}");
                result = GameObjectOperationResult.Error($"Internal error processing component mutation '{operation}': {ex.Message}", errorKind);
            }

            return ShapeLegacyComponentMutationResponse(result, timing, errorKind);
        }

        static object ShapeTsamResponse(GameObjectOperationResult result, GameObjectToolTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, result.data)
                    : Response.Error(result.message, result.errorData);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object ShapeLegacyComponentMutationResponse(GameObjectOperationResult result, GameObjectToolTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, result.data)
                    : Response.Error(result.message, result.errorData);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object ShapeLegacyComponentReadResponse(GameObjectOperationResult result, GameObjectToolTiming timing, string fallbackErrorKind, JObject @params, string action)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(
                        result.message,
                        ShapeComponentPayload(
                            result.data,
                            result.message,
                            new
                            {
                                action,
                                target = @params?["target"]?.ToString(),
                                search_method = @params?["search_method"]?.ToString(),
                                component_name = @params?["component_name"]?.ToString() ?? @params?["componentName"]?.ToString(),
                                include_non_public_serialized = @params?["include_non_public_serialized"]?.ToObject<bool>() ?? @params?["includeNonPublicSerialized"]?.ToObject<bool>() ?? false
                            }))
                    : Response.Error(result.message, result.errorData);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static bool IsSimpleModifyRequest(JObject parameters)
        {
            if (parameters == null)
                return true;

            string[] legacyModifyKeys =
            {
                "components_to_remove",
                "componentsToRemove",
                "components_to_add",
                "componentsToAdd",
                "component_properties",
                "componentProperties",
                "component_name",
                "componentName",
                "save_as_prefab",
                "saveAsPrefab",
                "prefab_path",
                "prefabPath",
                "prefab_folder",
                "prefabFolder",
                "primitive_type",
                "primitiveType"
            };

            return legacyModifyKeys.All(key => parameters[key] == null);
        }

        static int GetUtf8ByteCount(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
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
                        var center = UnityComponentResolver.GetObjectWorldCenter(targetGo);
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
                    object goId = UnityApiAdapter.GetObjectId(targetGo);
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

        static object GetBuiltinAssets()
        {
            return Response.Success("Retrieved builtin GameObject asset hints.", new
            {
                primitiveTypes = Enum.GetNames(typeof(PrimitiveType)),
                builtinResources = new[]
                {
                    "Default-Material",
                    "Sprites-Default",
                    "UI/Skin/Background.psd",
                    "UI/Skin/Knob.psd"
                },
                note = "Use create with primitive_type for primitives, or Unity.Asset.Search for project assets."
            });
        }

        static object ShapeComponentPayload(object data, string summary, object detailRef)
        {
            return ToolResultCompactor.ShapeJsonPayload("Unity.ManageGameObject", summary, data, detailRef);
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
            var result = TsamComponentMutationAdapter.ApplyAddComponent(new UnityGameObjectHandle(targetGo), typeName, properties);
            return result.success ? null : Response.Error(result.message, new { errorKind = result.errorKind, errors = result.errors });
        }

        /// <summary>
        /// Removes a component by type name.
        /// Returns null on success, or an error response object on failure.
        /// </summary>
        static object RemoveComponentInternal(GameObject targetGo, string typeName)
        {
            var result = TsamComponentMutationAdapter.ApplyRemoveComponent(new UnityGameObjectHandle(targetGo), typeName, null);
            return result.success ? null : Response.Error(result.message, new { errorKind = result.errorKind, errors = result.errors });
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
            UnityComponentMutationStatus result;
            if (targetComponentInstance != null)
            {
                result = TsamComponentMutationAdapter.ApplySetPropertiesToComponent(targetComponentInstance, propertiesToSet);
            }
            else
            {
                result = TsamComponentMutationAdapter.ApplySetProperties(new UnityGameObjectHandle(targetGo), compName, null, propertiesToSet);
            }

            return result.success ? null : Response.Error(result.message, new { errorKind = result.errorKind, errors = result.errors });
        }

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
            return TsamComponentMutationAdapter.FindObjectByInstruction(instruction, targetType);
        }

        /// <summary>
        /// Robust component resolver that avoids Assembly.LoadFrom and works with asmdefs.
        /// Searches already-loaded assemblies, prioritizing runtime script assemblies.
        /// </summary>
        internal static Type FindType(string typeName)
        {
            return TsamComponentMutationAdapter.FindType(typeName);
        }
    }

}
