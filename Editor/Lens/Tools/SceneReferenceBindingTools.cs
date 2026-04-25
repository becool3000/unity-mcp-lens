#nullable disable
using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Scene;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Models.Scene;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Services.Scene;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class SceneReferenceBindingTools
    {
        const string PreviewToolName = "Unity.Scene.PreviewBindSerializedReferences";
        const string ApplyToolName = "Unity.Scene.ApplyBindSerializedReferences";

        const string PreviewDescription = @"Previews serialized object-reference bindings on scene components without mutation.

Supports single ObjectReference fields and object-reference arrays/lists only.";

        const string ApplyDescription = @"Applies serialized object-reference bindings on scene components and saves open scenes when changes are required.

Supports single ObjectReference fields and object-reference arrays/lists only.";

        static readonly UnitySceneReferenceBindingAdapter Adapter = new UnitySceneReferenceBindingAdapter();
        static readonly SceneReferenceBindingService Service = new SceneReferenceBindingService(Adapter);

        [McpSchema(PreviewToolName)]
        public static object GetPreviewSchema()
        {
            return BuildSchema();
        }

        [McpSchema(ApplyToolName)]
        public static object GetApplySchema()
        {
            return BuildSchema();
        }

        [McpTool(PreviewToolName, PreviewDescription, "Preview Bind Serialized References", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object Preview(JObject @params)
        {
            return HandleTool(PreviewToolName, "preview_bind_serialized_references", @params, apply: false);
        }

        [McpTool(ApplyToolName, ApplyDescription, "Apply Bind Serialized References", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object Apply(JObject @params)
        {
            return HandleTool(ApplyToolName, "apply_bind_serialized_references", @params, apply: true);
        }

        static object HandleTool(string toolName, string action, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            SceneReferenceBindingOperationResult result;
            string errorKind = null;

            try
            {
                SceneReferenceBindingRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? Service.Apply(request, timing)
                        : Service.Preview(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = SceneReferenceBindingOperationResult.Error($"Internal error processing serialized reference bindings: {ex.Message}", errorKind);
            }

            return ShapeResponse(toolName, result, timing, errorKind);
        }

        static object BuildSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { description = "Scene GameObject target, path, or instance id." },
                    searchMethod = new { type = "string", description = "How to find the scene target ('by_name', 'by_id', 'by_path')." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving the target." },
                    bindings = new { type = "array", description = "Serialized reference bindings to preview or apply." }
                },
                required = new[] { "target", "bindings" }
            };
        }

        static SceneReferenceBindingRequest NormalizeRequest(JObject parameters)
        {
            return new SceneReferenceBindingRequest
            {
                Target = GetToken(parameters, "target", "Target"),
                SearchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_name",
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                Bindings = GetToken(parameters, "bindings", "Bindings")?.ToObject<SceneReferenceBindingEntry[]>() ?? Array.Empty<SceneReferenceBindingEntry>()
            };
        }

        static object ShapeResponse(string toolName, SceneReferenceBindingOperationResult result, ToolOperationTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, ToolResultCompactor.ShapeJsonPayload(toolName, result.message, result.data))
                    : Response.Error(result.message, result.errorData ?? new { errorKind = result.errorKind ?? fallbackErrorKind });

                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static string GetString(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token?.Type == JTokenType.Null ? null : token?.ToString();
            }

            return null;
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token;
            }

            return null;
        }

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out bool parsed) ? parsed : defaultValue;
            }

            return defaultValue;
        }

        static int GetUtf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
