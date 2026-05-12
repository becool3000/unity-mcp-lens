#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class RuntimeGetComponentSnapshotTools
    {
        const string ToolName = "Unity.Runtime.GetComponentSnapshot";
        const int MaxCollectionItems = 16;
        const int MaxNestedMembers = 20;

        static readonly HashSet<string> k_UnsafeUnityPropertyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "material",
            "materials",
            "mesh"
        };

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { type = "string", description = "Target GameObject name, hierarchy path, or id." },
                    searchMethod = new { type = "string", description = "Target search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_name." },
                    componentType = new { type = "string", description = "Component type name. Short or fully-qualified names are accepted." },
                    componentIndex = new { type = "integer", description = "0-based index among matching components. Defaults to 0." },
                    includePublicProperties = new { type = "boolean", description = "Read public instance properties with zero-argument getters. Defaults to true." },
                    includeSerializedFields = new { type = "boolean", description = "Read Unity serialized fields through SerializedObject. Defaults to true." },
                    maxDepth = new { type = "integer", description = "Maximum nested value depth. Defaults to 2 and clamps to 0-5." },
                    maxMembers = new { type = "integer", description = "Maximum member rows returned across fields and properties. Defaults to 80." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects when resolving target. Defaults to false." },
                    requirePlayMode = new { type = "boolean", description = "Refuse outside play mode. Defaults to true." }
                },
                required = new[] { "target", "componentType" }
            };
        }

        [McpTool(ToolName,
            "Reads public properties and serialized fields from a runtime component without executing arbitrary project code.",
            "Get Runtime Component Snapshot",
            Groups = new[] { "runtime" },
            EnabledByDefault = true)]
        public static object GetComponentSnapshot(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "get_component_snapshot", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                string target;
                string searchMethod;
                string componentTypeName;
                int componentIndex;
                bool includePublicProperties;
                bool includeSerializedFields;
                int maxDepth;
                int maxMembers;
                bool includeInactive;
                bool requirePlayMode;

                using (timing.Measure("normalization"))
                {
                    target = GetString(@params, "target", "Target");
                    searchMethod = GetString(@params, "searchMethod", "SearchMethod") ?? "by_name";
                    componentTypeName = GetString(@params, "componentType", "ComponentType");
                    componentIndex = Math.Max(0, GetInt(@params, 0, "componentIndex", "ComponentIndex"));
                    includePublicProperties = GetBool(@params, true, "includePublicProperties", "IncludePublicProperties");
                    includeSerializedFields = GetBool(@params, true, "includeSerializedFields", "IncludeSerializedFields");
                    maxDepth = Math.Clamp(GetInt(@params, 2, "maxDepth", "MaxDepth"), 0, 5);
                    maxMembers = Math.Clamp(GetInt(@params, 80, "maxMembers", "MaxMembers"), 1, 400);
                    includeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive");
                    requirePlayMode = GetBool(@params, true, "requirePlayMode", "RequirePlayMode");
                }

                using (timing.Measure("service"))
                {
                    data = BuildSnapshot(
                        target,
                        searchMethod,
                        componentTypeName,
                        componentIndex,
                        includePublicProperties,
                        includeSerializedFields,
                        maxDepth,
                        maxMembers,
                        includeInactive,
                        requirePlayMode);
                    var dataObject = JObject.FromObject(data);
                    success = string.Equals(dataObject.Value<string>("status"), "ready", StringComparison.OrdinalIgnoreCase);
                    errorKind = success ? null : dataObject.Value<string>("reason") ?? "runtime_component_snapshot_failed";
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
                    ? Response.Success("Runtime component snapshot completed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "runtime_component_snapshot_full_result" },
                        "runtime_component_snapshot",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("RUNTIME_COMPONENT_SNAPSHOT_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static object BuildSnapshot(
            string target,
            string searchMethod,
            string componentTypeName,
            int componentIndex,
            bool includePublicProperties,
            bool includeSerializedFields,
            int maxDepth,
            int maxMembers,
            bool includeInactive,
            bool requirePlayMode)
        {
            if (requirePlayMode && !EditorApplication.isPlaying)
            {
                return Failed("not_in_play_mode", "Runtime component snapshots require Play Mode by default.", requirePlayMode);
            }

            if (string.IsNullOrWhiteSpace(target))
                return Failed("target_required", "target is required.", requirePlayMode);
            if (string.IsNullOrWhiteSpace(componentTypeName))
                return Failed("component_type_required", "componentType is required.", requirePlayMode);
            if (!includePublicProperties && !includeSerializedFields)
                return Failed("no_member_sources_enabled", "At least one of includePublicProperties or includeSerializedFields must be true.", requirePlayMode);
            if (!UnityComponentResolver.TryResolve(componentTypeName, out Type componentType, out string typeError))
                return Failed("component_type_not_found", typeError, requirePlayMode);

            GameObject targetObject = ResolveTarget(target, searchMethod, includeInactive);
            if (targetObject == null)
                return Failed("target_not_found", $"Target '{target}' was not found using search method '{searchMethod}'.", requirePlayMode);

            Component[] components = targetObject.GetComponents(componentType);
            if (components.Length <= componentIndex || components[componentIndex] == null)
                return Failed("component_not_found", $"Component '{componentTypeName}' with index {componentIndex} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}'.", requirePlayMode);

            Component component = components[componentIndex];
            var members = new List<object>();
            int skippedMemberCount = 0;
            int truncatedMemberCount = 0;
            int errorCount = 0;

            if (includeSerializedFields)
                ReadSerializedMembers(component, maxDepth, maxMembers, members, ref skippedMemberCount, ref truncatedMemberCount, ref errorCount);
            if (includePublicProperties)
                ReadPublicProperties(component, maxDepth, maxMembers, members, ref skippedMemberCount, ref truncatedMemberCount, ref errorCount);

            return new
            {
                status = "ready",
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode,
                target = new
                {
                    name = targetObject.name,
                    path = UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform),
                    sceneName = targetObject.scene.name,
                    scenePath = targetObject.scene.path,
                    activeSelf = targetObject.activeSelf,
                    activeInHierarchy = targetObject.activeInHierarchy,
                    objectId = GetStableObjectId(targetObject)
                },
                component = new
                {
                    requestedType = componentTypeName,
                    resolvedType = component.GetType().FullName,
                    componentIndex,
                    componentId = GetStableObjectId(component),
                    enabled = component is Behaviour behaviour ? behaviour.enabled : (bool?)null
                },
                snapshot = new
                {
                    includePublicProperties,
                    includeSerializedFields,
                    maxDepth,
                    maxMembers,
                    memberCount = members.Count,
                    skippedMemberCount,
                    truncatedMemberCount,
                    errorCount,
                    members = members.ToArray()
                }
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JObject snapshot = root["snapshot"] as JObject ?? new JObject();
            JArray members = snapshot["members"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                reason = root["reason"],
                isPlaying = root["isPlaying"],
                requirePlayMode = root["requirePlayMode"],
                target = root["target"],
                component = root["component"],
                snapshot = new
                {
                    includePublicProperties = snapshot["includePublicProperties"],
                    includeSerializedFields = snapshot["includeSerializedFields"],
                    maxDepth = snapshot["maxDepth"],
                    maxMembers = snapshot["maxMembers"],
                    memberCount = snapshot["memberCount"],
                    skippedMemberCount = snapshot["skippedMemberCount"],
                    truncatedMemberCount = snapshot["truncatedMemberCount"],
                    errorCount = snapshot["errorCount"],
                    members = members.Take(40).ToArray(),
                    compactOmittedMemberCount = Math.Max(0, members.Count - 40)
                }
            };
        }

        static void ReadSerializedMembers(
            Component component,
            int maxDepth,
            int maxMembers,
            List<object> members,
            ref int skippedMemberCount,
            ref int truncatedMemberCount,
            ref int errorCount)
        {
            SerializedObject serializedObject = null;
            try
            {
                serializedObject = new SerializedObject(component);
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (string.Equals(iterator.name, "m_Script", StringComparison.Ordinal))
                    {
                        skippedMemberCount++;
                        continue;
                    }

                    bool isArray = IsArrayProperty(iterator);
                    if (iterator.propertyType == SerializedPropertyType.Generic && iterator.hasVisibleChildren && !isArray)
                    {
                        enterChildren = true;
                        continue;
                    }

                    if (members.Count >= maxMembers)
                    {
                        truncatedMemberCount++;
                        continue;
                    }

                    SerializedProperty copy = iterator.Copy();
                    members.Add(new
                    {
                        kind = "serializedField",
                        name = copy.displayName,
                        path = copy.propertyPath,
                        valueType = copy.type,
                        serializedPropertyType = copy.propertyType.ToString(),
                        value = ReadSerializedPropertyValue(copy, maxDepth),
                        declaringType = component.GetType().FullName
                    });
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                if (members.Count < maxMembers)
                {
                    members.Add(new
                    {
                        kind = "serializedField",
                        name = "SerializedObject",
                        errorKind = ex.GetType().Name,
                        error = ex.Message,
                        declaringType = component.GetType().FullName
                    });
                }
            }
            finally
            {
                serializedObject?.Dispose();
            }
        }

        static void ReadPublicProperties(
            Component component,
            int maxDepth,
            int maxMembers,
            List<object> members,
            ref int skippedMemberCount,
            ref int truncatedMemberCount,
            ref int errorCount)
        {
            foreach (PropertyInfo property in component.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                MethodInfo getter = property.GetGetMethod();
                if (getter == null || getter.IsStatic || property.GetIndexParameters().Length > 0 || IsUnsafeProperty(property))
                {
                    skippedMemberCount++;
                    continue;
                }

                if (members.Count >= maxMembers)
                {
                    truncatedMemberCount++;
                    continue;
                }

                try
                {
                    object value = property.GetValue(component, null);
                    members.Add(new
                    {
                        kind = "publicProperty",
                        name = property.Name,
                        path = property.Name,
                        valueType = property.PropertyType.FullName ?? property.PropertyType.Name,
                        declaringType = property.DeclaringType?.FullName,
                        value = DescribeValue(value, maxDepth, 0)
                    });
                }
                catch (Exception ex)
                {
                    errorCount++;
                    members.Add(new
                    {
                        kind = "publicProperty",
                        name = property.Name,
                        path = property.Name,
                        valueType = property.PropertyType.FullName ?? property.PropertyType.Name,
                        declaringType = property.DeclaringType?.FullName,
                        errorKind = ex.GetType().Name,
                        error = ex.InnerException?.Message ?? ex.Message
                    });
                }
            }
        }

        static object ReadSerializedPropertyValue(SerializedProperty property, int maxDepth)
        {
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Character:
                    case SerializedPropertyType.ArraySize:
                    case SerializedPropertyType.FixedBufferSize:
                        return property.intValue;
                    case SerializedPropertyType.Boolean:
                        return property.boolValue;
                    case SerializedPropertyType.Float:
                        return property.floatValue;
                    case SerializedPropertyType.String:
                        return property.stringValue;
                    case SerializedPropertyType.Color:
                        return DescribeColor(property.colorValue);
                    case SerializedPropertyType.ObjectReference:
                    case SerializedPropertyType.ExposedReference:
                        return DescribeUnityObject(property.objectReferenceValue);
                    case SerializedPropertyType.Enum:
                        return new
                        {
                            enumValueIndex = property.enumValueIndex,
                            enumValue = property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                                ? property.enumDisplayNames[property.enumValueIndex]
                                : null
                        };
                    case SerializedPropertyType.Vector2:
                        return DescribeVector2(property.vector2Value);
                    case SerializedPropertyType.Vector3:
                        return DescribeVector3(property.vector3Value);
                    case SerializedPropertyType.Vector4:
                        return DescribeVector4(property.vector4Value);
                    case SerializedPropertyType.Rect:
                        return DescribeRect(property.rectValue);
                    case SerializedPropertyType.Bounds:
                        return DescribeBounds(property.boundsValue);
                    case SerializedPropertyType.Quaternion:
                        return DescribeQuaternion(property.quaternionValue);
                    case SerializedPropertyType.Vector2Int:
                        return DescribeVector2Int(property.vector2IntValue);
                    case SerializedPropertyType.Vector3Int:
                        return DescribeVector3Int(property.vector3IntValue);
                    case SerializedPropertyType.RectInt:
                        return DescribeRectInt(property.rectIntValue);
                    case SerializedPropertyType.BoundsInt:
                        return DescribeBoundsInt(property.boundsIntValue);
                    case SerializedPropertyType.AnimationCurve:
                        return new
                        {
                            keyCount = property.animationCurveValue?.length ?? 0
                        };
                    case SerializedPropertyType.ManagedReference:
                        return new
                        {
                            managedReferenceType = property.managedReferenceFullTypename,
                            value = DescribeValue(property.managedReferenceValue, maxDepth, 0)
                        };
                    case SerializedPropertyType.Generic when IsArrayProperty(property):
                        return new
                        {
                            kind = "array",
                            size = property.arraySize,
                            omittedElementCount = property.arraySize
                        };
                    default:
                        return new
                        {
                            propertyType = property.propertyType.ToString(),
                            valueType = property.type,
                            omittedReason = "unsupported_serialized_property_type"
                        };
                }
            }
            catch (Exception ex)
            {
                return new
                {
                    errorKind = ex.GetType().Name,
                    error = ex.Message
                };
            }
        }

        static object DescribeValue(object value, int maxDepth, int depth)
        {
            if (value == null)
                return null;

            if (value is Object unityObject)
                return DescribeUnityObject(unityObject);

            Type type = value.GetType();
            if (type.IsEnum)
                return new { enumValue = value.ToString(), rawValue = Convert.ToInt64(value, CultureInfo.InvariantCulture) };
            if (value is string || type.IsPrimitive || value is decimal)
                return value;
            if (value is Vector2 vector2)
                return DescribeVector2(vector2);
            if (value is Vector3 vector3)
                return DescribeVector3(vector3);
            if (value is Vector4 vector4)
                return DescribeVector4(vector4);
            if (value is Quaternion quaternion)
                return DescribeQuaternion(quaternion);
            if (value is Color color)
                return DescribeColor(color);
            if (value is Rect rect)
                return DescribeRect(rect);
            if (value is Bounds bounds)
                return DescribeBounds(bounds);
            if (value is Vector2Int vector2Int)
                return DescribeVector2Int(vector2Int);
            if (value is Vector3Int vector3Int)
                return DescribeVector3Int(vector3Int);
            if (value is RectInt rectInt)
                return DescribeRectInt(rectInt);
            if (value is BoundsInt boundsInt)
                return DescribeBoundsInt(boundsInt);
            if (value is IDictionary dictionary)
                return DescribeDictionary(dictionary, maxDepth, depth);
            if (value is IEnumerable enumerable && value is not string)
                return DescribeEnumerable(enumerable, maxDepth, depth);

            if (depth >= maxDepth || type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
            {
                return new
                {
                    valueType = type.FullName ?? type.Name,
                    text = value.ToString(),
                    truncated = depth >= maxDepth
                };
            }

            var members = new List<object>();
            int omittedMemberCount = 0;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (members.Count >= MaxNestedMembers)
                {
                    omittedMemberCount++;
                    continue;
                }

                try
                {
                    members.Add(new
                    {
                        kind = "field",
                        name = field.Name,
                        valueType = field.FieldType.FullName ?? field.FieldType.Name,
                        value = DescribeValue(field.GetValue(value), maxDepth, depth + 1)
                    });
                }
                catch (Exception ex)
                {
                    members.Add(new
                    {
                        kind = "field",
                        name = field.Name,
                        errorKind = ex.GetType().Name,
                        error = ex.Message
                    });
                }
            }

            return new
            {
                valueType = type.FullName ?? type.Name,
                members = members.ToArray(),
                omittedMemberCount
            };
        }

        static object DescribeDictionary(IDictionary dictionary, int maxDepth, int depth)
        {
            if (depth >= maxDepth)
            {
                return new
                {
                    valueType = dictionary.GetType().FullName,
                    count = dictionary.Count,
                    omittedReason = "max_depth"
                };
            }

            var items = new List<object>();
            int index = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                if (index >= MaxCollectionItems)
                    break;
                items.Add(new
                {
                    key = DescribeValue(entry.Key, maxDepth, depth + 1),
                    value = DescribeValue(entry.Value, maxDepth, depth + 1)
                });
                index++;
            }

            return new
            {
                valueType = dictionary.GetType().FullName,
                count = dictionary.Count,
                items = items.ToArray(),
                omittedItemCount = Math.Max(0, dictionary.Count - items.Count)
            };
        }

        static object DescribeEnumerable(IEnumerable enumerable, int maxDepth, int depth)
        {
            if (depth >= maxDepth)
            {
                return new
                {
                    valueType = enumerable.GetType().FullName,
                    omittedReason = "max_depth"
                };
            }

            var items = new List<object>();
            int totalCount = 0;
            foreach (object item in enumerable)
            {
                if (items.Count < MaxCollectionItems)
                    items.Add(DescribeValue(item, maxDepth, depth + 1));
                totalCount++;
            }

            return new
            {
                valueType = enumerable.GetType().FullName,
                count = totalCount,
                items = items.ToArray(),
                omittedItemCount = Math.Max(0, totalCount - items.Count)
            };
        }

        static object DescribeUnityObject(Object unityObject)
        {
            if (unityObject == null)
                return null;

            GameObject gameObject = unityObject as GameObject;
            Component component = unityObject as Component;
            Transform transform = gameObject != null ? gameObject.transform : component?.transform;
            return new
            {
                objectType = unityObject.GetType().FullName,
                name = unityObject.name,
                objectId = GetStableObjectId(unityObject),
                gameObjectPath = transform == null ? null : UiDiagnosticsHelper.GetHierarchyPath(transform),
                sceneName = transform == null ? null : transform.gameObject.scene.name,
                scenePath = transform == null ? null : transform.gameObject.scene.path
            };
        }

        static object DescribeVector2(Vector2 value) => new { x = value.x, y = value.y };

        static object DescribeVector3(Vector3 value) => new { x = value.x, y = value.y, z = value.z };

        static object DescribeVector4(Vector4 value) => new { x = value.x, y = value.y, z = value.z, w = value.w };

        static object DescribeVector2Int(Vector2Int value) => new { x = value.x, y = value.y };

        static object DescribeVector3Int(Vector3Int value) => new { x = value.x, y = value.y, z = value.z };

        static object DescribeQuaternion(Quaternion value) => new { x = value.x, y = value.y, z = value.z, w = value.w };

        static object DescribeColor(Color value) => new { r = value.r, g = value.g, b = value.b, a = value.a };

        static object DescribeRect(Rect value) => new { x = value.x, y = value.y, width = value.width, height = value.height };

        static object DescribeBounds(Bounds value) => new { center = DescribeVector3(value.center), size = DescribeVector3(value.size) };

        static object DescribeRectInt(RectInt value) => new { x = value.x, y = value.y, width = value.width, height = value.height };

        static object DescribeBoundsInt(BoundsInt value) => new { position = DescribeVector3Int(value.position), size = DescribeVector3Int(value.size) };

        static bool IsUnsafeProperty(PropertyInfo property)
        {
            if (property.GetCustomAttribute<ObsoleteAttribute>() != null)
                return true;

            return k_UnsafeUnityPropertyNames.Contains(property.Name);
        }

        static GameObject ResolveTarget(string target, string searchMethod, bool includeInactive)
        {
            if (string.IsNullOrWhiteSpace(target))
                return null;

            string method = (searchMethod ?? "by_name").Trim().ToLowerInvariant();
            GameObject[] objects = UnityApiAdapter.FindObjectsByType<GameObject>(includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude);
            foreach (GameObject candidate in objects.OrderBy(candidate => UiDiagnosticsHelper.GetHierarchyPath(candidate.transform), StringComparer.Ordinal))
            {
                string path = UiDiagnosticsHelper.GetHierarchyPath(candidate.transform);
                if (method == "by_id" && ObjectIdEquals(candidate, target))
                    return candidate;
                if (method == "by_path" && string.Equals(path, target, StringComparison.Ordinal))
                    return candidate;
                if (method == "by_id_or_name_or_path" &&
                    (ObjectIdEquals(candidate, target) ||
                     string.Equals(path, target, StringComparison.Ordinal) ||
                     string.Equals(candidate.name, target, StringComparison.Ordinal)))
                {
                    return candidate;
                }
                if (method != "by_id" && method != "by_path" && method != "by_id_or_name_or_path" && string.Equals(candidate.name, target, StringComparison.Ordinal))
                    return candidate;
            }

            return null;
        }

        static bool IsArrayProperty(SerializedProperty property)
        {
            try
            {
                return property.isArray && property.propertyType == SerializedPropertyType.Generic;
            }
            catch
            {
                return false;
            }
        }

        static object Failed(string reason, string message, bool requirePlayMode)
        {
            return new
            {
                status = "failed",
                reason,
                message,
                isPlaying = EditorApplication.isPlaying,
                requirePlayMode
            };
        }

        static string GetStableObjectId(Object obj)
        {
            if (obj == null)
                return null;

#pragma warning disable CS0618
            return obj.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#pragma warning restore CS0618
        }

        static bool ObjectIdEquals(Object obj, string id)
        {
            string trimmed = id?.Trim();
            return string.Equals(GetStableObjectId(obj), trimmed, StringComparison.Ordinal) ||
                UnityApiAdapter.ObjectIdEquals(obj, trimmed);
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
