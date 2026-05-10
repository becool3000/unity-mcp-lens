#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class AssetSerializedPropertyTools
    {
        const string ToolName = "Unity.Asset.SetSerializedProperties";

        sealed class Assignment
        {
            public string PropertyPath;
            public JToken Value;
            public string ObjectReferencePath;
            public string ObjectReferenceName;
            public bool HasValue;
        }

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    assetPath = new { type = "string", description = "ScriptableObject or data asset path under Assets/." },
                    mode = new { type = "string", description = "preview or apply. Defaults to preview.", @enum = new[] { "preview", "apply" } },
                    assignments = new
                    {
                        type = "array",
                        description = "Serialized property assignments.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                propertyPath = new { type = "string", description = "Serialized property path, for example sprite or nested.field." },
                                value = new { description = "Primitive value for scalar fields, or null to clear object references." },
                                objectReferencePath = new { type = "string", description = "Project asset path under Assets/ for ObjectReference properties." },
                                objectReferenceName = new { type = "string", description = "Optional subasset/object name when a path contains multiple objects of the expected type." }
                            },
                            required = new[] { "propertyPath" }
                        }
                    },
                    properties = new
                    {
                        type = "object",
                        description = "Compatibility object form: property path to primitive value or asset path."
                    }
                },
                required = new[] { "assetPath" }
            };
        }

        [McpTool(ToolName,
            "Previews or applies SerializedObject field assignments on ScriptableObject/data assets under Assets/. Object references must resolve to project assets.",
            "Set Asset Serialized Properties",
            Groups = new[] { "assets", "editor" },
            EnabledByDefault = true)]
        public static object SetSerializedProperties(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "set_serialized_properties", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = false;
            string errorKind = null;
            object data = null;
            string message = null;

            try
            {
                string assetPath;
                string mode;
                Assignment[] assignments;
                using (timing.Measure("normalization"))
                {
                    assetPath = NormalizeAssetPath(GetString(@params, "assetPath", "AssetPath", "path", "Path"));
                    mode = NormalizeMode(GetString(@params, "mode", "Mode"));
                    assignments = NormalizeAssignments(@params);
                }

                using (timing.Measure("service"))
                {
                    (success, message, data, errorKind) = Execute(assetPath, mode, assignments);
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                message = $"Asset serialized-property assignment failed: {ex.Message}";
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
                    ? Response.Success(message, ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "asset_set_serialized_properties_full_result" },
                        "asset_set_serialized_properties",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(message ?? "Asset serialized-property assignment failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static (bool success, string message, object data, string errorKind) Execute(string assetPath, string mode, Assignment[] assignments)
        {
            bool apply = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase);
            if (!IsSafeAssetsPath(assetPath))
            {
                return Failure("INVALID_ASSET_PATH", "assetPath must be a project path under Assets/.", new
                {
                    status = "failed",
                    mode,
                    assetPath,
                    applied = false,
                    savedAssets = false
                });
            }

            if (assignments.Length == 0)
            {
                return Failure("ASSIGNMENTS_REQUIRED", "At least one serialized property assignment is required.", new
                {
                    status = "failed",
                    mode,
                    assetPath,
                    applied = false,
                    savedAssets = false
                });
            }

            Object target = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (target == null)
            {
                return Failure("ASSET_NOT_FOUND", $"Asset '{assetPath}' was not found.", new
                {
                    status = "failed",
                    mode,
                    assetPath,
                    applied = false,
                    savedAssets = false
                });
            }

            if (target is GameObject)
            {
                return Failure("PREFAB_ASSET_NOT_SUPPORTED", "Use Unity.Prefab.SetSerializedProperties for prefab assets.", new
                {
                    status = "failed",
                    mode,
                    assetPath,
                    targetType = target.GetType().FullName,
                    applied = false,
                    savedAssets = false
                });
            }

            var serializedObject = new SerializedObject(target);
            serializedObject.Update();
            var rows = new List<object>();
            var missing = new List<string>();
            var unsupported = new List<object>();
            int willChangeCount = 0;

            foreach (Assignment assignment in assignments)
            {
                SerializedProperty property = serializedObject.FindProperty(assignment.PropertyPath);
                if (property == null)
                {
                    missing.Add(assignment.PropertyPath);
                    rows.Add(new
                    {
                        propertyPath = assignment.PropertyPath,
                        found = false,
                        changed = false,
                        error = "missing_property"
                    });
                    continue;
                }

                object before = ReadPropertyValue(property);
                bool prepared = TryPrepareValue(property, assignment, out object requested, out string error);
                bool willChange = prepared && !JsonEquals(before, requested);
                if (willChange)
                    willChangeCount++;

                if (!prepared)
                {
                    unsupported.Add(new
                    {
                        propertyPath = assignment.PropertyPath,
                        propertyType = property.propertyType.ToString(),
                        error
                    });
                    rows.Add(new
                    {
                        propertyPath = assignment.PropertyPath,
                        found = true,
                        propertyType = property.propertyType.ToString(),
                        before,
                        requested,
                        changed = false,
                        error
                    });
                    continue;
                }

                if (apply)
                    ApplyPreparedValue(property, requested);

                rows.Add(new
                {
                    propertyPath = assignment.PropertyPath,
                    found = true,
                    propertyType = property.propertyType.ToString(),
                    before,
                    requested,
                    changed = willChange,
                    error = (string)null
                });
            }

            if (missing.Count > 0 || unsupported.Count > 0)
            {
                object failureData = BuildData("failed", mode, apply, assetPath, target, rows, missing, unsupported, willChangeCount, 0, savedAssets: false);
                return Failure("UNSUPPORTED_OR_MISSING_PROPERTIES", "One or more serialized property assignments could not be prepared.", failureData);
            }

            bool applied = false;
            bool savedAssets = false;
            int changedCount = 0;
            if (apply)
            {
                applied = serializedObject.ApplyModifiedProperties();
                if (applied)
                {
                    changedCount = rows.Count(row => JObject.FromObject(row)["changed"]?.Value<bool>() == true);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssets();
                    savedAssets = true;
                }
            }

            string status = apply ? "applied" : "preview";
            object data = BuildData(status, mode, apply, assetPath, target, rows, missing, unsupported, willChangeCount, changedCount, savedAssets);
            string message = apply
                ? $"Applied {changedCount} serialized asset propert{(changedCount == 1 ? "y" : "ies")} on '{assetPath}'."
                : $"Previewed {willChangeCount} serialized asset propert{(willChangeCount == 1 ? "y" : "ies")} on '{assetPath}'.";
            return (true, message, data, null);
        }

        static object BuildData(
            string status,
            string mode,
            bool apply,
            string assetPath,
            Object target,
            List<object> rows,
            List<string> missing,
            List<object> unsupported,
            int willChangeCount,
            int changedCount,
            bool savedAssets)
        {
            return new
            {
                status,
                mode,
                previewOnly = !apply,
                applied = apply,
                assetPath,
                assetGuid = AssetDatabase.AssetPathToGUID(assetPath),
                targetName = target?.name,
                targetType = target?.GetType().FullName,
                assignmentCount = rows.Count,
                willChangeCount,
                changedCount,
                missingPropertyCount = missing.Count,
                unsupportedPropertyCount = unsupported.Count,
                savedAssets,
                missingProperties = missing.ToArray(),
                unsupportedProperties = unsupported.ToArray(),
                properties = rows.ToArray()
            };
        }

        static (bool success, string message, object data, string errorKind) Failure(string errorKind, string message, object data)
        {
            return (false, message, data, errorKind);
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray rows = root["properties"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                mode = root["mode"],
                previewOnly = root["previewOnly"],
                applied = root["applied"],
                assetPath = root["assetPath"],
                assetGuid = root["assetGuid"],
                targetType = root["targetType"],
                assignmentCount = root["assignmentCount"],
                willChangeCount = root["willChangeCount"],
                changedCount = root["changedCount"],
                missingPropertyCount = root["missingPropertyCount"],
                unsupportedPropertyCount = root["unsupportedPropertyCount"],
                savedAssets = root["savedAssets"],
                missingProperties = root["missingProperties"],
                unsupportedProperties = root["unsupportedProperties"],
                properties = rows.Take(12).ToArray(),
                omittedPropertyCount = Math.Max(0, rows.Count - 12)
            };
        }

        static bool TryPrepareValue(SerializedProperty property, Assignment assignment, out object requested, out string error)
        {
            requested = null;
            error = null;

            try
            {
                if (!assignment.HasValue && string.IsNullOrWhiteSpace(assignment.ObjectReferencePath))
                {
                    error = "Assignment requires value or objectReferencePath.";
                    return false;
                }

                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        requested = assignment.Value?.Value<int>() ?? 0;
                        return true;
                    case SerializedPropertyType.Boolean:
                        requested = assignment.Value?.Value<bool>() ?? false;
                        return true;
                    case SerializedPropertyType.Float:
                        requested = assignment.Value?.Value<float>() ?? 0f;
                        return true;
                    case SerializedPropertyType.String:
                        requested = assignment.Value?.Type == JTokenType.Null ? null : assignment.Value?.Value<string>() ?? string.Empty;
                        return true;
                    case SerializedPropertyType.Enum:
                        requested = ResolveEnumValue(property, assignment.Value);
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        return TryPrepareObjectReference(property, assignment, out requested, out error);
                    default:
                        error = $"Serialized property type '{property.propertyType}' is not supported by this tool.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool TryPrepareObjectReference(SerializedProperty property, Assignment assignment, out object requested, out string error)
        {
            requested = null;
            error = null;
            string referencePath = assignment.ObjectReferencePath;
            if (string.IsNullOrWhiteSpace(referencePath) && assignment.Value?.Type == JTokenType.String)
                referencePath = assignment.Value.Value<string>();

            if (string.IsNullOrWhiteSpace(referencePath) || assignment.Value?.Type == JTokenType.Null)
            {
                requested = null;
                return true;
            }

            referencePath = NormalizeAssetPath(referencePath);
            if (!IsSafeAssetsPath(referencePath))
            {
                error = "Object reference paths must resolve under Assets/.";
                return false;
            }

            Type expectedType = ResolveExpectedObjectReferenceType(property);
            if (!TryResolveObjectReference(referencePath, expectedType, assignment.ObjectReferenceName, out Object reference, out error))
                return false;

            string actualPath = AssetDatabase.GetAssetPath(reference);
            if (!IsSafeAssetsPath(actualPath))
            {
                error = "Resolved object reference is not a project asset under Assets/.";
                return false;
            }

            requested = DescribeObjectReference(reference);
            return true;
        }

        static void ApplyPreparedValue(SerializedProperty property, object requested)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = Convert.ToInt32(requested);
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = Convert.ToBoolean(requested);
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = Convert.ToSingle(requested);
                    break;
                case SerializedPropertyType.String:
                    property.stringValue = requested?.ToString();
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = Convert.ToInt32(JObject.FromObject(requested)["enumValueIndex"]?.Value<int>() ?? 0);
                    break;
                case SerializedPropertyType.ObjectReference:
                    var requestedReference = JObject.FromObject(requested ?? new { });
                    string referencePath = requestedReference["assetPath"]?.Value<string>();
                    property.objectReferenceValue = string.IsNullOrWhiteSpace(referencePath)
                        ? null
                        : ResolveObjectReferenceForApply(property, referencePath, requestedReference);
                    break;
            }
        }

        static Object ResolveObjectReferenceForApply(SerializedProperty property, string referencePath, JObject requestedReference)
        {
            Type expectedType = ResolveObjectReferenceTypeByName(requestedReference?["type"]?.Value<string>())
                ?? ResolveExpectedObjectReferenceType(property);
            string objectReferenceName = requestedReference?["name"]?.Value<string>();
            if (!TryResolveObjectReference(referencePath, expectedType, objectReferenceName, out Object reference, out string error))
                throw new InvalidOperationException(error);

            return reference;
        }

        static bool TryResolveObjectReference(string referencePath, Type expectedType, string objectReferenceName, out Object reference, out string error)
        {
            reference = null;
            error = null;

            Type requestedType = NormalizeObjectReferenceType(expectedType);
            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(referencePath);
            if (mainAsset == null)
            {
                error = $"Object reference asset '{referencePath}' was not found.";
                return false;
            }

            if (requestedType != typeof(Object))
            {
                reference = LoadAssetAtPath(referencePath, requestedType, objectReferenceName);
                if (reference == null)
                {
                    error = $"Object reference asset '{referencePath}' was found, but no {requestedType.Name} object matched the serialized field.";
                    return false;
                }

                return true;
            }

            reference = string.IsNullOrWhiteSpace(objectReferenceName)
                ? mainAsset
                : FindAssetObjectAtPath(referencePath, typeof(Object), objectReferenceName);
            if (reference == null)
            {
                error = $"Object reference asset '{referencePath}' was found, but no object named '{objectReferenceName}' matched.";
                return false;
            }

            return true;
        }

        static Object LoadAssetAtPath(string referencePath, Type requestedType, string objectReferenceName)
        {
            if (string.IsNullOrWhiteSpace(objectReferenceName))
            {
                Object reference = AssetDatabase.LoadAssetAtPath(referencePath, requestedType);
                if (reference != null)
                    return reference;
            }

            return FindAssetObjectAtPath(referencePath, requestedType, objectReferenceName);
        }

        static Object FindAssetObjectAtPath(string referencePath, Type requestedType, string objectReferenceName)
        {
            return AssetDatabase.LoadAllAssetsAtPath(referencePath)
                .Where(candidate => candidate != null && requestedType.IsInstanceOfType(candidate))
                .FirstOrDefault(candidate => string.IsNullOrWhiteSpace(objectReferenceName) ||
                    string.Equals(candidate.name, objectReferenceName, StringComparison.Ordinal));
        }

        static Type ResolveExpectedObjectReferenceType(SerializedProperty property)
        {
            Type currentType = property?.objectReferenceValue?.GetType();
            if (IsUsableObjectReferenceType(currentType))
                return currentType;

            return ResolveObjectReferenceTypeByName(ExtractSerializedObjectReferenceTypeName(property?.type))
                ?? typeof(Object);
        }

        static string ExtractSerializedObjectReferenceTypeName(string serializedType)
        {
            if (string.IsNullOrWhiteSpace(serializedType))
                return null;

            int start = serializedType.IndexOf('<');
            int end = serializedType.LastIndexOf('>');
            string typeName = start >= 0 && end > start
                ? serializedType.Substring(start + 1, end - start - 1)
                : serializedType;

            return typeName.Trim().TrimStart('$');
        }

        static Type ResolveObjectReferenceTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type direct = Type.GetType(typeName);
            if (IsUsableObjectReferenceType(direct))
                return direct;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type resolved = null;
                try
                {
                    resolved = assembly.GetTypes().FirstOrDefault(type =>
                        IsUsableObjectReferenceType(type) &&
                        (string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                         string.Equals(type.Name, typeName, StringComparison.Ordinal)));
                }
                catch
                {
                    continue;
                }

                if (resolved != null)
                    return resolved;
            }

            return null;
        }

        static Type NormalizeObjectReferenceType(Type type)
        {
            return IsUsableObjectReferenceType(type) ? type : typeof(Object);
        }

        static bool IsUsableObjectReferenceType(Type type)
        {
            return type != null && typeof(Object).IsAssignableFrom(type);
        }

        static object ResolveEnumValue(SerializedProperty property, JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return new { enumValueIndex = 0, enumValue = property.enumNames.FirstOrDefault() ?? string.Empty };

            if (token.Type == JTokenType.Integer)
            {
                int index = Math.Clamp(token.Value<int>(), 0, Math.Max(0, property.enumNames.Length - 1));
                return new { enumValueIndex = index, enumValue = property.enumNames.ElementAtOrDefault(index) ?? string.Empty };
            }

            string text = token.Value<string>() ?? string.Empty;
            int matched = Array.FindIndex(property.enumNames, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
            if (matched < 0)
                matched = Array.FindIndex(property.enumDisplayNames, name => string.Equals(name, text, StringComparison.OrdinalIgnoreCase));
            if (matched < 0)
                throw new InvalidOperationException($"Enum value '{text}' was not found.");

            return new { enumValueIndex = matched, enumValue = property.enumNames.ElementAtOrDefault(matched) ?? text };
        }

        static object ReadPropertyValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Enum => new { enumValueIndex = property.enumValueIndex, enumValue = property.enumNames.ElementAtOrDefault(property.enumValueIndex) ?? string.Empty },
                SerializedPropertyType.ObjectReference => DescribeObjectReference(property.objectReferenceValue),
                _ => new { unsupported = true, propertyType = property.propertyType.ToString() }
            };
        }

        static object DescribeObjectReference(Object reference)
        {
            if (reference == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(reference);
            return new
            {
                name = reference.name,
                type = reference.GetType().FullName,
                assetPath,
                assetGuid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath),
                objectId = UnityApiAdapter.GetObjectIdOrZero(reference)
            };
        }

        static Assignment[] NormalizeAssignments(JObject parameters)
        {
            var assignments = new List<Assignment>();
            JToken token = GetToken(parameters, "assignments", "Assignments", "propertyAssignments", "PropertyAssignments");
            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    if (item is not JObject obj)
                        continue;

                    assignments.Add(new Assignment
                    {
                        PropertyPath = GetString(obj, "propertyPath", "PropertyPath", "path", "Path", "fieldName", "FieldName"),
                        Value = GetToken(obj, "value", "Value"),
                        ObjectReferencePath = GetString(obj, "objectReferencePath", "ObjectReferencePath", "referencePath", "ReferencePath", "assetReferencePath", "AssetReferencePath"),
                        ObjectReferenceName = GetString(obj, "objectReferenceName", "ObjectReferenceName", "referenceName", "ReferenceName", "assetReferenceName", "AssetReferenceName"),
                        HasValue = GetToken(obj, "value", "Value") != null
                    });
                }
            }

            if (assignments.Count == 0 && GetToken(parameters, "properties", "Properties") is JObject properties)
            {
                foreach (JProperty property in properties.Properties())
                {
                    assignments.Add(new Assignment
                    {
                        PropertyPath = property.Name,
                        Value = property.Value,
                        HasValue = true
                    });
                }
            }

            return assignments
                .Where(assignment => !string.IsNullOrWhiteSpace(assignment.PropertyPath))
                .ToArray();
        }

        static string NormalizeMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
                return "preview";

            string normalized = mode.Trim().ToLowerInvariant();
            if (normalized == "preview" || normalized == "apply")
                return normalized;

            throw new InvalidOperationException("mode must be 'preview' or 'apply'.");
        }

        static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Trim().Replace('\\', '/');
        }

        static bool IsSafeAssetsPath(string assetPath)
        {
            return !string.IsNullOrWhiteSpace(assetPath) &&
                assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.Contains("..");
        }

        static bool JsonEquals(object left, object right)
        {
            return string.Equals(JsonConvert.SerializeObject(left, Formatting.None), JsonConvert.SerializeObject(right, Formatting.None), StringComparison.Ordinal);
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
    }
}
