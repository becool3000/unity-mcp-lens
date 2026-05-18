using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PrefabTools
    {
        const string SetSerializedPropertiesToolName = "Unity.Prefab.SetSerializedProperties";
        const string VerifySerializedPropertiesToolName = "Unity.Prefab.VerifySerializedProperties";

        public const string SetSerializedPropertiesDescription = @"Sets serialized property values on a prefab asset or prefab instance without requiring a custom RunCommand.

Args:
    PrefabPath: Prefab asset path under Assets/. When omitted, Target must resolve to a scene prefab instance.
    Target: Scene prefab instance GameObject target, path, or instance id.
    SearchMethod: How to find Target when editing a scene prefab instance.
    IncludeInactive: Include inactive scene objects when resolving Target.
    Assignments: Array of serialized property assignments.
      TargetPath: Relative child path under the prefab root. Use '.' or omit for the root GameObject.
      ComponentType: Component type name on the target GameObject.
      ComponentIndex: 0-based component index when multiple matching components exist.
      PropertyPath: Serialized property path to set.
      Value: Primitive value, asset path string, null, or a scene object-reference descriptor when Target mode is used.
    PreviewOnly: When true, validates and reports the assignments without mutating or saving.

Returns:
    Dictionary with success/message/data. Data contains changed fields, stable target identifiers, dirty state, asset state, and save state.";

        public const string VerifySerializedPropertiesDescription = @"Verifies serialized property values on a prefab asset or prefab instance without mutation.

Args:
    PrefabPath: Prefab asset path under Assets/. When omitted, Target must resolve to a scene prefab instance.
    Target: Scene prefab instance GameObject target, path, or instance id.
    Checks: Array of serialized property checks.
      TargetPath: Relative child path under the prefab root. Use '.' or omit for the root GameObject.
      ComponentType: Component type name on the target GameObject.
      ComponentIndex: 0-based component index when multiple matching components exist.
      PropertyPath: Serialized property path to read.
      ExpectedValue: Optional value to compare against the serialized property's display value.

Returns:
    Dictionary with success/message/data. Data contains pass/fail counts, current values, prefab/asset state, and save state.";

        [McpTool(SetSerializedPropertiesToolName, SetSerializedPropertiesDescription, Groups = new[] { "assets", "editor" }, EnabledByDefault = true)]
        public static object SetSerializedProperties(SetPrefabSerializedPropertiesParams parameters)
        {
            parameters ??= new SetPrefabSerializedPropertiesParams();
            if (parameters.Assignments == null || parameters.Assignments.Length == 0)
            {
                return Response.Error("At least one assignment is required.");
            }

            if (!string.IsNullOrWhiteSpace(parameters.PrefabPath))
            {
                return SetPrefabAssetSerializedProperties(parameters);
            }

            if (parameters.Target == null)
            {
                return Response.Error("PrefabPath or Target is required.");
            }

            return SetPrefabInstanceSerializedProperties(parameters);
        }

        [McpTool(VerifySerializedPropertiesToolName, VerifySerializedPropertiesDescription, "Verify Prefab Serialized Properties", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object VerifySerializedProperties(VerifyPrefabSerializedPropertiesParams parameters)
        {
            parameters ??= new VerifyPrefabSerializedPropertiesParams();
            if (parameters.Checks == null || parameters.Checks.Length == 0)
            {
                return Response.Error("At least one check is required.");
            }

            return !string.IsNullOrWhiteSpace(parameters.PrefabPath)
                ? VerifyPrefabAssetSerializedProperties(parameters)
                : VerifyPrefabInstanceSerializedProperties(parameters);
        }

        static object SetPrefabAssetSerializedProperties(SetPrefabSerializedPropertiesParams parameters)
        {
            string prefabPath = SanitizeAssetPath(parameters.PrefabPath);
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error($"PrefabPath must point to a .prefab asset. Received '{prefabPath}'.");
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                return Response.Error($"Prefab asset '{prefabPath}' could not be loaded.");
            }

            object prefabStateBefore = CapturePrefabState(prefabPath);
            object assetStateBefore = CaptureAssetState(prefabPath);
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
                    object applied = ApplyAssignment(prefabRoot, assignment, parameters.PreviewOnly, allowSceneReferences: false, recordPrefabInstanceOverride: false, out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return Response.Error(error, new
                        {
                            prefabPath,
                            assignmentResults,
                            prefabStateBefore,
                            prefabStateAfter = CapturePrefabState(prefabPath),
                            assetStateBefore,
                            assetStateAfter = CaptureAssetState(prefabPath),
                            saveState = BuildAssetSaveState(requested: !parameters.PreviewOnly, attempted: false, saved: false, message: "failed_before_save", error: error)
                        });
                    }

                    assignmentResults.Add(applied);
                }

                bool saveAttempted = false;
                bool saved = false;
                if (!parameters.PreviewOnly)
                {
                    saveAttempted = true;
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                    AssetDatabase.SaveAssets();
                    saved = true;
                }

                return Response.Success(parameters.PreviewOnly
                    ? $"Validated serialized property assignments for '{prefabPath}'."
                    : $"Saved serialized property assignments for '{prefabPath}'.", new
                {
                    mode = "prefab_asset",
                    prefabPath,
                    previewOnly = parameters.PreviewOnly,
                    changedObjects = new[] { DescribePrefabObject(prefabRoot, ".") },
                    fields = assignmentResults,
                    assignments = assignmentResults,
                    warnings = Array.Empty<string>(),
                    prefabStateBefore,
                    prefabStateAfter = CapturePrefabState(prefabPath),
                    assetStateBefore,
                    assetStateAfter = CaptureAssetState(prefabPath),
                    saveState = BuildAssetSaveState(
                        requested: !parameters.PreviewOnly,
                        attempted: saveAttempted,
                        saved: saved,
                        savedAssets: saved ? new object[] { CaptureAssetState(prefabPath) } : Array.Empty<object>(),
                        message: parameters.PreviewOnly ? "not_requested" : "prefab_asset_saved_by_tool_contract")
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to set prefab serialized properties: {ex.Message}", new
                {
                    prefabPath,
                    prefabStateBefore,
                    prefabStateAfter = CapturePrefabState(prefabPath),
                    assetStateBefore,
                    assetStateAfter = CaptureAssetState(prefabPath),
                    saveState = BuildAssetSaveState(requested: !parameters.PreviewOnly, attempted: false, saved: false, message: "exception", error: ex.Message)
                });
            }
            finally
            {
                if (prefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        static object SetPrefabInstanceSerializedProperties(SetPrefabSerializedPropertiesParams parameters)
        {
            JObject findParams = new()
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetRoot = ObjectsHelper.FindObject(parameters.Target, parameters.SearchMethod, findParams);
            if (targetRoot == null)
            {
                return Response.Error("Scene prefab instance target could not be found.");
            }

            if (!targetRoot.scene.IsValid())
            {
                return Response.Error("Target does not belong to a valid loaded scene.");
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(targetRoot))
            {
                return Response.Error($"Target '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}' is not part of a prefab instance.", new
                {
                    target = DescribePrefabObject(targetRoot, "."),
                    dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes()
                });
            }

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetRoot) ?? targetRoot;
            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            object prefabStateBefore = CapturePrefabState(sourcePrefabPath);
            object assetStateBefore = CaptureAssetState(sourcePrefabPath);
            var assignmentResults = new List<object>();

            try
            {
                foreach (PrefabSerializedPropertyAssignment assignment in parameters.Assignments)
                {
                    object applied = ApplyAssignment(targetRoot, assignment, parameters.PreviewOnly, allowSceneReferences: true, recordPrefabInstanceOverride: true, out string error);
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        return Response.Error(error, new
                        {
                            target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                            instanceRoot = DescribePrefabObject(instanceRoot, "."),
                            assignmentResults,
                            dirtyStateBefore,
                            dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                            prefabStateBefore,
                            prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                            assetStateBefore,
                            assetStateAfter = CaptureAssetState(sourcePrefabPath),
                            saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                        });
                    }

                    assignmentResults.Add(applied);
                }

                if (!parameters.PreviewOnly)
                {
                    SceneDirtyStateUtility.MarkSceneDirty(instanceRoot);
                }

                return Response.Success(parameters.PreviewOnly
                    ? $"Validated serialized property assignments on prefab instance '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                    : $"Applied serialized property assignments on prefab instance '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.", new
                {
                    mode = "prefab_instance",
                    target = DescribePrefabObject(targetRoot, "."),
                    instanceRoot = DescribePrefabObject(instanceRoot, "."),
                    sourcePrefabPath,
                    previewOnly = parameters.PreviewOnly,
                    changedObjects = new[] { DescribePrefabObject(targetRoot, ".") },
                    fields = assignmentResults,
                    assignments = assignmentResults,
                    warnings = Array.Empty<string>(),
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    prefabStateBefore,
                    prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                    assetStateBefore,
                    assetStateAfter = CaptureAssetState(sourcePrefabPath),
                    saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                });
            }
            catch (Exception ex)
            {
                return Response.Error($"Failed to set prefab instance serialized properties: {ex.Message}", new
                {
                    target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    prefabStateBefore,
                    prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                    assetStateBefore,
                    assetStateAfter = CaptureAssetState(sourcePrefabPath),
                    saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                });
            }
        }

        static object VerifyPrefabAssetSerializedProperties(VerifyPrefabSerializedPropertiesParams parameters)
        {
            string prefabPath = SanitizeAssetPath(parameters.PrefabPath);
            if (!prefabPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return Response.Error($"PrefabPath must point to a .prefab asset. Received '{prefabPath}'.");
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                return Response.Error($"Prefab asset '{prefabPath}' could not be loaded.");
            }

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                if (prefabRoot == null)
                {
                    return Response.Error($"Failed to load prefab contents for '{prefabPath}'.");
                }

                object[] checkResults = VerifyChecks(prefabRoot, parameters.Checks, out int passedCount, out int failedCount);
                return Response.Success(failedCount == 0
                    ? $"Verified {passedCount} prefab serialized properties for '{prefabPath}'."
                    : $"Verified prefab serialized properties for '{prefabPath}' with {failedCount} failed checks.", new
                {
                    mode = "prefab_asset",
                    prefabPath,
                    passed = failedCount == 0,
                    checkCount = checkResults.Length,
                    passedCount,
                    failedCount,
                    checks = checkResults,
                    warnings = Array.Empty<string>(),
                    prefabState = CapturePrefabState(prefabPath),
                    assetState = CaptureAssetState(prefabPath),
                    saveState = BuildAssetSaveState(message: "not_requested")
                });
            }
            finally
            {
                if (prefabRoot != null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        static object VerifyPrefabInstanceSerializedProperties(VerifyPrefabSerializedPropertiesParams parameters)
        {
            if (parameters.Target == null)
            {
                return Response.Error("PrefabPath or Target is required.");
            }

            JObject findParams = new()
            {
                ["search_inactive"] = parameters.IncludeInactive
            };
            GameObject targetRoot = ObjectsHelper.FindObject(parameters.Target, parameters.SearchMethod, findParams);
            if (targetRoot == null)
            {
                return Response.Error("Scene prefab instance target could not be found.");
            }

            if (!targetRoot.scene.IsValid())
            {
                return Response.Error("Target does not belong to a valid loaded scene.");
            }

            if (!PrefabUtility.IsPartOfPrefabInstance(targetRoot))
            {
                return Response.Error($"Target '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}' is not part of a prefab instance.", new
                {
                    target = DescribePrefabObject(targetRoot, "."),
                    dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes()
                });
            }

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetRoot) ?? targetRoot;
            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            object[] checkResults = VerifyChecks(targetRoot, parameters.Checks, out int passedCount, out int failedCount);
            return Response.Success(failedCount == 0
                ? $"Verified {passedCount} prefab instance serialized properties on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                : $"Verified prefab instance serialized properties on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}' with {failedCount} failed checks.", new
            {
                mode = "prefab_instance",
                target = DescribePrefabObject(targetRoot, "."),
                instanceRoot = DescribePrefabObject(instanceRoot, "."),
                sourcePrefabPath,
                passed = failedCount == 0,
                checkCount = checkResults.Length,
                passedCount,
                failedCount,
                checks = checkResults,
                warnings = Array.Empty<string>(),
                dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabState = CapturePrefabState(sourcePrefabPath),
                assetState = CaptureAssetState(sourcePrefabPath),
                saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
            });
        }

        static object[] VerifyChecks(GameObject prefabRoot, PrefabSerializedPropertyVerifyCheck[] checks, out int passedCount, out int failedCount)
        {
            var rows = new List<object>();
            passedCount = 0;
            failedCount = 0;

            for (int i = 0; i < checks.Length; i++)
            {
                PrefabSerializedPropertyVerifyCheck check = checks[i];
                object row = ReadSerializedProperty(prefabRoot, check, i, out bool passed);
                rows.Add(row);
                if (passed)
                {
                    passedCount++;
                }
                else
                {
                    failedCount++;
                }
            }

            return rows.ToArray();
        }

        static object ReadSerializedProperty(GameObject prefabRoot, PrefabSerializedPropertyVerifyCheck check, int index, out bool passed)
        {
            passed = false;
            string label = string.IsNullOrWhiteSpace(check?.Label) ? $"check-{index}" : check.Label.Trim();
            if (check == null)
            {
                return new { label, passed, error = "Check entry cannot be null." };
            }

            string targetPath = string.IsNullOrWhiteSpace(check.TargetPath) ? "." : check.TargetPath.Trim();
            Transform targetTransform = targetPath == "." ? prefabRoot.transform : prefabRoot.transform.Find(targetPath);
            if (targetTransform == null)
            {
                return new { label, targetPath, passed, targetFound = false, error = $"TargetPath '{targetPath}' was not found under prefab '{prefabRoot.name}'." };
            }

            Type componentType = ResolveType(check.ComponentType);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return new { label, targetPath, hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform), passed, targetFound = true, componentFound = false, error = $"Component type '{check.ComponentType}' could not be resolved." };
            }

            Component[] matches = targetTransform.GetComponents(componentType);
            int componentIndex = Math.Max(0, check.ComponentIndex);
            if (matches == null || matches.Length <= componentIndex || matches[componentIndex] == null)
            {
                return new { label, targetPath, hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform), passed, targetFound = true, componentFound = false, componentType = componentType.FullName, componentIndex, error = $"Component '{check.ComponentType}' with index {componentIndex} was not found." };
            }

            Component component = matches[componentIndex];
            SerializedObject serializedObject = new(component);
            SerializedProperty property = serializedObject.FindProperty(check.PropertyPath);
            if (property == null)
            {
                return new { label, targetPath, hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform), passed, targetFound = true, componentFound = true, componentType = component.GetType().FullName, componentIndex, propertyFound = false, propertyPath = check.PropertyPath, error = $"Serialized property '{check.PropertyPath}' was not found." };
            }

            string currentValue = DescribeProperty(property);
            bool hasExpectedValue = check.ExpectedValue != null;
            bool valueMatches = !hasExpectedValue || SerializedPropertyValueMatches(property, currentValue, check.ExpectedValue);
            passed = valueMatches;
            return new
            {
                label,
                targetPath,
                hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform),
                targetObjectId = UnityApiAdapter.GetObjectIdOrZero(targetTransform.gameObject),
                componentType = component.GetType().FullName,
                componentId = UnityApiAdapter.GetObjectIdOrZero(component),
                componentIndex,
                propertyPath = check.PropertyPath,
                propertyType = property.propertyType.ToString(),
                propertyFound = true,
                currentValue,
                expectedValue = hasExpectedValue ? check.ExpectedValue : null,
                hasExpectedValue,
                valueMatches,
                passed
            };
        }

        static bool SerializedPropertyValueMatches(SerializedProperty property, string currentValue, JToken expectedValue)
        {
            if (expectedValue == null)
                return true;

            string expectedText = expectedValue.Type == JTokenType.String
                ? expectedValue.Value<string>()
                : expectedValue.ToString(Newtonsoft.Json.Formatting.None);

            if (string.Equals(currentValue, expectedText, StringComparison.OrdinalIgnoreCase))
                return true;

            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => expectedValue.Type == JTokenType.Boolean && property.boolValue == expectedValue.Value<bool>(),
                SerializedPropertyType.Integer => expectedValue.Type == JTokenType.Integer && property.intValue == expectedValue.Value<int>(),
                SerializedPropertyType.Float => expectedValue.Type is JTokenType.Float or JTokenType.Integer && Mathf.Approximately(property.floatValue, expectedValue.Value<float>()),
                SerializedPropertyType.String => string.Equals(property.stringValue ?? string.Empty, expectedText ?? string.Empty, StringComparison.Ordinal),
                SerializedPropertyType.ObjectReference => string.Equals(
                    property.objectReferenceValue != null ? AssetDatabase.GetAssetPath(property.objectReferenceValue) : "null",
                    SanitizeAssetPath(expectedText),
                    StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        static object ApplyAssignment(
            GameObject prefabRoot,
            PrefabSerializedPropertyAssignment assignment,
            bool previewOnly,
            bool allowSceneReferences,
            bool recordPrefabInstanceOverride,
            out string error)
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
                if (!TryAssignValue(property, assignment.Value, allowSceneReferences, out string assignError))
                {
                    error = $"Failed to assign '{assignment.PropertyPath}' on '{assignment.ComponentType}': {assignError}";
                    return null;
                }

                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
                if (recordPrefabInstanceOverride)
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(targetTransform.gameObject);
                }

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
                targetObjectId = UnityApiAdapter.GetObjectIdOrZero(targetTransform.gameObject),
                componentType = component.GetType().FullName,
                componentId = UnityApiAdapter.GetObjectIdOrZero(component),
                componentIndex = index,
                propertyPath = assignment.PropertyPath,
                propertyType = property.propertyType.ToString(),
                previousValue = beforeValue,
                value = assignment.Value,
                newValue = afterValue,
                changed = !string.Equals(beforeValue, afterValue, StringComparison.Ordinal),
                applied = !previewOnly
            };
        }

        static bool TryAssignValue(SerializedProperty property, JToken value, bool allowSceneReferences, out string error)
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
                        if (TryResolveObjectReference(value, allowSceneReferences, out UnityEngine.Object resolved, out error))
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

        static bool TryResolveObjectReference(JToken value, bool allowSceneReferences, out UnityEngine.Object resolved, out string error)
        {
            resolved = null;
            error = null;
            if (value == null || value.Type == JTokenType.Null)
            {
                return true;
            }

            if (allowSceneReferences && SceneTools.TryResolveObjectReference(value, out resolved, out error))
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

        static object CapturePrefabState(string prefabPath)
        {
            prefabPath = SanitizeAssetPath(prefabPath);
            GameObject asset = string.IsNullOrWhiteSpace(prefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return new
            {
                prefabPath,
                exists = asset != null,
                guid = string.IsNullOrWhiteSpace(prefabPath) ? null : AssetDatabase.AssetPathToGUID(prefabPath),
                name = asset != null ? asset.name : null,
                prefabAssetType = asset != null ? PrefabUtility.GetPrefabAssetType(asset).ToString() : null,
                isDirty = asset != null && EditorUtility.IsDirty(asset)
            };
        }

        static object CaptureAssetState(string assetPath)
        {
            assetPath = SanitizeAssetPath(assetPath);
            UnityEngine.Object asset = string.IsNullOrWhiteSpace(assetPath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(assetPath);
            return new
            {
                assetPath,
                exists = asset != null,
                guid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath),
                name = asset != null ? asset.name : null,
                type = asset != null ? asset.GetType().FullName : null,
                isDirty = asset != null && EditorUtility.IsDirty(asset)
            };
        }

        static object BuildAssetSaveState(
            bool requested = false,
            bool attempted = false,
            bool saved = false,
            object savedAssets = null,
            string message = null,
            string error = null)
        {
            return new
            {
                requested,
                attempted,
                saved,
                savedAssets = savedAssets ?? Array.Empty<object>(),
                message = message ?? (requested ? "save_requested" : "not_requested"),
                error
            };
        }

        static object DescribePrefabObject(GameObject gameObject, string targetPath)
        {
            if (gameObject == null)
                return null;

            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            return new
            {
                name = gameObject.name,
                targetPath,
                hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                sourcePrefabPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                isPrefabInstance = PrefabUtility.IsPartOfPrefabInstance(gameObject),
                prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString()
            };
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
