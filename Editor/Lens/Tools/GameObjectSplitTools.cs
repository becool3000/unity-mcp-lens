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

        static readonly UnityGameObjectAdapter GameObjectAdapter = new UnityGameObjectAdapter();
        static readonly GameObjectRequestNormalizer RequestNormalizer = new GameObjectRequestNormalizer();
        static readonly GameObjectQueryService QueryService = new GameObjectQueryService(GameObjectAdapter);
        static readonly GameObjectMutationService MutationService = new GameObjectMutationService(GameObjectAdapter);

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
    }
}
