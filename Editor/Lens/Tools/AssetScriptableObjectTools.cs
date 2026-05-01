#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
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
    public static class AssetScriptableObjectTools
    {
        const string PreviewToolName = "Unity.Asset.PreviewCreateOrUpdateScriptableObject";
        const string ApplyToolName = "Unity.Asset.ApplyCreateOrUpdateScriptableObject";

        const string Description = @"Previews or applies ScriptableObject asset creation/update through serialized properties.

Supports primitive, enum, color, Vector2, Vector3, object-reference asset paths, and null object references. Managed references are reported as unsupported in v1.";

        sealed class Assignment
        {
            public string propertyPath;
            public JToken value;
        }

        sealed class Request
        {
            public string assetPath;
            public string scriptType;
            public bool createIfMissing = true;
            public bool updateIfExists = true;
            public Assignment[] assignments = Array.Empty<Assignment>();
        }

        [McpSchema(PreviewToolName)]
        public static object GetPreviewSchema() => BuildSchema();

        [McpSchema(ApplyToolName)]
        public static object GetApplySchema() => BuildSchema();

        [McpTool(PreviewToolName, Description, "Preview Create Or Update ScriptableObject", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object PreviewCreateOrUpdateScriptableObject(JObject parameters)
        {
            return Handle(parameters, apply: false);
        }

        [McpTool(ApplyToolName, Description, "Apply Create Or Update ScriptableObject", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object ApplyCreateOrUpdateScriptableObject(JObject parameters)
        {
            return Handle(parameters, apply: true);
        }

        static object BuildSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    assetPath = new { type = "string", description = "ScriptableObject asset path under Assets, ending in .asset." },
                    scriptType = new { type = "string", description = "ScriptableObject type name to create or validate." },
                    createIfMissing = new { type = "boolean", description = "Create the asset when it does not exist.", @default = true },
                    updateIfExists = new { type = "boolean", description = "Update assignments when the asset already exists.", @default = true },
                    assignments = new
                    {
                        type = "array",
                        description = "Serialized property assignments.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                propertyPath = new { type = "string", description = "SerializedProperty path." },
                                value = new { description = "Primitive value, color/vector object, asset path string, or null." }
                            },
                            required = new[] { "propertyPath" }
                        }
                    }
                },
                required = new[] { "assetPath", "scriptType" }
            };
        }

        static object Handle(JObject parameters, bool apply)
        {
            parameters ??= new JObject();
            string toolName = apply ? ApplyToolName : PreviewToolName;
            string action = apply ? "apply_create_update_scriptable_object" : "preview_create_update_scriptable_object";
            var timing = new ToolOperationTiming(toolName, action, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                Request request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(parameters);
                }

                using (timing.Measure("service"))
                {
                    ValidateRequest(request);
                }

                using (timing.Measure("adapter"))
                {
                    data = Run(request, apply);
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new { errorKind, error = ex.Message };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(apply ? "Applied ScriptableObject asset authoring." : "Previewed ScriptableObject asset authoring.",
                        ToolResultCompactor.ShapeStructuredPayload(toolName, data, BuildCompactData(data), new { kind = "scriptable_object_asset_full_result" }, "asset_scriptable_object"))
                    : Response.Error("ScriptableObject asset authoring failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static Request Normalize(JObject parameters)
        {
            return new Request
            {
                assetPath = SanitizeAssetPath(parameters["assetPath"]?.ToString() ?? parameters["AssetPath"]?.ToString()),
                scriptType = parameters["scriptType"]?.ToString() ?? parameters["ScriptType"]?.ToString(),
                createIfMissing = parameters["createIfMissing"]?.ToObject<bool?>() ?? parameters["CreateIfMissing"]?.ToObject<bool?>() ?? true,
                updateIfExists = parameters["updateIfExists"]?.ToObject<bool?>() ?? parameters["UpdateIfExists"]?.ToObject<bool?>() ?? true,
                assignments = ParseAssignments(parameters["assignments"] ?? parameters["Assignments"]).ToArray()
            };
        }

        static IEnumerable<Assignment> ParseAssignments(JToken token)
        {
            if (token is not JArray array)
                yield break;

            foreach (JToken item in array)
            {
                if (item is not JObject obj)
                    continue;

                yield return new Assignment
                {
                    propertyPath = obj["propertyPath"]?.ToString() ?? obj["PropertyPath"]?.ToString(),
                    value = obj["value"] ?? obj["Value"] ?? JValue.CreateNull()
                };
            }
        }

        static void ValidateRequest(Request request)
        {
            if (string.IsNullOrWhiteSpace(request.assetPath) || !request.assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || !request.assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("assetPath must be a .asset path under Assets.");

            if (string.IsNullOrWhiteSpace(request.scriptType))
                throw new ArgumentException("scriptType is required.");

            if (!TryResolveScriptableObjectType(request.scriptType, out Type resolvedType, out string resolveError))
            {
                throw new ArgumentException($"scriptType '{request.scriptType}' is not a ScriptableObject type: {resolveError ?? "type not found"}");
            }

            foreach (Assignment assignment in request.assignments)
            {
                if (assignment == null || string.IsNullOrWhiteSpace(assignment.propertyPath))
                    throw new ArgumentException("Each assignment requires propertyPath.");
            }
        }

        static object Run(Request request, bool apply)
        {
            TryResolveScriptableObjectType(request.scriptType, out Type scriptType, out _);
            ScriptableObject asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(request.assetPath);
            bool exists = asset != null;
            bool created = false;
            bool applied = false;
            var issues = new List<object>();
            var assignmentRows = new List<object>();

            if (!exists && !request.createIfMissing)
            {
                issues.Add(new { code = "asset_missing", severity = "error", message = $"Asset '{request.assetPath}' does not exist and createIfMissing=false." });
                return BuildData(request, exists, created, applied, willModify: false, scriptType, assignmentRows, issues);
            }

            if (exists && !request.updateIfExists)
            {
                return BuildData(request, exists, created, applied, willModify: false, asset.GetType(), assignmentRows, issues);
            }

            if (!exists)
            {
                if (apply)
                {
                    string directory = Path.GetDirectoryName(request.assetPath)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(directory) && !AssetDatabase.IsValidFolder(directory))
                        Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), directory));

                    asset = ScriptableObject.CreateInstance(scriptType);
                    AssetDatabase.CreateAsset(asset, request.assetPath);
                    created = true;
                    applied = true;
                }
                else
                {
                    assignmentRows.AddRange(request.assignments.Select(assignment => new
                    {
                        propertyPath = assignment.propertyPath,
                        action = "pending_create",
                        requestedValue = NormalizeToken(assignment.value)
                    }));
                    return BuildData(request, exists, created, applied, willModify: true, scriptType, assignmentRows, issues);
                }
            }

            if (asset == null)
            {
                issues.Add(new { code = "asset_load_failed", severity = "error", message = $"Asset '{request.assetPath}' could not be loaded." });
                return BuildData(request, exists, created, applied, willModify: false, scriptType, assignmentRows, issues);
            }

            if (!scriptType.IsAssignableFrom(asset.GetType()))
            {
                issues.Add(new { code = "script_type_mismatch", severity = "error", message = $"Asset type '{asset.GetType().FullName}' is not assignable to requested type '{scriptType.FullName}'." });
                return BuildData(request, exists, created, applied, willModify: false, asset.GetType(), assignmentRows, issues);
            }

            var serializedObject = new SerializedObject(asset);
            serializedObject.Update();
            bool willModify = created;
            foreach (Assignment assignment in request.assignments)
            {
                SerializedProperty property = serializedObject.FindProperty(assignment.propertyPath);
                if (property == null)
                {
                    issues.Add(new { code = "property_not_found", severity = "error", message = $"Serialized property '{assignment.propertyPath}' was not found." });
                    continue;
                }

                string previousValue = DescribeProperty(property);
                bool canAssign = TryPreviewAssignment(property, assignment.value, out string previewValue, out string assignError);
                if (!canAssign)
                {
                    issues.Add(new { code = "unsupported_assignment", severity = "error", message = $"Property '{assignment.propertyPath}': {assignError}" });
                    assignmentRows.Add(new
                    {
                        assignment.propertyPath,
                        propertyType = property.propertyType.ToString(),
                        previousValue,
                        requestedValue = NormalizeToken(assignment.value),
                        supported = false,
                        error = assignError
                    });
                    continue;
                }

                bool changed = previousValue != previewValue;
                willModify |= changed;
                if (apply && changed)
                {
                    if (!TryAssignValue(property, assignment.value, out assignError))
                    {
                        issues.Add(new { code = "assignment_failed", severity = "error", message = $"Property '{assignment.propertyPath}': {assignError}" });
                        continue;
                    }

                    applied = true;
                }

                assignmentRows.Add(new
                {
                    assignment.propertyPath,
                    propertyType = property.propertyType.ToString(),
                    previousValue,
                    requestedValue = NormalizeToken(assignment.value),
                    newValue = apply && changed ? null : previewValue,
                    changed,
                    supported = true
                });
            }

            if (apply && applied)
            {
                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                serializedObject.UpdateIfRequiredOrScript();

                for (int i = 0; i < assignmentRows.Count; i++)
                {
                    JObject row = JObject.FromObject(assignmentRows[i]);
                    SerializedProperty property = serializedObject.FindProperty(row["propertyPath"]?.ToString());
                    if (property != null)
                        row["newValue"] = DescribeProperty(property);
                    assignmentRows[i] = row;
                }
            }

            return BuildData(request, exists, created, applied, willModify, asset.GetType(), assignmentRows, issues);
        }

        static bool TryResolveScriptableObjectType(string typeName, out Type resolvedType, out string error)
        {
            resolvedType = null;
            error = null;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                error = "type name is empty";
                return false;
            }

            resolvedType = Type.GetType(typeName, throwOnError: false);
            if (resolvedType == null && UnityComponentResolver.TryResolve(typeName, out Type componentResolved, out _))
                resolvedType = componentResolved;

            if (resolvedType == null)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type match = null;
                    try
                    {
                        match = assembly.GetTypes().FirstOrDefault(type =>
                            string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                            string.Equals(type.Name, typeName, StringComparison.Ordinal));
                    }
                    catch
                    {
                        continue;
                    }

                    if (match != null)
                    {
                        resolvedType = match;
                        break;
                    }
                }
            }

            if (resolvedType == null)
            {
                error = $"Type '{typeName}' was not found in loaded assemblies.";
                return false;
            }

            if (!typeof(ScriptableObject).IsAssignableFrom(resolvedType))
            {
                error = $"Resolved type '{resolvedType.FullName}' is not assignable to ScriptableObject.";
                return false;
            }

            if (resolvedType.IsAbstract)
            {
                error = $"Resolved type '{resolvedType.FullName}' is abstract.";
                return false;
            }

            return true;
        }

        static object BuildData(Request request, bool exists, bool created, bool applied, bool willModify, Type scriptType, List<object> assignmentRows, List<object> issues)
        {
            string guid = AssetDatabase.AssetPathToGUID(request.assetPath);
            return new
            {
                assetPath = request.assetPath,
                exists,
                created,
                willModify,
                applied,
                scriptType = scriptType?.FullName ?? request.scriptType,
                guid = string.IsNullOrWhiteSpace(guid) ? null : guid,
                assignmentCount = request.assignments.Length,
                changedAssignmentCount = assignmentRows.Count(row => JObject.FromObject(row)["changed"]?.Value<bool>() == true),
                assignments = assignmentRows.ToArray(),
                issues = issues.ToArray()
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray assignments = root["assignments"] as JArray ?? new JArray();
            JArray issues = root["issues"] as JArray ?? new JArray();
            return new
            {
                assetPath = root["assetPath"],
                exists = root["exists"],
                created = root["created"],
                willModify = root["willModify"],
                applied = root["applied"],
                scriptType = root["scriptType"],
                guid = root["guid"],
                assignmentCount = root["assignmentCount"],
                changedAssignmentCount = root["changedAssignmentCount"],
                changedAssignments = assignments.Where(row => row["changed"]?.Value<bool>() == true || row["supported"]?.Value<bool>() == false).Take(16).ToArray(),
                issues
            };
        }

        static bool TryPreviewAssignment(SerializedProperty property, JToken value, out string previewValue, out string error)
        {
            previewValue = null;
            error = null;
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                error = "ManagedReference assignments are not supported in v1.";
                return false;
            }

            if (!TryCoerceValue(property, value, out object coerced, out error))
                return false;

            previewValue = DescribeCoercedValue(property, coerced);
            return true;
        }

        static bool TryAssignValue(SerializedProperty property, JToken value, out string error)
        {
            error = null;
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                error = "ManagedReference assignments are not supported in v1.";
                return false;
            }

            if (!TryCoerceValue(property, value, out object coerced, out error))
                return false;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    property.boolValue = (bool)coerced;
                    return true;
                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(coerced);
                    return true;
                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(coerced);
                    return true;
                case SerializedPropertyType.String:
                    property.stringValue = coerced?.ToString();
                    return true;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = Convert.ToInt32(coerced);
                    return true;
                case SerializedPropertyType.Color:
                    property.colorValue = (Color)coerced;
                    return true;
                case SerializedPropertyType.Vector2:
                    property.vector2Value = (Vector2)coerced;
                    return true;
                case SerializedPropertyType.Vector3:
                    property.vector3Value = (Vector3)coerced;
                    return true;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = coerced as UnityEngine.Object;
                    return true;
                default:
                    error = $"Serialized property type '{property.propertyType}' is not supported.";
                    return false;
            }
        }

        static bool TryCoerceValue(SerializedProperty property, JToken value, out object coerced, out string error)
        {
            coerced = null;
            error = null;
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        coerced = value != null && value.Type != JTokenType.Null && value.Value<bool>();
                        return true;
                    case SerializedPropertyType.Integer:
                        coerced = value == null || value.Type == JTokenType.Null ? 0 : value.Value<int>();
                        return true;
                    case SerializedPropertyType.Float:
                        coerced = value == null || value.Type == JTokenType.Null ? 0f : value.Value<float>();
                        return true;
                    case SerializedPropertyType.String:
                        coerced = value == null || value.Type == JTokenType.Null ? null : value.ToString();
                        return true;
                    case SerializedPropertyType.Enum:
                        if (value != null && value.Type == JTokenType.String)
                        {
                            string text = value.ToString();
                            int index = Array.FindIndex(property.enumDisplayNames, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
                            if (index < 0)
                                index = Array.FindIndex(property.enumNames, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
                            if (index < 0)
                            {
                                error = $"Enum value '{text}' was not found.";
                                return false;
                            }
                            coerced = index;
                            return true;
                        }
                        coerced = value == null || value.Type == JTokenType.Null ? 0 : value.Value<int>();
                        return true;
                    case SerializedPropertyType.Color:
                        if (UiAuthoringTools.TryParseColor(value, out Color color))
                        {
                            coerced = color;
                            return true;
                        }
                        error = "Expected color {r,g,b,a} or [r,g,b,a].";
                        return false;
                    case SerializedPropertyType.Vector2:
                        if (UiAuthoringTools.TryParseVector2(value, out Vector2 vector2))
                        {
                            coerced = vector2;
                            return true;
                        }
                        error = "Expected Vector2 {x,y} or [x,y].";
                        return false;
                    case SerializedPropertyType.Vector3:
                        if (TryParseVector3(value, out Vector3 vector3))
                        {
                            coerced = vector3;
                            return true;
                        }
                        error = "Expected Vector3 {x,y,z} or [x,y,z].";
                        return false;
                    case SerializedPropertyType.ObjectReference:
                        if (value == null || value.Type == JTokenType.Null)
                        {
                            coerced = null;
                            return true;
                        }
                        string path = SanitizeAssetPath(value.ToString());
                        UnityEngine.Object resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                        if (resolved == null)
                        {
                            error = $"Object reference asset '{path}' could not be loaded.";
                            return false;
                        }
                        coerced = resolved;
                        return true;
                    default:
                        error = $"Serialized property type '{property.propertyType}' is not supported.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static string DescribeProperty(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Integer => property.intValue.ToString(),
                SerializedPropertyType.Float => property.floatValue.ToString("R"),
                SerializedPropertyType.String => property.stringValue ?? string.Empty,
                SerializedPropertyType.Enum => property.enumDisplayNames.ElementAtOrDefault(property.enumValueIndex) ?? property.enumValueIndex.ToString(),
                SerializedPropertyType.Color => JsonConvert.SerializeObject(new { r = property.colorValue.r, g = property.colorValue.g, b = property.colorValue.b, a = property.colorValue.a }, Formatting.None),
                SerializedPropertyType.Vector2 => JsonConvert.SerializeObject(new { x = property.vector2Value.x, y = property.vector2Value.y }, Formatting.None),
                SerializedPropertyType.Vector3 => JsonConvert.SerializeObject(new { x = property.vector3Value.x, y = property.vector3Value.y, z = property.vector3Value.z }, Formatting.None),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue == null ? "null" : AssetDatabase.GetAssetPath(property.objectReferenceValue),
                _ => property.propertyType.ToString()
            };
        }

        static string DescribeCoercedValue(SerializedProperty property, object value)
        {
            return value switch
            {
                null => "null",
                Color color => JsonConvert.SerializeObject(new { r = color.r, g = color.g, b = color.b, a = color.a }, Formatting.None),
                Vector2 vector2 => JsonConvert.SerializeObject(new { x = vector2.x, y = vector2.y }, Formatting.None),
                Vector3 vector3 => JsonConvert.SerializeObject(new { x = vector3.x, y = vector3.y, z = vector3.z }, Formatting.None),
                UnityEngine.Object unityObject => AssetDatabase.GetAssetPath(unityObject),
                int index when property.propertyType == SerializedPropertyType.Enum => property.enumDisplayNames.ElementAtOrDefault(index) ?? index.ToString(),
                _ => value.ToString()
            };
        }

        static bool TryParseVector3(JToken value, out Vector3 vector)
        {
            vector = default;
            if (value is JArray array && array.Count >= 3)
            {
                vector = new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out JToken x) &&
                obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out JToken y) &&
                obj.TryGetValue("z", StringComparison.OrdinalIgnoreCase, out JToken z))
            {
                vector = new Vector3(x.Value<float>(), y.Value<float>(), z.Value<float>());
                return true;
            }

            return false;
        }

        static object NormalizeToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;
            return token.Type == JTokenType.Object || token.Type == JTokenType.Array ? token : token.ToObject<object>();
        }

        static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return normalized;
            return "Assets/" + normalized.TrimStart('/');
        }
    }
}
