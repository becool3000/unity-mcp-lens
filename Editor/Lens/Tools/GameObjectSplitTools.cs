#nullable disable
using System;
using System.Text;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using Becool.UnityMcpLens.Editor.Services.GameObjects;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class GameObjectSplitTools
    {
        const string InspectToolName = "Unity.GameObject.Inspect";
        const string PreviewChangesToolName = "Unity.GameObject.PreviewChanges";
        const string ApplyChangesToolName = "Unity.GameObject.ApplyChanges";
        const string ListComponentsToolName = "Unity.GameObject.ListComponents";
        const string GetComponentToolName = "Unity.GameObject.GetComponent";
        const string PreviewComponentChangesToolName = "Unity.GameObject.PreviewComponentChanges";
        const string ApplyComponentChangesToolName = "Unity.GameObject.ApplyComponentChanges";
        const string PreviewCreateToolName = "Unity.GameObject.PreviewCreate";
        const string CreateToolName = "Unity.GameObject.Create";
        const string PreviewDeleteToolName = "Unity.GameObject.PreviewDelete";
        const string DeleteToolName = "Unity.GameObject.Delete";

        public const string InspectDescription = @"Inspects scene GameObjects without mutation.

Modes:
  find: Finds scene objects by target/searchTerm and searchMethod.
  selection: Returns the current Unity selection.
  bounds: Returns renderer/collider bounds for a target GameObject.

Returns compact GameObject data with stable string ids for model-facing use.";

        public const string PreviewChangesDescription = @"Previews simple scene GameObject changes without mutation.

Supports name, setActive, tag, layer, position, positionType, rotation, scale, and parent.
Does not call Undo, mark scenes dirty, save scenes, or create tags.";

        public const string ApplyChangesDescription = @"Applies simple scene GameObject changes after the same validation used by PreviewChanges.

Supports name, setActive, tag, layer, position, positionType, rotation, scale, and parent.
Returns compact readback data and reports repeated identical calls as applied=false.";

        public const string ListComponentsDescription = @"Lists components on a scene GameObject without mutation.

Returns compact component inventory only. Use Unity.GameObject.GetComponent for serialized component data.";

        public const string GetComponentDescription = @"Reads one serialized component from a scene GameObject without mutation.

Use componentName and optional componentIndex to select among duplicate component types.";

        public const string PreviewComponentChangesDescription = @"Previews component changes on a scene GameObject without mutation.

Supports add, remove, and setProperties operations. Preview does not call Undo, add/remove components, set properties, or mark objects dirty.";

        public const string ApplyComponentChangesDescription = @"Applies component changes on a scene GameObject after the same validation used by PreviewComponentChanges.

Supports add, remove, and setProperties operations. Returns compact readback data and reports no-op property updates as applied=false.";

        public const string PreviewCreateDescription = @"Previews GameObject creation without mutation.

Supports empty objects, primitives, prefab instantiation, save-as-prefab planning, parent, transform, tag, layer, and initial component validation.";

        public const string CreateDescription = @"Creates or instantiates a scene GameObject after the same validation used by PreviewCreate.

Supports empty objects, primitives, prefab instantiation, save-as-prefab, parent, transform, tag, layer, and initial components.";

        public const string PreviewDeleteDescription = @"Previews scene GameObject deletion without mutation.

Defaults to single-target safety and reports ambiguous matches unless findAll=true.";

        public const string DeleteDescription = @"Deletes scene GameObjects after the same validation used by PreviewDelete.

Defaults to single-target safety and requires findAll=true to delete multiple matches.";

        static readonly UnityGameObjectAdapter GameObjectAdapter = new UnityGameObjectAdapter();
        static readonly UnityComponentMutationAdapter ComponentMutationAdapter = new UnityComponentMutationAdapter(GameObjectAdapter);
        static readonly UnityGameObjectLifecycleAdapter LifecycleAdapter = new UnityGameObjectLifecycleAdapter(GameObjectAdapter);
        static readonly GameObjectRequestNormalizer RequestNormalizer = new GameObjectRequestNormalizer();
        static readonly GameObjectQueryService QueryService = new GameObjectQueryService(GameObjectAdapter);
        static readonly GameObjectMutationService MutationService = new GameObjectMutationService(GameObjectAdapter);
        static readonly GameObjectComponentReadService ComponentReadService = new GameObjectComponentReadService(GameObjectAdapter);
        static readonly GameObjectComponentMutationService ComponentMutationService = new GameObjectComponentMutationService(GameObjectAdapter, ComponentMutationAdapter);
        static readonly GameObjectLifecycleService LifecycleService = new GameObjectLifecycleService(LifecycleAdapter, ComponentMutationAdapter);

        [McpSchema(InspectToolName)]
        public static object GetInspectSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    mode = new
                    {
                        type = "string",
                        description = "Inspection mode.",
                        @enum = new[] { "find", "selection", "bounds" }
                    },
                    target = ToolSchemaFragments.TargetRef("GameObject target, path, name, or id."),
                    searchMethod = ToolSchemaFragments.SearchMethod(),
                    searchTerm = new { type = "string", description = "Search term for find mode. Defaults to target when omitted." },
                    findAll = new { type = "boolean", description = "When true, return all matching objects." },
                    searchInChildren = new { type = "boolean", description = "Search under the target object." },
                    searchInactive = new { type = "boolean", description = "Include inactive scene objects." }
                },
                required = new[] { "mode" }
            };
        }

        [McpSchema(PreviewChangesToolName)]
        public static object GetPreviewChangesSchema()
        {
            return BuildChangeSchema();
        }

        [McpSchema(ApplyChangesToolName)]
        public static object GetApplyChangesSchema()
        {
            return BuildChangeSchema();
        }

        [McpSchema(ListComponentsToolName)]
        public static object GetListComponentsSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = ToolSchemaFragments.TargetRef("GameObject target, path, name, or id."),
                    searchMethod = ToolSchemaFragments.SearchMethod(),
                    searchInactive = new { type = "boolean", description = "Include inactive scene objects while resolving the target." }
                },
                required = new[] { "target" }
            };
        }

        [McpSchema(GetComponentToolName)]
        public static object GetGetComponentSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = ToolSchemaFragments.TargetRef("GameObject target, path, name, or id."),
                    componentName = new { type = "string", description = "Component type name, full type name, or resolvable Unity component name." },
                    componentIndex = new { type = "integer", description = "0-based index among components matching componentName. Defaults to 0." },
                    searchMethod = ToolSchemaFragments.SearchMethod(),
                    searchInactive = new { type = "boolean", description = "Include inactive scene objects while resolving the target." },
                    includeNonPublicSerialized = new { type = "boolean", description = "Include private fields marked [SerializeField] in component data." }
                },
                required = new[] { "target", "componentName" }
            };
        }

        [McpSchema(PreviewComponentChangesToolName)]
        public static object GetPreviewComponentChangesSchema()
        {
            return BuildComponentChangeSchema();
        }

        [McpSchema(ApplyComponentChangesToolName)]
        public static object GetApplyComponentChangesSchema()
        {
            return BuildComponentChangeSchema();
        }

        [McpSchema(PreviewCreateToolName)]
        public static object GetPreviewCreateSchema()
        {
            return BuildCreateSchema();
        }

        [McpSchema(CreateToolName)]
        public static object GetCreateSchema()
        {
            return BuildCreateSchema();
        }

        [McpSchema(PreviewDeleteToolName)]
        public static object GetPreviewDeleteSchema()
        {
            return BuildDeleteSchema();
        }

        [McpSchema(DeleteToolName)]
        public static object GetDeleteSchema()
        {
            return BuildDeleteSchema();
        }

        [McpTool(InspectToolName, InspectDescription, "Inspect GameObject", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object Inspect(JObject @params)
        {
            @params ??= new JObject();
            string mode = GetString(@params, "mode", "Mode")?.ToLowerInvariant();
            string requestType = string.IsNullOrEmpty(mode) ? "inspect" : $"inspect_{mode}";
            var timing = new GameObjectToolTiming(InspectToolName, requestType, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result = null;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                    if (string.IsNullOrEmpty(mode))
                    {
                        result = GameObjectOperationResult.Error("mode is required.", "missing_mode", new { errorKind = "missing_mode" });
                        errorKind = "missing_mode";
                    }
                }

                if (!string.IsNullOrEmpty(mode))
                {
                    switch (mode)
                    {
                        case "find":
                        {
                            GameObjectQueryRequest request;
                            using (timing.Measure("normalization"))
                            {
                                request = RequestNormalizer.NormalizeQuery(@params, GetToken(@params, "target", "Target"), GetSearchMethod(@params));
                            }

                            using (timing.Measure("service"))
                            {
                                result = QueryService.InspectFind(request, timing);
                            }
                            break;
                        }
                        case "selection":
                            using (timing.Measure("service"))
                            {
                                result = QueryService.GetSelection(timing);
                            }
                            break;
                        case "bounds":
                        {
                            GameObjectBoundsRequest request;
                            using (timing.Measure("normalization"))
                            {
                                request = RequestNormalizer.NormalizeBounds(GetToken(@params, "target", "Target"), GetSearchMethod(@params));
                            }

                            using (timing.Measure("service"))
                            {
                                result = QueryService.GetBounds(request, timing);
                            }
                            break;
                        }
                        default:
                            result = GameObjectOperationResult.Error($"Unknown inspect mode: '{mode}'.", "invalid_mode", new { errorKind = "invalid_mode" });
                            errorKind = "invalid_mode";
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error processing GameObject inspect: {ex.Message}", errorKind);
            }

            result ??= GameObjectOperationResult.Error("GameObject inspect did not produce a result.", "missing_result");
            return ShapeResponse(result, timing, errorKind);
        }

        [McpTool(PreviewChangesToolName, PreviewChangesDescription, "Preview GameObject Changes", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewChanges(JObject @params)
        {
            return HandleChangeTool(PreviewChangesToolName, "preview", @params, apply: false);
        }

        [McpTool(ApplyChangesToolName, ApplyChangesDescription, "Apply GameObject Changes", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ApplyChanges(JObject @params)
        {
            return HandleChangeTool(ApplyChangesToolName, "apply", @params, apply: true);
        }

        [McpTool(ListComponentsToolName, ListComponentsDescription, "List GameObject Components", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ListComponents(JObject @params)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(ListComponentsToolName, "list_components", GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectComponentListRequest request;
                using (timing.Measure("normalization"))
                {
                    request = RequestNormalizer.NormalizeComponentList(@params, GetToken(@params, "target", "Target"), GetSearchMethod(@params));
                }

                using (timing.Measure("service"))
                {
                    result = ComponentReadService.ListComponents(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error listing GameObject components: {ex.Message}", errorKind);
            }

            return ShapeComponentReadResponse(ListComponentsToolName, result, timing, errorKind);
        }

        [McpTool(GetComponentToolName, GetComponentDescription, "Get GameObject Component", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object GetComponent(JObject @params)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(GetComponentToolName, "get_component", GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectComponentGetRequest request;
                using (timing.Measure("normalization"))
                {
                    request = RequestNormalizer.NormalizeComponentGet(@params, GetToken(@params, "target", "Target"), GetSearchMethod(@params));
                }

                using (timing.Measure("service"))
                {
                    result = ComponentReadService.GetComponent(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error getting GameObject component: {ex.Message}", errorKind);
            }

            return ShapeComponentReadResponse(GetComponentToolName, result, timing, errorKind);
        }

        [McpTool(PreviewComponentChangesToolName, PreviewComponentChangesDescription, "Preview GameObject Component Changes", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewComponentChanges(JObject @params)
        {
            return HandleComponentChangeTool(PreviewComponentChangesToolName, "preview_component_changes", @params, apply: false);
        }

        [McpTool(ApplyComponentChangesToolName, ApplyComponentChangesDescription, "Apply GameObject Component Changes", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ApplyComponentChanges(JObject @params)
        {
            return HandleComponentChangeTool(ApplyComponentChangesToolName, "apply_component_changes", @params, apply: true);
        }

        [McpTool(PreviewCreateToolName, PreviewCreateDescription, "Preview GameObject Create", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewCreate(JObject @params)
        {
            return HandleCreateTool(PreviewCreateToolName, "preview_create", @params, apply: false);
        }

        [McpTool(CreateToolName, CreateDescription, "Create GameObject", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object Create(JObject @params)
        {
            return HandleCreateTool(CreateToolName, "create", @params, apply: true);
        }

        [McpTool(PreviewDeleteToolName, PreviewDeleteDescription, "Preview GameObject Delete", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewDelete(JObject @params)
        {
            return HandleDeleteTool(PreviewDeleteToolName, "preview_delete", @params, apply: false);
        }

        [McpTool(DeleteToolName, DeleteDescription, "Delete GameObject", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object Delete(JObject @params)
        {
            return HandleDeleteTool(DeleteToolName, "delete", @params, apply: true);
        }

        static object HandleChangeTool(string toolName, string requestType, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(toolName, requestType, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectSimpleModifyRequest request;
                using (timing.Measure("normalization"))
                {
                    request = RequestNormalizer.NormalizeSimpleModify(@params, GetToken(@params, "target", "Target"), GetSearchMethod(@params));
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? MutationService.ApplySimple(request, timing)
                        : MutationService.PreviewSimple(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error processing GameObject {requestType}: {ex.Message}", errorKind);
            }

            return ShapeResponse(result, timing, errorKind);
        }

        static object HandleComponentChangeTool(string toolName, string requestType, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(toolName, requestType, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectComponentMutationRequest request;
                using (timing.Measure("normalization"))
                {
                    request = RequestNormalizer.NormalizeComponentMutation(@params, GetToken(@params, "target", "Target"), GetSearchMethod(@params));
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? ComponentMutationService.Apply(request, timing)
                        : ComponentMutationService.Preview(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error processing GameObject component changes: {ex.Message}", errorKind);
            }

            return ShapeResponse(result, timing, errorKind);
        }

        static object HandleCreateTool(string toolName, string requestType, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(toolName, requestType, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectCreateRequest request;
                using (timing.Measure("normalization"))
                {
                    request = RequestNormalizer.NormalizeCreate(@params, legacyCompatibility: false);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? LifecycleService.ApplyCreate(request, timing)
                        : LifecycleService.PreviewCreate(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error processing GameObject create: {ex.Message}", errorKind);
            }

            return ShapeResponse(result, timing, errorKind);
        }

        static object HandleDeleteTool(string toolName, string requestType, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(toolName, requestType, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            GameObjectOperationResult result;
            string errorKind = null;

            try
            {
                GameObjectDeleteRequest request;
                using (timing.Measure("normalization"))
                {
                    request = RequestNormalizer.NormalizeDelete(@params, GetToken(@params, "target", "Target"), GetSearchMethod(@params), legacyCompatibility: false);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? LifecycleService.ApplyDelete(request, timing)
                        : LifecycleService.PreviewDelete(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = GameObjectOperationResult.Error($"Internal error processing GameObject delete: {ex.Message}", errorKind);
            }

            return ShapeResponse(result, timing, errorKind);
        }

        static object ShapeResponse(GameObjectOperationResult result, GameObjectToolTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, result.data)
                    : Response.Error(result.message, EnsureErrorKind(result.errorData, result.errorKind ?? fallbackErrorKind));
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object ShapeComponentReadResponse(string toolName, GameObjectOperationResult result, GameObjectToolTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, ToolResultCompactor.ShapeJsonPayload(toolName, result.message, result.data))
                    : Response.Error(result.message, EnsureErrorKind(result.errorData, result.errorKind ?? fallbackErrorKind));
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object EnsureErrorKind(object errorData, string errorKind)
        {
            if (errorData != null)
                return errorData;

            return new
            {
                errorKind
            };
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                    return token;
            }

            return null;
        }

        static string GetString(JObject parameters, params string[] names)
        {
            return GetToken(parameters, names)?.ToString();
        }

        static string GetSearchMethod(JObject parameters)
        {
            return GetString(parameters, "searchMethod", "search_method", "SearchMethod");
        }

        static int GetUtf8ByteCount(string text)
        {
            return string.IsNullOrEmpty(text) ? 0 : Encoding.UTF8.GetByteCount(text);
        }

        static object BuildChangeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = ToolSchemaFragments.TargetRef("GameObject target, path, name, or id."),
                    searchMethod = ToolSchemaFragments.SearchMethod(),
                    name = new { type = "string", description = "New GameObject name." },
                    setActive = new { type = "boolean", description = "Set GameObject active state." },
                    tag = new { type = "string", description = "Tag to assign. Empty means Untagged." },
                    layer = new { type = "string", description = "Layer name to assign." },
                    position = ToolSchemaFragments.Vector3Array("Local position [x, y, z]."),
                    positionType = new
                    {
                        type = "string",
                        description = "How to interpret position.",
                        @enum = new[] { "center", "pivot" }
                    },
                    rotation = ToolSchemaFragments.Vector3Array("Local Euler rotation [x, y, z]."),
                    scale = ToolSchemaFragments.Vector3Array("Local scale [x, y, z]."),
                    parent = ToolSchemaFragments.TargetRef("Parent GameObject target. Null or empty string clears parent.", allowNull: true)
                },
                required = new[] { "target" }
            };
        }

        static object BuildComponentChangeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    operation = new
                    {
                        type = "string",
                        description = "Component mutation operation.",
                        @enum = new[] { "add", "remove", "setProperties" }
                    },
                    target = ToolSchemaFragments.TargetRef("GameObject target, path, name, or id."),
                    searchMethod = ToolSchemaFragments.SearchMethod(),
                    searchInactive = new { type = "boolean", description = "Include inactive scene objects while resolving the target." },
                    componentName = new { type = "string", description = "Component type name, full type name, or resolvable Unity component name." },
                    componentIndex = new { type = "integer", description = "0-based index among components matching componentName. Defaults to 0." },
                    componentProperties = new
                    {
                        type = "object",
                        description = "Flat property dictionary for setProperties or optional initial properties for add.",
                        additionalProperties = true
                    }
                },
                required = new[] { "operation", "target", "componentName" }
            };
        }

        static object BuildCreateSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    name = new { type = "string", description = "Name for the created or instantiated GameObject." },
                    primitiveType = new
                    {
                        type = "string",
                        description = "Unity primitive type to create when prefabPath is not resolved.",
                        @enum = new[] { "Sphere", "Capsule", "Cylinder", "Cube", "Plane", "Quad" }
                    },
                    prefabPath = new { type = "string", description = "Prefab asset path or unique prefab name to instantiate. Also used as save path when saveAsPrefab=true." },
                    saveAsPrefab = new { type = "boolean", description = "Save a newly created object as a prefab and connect the scene instance." },
                    prefabFolder = new { type = "string", description = "Folder used to construct a prefab path when saveAsPrefab=true and prefabPath is omitted." },
                    parent = ToolSchemaFragments.TargetRef("Parent GameObject target. Null or empty string creates at scene root.", allowNull: true),
                    tag = new { type = "string", description = "Tag to assign. Missing tags are created during apply." },
                    layer = new { type = "string", description = "Layer name to assign." },
                    position = ToolSchemaFragments.Vector3Array("Local position [x, y, z]."),
                    rotation = ToolSchemaFragments.Vector3Array("Local Euler rotation [x, y, z]."),
                    scale = ToolSchemaFragments.Vector3Array("Local scale [x, y, z]."),
                    componentsToAdd = new
                    {
                        type = "array",
                        description = "Initial components to add after creation.",
                        items = new
                        {
                            anyOf = new object[]
                            {
                                new { type = "string" },
                                new
                                {
                                    type = "object",
                                    properties = new
                                    {
                                        typeName = new { type = "string" },
                                        componentName = new { type = "string" },
                                        properties = new { type = "object", additionalProperties = true },
                                        componentProperties = new { type = "object", additionalProperties = true }
                                    }
                                }
                            }
                        }
                    }
                },
                required = new[] { "name" }
            };
        }

        static object BuildDeleteSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = ToolSchemaFragments.TargetRef("GameObject target, path, name, or id."),
                    searchMethod = ToolSchemaFragments.SearchMethod(),
                    findAll = new { type = "boolean", description = "When true, delete all matching scene GameObjects." },
                    searchInactive = new { type = "boolean", description = "Include inactive scene objects while resolving the target." }
                },
                required = new[] { "target" }
            };
        }
    }
}
