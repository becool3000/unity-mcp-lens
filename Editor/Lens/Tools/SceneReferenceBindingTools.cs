#nullable disable
using System;
using System.Collections.Generic;
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
        const string PreviewInstantiatePrefabToolName = "Unity.Scene.PreviewInstantiatePrefabAndBind";
        const string ApplyInstantiatePrefabToolName = "Unity.Scene.ApplyInstantiatePrefabAndBind";
        const string VerifySerializedReferencesToolName = "Unity.Scene.VerifySerializedReferences";

        const string PreviewDescription = @"Previews serialized object-reference bindings on scene components without mutation.

Supports single ObjectReference fields and object-reference arrays/lists only.";

        const string ApplyDescription = @"Applies serialized object-reference bindings on scene components and saves open scenes when changes are required.

Supports single ObjectReference fields and object-reference arrays/lists only.";

        const string PreviewInstantiatePrefabDescription = @"Previews scene prefab instantiation plus serialized reference binding without mutation.

Use this when a durable scene instance should exist and have component object-reference fields bound.";

        const string ApplyInstantiatePrefabDescription = @"Applies scene prefab instantiation plus serialized reference binding and saves open scenes when changes are required.

Use this when a durable scene instance should exist and have component object-reference fields bound.";

        const string VerifySerializedReferencesDescription = @"Verifies effective serialized object references on scene components without mutation.

