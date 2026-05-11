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
    public static class SceneQueryObjectsTools
    {
        const string ToolName = "Unity.Scene.QueryObjects";

        sealed class FieldRequest
        {
            public string Key { get; set; }
            public string ComponentType { get; set; }
            public int ComponentIndex { get; set; }
            public string PropertyPath { get; set; }
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    namePrefix = new { type = "string", description = "Optional GameObject name prefix filter." },
                    nameExact = new { type = "string", description = "Optional exact GameObject name filter." },
                    componentTypes = new
                    {
                        type = "array",
                        description = "Optional component type names. Short or fully-qualified names are accepted.",
                        items = new { type = "string" }
                    },
                    componentMatch = new { type = "string", description = "How componentTypes are matched: all or any. Defaults to all." },
                    root = new { type = "string", description = "Optional root GameObject name, hierarchy path, or id filter." },
                    rootSearchMethod = new { type = "string", description = "Root search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_name." },
                    scene = new { type = "string", description = "Optional scene name or path filter." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects. Defaults to false." },
                    fields = new
                    {
                        type = "array",
                        description = "Optional serialized field reads. Prefer objects {key, componentType, componentIndex, propertyPath}; strings may use ComponentType.propertyPath for simple cases.",
                        items = new { }
                    },
                    maxRows = new { type = "integer", description = "Maximum returned object rows. Defaults to 50 and is capped at 500." }
                }
            };
        }

        [McpTool(ToolName,
            "Queries loaded scene GameObjects by name, component type, scene, or root scope and optionally reads serialized component fields without mutation.",
            "Query Scene Objects",
            Groups = new[] { "scene", "diagnostics" },
            EnabledByDefault = true)]
        public static object QueryObjects(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "query_objects", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                string namePrefix;
                string nameExact;
                string[] componentTypeNames;
                string componentMatch;
                string root;
                string rootSearchMethod;
                string scene;
                bool includeInactive;
                FieldRequest[] fields;
                int maxRows;

                using (timing.Measure("normalization"))
                {
                    namePrefix = GetString(@params, "namePrefix", "NamePrefix");
                    nameExact = GetString(@params, "nameExact", "NameExact");
                    componentTypeNames = GetStringArray(@params, "componentTypes", "ComponentTypes")
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Select(value => value.Trim())
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    componentMatch = (GetString(@params, "componentMatch", "ComponentMatch") ?? "all").Trim().ToLowerInvariant();
                    root = GetString(@params, "root", "Root");
                    rootSearchMethod = GetString(@params, "rootSearchMethod", "RootSearchMethod") ?? "by_name";
                    scene = GetString(@params, "scene", "Scene");
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    fields = ParseFields(GetToken(@params, "fields", "Fields"));
                    maxRows = Math.Clamp(GetInt(@params, 50, "maxRows", "MaxRows"), 1, 500);
                }

                using (timing.Measure("service"))
                {
                    data = Query(namePrefix, nameExact, componentTypeNames, componentMatch, root, rootSearchMethod, scene, includeInactive, fields, maxRows);
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
                    error = ex.Message
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Scene object query completed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "scene_query_objects_full_result" },
                        "scene_query_objects",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("SCENE_QUERY_OBJECTS_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static object Query(
            string namePrefix,
            string nameExact,
            string[] componentTypeNames,
            string componentMatch,
            string root,
            string rootSearchMethod,
            string scene,
            bool includeInactive,
            FieldRequest[] fields,
            int maxRows)
        {
            var inactiveMode = includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            var allObjects = UnityApiAdapter.FindObjectsByType<GameObject>(inactiveMode);
            GameObject rootObject = ResolveRoot(allObjects, root, rootSearchMethod);
            var resolvedTypes = ResolveComponentTypes(componentTypeNames, out var missingTypes);
            bool matchAny = string.Equals(componentMatch, "any", StringComparison.OrdinalIgnoreCase);

            var matches = allObjects
                .Where(go => MatchesScene(go, scene))
                .Where(go => MatchesRoot(go, rootObject))
                .Where(go => MatchesName(go, namePrefix, nameExact))
                .Where(go => MatchesComponents(go, resolvedTypes, matchAny))
                .OrderBy(go => UiDiagnosticsHelper.GetHierarchyPath(go.transform), StringComparer.Ordinal)
                .ToArray();

            var rows = matches
                .Take(maxRows)
                .Select(go => BuildRow(go, fields, componentTypeNames))
                .ToArray();

            return new
            {
                status = "ready",
                includeInactive,
                namePrefix,
                nameExact,
                componentTypes = componentTypeNames,
                componentMatch = matchAny ? "any" : "all",
                missingTypeCount = missingTypes.Length,
                missingTypes,
                scene = string.IsNullOrWhiteSpace(scene) ? null : scene,
                root = rootObject == null ? null : new
                {
                    name = rootObject.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(rootObject.transform),
                    activeSelf = rootObject.activeSelf,
                    activeInHierarchy = rootObject.activeInHierarchy,
                    objectId = UnityApiAdapter.GetObjectIdOrZero(rootObject)
                },
                count = matches.Length,
                rowCount = rows.Length,
                omittedCount = Math.Max(0, matches.Length - rows.Length),
                maxRows,
                rows
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray rows = root["rows"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                includeInactive = root["includeInactive"],
                namePrefix = root["namePrefix"],
                nameExact = root["nameExact"],
                componentTypes = root["componentTypes"],
                componentMatch = root["componentMatch"],
                missingTypeCount = root["missingTypeCount"],
                missingTypes = root["missingTypes"],
                scene = root["scene"],
                root = root["root"],
                count = root["count"],
                rowCount = root["rowCount"],
                omittedCount = root["omittedCount"],
                maxRows = root["maxRows"],
                rows = rows.Take(25).ToArray(),
                compactOmittedRowCount = Math.Max(0, rows.Count - 25)
            };
        }

        static Dictionary<string, Type> ResolveComponentTypes(string[] componentTypeNames, out string[] missingTypes)
        {
            var resolved = new Dictionary<string, Type>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string componentTypeName in componentTypeNames ?? Array.Empty<string>())
            {
                if (UnityComponentResolver.TryResolve(componentTypeName, out Type type, out _))
                    resolved[componentTypeName] = type;
                else
                    missing.Add(componentTypeName);
            }

            missingTypes = missing.ToArray();
            return resolved;
        }

        static bool MatchesScene(GameObject gameObject, string scene)
        {
            return string.IsNullOrWhiteSpace(scene) ||
                string.Equals(gameObject.scene.name, scene, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gameObject.scene.path, scene, StringComparison.OrdinalIgnoreCase);
        }

        static bool MatchesRoot(GameObject gameObject, GameObject rootObject)
        {
            return rootObject == null ||
                gameObject.transform == rootObject.transform ||
                gameObject.transform.IsChildOf(rootObject.transform);
        }

        static bool MatchesName(GameObject gameObject, string namePrefix, string nameExact)
        {
            if (!string.IsNullOrWhiteSpace(nameExact) &&
                !string.Equals(gameObject.name, nameExact, StringComparison.Ordinal))
                return false;

            return string.IsNullOrWhiteSpace(namePrefix) ||
                gameObject.name.StartsWith(namePrefix, StringComparison.Ordinal);
        }

        static bool MatchesComponents(GameObject gameObject, IReadOnlyDictionary<string, Type> componentTypes, bool matchAny)
        {
            if (componentTypes == null || componentTypes.Count == 0)
                return true;

            var components = gameObject.GetComponents<Component>();
            return matchAny
                ? componentTypes.Values.Any(type => components.Any(component => component != null && type.IsInstanceOfType(component)))
                : componentTypes.Values.All(type => components.Any(component => component != null && type.IsInstanceOfType(component)));
        }

        static object BuildRow(GameObject gameObject, FieldRequest[] fields, string[] defaultComponentTypes)
        {
            var components = gameObject.GetComponents<Component>()
                .Select(component => component == null ? null : component.GetType().FullName)
                .ToArray();

            return new
            {
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                name = gameObject.name,
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy,
                sceneName = gameObject.scene.name,
                scenePath = gameObject.scene.path,
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                components,
                fieldValues = ReadFields(gameObject, fields, defaultComponentTypes)
            };
        }

        static object ReadFields(GameObject gameObject, FieldRequest[] fields, string[] defaultComponentTypes)
        {
            var values = new JObject();
            if (fields == null || fields.Length == 0)
                return values;

            foreach (var field in fields)
            {
                string componentTypeName = string.IsNullOrWhiteSpace(field.ComponentType) && defaultComponentTypes.Length == 1
                    ? defaultComponentTypes[0]
                    : field.ComponentType;
                string key = !string.IsNullOrWhiteSpace(field.Key)
                    ? field.Key
                    : $"{componentTypeName}.{field.PropertyPath}";

                values[key] = JObject.FromObject(ReadField(gameObject, componentTypeName, field.ComponentIndex, field.PropertyPath));
            }

            return values;
        }

        static object ReadField(GameObject gameObject, string componentTypeName, int componentIndex, string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return new { resolved = false, error = "componentType is required for field reads unless exactly one componentTypes entry was provided." };
            if (string.IsNullOrWhiteSpace(propertyPath))
                return new { resolved = false, componentType = componentTypeName, error = "propertyPath is required." };
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string typeError))
                return new { resolved = false, componentType = componentTypeName, propertyPath, error = typeError };

            Component[] matches = gameObject.GetComponents(componentType);
            int index = Math.Max(0, componentIndex);
            if (matches.Length <= index || matches[index] == null)
                return new { resolved = false, componentType = componentTypeName, componentIndex = index, propertyPath, error = "component_not_found" };

            var serializedObject = new SerializedObject(matches[index]);
            var property = serializedObject.FindProperty(propertyPath);
            if (property == null)
                return new { resolved = false, componentType = matches[index].GetType().FullName, componentIndex = index, propertyPath, error = "property_not_found" };

            return new
            {
                resolved = true,
                componentType = matches[index].GetType().FullName,
                componentIndex = index,
                propertyPath,
                propertyType = property.propertyType.ToString(),
                value = SceneTools.DescribeProperty(property)
            };
        }

        static GameObject ResolveRoot(GameObject[] objects, string root, string searchMethod)
        {
            if (string.IsNullOrWhiteSpace(root))
                return null;

            string method = (searchMethod ?? "by_name").Trim().ToLowerInvariant();
            return objects.FirstOrDefault(candidate =>
                (method == "by_id" && UnityApiAdapter.ObjectIdEquals(candidate, root)) ||
                (method == "by_path" && string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), root, StringComparison.Ordinal)) ||
                (method == "by_id_or_name_or_path" && (
                    UnityApiAdapter.ObjectIdEquals(candidate, root) ||
                    string.Equals(candidate.name, root, StringComparison.Ordinal) ||
                    string.Equals(UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), root, StringComparison.Ordinal))) ||
                (method != "by_id" && method != "by_path" && method != "by_id_or_name_or_path" && string.Equals(candidate.name, root, StringComparison.Ordinal)));
        }

        static FieldRequest[] ParseFields(JToken token)
        {
            if (token is not JArray fields)
                return Array.Empty<FieldRequest>();

            var results = new List<FieldRequest>();
            foreach (JToken entry in fields)
            {
                if (entry is JObject obj)
                {
                    string propertyPath = GetString(obj, "propertyPath", "PropertyPath", "field", "Field");
                    if (string.IsNullOrWhiteSpace(propertyPath))
                        continue;

                    results.Add(new FieldRequest
                    {
                        Key = GetString(obj, "key", "Key"),
                        ComponentType = GetString(obj, "componentType", "ComponentType"),
                        ComponentIndex = Math.Max(0, GetInt(obj, 0, "componentIndex", "ComponentIndex")),
                        PropertyPath = propertyPath
                    });
                }
                else if (entry.Type == JTokenType.String)
                {
                    string text = entry.Value<string>()?.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    int separator = text.LastIndexOf('.');
                    results.Add(new FieldRequest
                    {
                        Key = text,
                        ComponentType = separator > 0 ? text.Substring(0, separator) : null,
                        PropertyPath = separator > 0 ? text.Substring(separator + 1) : text
                    });
                }
            }

            return results.ToArray();
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
            return token is JArray array
                ? array.Select(item => item?.Value<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
                : Array.Empty<string>();
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
