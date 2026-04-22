#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using Becool.UnityMcpLens.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects
{
    sealed class UnityComponentMutationStatus
    {
        public bool success { get; set; }
        public string message { get; set; }
        public string errorKind { get; set; }
        public UnityComponentHandle component { get; set; }
        public List<GameObjectChangeEntry> changes { get; } = new List<GameObjectChangeEntry>();
        public List<ValidationMessage> validationMessages { get; } = new List<ValidationMessage>();
        public List<string> errors { get; } = new List<string>();
        public List<Action> applyActions { get; } = new List<Action>();

        public bool willModify => changes.Count > 0;

        public static UnityComponentMutationStatus Ok(string message = null)
        {
            return new UnityComponentMutationStatus
            {
                success = true,
                message = message
            };
        }

        public static UnityComponentMutationStatus Error(string message, string errorKind)
        {
            var status = new UnityComponentMutationStatus
            {
                success = false,
                message = message,
                errorKind = errorKind
            };
            if (!string.IsNullOrEmpty(message))
                status.errors.Add(message);
            return status;
        }
    }

    sealed class UnityComponentMutationAdapter
    {
        sealed class ComponentTypeResolution
        {
            public bool success { get; set; }
            public Type type { get; set; }
            public string message { get; set; }
            public string errorKind { get; set; }
        }

        sealed class PropertyChangePlan
        {
            public bool changed { get; set; }
            public GameObjectChangeEntry entry { get; set; }
            public Action apply { get; set; }
        }

        readonly UnityGameObjectAdapter m_GameObjectAdapter;
        readonly JsonSerializer m_InputSerializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new Vector3Converter(),
                new Vector2Converter(),
                new QuaternionConverter(),
                new ColorConverter(),
                new RectConverter(),
                new BoundsConverter(),
                new UnityEngineObjectConverter()
            }
        });

        public UnityComponentMutationAdapter(UnityGameObjectAdapter gameObjectAdapter)
        {
            m_GameObjectAdapter = gameObjectAdapter;
        }

        public UnityComponentMutationStatus PreviewAddComponent(UnityGameObjectHandle target, string componentName, JObject properties)
        {
            var resolution = ResolveComponentType(componentName);
            if (!resolution.success)
                return UnityComponentMutationStatus.Error(resolution.message, resolution.errorKind);

            var validation = ValidateCanAddComponent(target, resolution.type, componentName);
            if (!validation.success)
                return validation;

            validation.changes.Add(new GameObjectChangeEntry
            {
                field = "component",
                before = null,
                after = new
                {
                    typeName = resolution.type.FullName,
                    shortTypeName = resolution.type.Name
                }
            });

            if (properties != null && properties.HasValues)
            {
                validation.validationMessages.Add(new ValidationMessage
                {
                    severity = "info",
                    code = "add_component_properties_apply_only",
                    message = "Component properties will be validated against the added component during apply."
                });
            }

            return validation;
        }

        public UnityComponentMutationStatus ApplyAddComponent(UnityGameObjectHandle target, string componentName, JObject properties)
        {
            var resolution = ResolveComponentType(componentName);
            if (!resolution.success)
                return UnityComponentMutationStatus.Error(resolution.message, resolution.errorKind);

            var validation = ValidateCanAddComponent(target, resolution.type, componentName);
            if (!validation.success)
                return validation;

            Component newComponent;
            try
            {
                newComponent = Undo.AddComponent(target.GameObject, resolution.type);
            }
            catch (Exception ex)
            {
                return UnityComponentMutationStatus.Error(
                    $"Error adding component '{componentName}' to '{target.Name}': {ex.Message}",
                    "apply_failed");
            }

            if (newComponent == null)
            {
                return UnityComponentMutationStatus.Error(
                    $"Failed to add component '{componentName}' to '{target.Name}'. It might be disallowed (e.g., adding script twice).",
                    "apply_failed");
            }

            if (newComponent is Light light)
                light.type = LightType.Directional;

            if (properties != null && properties.HasValues)
            {
                var propertyResult = ApplyComponentProperties(newComponent, properties, "Set Component Properties");
                if (!propertyResult.success)
                {
                    Undo.DestroyObjectImmediate(newComponent);
                    return propertyResult;
                }
            }

            EditorUtility.SetDirty(target.GameObject);
            var handle = m_GameObjectAdapter.ToComponentHandle(newComponent);
            var result = UnityComponentMutationStatus.Ok($"Component '{componentName}' added to '{target.Name}'.");
            result.component = handle;
            result.changes.Add(new GameObjectChangeEntry
            {
                field = "component",
                before = null,
                after = handle?.Info
            });
            return result;
        }

        public UnityComponentMutationStatus PreviewRemoveComponent(UnityGameObjectHandle target, string componentName, int? componentIndex)
        {
            var componentResult = ResolveComponentForMutation(target, componentName, componentIndex, remove: true);
            if (!componentResult.success)
                return componentResult;

            componentResult.changes.Add(new GameObjectChangeEntry
            {
                field = "component",
                before = componentResult.component?.Info,
                after = null
            });
            return componentResult;
        }

        public UnityComponentMutationStatus ApplyRemoveComponent(UnityGameObjectHandle target, string componentName, int? componentIndex)
        {
            var componentResult = ResolveComponentForMutation(target, componentName, componentIndex, remove: true);
            if (!componentResult.success)
                return componentResult;

            var componentInfo = componentResult.component.Info;
            try
            {
                Undo.DestroyObjectImmediate(componentResult.component.Component);
                EditorUtility.SetDirty(target.GameObject);
            }
            catch (Exception ex)
            {
                return UnityComponentMutationStatus.Error(
                    $"Error removing component '{componentName}' from '{target.Name}': {ex.Message}",
                    "apply_failed");
            }

            var result = UnityComponentMutationStatus.Ok($"Component '{componentName}' removed from '{target.Name}'.");
            result.component = componentResult.component;
            result.changes.Add(new GameObjectChangeEntry
            {
                field = "component",
                before = componentInfo,
                after = null
            });
            return result;
        }

        public UnityComponentMutationStatus PreviewSetProperties(UnityGameObjectHandle target, string componentName, int? componentIndex, JObject properties)
        {
            var componentResult = ResolveComponentForMutation(target, componentName, componentIndex, remove: false);
            if (!componentResult.success)
                return componentResult;

            var propertyResult = BuildPropertyPlan(componentResult.component.Component, properties);
            propertyResult.component = componentResult.component;
            return propertyResult;
        }

        public UnityComponentMutationStatus ApplySetProperties(UnityGameObjectHandle target, string componentName, int? componentIndex, JObject properties)
        {
            var componentResult = ResolveComponentForMutation(target, componentName, componentIndex, remove: false);
            if (!componentResult.success)
                return componentResult;

            var propertyResult = ApplyComponentProperties(componentResult.component.Component, properties, "Set Component Properties");
            propertyResult.component = componentResult.component;
            return propertyResult;
        }

        public UnityComponentMutationStatus ApplySetPropertiesToComponent(Component component, JObject properties)
        {
            var propertyResult = ApplyComponentProperties(component, properties, "Set Component Properties");
            propertyResult.component = m_GameObjectAdapter.ToComponentHandle(component);
            return propertyResult;
        }

        public Type FindType(string typeName)
        {
            var resolution = ResolveComponentType(typeName);
            return resolution.success ? resolution.type : null;
        }

        public UnityEngine.Object FindObjectByInstruction(JObject instruction, Type targetType)
        {
            string findTerm = instruction?["find"]?.ToString();
            string method = instruction?["method"]?.ToString()?.ToLowerInvariant();
            string componentName = instruction?["component"]?.ToString();

            if (string.IsNullOrEmpty(findTerm))
            {
                Debug.LogWarning("Find instruction missing 'find' term.");
                return null;
            }

            string searchMethodToUse = string.IsNullOrEmpty(method) ? "by_id_or_name_or_path" : method;

            if (IsAssetReferenceTarget(targetType, findTerm))
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(findTerm, targetType);
                if (asset != null)
                    return asset;

                asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(findTerm);
                if (asset != null && targetType.IsAssignableFrom(asset.GetType()))
                    return asset;

                string searchFilter = $"t:{targetType.Name} {System.IO.Path.GetFileNameWithoutExtension(findTerm)}";
                string[] guids = AssetDatabase.FindAssets(searchFilter);
                if (guids.Length == 1)
                {
                    asset = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guids[0]), targetType);
                    if (asset != null)
                        return asset;
                }
                else if (guids.Length > 1)
                {
                    Debug.LogWarning($"[FindObjectByInstruction] Ambiguous asset find: Found {guids.Length} assets matching filter '{searchFilter}'. Provide a full path or unique name.");
                    return null;
                }
            }

            var foundGo = m_GameObjectAdapter.FindObject(
                new GameObjectTargetRef
                {
                    text = findTerm,
                    isInteger = ulong.TryParse(findTerm, out _)
                },
                searchMethodToUse,
                true);

            if (foundGo == null)
                return null;

            if (targetType == typeof(GameObject))
                return foundGo.GameObject;

            if (typeof(Component).IsAssignableFrom(targetType))
            {
                Type componentToGetType = targetType;
                if (!string.IsNullOrEmpty(componentName))
                {
                    Type specificCompType = FindType(componentName);
                    if (specificCompType != null && typeof(Component).IsAssignableFrom(specificCompType))
                    {
                        componentToGetType = specificCompType;
                    }
                    else
                    {
                        Debug.LogWarning($"Could not find component type '{componentName}' specified in find instruction. Falling back to target type '{targetType.Name}'.");
                    }
                }

                Component foundComp = foundGo.GameObject.GetComponent(componentToGetType);
                if (foundComp == null)
                    Debug.LogWarning($"Found GameObject '{foundGo.Name}' but could not find component of type '{componentToGetType.Name}'.");
                return foundComp;
            }

            Debug.LogWarning($"Find instruction handling not implemented for target type: {targetType.Name}");
            return null;
        }

        ComponentTypeResolution ResolveComponentType(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return new ComponentTypeResolution
                {
                    success = false,
                    message = "Component type name is required.",
                    errorKind = "missing_component_name"
                };
            }

            if (!UnityComponentResolver.TryResolve(componentName, out var componentType, out var error) || componentType == null)
            {
                return new ComponentTypeResolution
                {
                    success = false,
                    message = string.IsNullOrEmpty(error) ? $"Component type '{componentName}' not found." : error,
                    errorKind = "component_type_not_found"
                };
            }

            if (!typeof(Component).IsAssignableFrom(componentType) || componentType.IsAbstract || componentType.IsInterface)
            {
                return new ComponentTypeResolution
                {
                    success = false,
                    message = $"Type '{componentName}' is not a valid concrete Component.",
                    errorKind = "invalid_component_type"
                };
            }

            return new ComponentTypeResolution
            {
                success = true,
                type = componentType
            };
        }

        UnityComponentMutationStatus ValidateCanAddComponent(UnityGameObjectHandle target, Type componentType, string componentName)
        {
            if (target?.GameObject == null)
                return UnityComponentMutationStatus.Error("Target GameObject is missing.", "target_not_found");

            if (componentType == typeof(Transform))
                return UnityComponentMutationStatus.Error("Cannot add another Transform component.", "cannot_add_transform");

            if (target.GameObject.GetComponent(componentType) != null && Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent), true))
            {
                return UnityComponentMutationStatus.Error(
                    $"Component '{componentName}' already exists on '{target.Name}' and does not allow duplicates.",
                    "component_already_exists");
            }

            bool isAdding2DPhysics = typeof(Rigidbody2D).IsAssignableFrom(componentType) || typeof(Collider2D).IsAssignableFrom(componentType);
            bool isAdding3DPhysics = typeof(Rigidbody).IsAssignableFrom(componentType) || typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics && (target.GameObject.GetComponent<Rigidbody>() != null || target.GameObject.GetComponent<Collider>() != null))
            {
                return UnityComponentMutationStatus.Error(
                    $"Cannot add 2D physics component '{componentName}' because the GameObject '{target.Name}' already has a 3D Rigidbody or Collider.",
                    "physics_component_conflict");
            }

            if (isAdding3DPhysics && (target.GameObject.GetComponent<Rigidbody2D>() != null || target.GameObject.GetComponent<Collider2D>() != null))
            {
                return UnityComponentMutationStatus.Error(
                    $"Cannot add 3D physics component '{componentName}' because the GameObject '{target.Name}' already has a 2D Rigidbody or Collider.",
                    "physics_component_conflict");
            }

            return UnityComponentMutationStatus.Ok();
        }

        UnityComponentMutationStatus ResolveComponentForMutation(UnityGameObjectHandle target, string componentName, int? componentIndex, bool remove)
        {
            var resolution = ResolveComponentType(componentName);
            if (!resolution.success)
                return UnityComponentMutationStatus.Error(resolution.message, resolution.errorKind);

            if (remove && resolution.type == typeof(Transform))
                return UnityComponentMutationStatus.Error("Cannot remove the Transform component.", "cannot_remove_transform");

            if (componentIndex.HasValue && componentIndex.Value < 0)
                return UnityComponentMutationStatus.Error("componentIndex must be greater than or equal to 0.", "invalid_component_index");

            var matches = m_GameObjectAdapter.FindComponents(target, componentName);
            if (matches.Count == 0)
            {
                return UnityComponentMutationStatus.Error(
                    $"Component '{componentName}' not found on '{target.Name}'.",
                    "component_not_found");
            }

            int index = componentIndex ?? 0;
            if (index >= matches.Count)
            {
                return UnityComponentMutationStatus.Error(
                    $"componentIndex {index} is out of range for component '{componentName}' on '{target.Name}'.",
                    "invalid_component_index");
            }

            var result = UnityComponentMutationStatus.Ok();
            result.component = matches[index];
            return result;
        }

        UnityComponentMutationStatus ApplyComponentProperties(Component component, JObject properties, string undoName)
        {
            var plan = BuildPropertyPlan(component, properties);
            if (!plan.success)
                return plan;

            if (!plan.willModify)
                return plan;

            Undo.RecordObject(component, undoName);
            foreach (var applyAction in plan.applyActions)
                applyAction();

            EditorUtility.SetDirty(component);
            return plan;
        }

        UnityComponentMutationStatus BuildPropertyPlan(Component component, JObject properties)
        {
            if (component == null)
                return UnityComponentMutationStatus.Error("Component not found.", "component_not_found");

            if (properties == null || !properties.HasValues)
                return UnityComponentMutationStatus.Error("'componentProperties' dictionary for the specified component is required and cannot be empty.", "missing_component_properties");

            var result = UnityComponentMutationStatus.Ok();
            foreach (var property in properties.Properties())
            {
                if (!TryBuildPropertyChange(component, property.Name, property.Value, out var change, out var errorMessage, out var errorKind))
                {
                    result.success = false;
                    result.errorKind = errorKind;
                    result.message = $"One or more properties failed on '{component.GetType().Name}'.";
                    result.errors.Add(errorMessage);
                    result.validationMessages.Add(new ValidationMessage
                    {
                        severity = "error",
                        code = errorKind,
                        message = errorMessage
                    });
                    continue;
                }

                if (change.changed)
                {
                    result.changes.Add(change.entry);
                    if (change.apply != null)
                        result.applyActions.Add(change.apply);
                }
            }

            if (!result.success)
                return result;

            result.message = result.willModify
                ? $"Prepared {result.changes.Count} component propert{(result.changes.Count == 1 ? "y" : "ies")}."
                : $"No component property changes needed for '{component.GetType().Name}'.";
            return result;
        }

        bool TryBuildPropertyChange(Component component, string propertyName, JToken value, out PropertyChangePlan change, out string errorMessage, out string errorKind)
        {
            change = null;
            errorMessage = null;
            errorKind = null;

            try
            {
                if (propertyName.Contains(".") || propertyName.Contains("["))
                    return TryBuildNestedPropertyChange(component, propertyName, value, out change, out errorMessage, out errorKind);

                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
                var type = component.GetType();
                PropertyInfo propertyInfo = type.GetProperty(propertyName, flags);
                if (propertyInfo != null && propertyInfo.CanWrite)
                {
                    object convertedValue = ConvertJTokenToType(value, propertyInfo.PropertyType);
                    object currentValue = propertyInfo.CanRead ? propertyInfo.GetValue(component) : null;
                    change = CreateChange(propertyName, currentValue, convertedValue, () => propertyInfo.SetValue(component, convertedValue));
                    return true;
                }

                FieldInfo fieldInfo = type.GetField(propertyName, flags)
                    ?? type.GetField(propertyName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (fieldInfo != null && !fieldInfo.IsInitOnly && !fieldInfo.IsLiteral && (fieldInfo.IsPublic || fieldInfo.GetCustomAttribute<SerializeField>() != null))
                {
                    object convertedValue = ConvertJTokenToType(value, fieldInfo.FieldType);
                    object currentValue = fieldInfo.GetValue(component);
                    change = CreateChange(propertyName, currentValue, convertedValue, () => fieldInfo.SetValue(component, convertedValue));
                    return true;
                }

                var availableProperties = UnityComponentResolver.GetAllComponentProperties(type);
                var suggestions = UnityComponentResolver.GetAIPropertySuggestions(propertyName, availableProperties);
                errorMessage = suggestions.Any()
                    ? $"Property '{propertyName}' not found. Did you mean: {string.Join(", ", suggestions)}? Available: [{string.Join(", ", availableProperties)}]"
                    : $"Property '{propertyName}' not found. Available: [{string.Join(", ", availableProperties)}]";
                errorKind = "property_not_found";
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error setting '{propertyName}': {ex.Message}";
                errorKind = "property_conversion_failed";
                return false;
            }
        }

        bool TryBuildNestedPropertyChange(object target, string path, JToken value, out PropertyChangePlan change, out string errorMessage, out string errorKind)
        {
            change = null;
            errorMessage = null;
            errorKind = null;

            try
            {
                string[] pathParts = SplitPropertyPath(path);
                if (pathParts.Length == 0)
                {
                    errorMessage = $"Property path '{path}' is invalid.";
                    errorKind = "property_not_found";
                    return false;
                }

                object currentObject = target;
                Type currentType = currentObject.GetType();

                for (int i = 0; i < pathParts.Length - 1; i++)
                {
                    if (!TryGetPathPartValue(currentObject, currentType, pathParts[i], out currentObject, out errorMessage))
                    {
                        errorKind = "property_not_found";
                        return false;
                    }

                    if (currentObject == null)
                    {
                        errorMessage = $"Property '{pathParts[i]}' is null, cannot access nested properties.";
                        errorKind = "property_not_found";
                        return false;
                    }

                    currentType = currentObject.GetType();
                }

                string finalPart = pathParts[pathParts.Length - 1];
                if (currentObject is Material material && finalPart.StartsWith("_", StringComparison.Ordinal))
                    return TryBuildMaterialPropertyChange(material, finalPart, value, path, out change, out errorMessage, out errorKind);

                return TryBuildFinalPropertyChange(currentObject, currentType, finalPart, path, value, out change, out errorMessage, out errorKind);
            }
            catch (Exception ex)
            {
                errorMessage = $"Error setting nested property '{path}': {ex.Message}";
                errorKind = "property_conversion_failed";
                return false;
            }
        }

        bool TryBuildFinalPropertyChange(object currentObject, Type currentType, string finalPart, string fullPath, JToken value, out PropertyChangePlan change, out string errorMessage, out string errorKind)
        {
            change = null;
            errorMessage = null;
            errorKind = null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            PropertyInfo propertyInfo = currentType.GetProperty(finalPart, flags);
            if (propertyInfo != null && propertyInfo.CanWrite)
            {
                object convertedValue = ConvertJTokenToType(value, propertyInfo.PropertyType);
                object currentValue = propertyInfo.CanRead ? propertyInfo.GetValue(currentObject) : null;
                change = CreateChange(fullPath, currentValue, convertedValue, () => propertyInfo.SetValue(currentObject, convertedValue));
                return true;
            }

            FieldInfo fieldInfo = currentType.GetField(finalPart, flags);
            if (fieldInfo != null && !fieldInfo.IsInitOnly && !fieldInfo.IsLiteral)
            {
                object convertedValue = ConvertJTokenToType(value, fieldInfo.FieldType);
                object currentValue = fieldInfo.GetValue(currentObject);
                change = CreateChange(fullPath, currentValue, convertedValue, () => fieldInfo.SetValue(currentObject, convertedValue));
                return true;
            }

            errorMessage = $"Property '{finalPart}' not found on type '{currentType.Name}'.";
            errorKind = "property_not_found";
            return false;
        }

        bool TryBuildMaterialPropertyChange(Material material, string propertyName, JToken value, string fullPath, out PropertyChangePlan change, out string errorMessage, out string errorKind)
        {
            change = null;
            errorMessage = null;
            errorKind = null;

            if (!material.HasProperty(propertyName))
            {
                errorMessage = $"Material property '{propertyName}' not found.";
                errorKind = "property_not_found";
                return false;
            }

            try
            {
                if (value is JArray array)
                {
                    if (array.Count == 4)
                    {
                        Color color = value.ToObject<Color>(m_InputSerializer);
                        change = CreateChange(fullPath, material.GetColor(propertyName), color, () => material.SetColor(propertyName, color));
                        return true;
                    }

                    Vector4 vector = value.ToObject<Vector4>(m_InputSerializer);
                    change = CreateChange(fullPath, material.GetVector(propertyName), vector, () => material.SetVector(propertyName, vector));
                    return true;
                }

                if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                {
                    float floatValue = value.ToObject<float>(m_InputSerializer);
                    change = CreateChange(fullPath, material.GetFloat(propertyName), floatValue, () => material.SetFloat(propertyName, floatValue));
                    return true;
                }

                if (value.Type == JTokenType.Boolean)
                {
                    float floatValue = value.ToObject<bool>(m_InputSerializer) ? 1f : 0f;
                    change = CreateChange(fullPath, material.GetFloat(propertyName), floatValue, () => material.SetFloat(propertyName, floatValue));
                    return true;
                }

                if (value.Type == JTokenType.String)
                {
                    Texture texture = value.ToObject<Texture>(m_InputSerializer);
                    change = CreateChange(fullPath, material.GetTexture(propertyName), texture, () => material.SetTexture(propertyName, texture));
                    return true;
                }
            }
            catch (Exception ex)
            {
                errorMessage = $"Unsupported or failed conversion for material property '{propertyName}': {ex.Message}";
                errorKind = "property_conversion_failed";
                return false;
            }

            errorMessage = $"Unsupported value for material property '{propertyName}'.";
            errorKind = "property_conversion_failed";
            return false;
        }

        bool TryGetPathPartValue(object currentObject, Type currentType, string pathPart, out object value, out string error)
        {
            value = null;
            error = null;
            string memberName = pathPart;
            bool isIndexed = TryParseIndex(pathPart, out memberName, out int index);

            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            PropertyInfo propertyInfo = currentType.GetProperty(memberName, flags);
            FieldInfo fieldInfo = propertyInfo == null ? currentType.GetField(memberName, flags) : null;
            if (propertyInfo == null && fieldInfo == null)
            {
                error = $"Could not find property or field '{memberName}' on type '{currentType.Name}'.";
                return false;
            }

            value = propertyInfo != null ? propertyInfo.GetValue(currentObject) : fieldInfo.GetValue(currentObject);
            if (!isIndexed)
                return true;

            if (value is Array array)
            {
                if (index < 0 || index >= array.Length)
                {
                    error = $"Index {index} out of range for '{memberName}'.";
                    return false;
                }

                value = array.GetValue(index);
                return true;
            }

            if (value is IList list)
            {
                if (index < 0 || index >= list.Count)
                {
                    error = $"Index {index} out of range for '{memberName}'.";
                    return false;
                }

                value = list[index];
                return true;
            }

            error = $"Property '{memberName}' is not an array or list, cannot access by index.";
            return false;
        }

        PropertyChangePlan CreateChange(string propertyName, object before, object after, Action apply)
        {
            return new PropertyChangePlan
            {
                entry = new GameObjectChangeEntry
                {
                    field = $"componentProperties.{propertyName}",
                    before = ToSerializableValue(before),
                    after = ToSerializableValue(after)
                },
                changed = !ValuesEqual(before, after),
                apply = apply
            };
        }

        object ConvertJTokenToType(JToken token, Type targetType)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null)
                    return Activator.CreateInstance(targetType);
                return null;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(targetType) && token is JObject objectToken && objectToken["find"] != null)
                return FindObjectByInstruction(objectToken, targetType);

            return token.ToObject(targetType, m_InputSerializer);
        }

        static bool ValuesEqual(object before, object after)
        {
            if (ReferenceEquals(before, after))
                return true;
            if (before == null || after == null)
                return false;

            const float epsilon = 0.0001f;
            if (before is float beforeFloat && after is float afterFloat)
                return Math.Abs(beforeFloat - afterFloat) <= epsilon;
            if (before is double beforeDouble && after is double afterDouble)
                return Math.Abs(beforeDouble - afterDouble) <= epsilon;
            if (before is Vector2 beforeVector2 && after is Vector2 afterVector2)
                return Vector2.Distance(beforeVector2, afterVector2) <= epsilon;
            if (before is Vector3 beforeVector3 && after is Vector3 afterVector3)
                return Vector3.Distance(beforeVector3, afterVector3) <= epsilon;
            if (before is Vector4 beforeVector4 && after is Vector4 afterVector4)
                return Vector4.Distance(beforeVector4, afterVector4) <= epsilon;
            if (before is Quaternion beforeQuaternion && after is Quaternion afterQuaternion)
                return Quaternion.Angle(beforeQuaternion, afterQuaternion) <= epsilon;
            if (before is Color beforeColor && after is Color afterColor)
                return Math.Abs(beforeColor.r - afterColor.r) <= epsilon
                    && Math.Abs(beforeColor.g - afterColor.g) <= epsilon
                    && Math.Abs(beforeColor.b - afterColor.b) <= epsilon
                    && Math.Abs(beforeColor.a - afterColor.a) <= epsilon;

            return before.Equals(after);
        }

        static object ToSerializableValue(object value)
        {
            if (value == null)
                return null;
            if (value is Vector2 vector2)
                return new { x = vector2.x, y = vector2.y };
            if (value is Vector3 vector3)
                return new Vector3Value { x = vector3.x, y = vector3.y, z = vector3.z };
            if (value is Vector4 vector4)
                return new { x = vector4.x, y = vector4.y, z = vector4.z, w = vector4.w };
            if (value is Quaternion quaternion)
                return new { x = quaternion.x, y = quaternion.y, z = quaternion.z, w = quaternion.w };
            if (value is Color color)
                return new { r = color.r, g = color.g, b = color.b, a = color.a };
            if (value is UnityEngine.Object unityObject)
            {
                object id = UnityApiAdapter.GetObjectId(unityObject);
                return new
                {
                    name = unityObject.name,
                    id = id == null ? null : Convert.ToString(id, System.Globalization.CultureInfo.InvariantCulture),
                    instanceID = id
                };
            }

            Type type = value.GetType();
            if (type.IsPrimitive || value is string || value is decimal)
                return value;

            return value.ToString();
        }

        static bool IsAssetReferenceTarget(Type targetType, string findTerm)
        {
            return typeof(Material).IsAssignableFrom(targetType)
                || typeof(Texture).IsAssignableFrom(targetType)
                || typeof(ScriptableObject).IsAssignableFrom(targetType)
                || (targetType.FullName?.StartsWith("UnityEngine.U2D", StringComparison.Ordinal) ?? false)
                || typeof(AudioClip).IsAssignableFrom(targetType)
                || typeof(AnimationClip).IsAssignableFrom(targetType)
                || typeof(Font).IsAssignableFrom(targetType)
                || typeof(Shader).IsAssignableFrom(targetType)
                || typeof(ComputeShader).IsAssignableFrom(targetType)
                || (typeof(GameObject).IsAssignableFrom(targetType) && findTerm.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));
        }

        static string[] SplitPropertyPath(string path)
        {
            var parts = new List<string>();
            int startIndex = 0;
            bool inBrackets = false;

            for (int i = 0; i < path.Length; i++)
            {
                char current = path[i];
                if (current == '[')
                    inBrackets = true;
                else if (current == ']')
                    inBrackets = false;
                else if (current == '.' && !inBrackets)
                {
                    parts.Add(path.Substring(startIndex, i - startIndex));
                    startIndex = i + 1;
                }
            }

            if (startIndex < path.Length)
                parts.Add(path.Substring(startIndex));

            return parts.ToArray();
        }

        static bool TryParseIndex(string pathPart, out string memberName, out int index)
        {
            memberName = pathPart;
            index = -1;

            int startBracket = pathPart.IndexOf('[');
            int endBracket = pathPart.IndexOf(']');
            if (startBracket <= 0 || endBracket <= startBracket)
                return false;

            memberName = pathPart.Substring(0, startBracket);
            return int.TryParse(pathPart.Substring(startBracket + 1, endBracket - startBracket - 1), out index);
        }

    }
}