Reports effective values, prefab-inherited source values, local override status, and expected-reference assertion results for single object-reference fields and object-reference arrays/lists.";

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

        [McpSchema(PreviewInstantiatePrefabToolName)]
        public static object GetPreviewInstantiatePrefabSchema()
        {
            return BuildInstantiatePrefabSchema();
        }

        [McpSchema(ApplyInstantiatePrefabToolName)]
        public static object GetApplyInstantiatePrefabSchema()
        {
            return BuildInstantiatePrefabSchema();
        }

        [McpSchema(VerifySerializedReferencesToolName)]
        public static object GetVerifySerializedReferencesSchema()
        {
            return BuildVerifySerializedReferencesSchema();
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

        [McpTool(PreviewInstantiatePrefabToolName, PreviewInstantiatePrefabDescription, "Preview Instantiate Prefab And Bind", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewInstantiatePrefabAndBind(JObject @params)
        {
            return HandleInstantiatePrefabTool(PreviewInstantiatePrefabToolName, "preview_instantiate_prefab_and_bind", @params, apply: false);
        }

        [McpTool(ApplyInstantiatePrefabToolName, ApplyInstantiatePrefabDescription, "Apply Instantiate Prefab And Bind", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ApplyInstantiatePrefabAndBind(JObject @params)
        {
            return HandleInstantiatePrefabTool(ApplyInstantiatePrefabToolName, "apply_instantiate_prefab_and_bind", @params, apply: true);
        }

        [McpTool(VerifySerializedReferencesToolName, VerifySerializedReferencesDescription, "Verify Serialized References", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object VerifySerializedReferences(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(VerifySerializedReferencesToolName, "verify_serialized_references", GetUtf8ByteCount(@params.ToString(Formatting.None)));
            SceneReferenceBindingOperationResult result;
            string errorKind = null;

            try
            {
                SceneSerializedReferenceVerifyRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeVerifySerializedReferencesRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = Service.VerifySerializedReferences(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = SceneReferenceBindingOperationResult.Error($"Internal error verifying serialized references: {ex.Message}", errorKind);
            }

            return ShapeResponse(VerifySerializedReferencesToolName, result, timing, errorKind);
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

        static object HandleInstantiatePrefabTool(string toolName, string action, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            SceneReferenceBindingOperationResult result;
            string errorKind = null;

            try
            {
                ScenePrefabInstantiateAndBindRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeInstantiatePrefabRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? Service.ApplyInstantiatePrefabAndBind(request, timing)
                        : Service.PreviewInstantiatePrefabAndBind(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = SceneReferenceBindingOperationResult.Error($"Internal error processing prefab instantiate/bind: {ex.Message}", errorKind);
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
                    bindings = new { type = "array", description = "Serialized reference bindings to preview or apply.", items = new { type = "object" } }
                },
                required = new[] { "target", "bindings" }
            };
        }

        static object BuildInstantiatePrefabSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    prefabPath = new { type = "string", description = "Prefab asset path under Assets." },
                    instanceName = new { type = "string", description = "Scene instance name. Defaults to prefab name." },
                    parent = new { description = "Optional parent GameObject, path, or instance id." },
                    parentSearchMethod = new { type = "string", description = "How to resolve parent ('by_name', 'by_id', 'by_path')." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects while resolving parent/existing instance." },
                    position = new { description = "Optional local position as {x,y,z} or [x,y,z]." },
                    rotation = new { description = "Optional local Euler rotation as {x,y,z} or [x,y,z]." },
                    scale = new { description = "Optional local scale as {x,y,z} or [x,y,z]." },
                    bindings = new { type = "array", description = "Serialized reference bindings to preview/apply after the instance exists.", items = new { type = "object" } }
                },
                required = new[] { "prefabPath" }
            };
        }

        static object BuildVerifySerializedReferencesSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { description = "Scene GameObject target, path, or instance id." },
                    searchMethod = new { type = "string", description = "How to find the scene target ('by_name', 'by_id', 'by_path')." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving the target." },
                    checks = new
                    {
                        type = "array",
                        description = "Serialized reference checks to verify.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                targetPath = new { type = "string", description = "Relative child path under the target root. Use '.' or omit for root." },
                                componentType = new { type = "string", description = "Component type name on the target GameObject." },
                                componentIndex = new { type = "integer", description = "0-based component index when multiple matching components exist." },
                                propertyPath = new { type = "string", description = "Serialized property path to verify." },
                                expectedReference = new { description = "Optional expected single object reference." },
                                expectedReferences = new { type = "array", description = "Optional expected object-reference array/list.", items = new { description = "Expected object reference." } }
                            },
                            required = new[] { "componentType", "propertyPath" }
                        }
                    }
                },
                required = new[] { "target", "checks" }
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

        static ScenePrefabInstantiateAndBindRequest NormalizeInstantiatePrefabRequest(JObject parameters)
        {
            return new ScenePrefabInstantiateAndBindRequest
            {
                PrefabPath = GetString(parameters, "prefabPath", "PrefabPath"),
                InstanceName = GetString(parameters, "instanceName", "InstanceName"),
                Parent = GetToken(parameters, "parent", "Parent"),
                ParentSearchMethod = GetString(parameters, "parentSearchMethod", "ParentSearchMethod") ?? "by_name",
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                Position = GetToken(parameters, "position", "Position"),
                Rotation = GetToken(parameters, "rotation", "Rotation"),
                Scale = GetToken(parameters, "scale", "Scale"),
                Bindings = GetToken(parameters, "bindings", "Bindings")?.ToObject<SceneReferenceBindingEntry[]>() ?? Array.Empty<SceneReferenceBindingEntry>()
            };
        }

        static SceneSerializedReferenceVerifyRequest NormalizeVerifySerializedReferencesRequest(JObject parameters)
        {
            return new SceneSerializedReferenceVerifyRequest
            {
                Target = GetToken(parameters, "target", "Target"),
                SearchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_name",
                IncludeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                Checks = GetToken(parameters, "checks", "Checks")?.ToObject<SceneSerializedReferenceVerifyCheck[]>() ?? Array.Empty<SceneSerializedReferenceVerifyCheck>()
            };
        }

        static object ShapeResponse(string toolName, SceneReferenceBindingOperationResult result, ToolOperationTiming timing, string fallbackErrorKind)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        result.data,
                        string.Equals(toolName, PreviewInstantiatePrefabToolName, StringComparison.Ordinal) ||
                        string.Equals(toolName, ApplyInstantiatePrefabToolName, StringComparison.Ordinal)
                            ? BuildInstantiatePrefabCompactData(result.data)
                            : string.Equals(toolName, VerifySerializedReferencesToolName, StringComparison.Ordinal)
                                ? BuildVerifySerializedReferencesCompactData(result.data)
                            : BuildCompactData(result.data),
                        detailRefMeta: new
                        {
                            kind = string.Equals(toolName, PreviewInstantiatePrefabToolName, StringComparison.Ordinal) ||
                                   string.Equals(toolName, ApplyInstantiatePrefabToolName, StringComparison.Ordinal)
                                ? "scene_prefab_instantiate_bind_full_result"
                                : string.Equals(toolName, VerifySerializedReferencesToolName, StringComparison.Ordinal)
                                    ? "scene_verify_serialized_references_full_result"
                                : "scene_reference_binding_full_result"
                        },
                        payloadClass: string.Equals(toolName, PreviewInstantiatePrefabToolName, StringComparison.Ordinal) ||
                                      string.Equals(toolName, ApplyInstantiatePrefabToolName, StringComparison.Ordinal)
                            ? "scene_prefab_instantiate_bind"
                            : string.Equals(toolName, VerifySerializedReferencesToolName, StringComparison.Ordinal)
                                ? "scene_verify_serialized_references"
                            : "scene_reference_binding"))
                    : Response.Error(result.message, result.errorData ?? new { errorKind = result.errorKind ?? fallbackErrorKind });

                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray bindings = root["bindings"] as JArray ?? new JArray();
            var bindingTypeCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var changedBindings = new JArray();
            int unchangedCount = 0;

            foreach (JObject binding in bindings.OfType<JObject>())
            {
                string bindingType = (string)binding["bindingType"] ?? "unknown";
                bindingTypeCounts[bindingType] = bindingTypeCounts.TryGetValue(bindingType, out int count) ? count + 1 : 1;

                bool willModify = binding["willModify"]?.Value<bool>() == true;
                bool applied = binding["applied"]?.Value<bool>() == true;
                if (!willModify && !applied)
                {
                    unchangedCount++;
                    continue;
                }

                changedBindings.Add(new JObject
                {
                    ["targetPath"] = binding["targetPath"]?.DeepClone(),
                    ["hierarchyPath"] = binding["hierarchyPath"]?.DeepClone(),
                    ["componentType"] = binding["componentType"]?.DeepClone(),
                    ["componentIndex"] = binding["componentIndex"]?.DeepClone(),
                    ["propertyPath"] = binding["propertyPath"]?.DeepClone(),
                    ["bindingType"] = binding["bindingType"]?.DeepClone(),
                    ["willModify"] = binding["willModify"]?.DeepClone(),
                    ["applied"] = binding["applied"]?.DeepClone(),
                    ["requestedReference"] = binding["requestedReference"]?.DeepClone(),
                    ["readbackReference"] = binding["readbackReference"]?.DeepClone(),
                    ["requestedReferences"] = binding["requestedReferences"]?.DeepClone(),
                    ["readbackReferences"] = binding["readbackReferences"]?.DeepClone()
                });
            }

            return new
            {
                target = root["target"],
                applied = root["applied"],
                willModify = root["willModify"],
                bindingCount = bindings.Count,
                bindingTypeCounts,
                changedBindingCount = changedBindings.Count,
                omittedUnchangedBindingCount = unchangedCount,
                changedBindings
            };
        }

        static object BuildVerifySerializedReferencesCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray checks = root["checks"] as JArray ?? new JArray();
            var compactChecks = new JArray();
            var failedChecks = new JArray();
            var statusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (JObject check in checks.OfType<JObject>())
            {
                string status = (string)check["status"] ?? "unknown";
                statusCounts[status] = statusCounts.TryGetValue(status, out int count) ? count + 1 : 1;
                var compact = new JObject
                {
                    ["targetPath"] = check["targetPath"]?.DeepClone(),
                    ["hierarchyPath"] = check["hierarchyPath"]?.DeepClone(),
                    ["componentType"] = check["componentType"]?.DeepClone(),
                    ["propertyPath"] = check["propertyPath"]?.DeepClone(),
                    ["bindingType"] = check["bindingType"]?.DeepClone(),
                    ["status"] = check["status"]?.DeepClone(),
                    ["localOverride"] = check["localOverride"]?.DeepClone(),
                    ["sourcePrefabPath"] = check["sourcePrefabPath"]?.DeepClone(),
                    ["passed"] = check["passed"]?.DeepClone(),
                    ["effectiveReference"] = check["effectiveReference"]?.DeepClone(),
                    ["inheritedReference"] = check["inheritedReference"]?.DeepClone()
                };
                compactChecks.Add(compact);
                if (check["passed"]?.Value<bool>() == false)
                    failedChecks.Add(compact.DeepClone());
            }

            return new
            {
                target = root["target"],
                passed = root["passed"],
                checkCount = root["checkCount"],
                failedCheckCount = failedChecks.Count,
                statusCounts,
                checks = compactChecks,
                failedChecks
            };
        }

        static object BuildInstantiatePrefabCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray bindings = root["bindings"] as JArray ?? new JArray();
            JArray changedBindings = new JArray();
            int unchangedCount = 0;
            foreach (JObject binding in bindings.OfType<JObject>())
            {
                bool willModify = binding["willModify"]?.Value<bool>() == true;
                bool applied = binding["applied"]?.Value<bool>() == true;
                if (!willModify && !applied)
                {
                    unchangedCount++;
                    continue;
                }

                changedBindings.Add(new JObject
                {
                    ["targetPath"] = binding["targetPath"]?.DeepClone(),
                    ["hierarchyPath"] = binding["hierarchyPath"]?.DeepClone(),
                    ["componentType"] = binding["componentType"]?.DeepClone(),
                    ["propertyPath"] = binding["propertyPath"]?.DeepClone(),
                    ["bindingType"] = binding["bindingType"]?.DeepClone(),
                    ["willModify"] = binding["willModify"]?.DeepClone(),
                    ["applied"] = binding["applied"]?.DeepClone()
                });
            }

            return new
            {
                prefabPath = root["prefabPath"],
                instanceName = root["instanceName"],
                parentPath = root["parentPath"],
                instancePath = root["instancePath"],
                exists = root["exists"],
                applied = root["applied"],
                willModify = root["willModify"],
                instanceChangeCount = (root["instanceChanges"] as JArray)?.Count ?? 0,
                instanceChanges = root["instanceChanges"],
                bindingCount = bindings.Count,
                changedBindingCount = changedBindings.Count,
                omittedUnchangedBindingCount = unchangedCount,
                changedBindings
            };
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
