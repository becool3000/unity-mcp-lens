using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class SceneTools
    {
        const string GetDirtyStateToolName = "Unity.Scene.GetDirtyState";
        const string SaveToolName = "Unity.Scene.Save";

        public const string SetSerializedPropertiesDescription = @"Sets serialized property values on scene objects without requiring a custom RunCommand.

Args:
    Target: Scene GameObject target, path, or instance id.
    SearchMethod: How to find the target root ('by_name', 'by_id', 'by_path').
    Assignments: Array of serialized property assignments.
      TargetPath: Relative child path under the target root. Use '.' or omit for the root GameObject.
      ComponentType: Component type name on the target GameObject.
      ComponentIndex: 0-based component index when multiple matching components exist.
      PropertyPath: Serialized property path to set.
      Value: Primitive value, asset path string, null, or an object reference descriptor like { find, method, component, componentIndex }.
    PreviewOnly: When true, validates and reports the assignments without mutating or saving the open scenes.

Returns:
    Dictionary with success/message/data. Data contains the applied assignments and resulting serialized values.";

        const string GetDirtyStateDescription = @"Reports dirty state for loaded scenes without saving or mutating scene content.";

        const string SaveDescription = @"Explicitly saves a loaded scene or all loaded dirty scenes.

No save occurs unless this tool is called. Without arguments, saves the active scene. Use allLoadedScenes=true to save all loaded dirty scenes.";

        [McpSchema(GetDirtyStateToolName)]
        public static object GetDirtyStateSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    scene = new { type = "string", description = "Optional loaded scene name or Assets-relative .unity path to highlight." }
                }
            };
        }

        [McpSchema(SaveToolName)]
        public static object GetSaveSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    scene = new { type = "string", description = "Optional loaded scene name or Assets-relative .unity path. Defaults to the active scene." },
                    allLoadedScenes = new { type = "boolean", description = "Save all loaded dirty scenes instead of one scene." },
                    saveAsPath = new { type = "string", description = "Optional Assets-relative .unity path for saving the selected scene." }
                }
            };
        }

        [McpTool(GetDirtyStateToolName, GetDirtyStateDescription, "Get Scene Dirty State", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object GetDirtyState(JObject @params)
        {
            @params ??= new JObject();
            string sceneTarget = GetString(@params, "scene", "Scene", "scenePath", "ScenePath", "sceneName", "SceneName");
            object selectedScene = null;
            if (!string.IsNullOrWhiteSpace(sceneTarget))
            {
                if (!SceneDirtyStateUtility.TryResolveScene(sceneTarget, out Scene scene, out string error))
                    return Response.Error(error, new { errorKind = "scene_not_found", dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes() });

                selectedScene = SceneDirtyStateUtility.ToSceneState(scene);
            }

            return Response.Success("Retrieved loaded scene dirty state.", new
            {
                selectedScene,
                dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes()
            });
        }

        [McpTool(SaveToolName, SaveDescription, "Save Scene", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object Save(JObject @params)
        {
            @params ??= new JObject();
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            bool allLoadedScenes = GetBool(@params, false, "allLoadedScenes", "AllLoadedScenes", "all", "All");
            string sceneTarget = GetString(@params, "scene", "Scene", "scenePath", "ScenePath", "sceneName", "SceneName", "target", "Target");
            string saveAsPath = GetString(@params, "saveAsPath", "SaveAsPath", "save_as_path", "path", "Path");

            if (allLoadedScenes && !string.IsNullOrWhiteSpace(saveAsPath))
            {
                return Response.Error("saveAsPath cannot be used with allLoadedScenes=true.", new
                {
                    errorKind = "invalid_save_arguments",
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    saveState = SceneDirtyStateUtility.BuildSaveState(requested: true, attempted: false, saved: false, message: "invalid_arguments")
                });
            }

            var savedScenes = new List<object>();
            var errors = new List<string>();
            bool attempted = false;

            if (allLoadedScenes)
            {
                foreach (Scene scene in SceneDirtyStateUtility.GetDirtyLoadedScenes())
                {
                    attempted = true;
                    SaveOneScene(scene, null, savedScenes, errors);
                }
            }
            else
            {
                if (!SceneDirtyStateUtility.TryResolveScene(sceneTarget, out Scene scene, out string error))
                    return Response.Error(error, new
                    {
                        errorKind = "scene_not_found",
                        dirtyStateBefore,
                        dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                        saveState = SceneDirtyStateUtility.BuildSaveState(requested: true, attempted: false, saved: false, message: "scene_not_found", error: error)
                    });

                attempted = true;
                SaveOneScene(scene, NormalizeSavePath(saveAsPath), savedScenes, errors);
            }

            if (savedScenes.Count > 0)
                AssetDatabase.Refresh();

            object dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes();
            bool success = errors.Count == 0;
            object saveState = SceneDirtyStateUtility.BuildSaveState(
                requested: true,
                attempted: attempted,
                saved: success && savedScenes.Count > 0,
                savedScenes: savedScenes.ToArray(),
                message: !attempted ? "no_dirty_loaded_scenes" : success ? "saved" : "save_failed",
                error: errors.Count == 0 ? null : string.Join("; ", errors));

            if (!success)
            {
                return Response.Error("One or more scene saves failed.", new
                {
                    errorKind = "scene_save_failed",
                    errors,
                    dirtyStateBefore,
                    dirtyStateAfter,
                    saveState
                });
            }

            return Response.Success(
                savedScenes.Count == 0 ? "No dirty loaded scenes required saving." : $"Saved {savedScenes.Count} scene(s).",
                new
                {
                    dirtyStateBefore,
                    dirtyStateAfter,
                    saveState
                });
        }

        [McpTool("Unity.Scene.SetSerializedProperties", SetSerializedPropertiesDescription, Groups = new[] { "scene", "editor" }, EnabledByDefault = true)]
        public static object SetSerializedProperties(SetSceneSerializedPropertiesParams parameters)
        {
            parameters ??= new SetSceneSerializedPropertiesParams();
            if (parameters.Target == null)
            {
                return Response.Error("Target is required.");
            }

            if (parameters.Assignments == null || parameters.Assignments.Length == 0)
            {
                return Response.Error("At least one assignment is required.");
            }

            JObject findParams = new()
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetRoot = ObjectsHelper.FindObject(parameters.Target, parameters.SearchMethod, findParams);
            if (targetRoot == null)
            {
                return Response.Error("Scene target could not be found.");
            }

            if (!targetRoot.scene.IsValid())
            {
                return Response.Error("Target does not belong to a valid loaded scene.");
            }

            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            var assignmentResults = new List<object>();
            try
            {
                foreach (SceneSerializedPropertyAssignment assignment in parameters.Assignments)
                {
                    object applied = ApplyAssignment(targetRoot, assignment, parameters.PreviewOnly, out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return Response.Error(error, new
                        {
                            target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                            assignmentResults
                        });
                    }

                    assignmentResults.Add(applied);
                }

                if (!parameters.PreviewOnly)
                {
                    EditorSceneManager.MarkSceneDirty(targetRoot.scene);
                }

                return Response.Success(parameters.PreviewOnly
                    ? $"Validated serialized property assignments on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                    : $"Applied serialized property assignments on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.", new
                {
                    target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                    previewOnly = parameters.PreviewOnly,
                    assignments = assignmentResults,
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    saveState = SceneDirtyStateUtility.BuildSaveState()
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to set scene serialized properties: {ex.Message}");
            }
        }

        static object ApplyAssignment(GameObject targetRoot, SceneSerializedPropertyAssignment assignment, bool previewOnly, out string error)
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
            Transform targetTransform = targetPath == "." ? targetRoot.transform : targetRoot.transform.Find(targetPath);
            if (targetTransform == null)
            {
                error = $"TargetPath '{targetPath}' was not found under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.";
                return null;
            }

            Type componentType = UnityComponentResolver.FindType(assignment.ComponentType);
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
                EditorUtility.SetDirty(component);
                if (assignment.Value != null &&
                    assignment.Value.Type != JTokenType.Null &&
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
                    default:
                        error = $"Serialized property type '{property.propertyType}' is not supported by Unity.Scene.SetSerializedProperties.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        internal static bool TryResolveObjectReference(JToken value, out UnityEngine.Object resolved, out string error)
        {
            resolved = null;
            error = null;
            if (value == null || value.Type == JTokenType.Null)
            {
                return true;
            }

            if (value.Type == JTokenType.String)
            {
                string raw = value.ToString();
                string assetPath = SanitizeAssetPath(raw);
                resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (resolved != null)
                {
                    return true;
                }

                JObject findParams = new()
                {
                    ["search_inactive"] = true
                };
                GameObject sceneObject = ObjectsHelper.FindObject(value, "by_id_or_name_or_path", findParams);
                if (sceneObject != null)
                {
                    resolved = sceneObject;
                    return true;
                }

                error = $"Object reference '{raw}' could not be resolved as an asset path or scene object.";
                return false;
            }

            if (value is JObject objectDescriptor)
            {
                if (!objectDescriptor.TryGetValue("find", StringComparison.OrdinalIgnoreCase, out JToken findToken) &&
                    !objectDescriptor.TryGetValue("target", StringComparison.OrdinalIgnoreCase, out findToken))
                {
                    error = "Scene object reference objects must include 'find' or 'target'.";
                    return false;
                }

                string searchMethod = objectDescriptor["method"]?.ToString()
                    ?? objectDescriptor["searchMethod"]?.ToString()
                    ?? "by_id_or_name_or_path";
                bool includeInactive = objectDescriptor["includeInactive"]?.ToObject<bool?>() ?? true;
                JObject findParams = new()
                {
                    ["search_inactive"] = includeInactive
                };
                GameObject sceneObject = ObjectsHelper.FindObject(findToken, searchMethod, findParams);
                if (sceneObject == null)
                {
                    error = $"Scene object reference '{findToken}' could not be resolved.";
                    return false;
                }

                string componentName = objectDescriptor["component"]?.ToString();
                if (string.IsNullOrWhiteSpace(componentName))
                {
                    resolved = sceneObject;
                    return true;
                }

                Type componentType = UnityComponentResolver.FindType(componentName);
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    error = $"Scene object reference component type '{componentName}' could not be resolved.";
                    return false;
                }

                int componentIndex = Math.Max(0, objectDescriptor["componentIndex"]?.ToObject<int?>() ?? 0);
                Component[] matches = sceneObject.GetComponents(componentType);
                if (matches == null || matches.Length <= componentIndex || matches[componentIndex] == null)
                {
                    error = $"Component '{componentName}' with index {componentIndex} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(sceneObject.transform)}'.";
                    return false;
                }

                resolved = matches[componentIndex];
                return true;
            }

            error = "Unsupported object reference payload. Use null, an asset path string, or an object like { find, method, component, componentIndex }.";
            return false;
        }

        static bool TryParseVector2(JToken value, out Vector2 vector)
        {
            vector = default;
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value is JArray array && array.Count >= 2)
            {
                vector = new Vector2(array[0].Value<float>(), array[1].Value<float>());
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out JToken x) &&
                obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out JToken y))
            {
                vector = new Vector2(x.Value<float>(), y.Value<float>());
                return true;
            }

            return false;
        }

        static bool TryParseVector3(JToken value, out Vector3 vector)
        {
            vector = default;
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

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

        static bool TryParseColor(JToken value, out Color color)
        {
            color = default;
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value is JArray array && array.Count >= 3)
            {
                float a = array.Count > 3 ? array[3].Value<float>() : 1f;
                color = new Color(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), a);
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("r", StringComparison.OrdinalIgnoreCase, out JToken r) &&
                obj.TryGetValue("g", StringComparison.OrdinalIgnoreCase, out JToken g) &&
                obj.TryGetValue("b", StringComparison.OrdinalIgnoreCase, out JToken b))
            {
                float a = obj.TryGetValue("a", StringComparison.OrdinalIgnoreCase, out JToken alpha) ? alpha.Value<float>() : 1f;
                color = new Color(r.Value<float>(), g.Value<float>(), b.Value<float>(), a);
                return true;
            }

            return false;
        }

        static bool TryParseEnum(SerializedProperty property, JToken value, out int enumIndex, out string error)
        {
            error = null;
            enumIndex = 0;
            if (value == null || value.Type == JTokenType.Null)
            {
                return true;
            }

            if (value.Type == JTokenType.Integer)
            {
                enumIndex = value.Value<int>();
                return true;
            }

            string enumName = value.ToString();
            int foundIndex = Array.FindIndex(property.enumDisplayNames, name =>
                string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase));
            if (foundIndex >= 0)
            {
                enumIndex = foundIndex;
                return true;
            }

            foundIndex = Array.FindIndex(property.enumNames, name =>
                string.Equals(name, enumName, StringComparison.OrdinalIgnoreCase));
            if (foundIndex >= 0)
            {
                enumIndex = foundIndex;
                return true;
            }

            error = $"Enum value '{enumName}' is not valid for '{property.propertyPath}'.";
            return false;
        }

        internal static string DescribeProperty(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue.ToString(),
                SerializedPropertyType.Integer => property.intValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Float => property.floatValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.String => property.stringValue ?? string.Empty,
                SerializedPropertyType.Color => property.colorValue.ToString(),
                SerializedPropertyType.ObjectReference => property.objectReferenceValue != null ? property.objectReferenceValue.name : "null",
                SerializedPropertyType.Enum => property.enumDisplayNames.Length > property.enumValueIndex && property.enumValueIndex >= 0
                    ? property.enumDisplayNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Vector2 => property.vector2Value.ToString(),
                SerializedPropertyType.Vector3 => property.vector3Value.ToString(),
                _ => property.propertyType.ToString()
            };
        }

        internal static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string normalized = path.Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "Assets/" + normalized.TrimStart('/');
        }

        static void SaveOneScene(Scene scene, string saveAsPath, List<object> savedScenes, List<string> errors)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                errors.Add("Cannot save an invalid or unloaded scene.");
                return;
            }

            string finalPath = string.IsNullOrWhiteSpace(saveAsPath) ? scene.path : saveAsPath;
            if (string.IsNullOrWhiteSpace(finalPath))
            {
                errors.Add($"Scene '{scene.name}' is untitled. Provide saveAsPath to save it.");
                return;
            }

            bool wasDirty = scene.isDirty;
            EnsureAssetDirectory(finalPath);
            bool saved = string.IsNullOrWhiteSpace(saveAsPath)
                ? EditorSceneManager.SaveScene(scene)
                : EditorSceneManager.SaveScene(scene, saveAsPath);

            if (!saved)
            {
                errors.Add($"Failed to save scene '{scene.name}' to '{finalPath}'.");
                return;
            }

            savedScenes.Add(new
            {
                scene.name,
                path = finalPath,
                scene.handle,
                wasDirty
            });
        }

        static string NormalizeSavePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Trim().Replace('\\', '/');
            if (!normalized.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                normalized += ".unity";
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                normalized = "Assets/" + normalized.TrimStart('/');
            return normalized;
        }

        static void EnsureAssetDirectory(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory))
                return;

            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            string fullDirectory = Path.Combine(projectRoot, directory);
            if (!Directory.Exists(fullDirectory))
                Directory.CreateDirectory(fullDirectory);
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

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out bool parsed) ? parsed : defaultValue;
            }

            return defaultValue;
        }
    }
}
