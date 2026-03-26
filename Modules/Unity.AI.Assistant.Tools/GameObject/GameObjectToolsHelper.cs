using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Backend.Socket.Utilities;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GameObjectToolsHelper
    {
        internal const string k_ComponentNameRequiredMessage = "Component name is required.";
        internal const string k_GameObjectInstanceIdRequiredMessage = "GameObject instance ID is required.";

        internal static string FormatGameObjectNotFoundMessage(int instanceId)
        {
            return $"GameObject with instance ID '{instanceId}' not found. Make sure the GameObject ID is valid.";
        }

        internal static string FormatComponentNotFoundMessage(int instanceId)
        {
            return $"Component with Id '{instanceId}' not found.";
        }

        internal static string FormatCannotRemoveComponentMessage(string componentName, string gameObjectName)
        {
            return $"Cannot remove '{componentName}' component from '{gameObjectName}'. Unity prevents removal of this component type.";
        }

        internal static string FormatNoPropertiesAppliedMessage(string componentName, string failedProperties)
        {
            return $"Properties '{failedProperties}' could not be applied to component '{componentName}'. Verify the provided property names and values.";
        }
        static readonly Type[] s_AllUnityObjectTypes = TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().ToArray();
        static readonly Dictionary<string, Type> s_UnityObjectTypesByFullName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Type> s_UnityObjectTypesByName = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        static GameObjectToolsHelper()
        {
            foreach (var type in s_AllUnityObjectTypes)
            {
                if (type == null)
                    continue;

                if (!string.IsNullOrEmpty(type.FullName) && !s_UnityObjectTypesByFullName.ContainsKey(type.FullName))
                    s_UnityObjectTypesByFullName.Add(type.FullName, type);

                if (!string.IsNullOrEmpty(type.Name) && !s_UnityObjectTypesByName.ContainsKey(type.Name))
                    s_UnityObjectTypesByName.Add(type.Name, type);
            }
        }

        public static GameObject FindGameObject(string target, string searchMethod)
        {
            if (string.IsNullOrEmpty(target))
                return null;

            searchMethod = searchMethod?.ToLower() ?? "by_name";

            switch (searchMethod)
            {
                case "by_id":
                    if (int.TryParse(target, out int instanceId))
                    {
#if UNITY_6000_3_OR_NEWER
                        return EditorUtility.EntityIdToObject(instanceId) as GameObject;
#else
                        return EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif
                    }
                    break;
                case "by_name":
                    return GameObject.Find(target);
                case "by_path":
                    return GameObject.Find(target);
                case "by_tag":
                    GameObject goByTag = GameObject.FindWithTag(target);
                    return goByTag;
                default:
                    // Default to by_name for backwards compatibility
                    return GameObject.Find(target);
            }
            return null;
        }

        public static GameObject FindGameObjectByName(string name)
        {
            return GameObject.Find(name);
        }


        public static bool SetGameObjectTag(GameObject gameObject, string tag)
        {
            var existingTags = UnityEditorInternal.InternalEditorUtility.tags;
            bool tagExists = false;

            foreach (var existingTag in existingTags)
            {
                if (existingTag == tag)
                {
                    tagExists = true;
                    break;
                }
            }

            if (!tagExists)
            {
                return false;
            }

            try
            {
                gameObject.tag = tag;
                return true;
            }
            catch (System.Exception)
            {
                // Failed to set tag (shouldn't happen if tag exists, but handle gracefully)
                return false;
            }
        }

        public static bool SetGameObjectLayer(GameObject gameObject, string layerName)
        {
            int layerId = LayerMask.NameToLayer(layerName);
            if (layerId != -1)
            {
                gameObject.layer = layerId;
                return true;
            }
            return false;
        }

        public static Component AddComponentByName(GameObject gameObject, string componentName)
        {
            return AddComponentByNameInternal(gameObject, componentName);
        }

        static Component AddComponentByNameInternal(GameObject gameObject, string componentName)
        {
            var componentType = FindType(componentName);

            if (componentType == null)
            {
                throw new InvalidOperationException($"Component type '{componentName}' not found.");
            }

            if (!typeof(Component).IsAssignableFrom(componentType))
            {
                throw new InvalidOperationException($"Type '{componentName}' is not a valid Component type.");
            }

            string capturedMessage = null;
            Application.LogCallback logHandler = (string logString, string stackTrace, LogType type) =>
            {
                if (capturedMessage == null)
                {
                    var firstLine = logString.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (!string.IsNullOrEmpty(firstLine))
                    {
                        capturedMessage = firstLine.Trim();
                    }
                }
            };

            try
            {
                Application.logMessageReceived += logHandler;

                var newComponent = gameObject.AddComponent(componentType);

                if (newComponent == null)
                {
                    // Use captured Unity error message if available
                    if (!string.IsNullOrEmpty(capturedMessage))
                    {
                        throw new InvalidOperationException(capturedMessage);
                    }

                    return null;
                }

                Undo.RegisterCreatedObjectUndo(newComponent, $"Add {componentName}");
                return newComponent;
            }
            finally
            {
                Application.logMessageReceived -= logHandler;
            }
        }

        public static bool RemoveComponentByName(GameObject gameObject, string componentName)
        {
            return RemoveComponentByNameInternal(gameObject, componentName);
        }

        private static bool RemoveComponentByNameInternal(GameObject gameObject, string componentName)
        {
            Type componentType = FindType(componentName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                Debug.LogWarning($"[GameObject Tools] Component type not found or not a Component: {componentName}");
                return false;
            }

            // Don't allow removing Transform
            if (componentType == typeof(Transform))
            {
                Debug.LogWarning($"[GameObject Tools] Cannot remove Transform component from GameObject");
                return false;
            }

            try
            {
                Component component = gameObject.GetComponent(componentType);
                if (component != null)
                {
                    Debug.Log($"[GameObject Tools] Removing component {componentName} ({componentType.FullName}) from {gameObject.name}");
                    Undo.DestroyObjectImmediate(component);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to remove component {componentName}: {ex.Message}");
                return false;
            }
        }

        public static Component FindComponent(int instanceId)
        {
#if UNITY_6000_3_OR_NEWER
            return EditorUtility.EntityIdToObject(instanceId) as Component;
#else
            return EditorUtility.InstanceIDToObject(instanceId) as Component;
#endif
        }

        public static void SetComponentProperties(Component component, JObject properties)
        {
            if (component == null)
                throw new ArgumentNullException(nameof(component));

            if (properties == null)
                throw new ArgumentNullException(nameof(properties));

            Debug.Log($"[GameObject Tools] Setting properties on component {component.GetType().Name} of {component.gameObject.name}");
            Undo.RecordObject(component, "Set Component Properties");

            try
            {
                // Use SerializedObject for Unity-serialized properties (preferred method)
                var serializedObject = new SerializedObject(component);
                serializedObject.Update();

                var setProperties = new Dictionary<string, bool>();

                foreach (var property in properties.Properties())
                {
                    Debug.Log($"[GameObject Tools] Setting property {property.Name} = {property.Value}");
                    // Try SerializedProperty first (Unity's preferred way)
                    if (!SetSerializedProperty(serializedObject, property.Name, property.Value))
                    {
                        setProperties.Add(property.Name, false);
                    }
                }

                var failedProperties = setProperties
                    .Select(kv => kv.Key)
                    .ToList();

                if (setProperties.Count != 0)
                {
                    throw new InvalidOperationException(
                        FormatNoPropertiesAppliedMessage(component.GetType().Name, string.Join(", ", failedProperties)));
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(component);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to set properties on component {component.GetType().Name}: {ex.Message}");
                throw;
            }
        }

        private static bool SetSerializedProperty(SerializedObject serializedObject, string propertyName, JToken value)
        {
            var serializedProperty = serializedObject.FindProperty(propertyName);
            if (serializedProperty == null)
            {
                Debug.LogWarning($"[GameObject Tools] SerializedProperty '{propertyName}' (mapped from '{propertyName}') not found, trying reflection fallback");
                return false;
            }

            try
            {
                return SetSerializedPropertyValue(serializedProperty, value);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GameObject Tools] Failed to set SerializedProperty '{propertyName}': {ex.Message}");
                return false;
            }
        }

        private static bool SetSerializedPropertyValue(SerializedProperty property, JToken value)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Integer:
                    property.intValue = value.ToObject<int>();
                    return true;

                case SerializedPropertyType.Boolean:
                    property.boolValue = value.ToObject<bool>();
                    return true;

                case SerializedPropertyType.Float:
                    property.floatValue = value.ToObject<float>();
                    return true;

                case SerializedPropertyType.String:
                    property.stringValue = value.ToObject<string>();
                    return true;

                case SerializedPropertyType.Vector2:
                    if (value is JObject v2Object && v2Object.ContainsKey("x") && v2Object.ContainsKey("y"))
                    {
                        property.vector2Value = new Vector2(v2Object["x"].ToObject<float>(), v2Object["y"].ToObject<float>());
                        return true;
                    }
                    break;

                case SerializedPropertyType.Vector3:
                    if (value is JObject v3Object && v3Object.ContainsKey("x") && v3Object.ContainsKey("y") && v3Object.ContainsKey("z"))
                    {
                        property.vector3Value = new Vector3(v3Object["x"].ToObject<float>(), v3Object["y"].ToObject<float>(), v3Object["z"].ToObject<float>());
                        return true;
                    }
                    break;

                case SerializedPropertyType.Vector4:
                    if (value is JObject v4Object && v4Object.ContainsKey("x") && v4Object.ContainsKey("y") && v4Object.ContainsKey("z") && v4Object.ContainsKey("w"))
                    {
                        property.vector4Value = new Vector4(v4Object["x"].ToObject<float>(), v4Object["y"].ToObject<float>(), v4Object["z"].ToObject<float>(), v4Object["w"].ToObject<float>());
                        return true;
                    }
                    break;

                case SerializedPropertyType.Quaternion:
                    if (value is JObject quatObject)
                    {
                        if (quatObject.ContainsKey("x") && quatObject.ContainsKey("y") && quatObject.ContainsKey("z") && quatObject.ContainsKey("w"))
                        {
                            // Treat as direct Quaternion values (x, y, z, w)
                            property.quaternionValue = new Quaternion(quatObject["x"].ToObject<float>(), quatObject["y"].ToObject<float>(), quatObject["z"].ToObject<float>(), quatObject["w"].ToObject<float>());
                            return true;
                        }
                        else if (quatObject.ContainsKey("x") && quatObject.ContainsKey("y") && quatObject.ContainsKey("z"))
                        {
                            // Treat as Euler angles and convert to Quaternion
                            var eulerAngles = new Vector3(quatObject["x"].ToObject<float>(), quatObject["y"].ToObject<float>(), quatObject["z"].ToObject<float>());
                            property.quaternionValue = Quaternion.Euler(eulerAngles);
                            return true;
                        }
                    }
                    break;

                case SerializedPropertyType.Color:
                    if (value is JObject colorObject && colorObject.ContainsKey("r") && colorObject.ContainsKey("g") && colorObject.ContainsKey("b"))
                    {
                        float r = colorObject["r"].ToObject<float>();
                        float g = colorObject["g"].ToObject<float>();
                        float b = colorObject["b"].ToObject<float>();
                        float a = colorObject.ContainsKey("a") ? colorObject["a"].ToObject<float>() : 1f;
                        property.colorValue = new Color(r, g, b, a);
                        return true;
                    }
                    break;

                case SerializedPropertyType.ObjectReference:
                    // Handle Unity Object references with {"fileID": instanceId} format
                    if (value is JObject objRef && objRef.TryGetValue("fileID", out JToken fileIdToken))
                    {
                        int instanceId = fileIdToken.ToObject<int>();
#if UNITY_6000_3_OR_NEWER
                        var unityObj = EditorUtility.EntityIdToObject(instanceId);
#else
                        var unityObj = EditorUtility.InstanceIDToObject(instanceId);
#endif
                        if (unityObj != null)
                        {
                            var expectedType = ResolveObjectReferenceType(property);

                            if (expectedType != null && !expectedType.IsInstanceOfType(unityObj))
                            {
                                throw new InvalidOperationException(
                                    $"Property '{property.name}' expects reference type '{expectedType.Name}', but received '{unityObj.GetType().Name}'.");
                            }

                            Debug.Log($"[GameObject Tools] Setting ObjectReference '{property.name}' to {unityObj.name} ({unityObj.GetType().Name})");
                            property.objectReferenceValue = unityObj;
                            return true;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Object with fileID {instanceId} not found.");
                        }
                    }
                    else if (value.Type == JTokenType.Null)
                    {
                        property.objectReferenceValue = null;
                        return true;
                    }
                    break;

                case SerializedPropertyType.Enum:
                    if (value.Type == JTokenType.Integer)
                    {
                        property.enumValueIndex = value.ToObject<int>();
                        return true;
                    }
                    else if (value.Type == JTokenType.String)
                    {
                        string enumName = value.ToObject<string>();
                        var enumNames = property.enumDisplayNames;
                        for (int i = 0; i < enumNames.Length; i++)
                        {
                            if (enumNames[i].Equals(enumName, StringComparison.OrdinalIgnoreCase))
                            {
                                property.enumValueIndex = i;
                                return true;
                            }
                        }
                    }
                    break;

                case SerializedPropertyType.ArraySize:
                    if (property.isArray)
                    {
                        int newSize = value.ToObject<int>();
                        property.arraySize = newSize;
                        return true;
                    }
                    break;

                default:
                    // Handle arrays and other complex properties
                    if (property.isArray && value is JArray arrayValue)
                    {
                        return SetArrayProperty(property, arrayValue);
                    }

                    Debug.LogWarning($"[GameObject Tools] Unsupported SerializedPropertyType: {property.propertyType} for property '{property.name}'");
                    return false;
            }

            return false;
        }

        static Type ResolveObjectReferenceType(SerializedProperty property)
        {
            if (property == null)
                return null;

            if (property.objectReferenceValue != null)
                return property.objectReferenceValue.GetType();

            var typeProperty = typeof(SerializedProperty).GetProperty(
                "objectReferenceTypeString",
                BindingFlags.NonPublic | BindingFlags.Instance);

            var typeString = typeProperty?.GetValue(property) as string;
            if (!string.IsNullOrEmpty(typeString))
            {
                var resolvedType = FindType(typeString);
                if (resolvedType != null)
                    return resolvedType;
            }

            return null;
        }

        private static bool SetArrayProperty(SerializedProperty arrayProperty, JArray arrayValue)
        {
            try
            {
                // Resize the array to match the input
                arrayProperty.arraySize = arrayValue.Count;

                // Set each element
                for (int i = 0; i < arrayValue.Count; i++)
                {
                    var elementProperty = arrayProperty.GetArrayElementAtIndex(i);
                    if (!SetSerializedPropertyValue(elementProperty, arrayValue[i]))
                    {
                        Debug.LogWarning($"[GameObject Tools] Failed to set array element {i} of '{arrayProperty.name}'");
                        // Continue with other elements even if one fails
                    }
                }

                Debug.Log($"[GameObject Tools] Successfully set array '{arrayProperty.name}' with {arrayValue.Count} elements");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GameObject Tools] Failed to set array '{arrayProperty.name}': {ex.Message}");
                return false;
            }
        }

        public static Type FindType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("Type name cannot be empty.");

            // TODO: type name may conflict accross namespaces, need better disambiguation
            Type type;
            s_UnityObjectTypesByName.TryGetValue(typeName, out type);
            if (type != null) return type;

            s_UnityObjectTypesByFullName.TryGetValue(typeName, out type);
            if (type != null) return type;

            // TODO: not sure if we need this fallback at all
            var candidateFullName = typeName.Contains(".") ? typeName : $"UnityEngine.{typeName}";

            // Search all loaded assemblies for types with matching names
            foreach (var assembly in AssemblyUtils.GetLoadedAssemblies())
            {
                try
                {
                    // Skip system assemblies to improve performance
                    if (assembly.GetName().Name.StartsWith("System.") ||
                        assembly.GetName().Name.StartsWith("Microsoft.") ||
                        assembly.GetName().Name == "mscorlib")
                        continue;

                    // Look for exact type match
                    type = assembly.GetType(typeName);
                    if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type))
                        return type;

                    // Look for type with namespace
                    type = assembly.GetType(candidateFullName);
                    if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type))
                        return type;

                    // Try by simple name (ignoring namespace)
                    foreach (var t in assembly.GetTypes())
                    {
                        if (t.Name.Equals(typeName, StringComparison.OrdinalIgnoreCase) &&
                            typeof(UnityEngine.Object).IsAssignableFrom(t))
                            return t;
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error searching assembly {assembly.GetName().Name}: {ex.Message}");
                    continue;
                }
            }

            // If we're here, we couldn't find the type
            Debug.LogWarning($"[GameObject Tools] Failed to find type: {typeName}");
            return null;
        }

        private static bool IsMainThread()
        {
            return System.Threading.Thread.CurrentThread.ManagedThreadId == 1;
        }

        public static GameObjectData GetGameObjectData(GameObject gameObject)
        {
            if (gameObject == null)
                return new GameObjectData();

            Component[] components = gameObject.GetComponents<Component>();
            string[] componentNames = components.Where(c => c != null).Select(c => c.GetType().Name).ToArray();

            return new GameObjectData
            {
                Name = gameObject.name,
                InstanceID = gameObject.GetInstanceID(),
                ActiveSelf = gameObject.activeSelf,
                ActiveInHierarchy = gameObject.activeInHierarchy,
                Tag = gameObject.tag,
                Layer = gameObject.layer,
                IsStatic = gameObject.isStatic,
                Components = componentNames,
                Transform = new TransformData
                {
                    Position = new Vector3Data
                    {
                        X = gameObject.transform.localPosition.x,
                        Y = gameObject.transform.localPosition.y,
                        Z = gameObject.transform.localPosition.z
                    },
                    Rotation = new Vector3Data
                    {
                        X = gameObject.transform.localEulerAngles.x,
                        Y = gameObject.transform.localEulerAngles.y,
                        Z = gameObject.transform.localEulerAngles.z
                    },
                    Scale = new Vector3Data
                    {
                        X = gameObject.transform.localScale.x,
                        Y = gameObject.transform.localScale.y,
                        Z = gameObject.transform.localScale.z
                    }
                },
                Children = new GameObjectData[0] // Could populate children if needed
            };
        }

        #region Common Validation and Response Building

        [Serializable]
        public struct ComponentOperationResult
        {
            [JsonProperty("success")]
            public bool Success;

            [JsonProperty("message")]
            public string Message;

            [JsonProperty("gameObject")]
            public GameObjectData GameObject;

            [JsonProperty("error")]
            public string Error;

            [JsonProperty("component")]
            public Component Component;
        }

        public static (GameObject gameObject, string error) ValidateGameObject(int instanceId)
        {
#if UNITY_6000_3_OR_NEWER
            GameObject targetGo = EditorUtility.EntityIdToObject(instanceId) as GameObject;
#else
            GameObject targetGo = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif
            if (targetGo == null)
            {
                return (null, FormatGameObjectNotFoundMessage(instanceId));
            }

            return (targetGo, null);
        }

        public static void ValidateComponentName(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException(k_ComponentNameRequiredMessage);

            var componentType = FindType(componentName);
            if (componentType == null)
                throw new InvalidOperationException($"Component type '{componentName}' not found. Make sure it's a valid Unity component type.");

            if (!typeof(Component).IsAssignableFrom(componentType))
                throw new InvalidOperationException($"Type '{componentName}' is not a valid Component type.");
        }

        public static Component ValidateComponentName(string componentName, int componentInstanceId)
        {
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException(k_ComponentNameRequiredMessage);

#if UNITY_6000_3_OR_NEWER
            var targetComponent = EditorUtility.EntityIdToObject(componentInstanceId) as Component;
#else
            var targetComponent = EditorUtility.InstanceIDToObject(componentInstanceId) as Component;
#endif
            if (targetComponent == null)
                throw new InvalidOperationException($"No component found with instance ID {componentInstanceId}");

            var expectedType = FindType(componentName);
            if (expectedType != null && !expectedType.IsInstanceOfType(targetComponent))
            {
                throw new InvalidOperationException(
                    $"Component with instance ID {componentInstanceId} is of type {targetComponent.GetType().Name}, but expected {componentName}.");
            }

            return targetComponent;
        }


        public static ComponentOperationResult SetComponentPropertiesWithResult(
            Component component,
            JObject componentProperties,
            bool includeStackTrace = false)
        {
            try
            {
                SetComponentProperties(component, componentProperties);
                EditorUtility.SetDirty(component.gameObject);

                return new ComponentOperationResult
                {
                    Success = true,
                    Message = $"Properties set for component '{component.GetType().Name}' on '{component.gameObject.name}'.",
                    GameObject = GetGameObjectData(component.gameObject),
                    Component = component
                };
            }
            catch (Exception ex)
            {
                var error = includeStackTrace
                    ? $"Error setting component properties: {ex.Message}\n\nStack trace:\n{ex.StackTrace}"
                    : $"Error setting component properties: {ex.Message}";

                return new ComponentOperationResult
                {
                    Success = false,
                    Error = error
                };
            }
        }

        #endregion
    }
}
