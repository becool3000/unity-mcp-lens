#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.Scene;
using Becool.UnityMcpLens.Editor.Tools;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.Scene
{
    sealed class UnitySceneReferenceBindingAdapter
    {
        public bool TryBindReferences(
            SceneReferenceBindingRequest request,
            bool previewOnly,
            out GameObject targetRoot,
            out List<object> bindings,
            out bool applied,
            out string error)
        {
            bindings = new List<object>();
            applied = false;
            error = null;
            targetRoot = null;

            if (request?.Target == null)
            {
                error = "target is required.";
                return false;
            }

            JObject findParams = new()
            {
                ["search_inactive"] = request.IncludeInactive
            };
            targetRoot = ObjectsHelper.FindObject(request.Target, request.SearchMethod, findParams);
            if (targetRoot == null)
            {
                error = "Scene target could not be found.";
                return false;
            }

            if (!targetRoot.scene.IsValid())
            {
                error = "Target does not belong to a valid loaded scene.";
                return false;
            }

            foreach (SceneReferenceBindingEntry entry in request.Bindings ?? Array.Empty<SceneReferenceBindingEntry>())
            {
                if (!TryBindEntry(targetRoot, entry, previewOnly, out object bindingRow, out bool entryApplied, out error))
                    return false;

                applied |= entryApplied;
                bindings.Add(bindingRow);
            }

            return true;
        }

        public bool TryInstantiatePrefabAndBind(
            ScenePrefabInstantiateAndBindRequest request,
            bool previewOnly,
            out GameObject instanceRoot,
            out object data,
            out bool applied,
            out string error)
        {
            instanceRoot = null;
            data = null;
            applied = false;
            error = null;

            string prefabPath = NormalizeAssetPath(request?.PrefabPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                error = $"Prefab '{prefabPath}' could not be loaded.";
                return false;
            }

            Transform parent = null;
            if (request?.Parent != null && request.Parent.Type != JTokenType.Null)
            {
                JObject findParams = new()
                {
                    ["search_inactive"] = request.IncludeInactive
                };
                GameObject parentGo = ObjectsHelper.FindObject(request.Parent, request.ParentSearchMethod, findParams);
                if (parentGo == null)
                {
                    error = "Parent target could not be resolved.";
                    return false;
                }

                parent = parentGo.transform;
            }

            string instanceName = string.IsNullOrWhiteSpace(request?.InstanceName) ? prefab.name : request.InstanceName.Trim();
            instanceRoot = FindExistingInstance(parent, instanceName, request?.IncludeInactive ?? true);
            bool exists = instanceRoot != null;
            var instanceChanges = new List<object>();
            var bindingRows = new List<object>();

            if (!exists)
            {
                applied = true;
                instanceChanges.Add(new { property = "instance", previousValue = (string)null, newValue = instanceName });
                if (!previewOnly)
                {
                    UnityEngine.Object createdObject = parent != null
                        ? PrefabUtility.InstantiatePrefab(prefab, parent)
                        : PrefabUtility.InstantiatePrefab(prefab);
                    instanceRoot = createdObject as GameObject;
                    if (instanceRoot == null)
                    {
                        error = $"Failed to instantiate prefab '{prefabPath}'.";
                        return false;
                    }

                    if (!string.Equals(instanceRoot.name, instanceName, StringComparison.Ordinal))
                        instanceRoot.name = instanceName;
                }
            }

            if (instanceRoot != null)
            {
                if (!TryApplyTransform(instanceRoot.transform, request, previewOnly, instanceChanges, out bool transformChanged, out error))
                    return false;

                applied |= transformChanged;
            }

            if (instanceRoot != null)
            {
                foreach (SceneReferenceBindingEntry binding in request?.Bindings ?? Array.Empty<SceneReferenceBindingEntry>())
                {
                    if (!TryBindEntry(instanceRoot, binding, previewOnly, out object row, out bool bindingApplied, out error))
                        return false;

                    applied |= bindingApplied;
                    bindingRows.Add(row);
                }
            }
            else
            {
                foreach (SceneReferenceBindingEntry binding in request?.Bindings ?? Array.Empty<SceneReferenceBindingEntry>())
                {
                    bindingRows.Add(new
                    {
                        targetPath = binding?.targetPath ?? ".",
                        componentType = binding?.componentType,
                        propertyPath = binding?.propertyPath,
                        bindingType = "pending_instance_create",
                        willModify = true,
                        applied = false
                    });
                }
            }

            data = new
            {
                prefabPath,
                instanceName,
                parentPath = parent != null ? UiDiagnosticsHelper.GetHierarchyPath(parent) : string.Empty,
                instancePath = instanceRoot != null ? UiDiagnosticsHelper.GetHierarchyPath(instanceRoot.transform) : instanceName,
                exists,
                applied = !previewOnly && applied,
                willModify = applied,
                instanceChanges = instanceChanges.ToArray(),
                bindings = bindingRows.ToArray()
            };
            return true;
        }

        static GameObject FindExistingInstance(Transform parent, string instanceName, bool includeInactive)
        {
            if (string.IsNullOrWhiteSpace(instanceName))
                return null;

            if (parent != null)
            {
                for (int i = 0; i < parent.childCount; i++)
                {
                    Transform child = parent.GetChild(i);
                    if (child != null &&
                        (includeInactive || child.gameObject.activeInHierarchy) &&
                        string.Equals(child.name, instanceName, StringComparison.Ordinal))
                    {
                        return child.gameObject;
                    }
                }

                return null;
            }

            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.IsValid())
            {
                foreach (GameObject root in activeScene.GetRootGameObjects())
                {
                    if (root != null &&
                        (includeInactive || root.activeInHierarchy) &&
                        string.Equals(root.name, instanceName, StringComparison.Ordinal))
                    {
                        return root;
                    }
                }
            }

            return null;
        }

        static bool TryApplyTransform(
            Transform transform,
            ScenePrefabInstantiateAndBindRequest request,
            bool previewOnly,
            List<object> changes,
            out bool changed,
            out string error)
        {
            bool hasChanges = false;
            changed = false;
            error = null;
            if (transform == null)
                return true;

            void RecordChange(string property, object previousValue, object newValue)
            {
                if (Equals(previousValue, newValue))
                    return;

                hasChanges = true;
                changes.Add(new
                {
                    property,
                    previousValue = NormalizeValue(previousValue),
                    newValue = NormalizeValue(newValue)
                });
            }

            if (request?.Position != null && request.Position.Type != JTokenType.Null)
            {
                if (!TryParseVector3(request.Position, out Vector3 position))
                {
                    error = "position must be {x,y,z} or [x,y,z].";
                    return false;
                }

                RecordChange("localPosition", transform.localPosition, position);
                if (!previewOnly)
                    transform.localPosition = position;
            }

            if (request?.Rotation != null && request.Rotation.Type != JTokenType.Null)
            {
                if (!TryParseVector3(request.Rotation, out Vector3 rotation))
                {
                    error = "rotation must be {x,y,z} or [x,y,z].";
                    return false;
                }

                RecordChange("localEulerAngles", transform.localEulerAngles, rotation);
                if (!previewOnly)
                    transform.localEulerAngles = rotation;
            }

            if (request?.Scale != null && request.Scale.Type != JTokenType.Null)
            {
                if (!TryParseVector3(request.Scale, out Vector3 scale))
                {
                    error = "scale must be {x,y,z} or [x,y,z].";
                    return false;
                }

                RecordChange("localScale", transform.localScale, scale);
                if (!previewOnly)
                    transform.localScale = scale;
            }

            changed = hasChanges;
            if (!previewOnly && hasChanges)
                EditorUtility.SetDirty(transform);

            return true;
        }

        static bool TryParseVector3(JToken token, out Vector3 value)
        {
            value = default;
            if (token is JArray array && array.Count >= 3)
            {
                value = new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                value = new Vector3(obj["x"]?.Value<float>() ?? 0f, obj["y"]?.Value<float>() ?? 0f, obj["z"]?.Value<float>() ?? 0f);
                return true;
            }

            return false;
        }

        static object NormalizeValue(object value)
        {
            return value switch
            {
                Vector3 vector => new { x = vector.x, y = vector.y, z = vector.z },
                _ => value
            };
        }

        static string NormalizeAssetPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Replace('\\', '/');
        }

        static bool TryBindEntry(GameObject targetRoot, SceneReferenceBindingEntry entry, bool previewOnly, out object bindingRow, out bool applied, out string error)
        {
            bindingRow = null;
            applied = false;
            error = null;
            if (entry == null)
            {
                error = "Binding entry cannot be null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.componentType))
            {
                error = "binding.componentType is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.propertyPath))
            {
                error = "binding.propertyPath is required.";
                return false;
            }

            string targetPath = string.IsNullOrWhiteSpace(entry.targetPath) ? "." : entry.targetPath.Trim();
            Transform targetTransform = targetPath == "." ? targetRoot.transform : targetRoot.transform.Find(targetPath);
            if (targetTransform == null)
            {
                error = $"TargetPath '{targetPath}' was not found under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.";
                return false;
            }

            Type componentType = UnityComponentResolver.FindType(entry.componentType);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{entry.componentType}' could not be resolved.";
                return false;
            }

            Component[] matches = targetTransform.GetComponents(componentType);
            int index = Math.Max(0, entry.componentIndex);
            if (matches == null || matches.Length <= index || matches[index] == null)
            {
                error = $"Component '{entry.componentType}' with index {index} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(targetTransform)}'.";
                return false;
            }

            Component component = matches[index];
            SerializedObject serializedObject = new(component);
            SerializedProperty property = serializedObject.FindProperty(entry.propertyPath);
            if (property == null)
            {
                error = $"Serialized property '{entry.propertyPath}' was not found on component '{entry.componentType}'.";
                return false;
            }

            if (!TryClassifyBindingTarget(component.GetType(), entry.propertyPath, out bool isSingleReference, out bool isReferenceArray, out string classificationError))
            {
                error = classificationError;
                return false;
            }

            if (isSingleReference && entry.references is { Length: > 0 })
            {
                error = $"Property '{entry.propertyPath}' accepts a single object reference; use 'reference' instead of 'references'.";
                return false;
            }

            if (isReferenceArray && entry.reference != null && entry.reference.Type != JTokenType.Null)
            {
                error = $"Property '{entry.propertyPath}' accepts an array/list of object references; use 'references'.";
                return false;
            }

            if (!TryResolveRequestedReferences(entry, isSingleReference, out UnityEngine.Object[] resolvedReferences, out string resolveError))
            {
                error = resolveError;
                return false;
            }

            if (isSingleReference)
            {
                UnityEngine.Object previous = property.objectReferenceValue;
                UnityEngine.Object next = resolvedReferences.Length > 0 ? resolvedReferences[0] : null;
                applied = !ReferenceEquals(previous, next);
                if (!previewOnly && applied)
                {
                    property.objectReferenceValue = next;
                    serializedObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(component);
                }

                serializedObject.UpdateIfRequiredOrScript();
                bindingRow = new
                {
                    targetPath,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform),
                    componentType = component.GetType().FullName,
                    componentIndex = index,
                    propertyPath = entry.propertyPath,
                    bindingType = "single",
                    previousReference = DescribeReference(previous),
                    requestedReference = DescribeReference(next),
                    readbackReference = DescribeReference(property.objectReferenceValue),
                    willModify = applied,
                    applied = !previewOnly && applied
                };
                return true;
            }

            if (!property.isArray)
            {
                error = $"Serialized property '{entry.propertyPath}' does not expose an object-reference array or list.";
                return false;
            }

            UnityEngine.Object[] previousReferences = Enumerable.Range(0, property.arraySize)
                .Select(i => property.GetArrayElementAtIndex(i))
                .Where(element => element != null)
                .Select(element => element.objectReferenceValue)
                .ToArray();

            applied = !AreReferenceArraysEqual(previousReferences, resolvedReferences);
            if (!previewOnly && applied)
            {
                property.arraySize = resolvedReferences.Length;
                for (int i = 0; i < resolvedReferences.Length; i++)
                {
                    SerializedProperty element = property.GetArrayElementAtIndex(i);
                    if (element == null || element.propertyType != SerializedPropertyType.ObjectReference)
                    {
                        error = $"Serialized property '{entry.propertyPath}' does not expose object-reference array elements.";
                        return false;
                    }

                    element.objectReferenceValue = resolvedReferences[i];
                }

                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
            }

            serializedObject.UpdateIfRequiredOrScript();
            UnityEngine.Object[] readbackReferences = Enumerable.Range(0, property.arraySize)
                .Select(i => property.GetArrayElementAtIndex(i))
                .Where(element => element != null)
                .Select(element => element.objectReferenceValue)
                .ToArray();

            bindingRow = new
            {
                targetPath,
                hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform),
                componentType = component.GetType().FullName,
                componentIndex = index,
                propertyPath = entry.propertyPath,
                bindingType = "array",
                previousReferences = previousReferences.Select(DescribeReference).ToArray(),
                requestedReferences = resolvedReferences.Select(DescribeReference).ToArray(),
                readbackReferences = readbackReferences.Select(DescribeReference).ToArray(),
                willModify = applied,
                applied = !previewOnly && applied
            };
            return true;
        }

        static bool TryResolveRequestedReferences(SceneReferenceBindingEntry entry, bool isSingleReference, out UnityEngine.Object[] resolvedReferences, out string error)
        {
            error = null;
            if (isSingleReference)
            {
                if (!SceneTools.TryResolveObjectReference(entry.reference, out UnityEngine.Object resolved, out error))
                {
                    resolvedReferences = Array.Empty<UnityEngine.Object>();
                    return false;
                }

                resolvedReferences = new[] { resolved };
                return true;
            }

            var values = new List<UnityEngine.Object>();
            foreach (JToken referenceToken in entry.references ?? Array.Empty<JToken>())
            {
                if (!SceneTools.TryResolveObjectReference(referenceToken, out UnityEngine.Object resolved, out error))
                {
                    resolvedReferences = Array.Empty<UnityEngine.Object>();
                    return false;
                }

                values.Add(resolved);
            }

            resolvedReferences = values.ToArray();
            return true;
        }

        static bool TryClassifyBindingTarget(Type componentType, string propertyPath, out bool isSingleReference, out bool isReferenceArray, out string error)
        {
            isSingleReference = false;
            isReferenceArray = false;
            error = null;

            Type propertyType = ResolvePropertyPathType(componentType, propertyPath);
            if (propertyType == null)
            {
                error = $"Could not determine the reflected type for '{propertyPath}'.";
                return false;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(propertyType))
            {
                isSingleReference = true;
                return true;
            }

            Type elementType = GetCollectionElementType(propertyType);
            if (elementType != null && typeof(UnityEngine.Object).IsAssignableFrom(elementType))
            {
                isReferenceArray = true;
                return true;
            }

            error = $"Property '{propertyPath}' is not an object reference or object-reference array/list.";
            return false;
        }

        static Type ResolvePropertyPathType(Type rootType, string propertyPath)
        {
            Type currentType = rootType;
            foreach (string segment in (propertyPath ?? string.Empty).Split('.'))
            {
                if (currentType == null)
                    return null;

                if (string.Equals(segment, "Array", StringComparison.OrdinalIgnoreCase))
                {
                    currentType = GetCollectionElementType(currentType);
                    continue;
                }

                if (segment.StartsWith("data[", StringComparison.OrdinalIgnoreCase))
                    continue;

                FieldInfo field = currentType.GetField(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    currentType = field.FieldType;
                    continue;
                }

                PropertyInfo property = currentType.GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null)
                {
                    currentType = property.PropertyType;
                    continue;
                }

                return null;
            }

            return currentType;
        }

        static Type GetCollectionElementType(Type type)
        {
            if (type == null || type == typeof(string))
                return null;

            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                Type genericDefinition = type.GetGenericTypeDefinition();
                if (genericDefinition == typeof(List<>) || genericDefinition == typeof(IList<>) || genericDefinition == typeof(IEnumerable<>))
                    return type.GetGenericArguments()[0];
            }

            Type enumerable = type.GetInterfaces()
                .Concat(new[] { type })
                .FirstOrDefault(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return enumerable?.GetGenericArguments()[0];
        }

        static bool AreReferenceArraysEqual(UnityEngine.Object[] left, UnityEngine.Object[] right)
        {
            if (left == null || right == null)
                return left == right;

            if (left.Length != right.Length)
                return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (!ReferenceEquals(left[i], right[i]))
                    return false;
            }

            return true;
        }

        static object DescribeReference(UnityEngine.Object reference)
        {
            if (reference == null)
                return null;

            if (reference is Component component)
            {
                return new
                {
                    type = component.GetType().FullName,
                    name = component.name,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(component.transform)
                };
            }

            if (reference is GameObject gameObject)
            {
                return new
                {
                    type = gameObject.GetType().FullName,
                    name = gameObject.name,
                    hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform)
                };
            }

            string assetPath = AssetDatabase.GetAssetPath(reference);
            return new
            {
                type = reference.GetType().FullName,
                name = reference.name,
                assetPath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath
            };
        }
    }
}
