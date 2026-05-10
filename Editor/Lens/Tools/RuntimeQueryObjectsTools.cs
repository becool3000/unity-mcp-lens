#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class RuntimeQueryObjectsTools
    {
        const string ToolName = "Unity.Runtime.QueryObjects";

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    componentTypes = new
                    {
                        type = "array",
                        description = "Component type names to count. Short or fully-qualified names are accepted.",
                        items = new { type = "string" }
                    },
                    componentType = new { type = "string", description = "Single component type compatibility shortcut." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects. Defaults to false." },
                    maxSamplesPerType = new { type = "integer", description = "Maximum sample object rows per component type. Defaults to 8." },
                    scene = new { type = "string", description = "Optional scene name or path filter." },
                    root = new { type = "string", description = "Optional root GameObject name, hierarchy path, or instance id filter." },
                    rootSearchMethod = new { type = "string", description = "Root search method: by_name, by_path, or by_id. Defaults to by_name." }
                },
                required = new[] { "componentTypes" }
            };
        }

        [McpTool(ToolName,
            "Counts runtime objects by multiple component types in play mode and returns capped sample object paths.",
            "Query Runtime Objects",
            Groups = new[] { "runtime", "diagnostics" },
            EnabledByDefault = true)]
        public static object QueryObjects(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "query_objects", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data = null;

            try
            {
                string[] componentTypes;
                bool includeInactive;
                int maxSamplesPerType;
                string scene;
                string root;
                string rootSearchMethod;
                using (timing.Measure("normalization"))
                {
                    componentTypes = GetStringArray(@params, "componentTypes", "ComponentTypes");
                    string singleComponentType = GetString(@params, "componentType", "ComponentType");
                    if (!string.IsNullOrWhiteSpace(singleComponentType))
                        componentTypes = componentTypes.Concat(new[] { singleComponentType }).ToArray();
                    componentTypes = componentTypes
                        .Where(type => !string.IsNullOrWhiteSpace(type))
                        .Select(type => type.Trim())
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    maxSamplesPerType = Math.Clamp(GetInt(@params, 8, "maxSamplesPerType", "MaxSamplesPerType"), 0, 50);
                    scene = GetString(@params, "scene", "Scene");
                    root = GetString(@params, "root", "Root");
                    rootSearchMethod = GetString(@params, "rootSearchMethod", "RootSearchMethod") ?? "by_name";
                }

                using (timing.Measure("service"))
                {
                    if (!EditorApplication.isPlaying)
                    {
                        success = false;
                        errorKind = "not_in_play_mode";
                        data = new
                        {
                            status = "refused",
                            reason = "not_in_play_mode",
                            isPlaying = EditorApplication.isPlaying,
                            componentTypes,
                            includeInactive,
                            maxSamplesPerType
                        };
                    }
                    else if (componentTypes.Length == 0)
                    {
                        success = false;
                        errorKind = "component_types_required";
                        data = new
                        {
                            status = "failed",
                            reason = "component_types_required",
                            isPlaying = EditorApplication.isPlaying,
                            componentTypes,
                            includeInactive,
                            maxSamplesPerType
                        };
                    }
                    else
                    {
                        data = Query(componentTypes, includeInactive, maxSamplesPerType, scene, root, rootSearchMethod);
                    }
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message,
                    isPlaying = EditorApplication.isPlaying
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Runtime object query completed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "runtime_query_objects_full_result" },
                        "runtime_query_objects",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(errorKind == "not_in_play_mode" ? "NOT_IN_PLAY_MODE" : "RUNTIME_QUERY_OBJECTS_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static object Query(string[] componentTypes, bool includeInactive, int maxSamplesPerType, string scene, string root, string rootSearchMethod)
        {
            FindObjectsInactive inactiveMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            GameObject rootObject = ResolveRoot(root, rootSearchMethod, includeInactive);
            var rows = new List<object>();
            var missingTypes = new List<string>();

            foreach (string componentTypeName in componentTypes)
            {
                if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string error))
                {
                    missingTypes.Add(componentTypeName);
                    rows.Add(new
                    {
                        componentType = componentTypeName,
                        resolved = false,
                        count = 0,
                        sampleCount = 0,
                        omittedSampleCount = 0,
                        error
                    });
                    continue;
                }

                Component[] components = UnityApiAdapter.FindObjectsByType(componentType, inactiveMode)
                    .OfType<Component>()
                    .Where(component => MatchesScope(component, scene, rootObject))
                    .OrderBy(component => UiDiagnosticsHelper.GetHierarchyPath(component.transform), StringComparer.Ordinal)
                    .ToArray();
                object[] samples = components
                    .Take(maxSamplesPerType)
                    .Select(component => BuildSample(component, componentType))
                    .ToArray();

                rows.Add(new
                {
                    componentType = componentTypeName,
                    resolvedComponentType = componentType.FullName,
                    resolved = true,
                    count = components.Length,
                    sampleCount = samples.Length,
                    omittedSampleCount = Math.Max(0, components.Length - samples.Length),
                    samples
                });
            }

            return new
            {
                status = "ready",
                isPlaying = EditorApplication.isPlaying,
                includeInactive,
                maxSamplesPerType,
                scene = string.IsNullOrWhiteSpace(scene) ? null : scene,
                root = rootObject == null ? null : new
                {
                    name = rootObject.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(rootObject.transform),
                    activeSelf = rootObject.activeSelf,
                    activeInHierarchy = rootObject.activeInHierarchy,
                    objectId = UnityApiAdapter.GetObjectIdOrZero(rootObject)
                },
                queryCount = componentTypes.Length,
                missingTypeCount = missingTypes.Count,
                missingTypes = missingTypes.ToArray(),
                results = rows.ToArray()
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray results = root["results"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                isPlaying = root["isPlaying"],
                includeInactive = root["includeInactive"],
                maxSamplesPerType = root["maxSamplesPerType"],
                scene = root["scene"],
                root = root["root"],
                queryCount = root["queryCount"],
                missingTypeCount = root["missingTypeCount"],
                missingTypes = root["missingTypes"],
                results = results.Select(CompactResultRow).ToArray()
            };
        }

        static object CompactResultRow(JToken row)
        {
            JArray samples = row["samples"] as JArray ?? new JArray();
            return new
            {
                componentType = row["componentType"],
                resolvedComponentType = row["resolvedComponentType"],
                resolved = row["resolved"],
                count = row["count"],
                sampleCount = row["sampleCount"],
                omittedSampleCount = row["omittedSampleCount"],
                error = row["error"],
                samples = samples.Take(5).ToArray(),
                compactOmittedSampleCount = Math.Max(0, samples.Count - 5)
            };
        }

        static object BuildSample(Component component, Type requestedType)
        {
            GameObject gameObject = component.gameObject;
            return new
            {
                name = gameObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                componentType = component.GetType().FullName,
                requestedType = requestedType.FullName,
                gameObjectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                componentId = UnityApiAdapter.GetObjectIdOrZero(component)
            };
        }

        static bool MatchesScope(Component component, string scene, GameObject rootObject)
        {
            if (!string.IsNullOrWhiteSpace(scene) &&
                !string.Equals(component.gameObject.scene.name, scene, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(component.gameObject.scene.path, scene, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return rootObject == null ||
                component.transform == rootObject.transform ||
                component.transform.IsChildOf(rootObject.transform);
        }

        static GameObject ResolveRoot(string root, string searchMethod, bool includeInactive)
        {
            if (string.IsNullOrWhiteSpace(root))
                return null;

            GameObject[] objects = UnityApiAdapter.FindObjectsByType<GameObject>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            string method = (searchMethod ?? "by_name").Trim().ToLowerInvariant();
            foreach (GameObject candidate in objects)
            {
                if (method == "by_id" && UnityApiAdapter.ObjectIdEquals(candidate, root))
                    return candidate;
                if (method == "by_path" && string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), root, StringComparison.Ordinal))
                    return candidate;
                if (method != "by_id" && method != "by_path" && string.Equals(candidate.name, root, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            if (parameters == null)
                return null;

            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value))
                    return value;
            }

            return null;
        }

        static string GetString(JObject parameters, params string[] names)
        {
            return GetToken(parameters, names)?.Value<string>();
        }

        static string[] GetStringArray(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token is not JArray array)
                return Array.Empty<string>();

            return array
                .Select(item => item?.Value<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        static int GetInt(JObject parameters, int fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }
    }
}
