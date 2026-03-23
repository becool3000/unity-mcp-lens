using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Tools
{
    public static class PrefabTools
    {
        public const string SetSerializedPropertiesDescription = @"Sets serialized property values on a prefab asset without requiring a custom RunCommand.

Args:
    PrefabPath: Prefab asset path under Assets/.
    Assignments: Array of serialized property assignments.
      TargetPath: Relative child path under the prefab root. Use '.' or omit for the root GameObject.
      ComponentType: Component type name on the target GameObject.
      ComponentIndex: 0-based component index when multiple matching components exist.
      PropertyPath: Serialized property path to set.
      Value: Primitive value or object-reference asset path string.
    PreviewOnly: When true, validates and reports the assignments without saving.

Returns:
    Dictionary with success/message/data. Data contains the applied assignments and resulting serialized values.";

        [McpTool("Unity.Prefab.SetSerializedProperties", SetSerializedPropertiesDescription, Groups = new[] { "assets", "editor" }, EnabledByDefault = true)]
        public static object SetSerializedProperties(SetPrefabSerializedPropertiesParams parameters)
        {
            parameters ??= new SetPrefabSerializedPropertiesParams();
            if (string.IsNullOrWhiteSpace(parameters.PrefabPath))
            {
                return Response.Error("PrefabPath is required.");
            }

            if (parameters.Assignments == null || parameters.Assignments.Length == 0)
            {
                return Response.Error("At least one assignment is required.");
            }

            string prefabPath = SanitizeAssetPath(parameters.PrefabPath);
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error($"PrefabPath must point to a .prefab asset. Received '{prefabPath}'.");
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                return Response.Error($"Prefab asset '{prefabPath}' could not be loaded.");
            }

            var assignmentResults = new List<object>();
            GameObject prefabRoot = null;

            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot == null)
                {
                    return Response.Error($"Failed to load prefab contents for '{prefabPath}'.");
                }

                foreach (PrefabSerializedPropertyAssignment assignment in parameters.Assignments)
                {
                    object applied = ApplyAssignment(prefabRoot, assignment, parameters.PreviewOnly, out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return Response.Error(error, new
                        {
                            prefabPath,
                            assignmentResults
                        });
                    }

                    assignmentResults.Add(applied);
                }

                if (!parameters.PreviewOnly)
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    AssetDatabase.SaveAssets();
                }

                return Response.Success(parameters.PreviewOnly
                    ? $"Validated serialized property assignments for '{prefabPath}'."
                    : $"Saved serialized property assignments for '{prefabPath}'.", new
                {
                    prefabPath,
                    previewOnly = parameters.PreviewOnly,
                    assignments = assignmentResults
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to set prefab serialized properties: {ex.Message}");
            }
            finally
            {
                if (prefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        static object ApplyAssignment(GameObject prefabRoot, PrefabSerializedPropertyAssignment assignment, bool previewOnly, out string error)
        {
            error = null;
            if (assignment == null)
            {
                error = "Assignment entry cannot be null.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(assignment.ComponentType))
            {
                error = "Assignment.ComponentType is required.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(assignment.PropertyPath))
            {
                error = "Assignment.PropertyPath is required.";
                return null;
            }

            string targetPath = string.IsNullOrWhiteSpace(assignment.TargetPath) ? "." : assignment.TargetPath.Trim();
            Transform targetTransform = targetPath == "." ? prefabRoot.transform : prefabRoot.transform.Find(targetPath);
            if (targetTransform == null)
            {
                error = $"TargetPath '{targetPath}' was not found under prefab '{prefabRoot.name}'.";
                return null;
            }

            Type componentType = ResolveType(assignment.ComponentType);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{assignment.ComponentType}' could not be resolved.";
                return null;
            }

            Component[] matches = targetTransform.GetComponents(componentType);
            int index = Math.Max(0, assignment.ComponentIndex);
            if (matches == null || matches.Length <= index || matches[index] == null)
            {
                error = $"Component '{assignment.ComponentType}' with index {index} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(targetTransform)}'.";
                return null;
            }

            Component component = matches[index];
            SerializedObject serializedObject = new(component);
            SerializedProperty property = serializedObject.FindProperty(assignment.PropertyPath);
            if (property == null)
            {
                error = $"Serialized property '{assignment.PropertyPath}' was not found on component '{assignment.ComponentType}'.";
                return null;
            }

            string beforeValue = DescribeProperty(property);
            if (!previewOnly)
            {
                if (!TryAssignValue(property, assignment.Value, out string assignError))
                {
                    error = $"Failed to assign '{assignment.PropertyPath}' on '{assignment.ComponentType}': {assignError}";
                    return null;
                }

                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                if (assignment.Value != null && assignment.Value.Type != JTokenType.Null &&
                    property.propertyType == SerializedPropertyType.ObjectReference &&
                    property.objectReferenceValue == null)
                {
                    error = $"Failed to assign '{assignment.PropertyPath}' on '{assignment.ComponentType}': the provided object reference could not be applied to the serialized property.";
                    return null;
                }
            }

            serializedObject.UpdateIfRequiredOrScript();
            string afterValue = DescribeProperty(property);

            return new
            {
                targetPath,
                hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform),
                componentType = component.GetType().FullName,
                componentIndex = index,
                propertyPath = assignment.PropertyPath,
                propertyType = property.propertyType.ToString(),
                previousValue = beforeValue,
                value = assignment.Value,
                newValue = afterValue
            };
        }

        static bool TryAssignValue(SerializedProperty property, JToken value, out string error)
        {
            error = null;
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        property.boolValue = value != null && value.Type != JTokenType.Null && value.Value<bool>();
                        return true;
                    case SerializedPropertyType.Integer:
                        property.intValue = value == null || value.Type == JTokenType.Null ? 0 : value.Value<int>();
                        return true;
                    case SerializedPropertyType.Float:
                        property.floatValue = value == null || value.Type == JTokenType.Null ? 0f : value.Value<float>();
                        return true;
                    case SerializedPropertyType.String:
                        property.stringValue = value == null || value.Type == JTokenType.Null ? null : value.ToString();
                        return true;
                    case SerializedPropertyType.Color:
                        if (TryParseColor(value, out Color color))
                        {
                            property.colorValue = color;
                            return true;
                        }

                        error = "Expected a color object with r/g/b/a or an array [r,g,b,a].";
                        return false;
                    case SerializedPropertyType.ObjectReference:
                        if (TryResolveObjectReference(value, out UnityEngine.Object resolved, out error))
                        {
                            property.objectReferenceValue = resolved;
                            return true;
                        }

                        return false;
                    case SerializedPropertyType.Enum:
                        if (TryParseEnum(property, value, out int enumIndex, out error))
                        {
                            property.enumValueIndex = enumIndex;
                            return true;
                        }

                        return false;
                    case SerializedPropertyType.Vector2:
                        if (TryParseVector2(value, out Vector2 vector2))
                        {
                            property.vector2Value = vector2;
                            return true;
                        }

                        error = "Expected a Vector2 object {x,y} or array [x,y].";
                        return false;
                    case SerializedPropertyType.Vector3:
                        if (TryParseVector3(value, out Vector3 vector3))
                        {
                            property.vector3Value = vector3;
                            return true;
                        }

                        error = "Expected a Vector3 object {x,y,z} or array [x,y,z].";
                        return false;
                    case SerializedPropertyType.Vector4:
                        if (TryParseVector4(value, out Vector4 vector4))
                        {
                            property.vector4Value = vector4;
                            return true;
                        }

                        error = "Expected a Vector4 object {x,y,z,w} or array [x,y,z,w].";
                        return false;
                    case SerializedPropertyType.Rect:
                        if (TryParseRect(value, out Rect rect))
                        {
                            property.rectValue = rect;
                            return true;
                        }

                        error = "Expected a Rect object {x,y,width,height} or array [x,y,width,height].";
                        return false;
                    case SerializedPropertyType.Bounds:
                        if (TryParseBounds(value, out Bounds bounds))
                        {
                            property.boundsValue = bounds;
                            return true;
                        }

                        error = "Expected a Bounds object with center/size or an array [cx,cy,cz,sx,sy,sz].";
                        return false;
                    default:
                        error = $"Unsupported serialized property type '{property.propertyType}'.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static bool TryResolveObjectReference(JToken value, out UnityEngine.Object resolved, out string error)
        {
            resolved = null;
            error = null;
            if (value == null || value.Type == JTokenType.Null)
            {
                return true;
            }

            string assetPath = null;
            string assetName = null;

            if (value.Type == JTokenType.String)
            {
                assetPath = SanitizeAssetPath(value.ToString());
            }
            else if (value is JObject obj)
            {
                assetPath = SanitizeAssetPath(obj.Value<string>("assetPath") ?? obj.Value<string>("path"));
                assetName = obj.Value<string>("assetName") ?? obj.Value<string>("name") ?? obj.Value<string>("subAssetName");
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = "Object reference values must be null, an asset path string, or an object with assetPath/path.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(assetName))
            {
                UnityEngine.Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                resolved = ChooseBestObjectReference(subAssets, assetName);
            }
            else
            {
                resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (resolved == null || resolved is Texture2D)
                {
                    resolved = ChooseBestObjectReference(AssetDatabase.LoadAllAssetsAtPath(assetPath), null) ?? resolved;
                }
            }

            if (resolved == null)
            {
                error = string.IsNullOrWhiteSpace(assetName)
                    ? $"No asset could be loaded from '{assetPath}'."
                    : $"No sub-asset named '{assetName}' could be loaded from '{assetPath}'.";
                return false;
            }

            return true;
        }

        static UnityEngine.Object ChooseBestObjectReference(UnityEngine.Object[] candidates, string assetName)
        {
            if (candidates == null || candidates.Length == 0)
            {
                return null;
            }

            StringComparison comparison = StringComparison.OrdinalIgnoreCase;
            UnityEngine.Object spriteMatch = null;
            UnityEngine.Object nonTextureMatch = null;
            UnityEngine.Object fallbackMatch = null;

            for (int i = 0; i < candidates.Length; i++)
            {
                UnityEngine.Object candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                bool nameMatches = string.IsNullOrWhiteSpace(assetName) || candidate.name.Equals(assetName, comparison);
                if (!nameMatches)
                {
                    continue;
                }

                fallbackMatch ??= candidate;
                if (candidate is Sprite)
                {
                    spriteMatch = candidate;
                    break;
                }

                if (candidate is not Texture2D && nonTextureMatch == null)
                {
                    nonTextureMatch = candidate;
                }
            }

            return spriteMatch ?? nonTextureMatch ?? fallbackMatch;
        }

        static bool TryParseEnum(SerializedProperty property, JToken value, out int enumIndex, out string error)
        {
            enumIndex = 0;
            error = null;

            if (value == null || value.Type == JTokenType.Null)
            {
                return true;
            }

            if (value.Type == JTokenType.Integer)
            {
                enumIndex = Mathf.Clamp(value.Value<int>(), 0, Math.Max(0, property.enumNames.Length - 1));
                return true;
            }

            string text = value.ToString();
            for (int i = 0; i < property.enumNames.Length; i++)
            {
                if (property.enumNames[i].Equals(text, StringComparison.OrdinalIgnoreCase) ||
                    property.enumDisplayNames[i].Equals(text, StringComparison.OrdinalIgnoreCase))
                {
                    enumIndex = i;
                    return true;
                }
            }

            error = $"Enum value '{text}' does not match any entry on '{property.propertyPath}'.";
            return false;
        }

        static bool TryParseColor(JToken token, out Color color)
        {
            color = default;
            if (token is JArray array && array.Count >= 3)
            {
                color = new Color(
                    array[0].Value<float>(),
                    array[1].Value<float>(),
                    array[2].Value<float>(),
                    array.Count > 3 ? array[3].Value<float>() : 1f);
                return true;
            }

            if (token is JObject obj)
            {
                color = new Color(
                    obj.Value<float?>("r") ?? 0f,
                    obj.Value<float?>("g") ?? 0f,
                    obj.Value<float?>("b") ?? 0f,
                    obj.Value<float?>("a") ?? 1f);
                return true;
            }

            return false;
        }

        static bool TryParseVector2(JToken token, out Vector2 vector)
        {
            vector = default;
            if (token is JArray array && array.Count >= 2)
            {
                vector = new Vector2(array[0].Value<float>(), array[1].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                vector = new Vector2(obj.Value<float?>("x") ?? 0f, obj.Value<float?>("y") ?? 0f);
                return true;
            }

            return false;
        }

        static bool TryParseVector3(JToken token, out Vector3 vector)
        {
            vector = default;
            if (token is JArray array && array.Count >= 3)
            {
                vector = new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                vector = new Vector3(
                    obj.Value<float?>("x") ?? 0f,
                    obj.Value<float?>("y") ?? 0f,
                    obj.Value<float?>("z") ?? 0f);
                return true;
            }

            return false;
        }

        static bool TryParseVector4(JToken token, out Vector4 vector)
        {
            vector = default;
            if (token is JArray array && array.Count >= 4)
            {
                vector = new Vector4(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array[3].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                vector = new Vector4(
                    obj.Value<float?>("x") ?? 0f,
                    obj.Value<float?>("y") ?? 0f,
                    obj.Value<float?>("z") ?? 0f,
                    obj.Value<float?>("w") ?? 0f);
                return true;
            }

            return false;
        }

        static bool TryParseRect(JToken token, out Rect rect)
        {
            rect = default;
            if (token is JArray array && array.Count >= 4)
            {
                rect = new Rect(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array[3].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                rect = new Rect(
                    obj.Value<float?>("x") ?? 0f,
                    obj.Value<float?>("y") ?? 0f,
                    obj.Value<float?>("width") ?? 0f,
                    obj.Value<float?>("height") ?? 0f);
                return true;
            }

            return false;
        }

        static bool TryParseBounds(JToken token, out Bounds bounds)
        {
            bounds = default;
            if (token is JArray array && array.Count >= 6)
            {
                bounds = new Bounds(
                    new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>()),
                    new Vector3(array[3].Value<float>(), array[4].Value<float>(), array[5].Value<float>()));
                return true;
            }

            if (token is JObject obj &&
                TryParseVector3(obj["center"], out Vector3 center) &&
                TryParseVector3(obj["size"], out Vector3 size))
            {
                bounds = new Bounds(center, size);
                return true;
            }

            return false;
        }

        static string DescribeProperty(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue ? "true" : "false",
                SerializedPropertyType.Integer => property.intValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Float => property.floatValue.ToString("R", CultureInfo.InvariantCulture),
                SerializedPropertyType.String => property.stringValue ?? string.Empty,
                SerializedPropertyType.Color => property.colorValue.ToString(),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue != null ? AssetDatabase.GetAssetPath(property.objectReferenceValue) : "null",
                SerializedPropertyType.Enum => property.enumDisplayNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length
                    ? property.enumDisplayNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Vector2 => property.vector2Value.ToString("F3"),
                SerializedPropertyType.Vector3 => property.vector3Value.ToString("F3"),
                SerializedPropertyType.Vector4 => property.vector4Value.ToString("F3"),
                SerializedPropertyType.Rect => property.rectValue.ToString(),
                SerializedPropertyType.Bounds => property.boundsValue.ToString(),
                _ => $"{property.propertyType}:{property.propertyPath}"
            };
        }

        static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            Type direct = Type.GetType(typeName, false);
            if (direct != null)
            {
                return direct;
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type byFullName = assembly.GetType(typeName, false);
                    if (byFullName != null)
                    {
                        return byFullName;
                    }

                    Type byShortName = assembly.GetTypes().FirstOrDefault(type => type.Name.Equals(typeName, StringComparison.Ordinal));
                    if (byShortName != null)
                    {
                        return byShortName;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = path.Replace('\\', '/');
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return "Assets/" + path.TrimStart('/');
        }
    }
}
