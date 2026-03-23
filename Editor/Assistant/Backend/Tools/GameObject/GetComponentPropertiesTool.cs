using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEditor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetComponentPropertiesTool
    {
        internal const string k_FunctionId = "Unity.GameObject.GetComponentProperties";
        internal const string k_ComponentNameRequiredMessage = "'componentName' parameter is required.";

        internal static string FormatComponentTypeNotFoundMessage(string componentName)
        {
            return $"Component type '{componentName}' not found. Make sure it's a valid Unity component type.";
        }

        internal static string FormatFallbackMessage(string componentName, string gameObjectName)
        {
            return $"GameObject '{gameObjectName}' does not have a '{componentName}' component yet, generic '{componentName}' component properties. Use AddComponent to add this component to the GameObject.";
        }

        internal static string FormatComponentInstanceTypeMismatchMessage(int componentInstanceId, string actualType, string expectedName)
        {
            return $"Component with instance ID {componentInstanceId} is of type {actualType}, but expected {expectedName}";
        }

        [Serializable]
        public struct ComponentPropertyData
        {
            [JsonProperty("name")]
            public string Name;

            [JsonProperty("type")]
            public string Type;

            [JsonProperty("typeDescription")]
            public string TypeDescription;

            [JsonProperty("currentValue")]
            public string CurrentValue;

            [JsonProperty("canWrite")]
            public bool CanWrite;

            [JsonProperty("description")]
            public string Description;
        }

        [Serializable]
        public struct ComponentInfo
        {
            [JsonProperty("componentType")]
            public string ComponentType;

            [JsonProperty("instanceId")]
            public int InstanceId;

            [JsonProperty("properties")]
            public ComponentPropertyData[] Properties;
        }

        [Serializable]
        public struct GetComponentPropertiesOutput
        {
            [JsonProperty("message")]
            public string Message;

            [JsonProperty("gameObjectName")]
            public string GameObjectName;

            [JsonProperty("components")]
            public ComponentInfo[] Components;
        }

        [AgentTool(
            "Get all settable/public properties of a component. Can get generic properties for a component type, or actual property values from a specific GameObject instance.",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_GameObjectTag)]
        public static GetComponentPropertiesOutput GetComponentProperties(
            [Parameter("Name or type of the component to inspect (e.g., 'Transform', 'Rigidbody', 'MeshRenderer'). Returns inspector-visible properties only.")]
            string componentName,
            [Parameter("Optional GameObject instance ID (e.g. 12345). If provided, returns actual property values from the component on this specific GameObject instance. If not provided, returns generic default values for the component type. Default: null.")]
            int? gameObjectInstanceId = null,
            [Parameter("Optional Component instance ID (e.g. 67890). If provided, returns actual property values from this specific component instance. Takes priority over gameObjectInstanceId if both are provided. Default: null.")]
            int? componentInstanceId = null)
        {
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException(k_ComponentNameRequiredMessage);

            GameObjectToolsHelper.ValidateComponentName(componentName);

            if (componentInstanceId.HasValue)
                return GetComponentPropertiesFromComponentId(componentName, componentInstanceId.Value);

            if (!gameObjectInstanceId.HasValue)
                return GetGenericComponentProperties(componentName);

            return GetGameObjectComponentProperties(gameObjectInstanceId.Value, componentName);
        }

        static GetComponentPropertiesOutput GetComponentPropertiesFromComponentId(string componentName, int componentInstanceId)
        {
#if UNITY_6000_3_OR_NEWER
            var component = EditorUtility.EntityIdToObject(componentInstanceId) as Component;
#else
            var component = EditorUtility.InstanceIDToObject(componentInstanceId) as Component;
#endif
            if (component == null)
                throw new InvalidOperationException(GameObjectToolsHelper.FormatComponentNotFoundMessage(componentInstanceId));

            var expectedType = GameObjectToolsHelper.FindType(componentName);
            if (expectedType != null && !expectedType.IsInstanceOfType(component))
            {
                throw new InvalidOperationException(
                    FormatComponentInstanceTypeMismatchMessage(componentInstanceId, component.GetType().Name, componentName));
            }

            var properties = GetSettableProperties(component);
            var gameObject = component.gameObject;

            return new GetComponentPropertiesOutput
            {
                GameObjectName = gameObject.name,
                Components = new[]
                {
                    new ComponentInfo
                    {
                        ComponentType = component.GetType().Name,
                        InstanceId = component.GetInstanceID(),
                        Properties = properties
                    }
                }
            };
        }

        private static GetComponentPropertiesOutput GetGenericComponentProperties(string componentName)
        {
            if (string.IsNullOrEmpty(componentName))
                throw new ArgumentException(k_ComponentNameRequiredMessage);

            var componentType = GameObjectToolsHelper.FindType(componentName);
            if (componentType == null)
            {
                throw new InvalidOperationException(
                    FormatComponentTypeNotFoundMessage(componentName));
            }

            var properties = GetGenericSettableProperties(componentType);

            return new GetComponentPropertiesOutput
            {
                GameObjectName = "N/A (Generic Component Properties)",
                Components = new[]
                {
                    new ComponentInfo
                    {
                        ComponentType = componentType.Name,
                        InstanceId = -1,
                        Properties = properties
                    }
                }
            };
        }

        [ToolPermissionIgnore]  // Used to ignore the AddComponent permission
        static ComponentPropertyData[] GetGenericSettableProperties(Type componentType)
        {
            var properties = new List<ComponentPropertyData>();

            // Create a temporary GameObject to get SerializedObject
            var tempGO = new GameObject("TempForGenericProperties");

            try
            {
                Component component;

                // Handle special case for Transform which is always present
                if (componentType == typeof(Transform))
                {
                    component = tempGO.transform;
                }
                else
                {
                    component = tempGO.AddComponent(componentType);
                    if (component == null)
                    {
                        throw new Exception($"Could not add component {componentType.Name}");
                    }
                }

                // Use the same GetSettableProperties method that handles instances
                // This ensures consistency between generic and instance property retrieval
                var settableProperties = GetSettableProperties(component);

                // Convert to generic values
                foreach (var prop in settableProperties)
                {
                    properties.Add(new ComponentPropertyData
                    {
                        Name = prop.Name,
                        Type = prop.Type,
                        TypeDescription = prop.TypeDescription,
                        CurrentValue = prop.CurrentValue,
                        CanWrite = prop.CanWrite,
                        Description = prop.Description
                    });
                }
            }
            finally
            {
                // Clean up temporary GameObject
                UnityEngine.Object.DestroyImmediate(tempGO);
            }

            return properties.ToArray();
        }


        private static GetComponentPropertiesOutput GetGameObjectComponentProperties(int instanceId, string componentName)
        {
#if UNITY_6000_3_OR_NEWER
            GameObject targetGo = EditorUtility.EntityIdToObject(instanceId) as GameObject;
#else
            GameObject targetGo = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
#endif
            if (targetGo == null)
            {
                throw new InvalidOperationException(
                    $"GameObject with instance ID '{instanceId}' not found. Make sure the GameObject ID is valid.");
            }

            var matchingComponents = FindComponentsByName(targetGo, componentName);
            if (!matchingComponents.Any())
            {
                Debug.Log($"Component '{componentName}' not found on GameObject '{targetGo.name}'. Returning generic component properties instead.");

                var genericResult = GetGenericComponentProperties(componentName);
                genericResult.Message = FormatFallbackMessage(componentName, targetGo.name);
                genericResult.GameObjectName = targetGo.name;
                return genericResult;
            }

            var componentInfos = new List<ComponentInfo>();
            foreach (var component in matchingComponents)
            {
                var properties = GetSettableProperties(component);
                componentInfos.Add(new ComponentInfo
                {
                    ComponentType = component.GetType().Name,
                    InstanceId = component.GetInstanceID(),
                    Properties = properties
                });
            }

            return new GetComponentPropertiesOutput
            {
                GameObjectName = targetGo.name,
                Components = componentInfos.ToArray()
            };
        }

        static List<Component> FindComponentsByName(GameObject gameObject, string componentName)
        {
            var matchingComponents = new List<Component>();
            var components = gameObject.GetComponents<Component>();

            foreach (var component in components)
            {
                if (component == null)
                    continue;

                var componentType = component.GetType();

                // Try exact name match first
                if (componentType.Name.Equals(componentName, StringComparison.OrdinalIgnoreCase) ||
                    componentType.FullName.Equals(componentName, StringComparison.OrdinalIgnoreCase) ||
                    componentType.Name.Contains(componentName, StringComparison.OrdinalIgnoreCase))
                {
                    matchingComponents.Add(component);
                }
            }

            return matchingComponents;
        }



        static ComponentPropertyData? CreatePropertyDataFromSerializedProperty(SerializedProperty property, Component component = null)
        {
            try
            {
                var propertyName = property.name;
                var propertyType = GetSerializedPropertyTypeName(property);
                var currentValue = GetSerializedPropertyValue(property);

                var typeDescription = GetSerializedPropertyTypeDescription(property);

                return new ComponentPropertyData
                {
                    Name = propertyName,
                    Type = propertyType,
                    TypeDescription = typeDescription,
                    CurrentValue = currentValue,
                    CanWrite = property.editable,
                    Description = property.tooltip
                };
            }
            catch
            {
                return null;
            }
        }

        static string GetSerializedPropertyTypeName(SerializedProperty property)
        {
            // Use reflection to get the 'type' property
            // Check if it's a vector (array) type
            if (property.type == "vector")
            {
                // Use reflection to get arrayElementType
                var arrayElementTypeProperty = property.arrayElementType;
                if (arrayElementTypeProperty != null)
                {
                    var elementType = property.arrayElementType;
                    if (!string.IsNullOrEmpty(elementType))
                    {
                        // Convert PPtr<T> to T (ObjectReference)
                        if (elementType.StartsWith("PPtr<") && elementType.EndsWith(">"))
                        {
                            var typeName = elementType.Substring(5, elementType.Length - 6);
                            return $"Array<{typeName} (ObjectReference)>";
                        }
                        return $"Array<{elementType}>";
                    }
                }
                return "Array";
            }

            return property.propertyType.ToString();
        }



        static string GetSerializedPropertyTypeDescription(SerializedProperty property)
        {
            var propertyName = property.name;
            var propertyType = property.propertyType;

            // Handle arrays specially
            if (property.isArray && propertyType == SerializedPropertyType.Generic)
            {
                var elementType = property.arrayElementType;
                if (!string.IsNullOrEmpty(elementType))
                {
                    // Handle PPtr<T> types (object references)
                    if (elementType.StartsWith("PPtr<") && elementType.EndsWith(">"))
                    {
                        var objectType = elementType.Substring(5, elementType.Length - 6);
                        return $"Array of {objectType} references. Set as array of object references: [{{\"fileID\": instanceId1}}, {{\"fileID\": instanceId2}}, ...] where instanceIds come from asset searches. Empty array [] to clear all.";
                    }
                    return $"Array of {elementType} values.";
                }
            }

            // Handle ObjectReference type (Unity asset references)
            if (propertyType == SerializedPropertyType.ObjectReference)
            {
                return $"ObjectReference property. Important: Do NOT set to string values like 'Cube' or asset names. Must use object reference format: {{\"fileID\": instanceId}} where instanceId comes from asset searches, or set to null to clear.";
            }

            // Handle other specific property types
            switch (propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return "Boolean value (true/false)";
                case SerializedPropertyType.Integer:
                    return "Integer numeric value";
                case SerializedPropertyType.Float:
                    return "Floating point numeric value";
                case SerializedPropertyType.String:
                    return "Text string value";
                case SerializedPropertyType.Vector2:
                    return "2D vector object with x, y properties. Use format: {\"x\": 0.0, \"y\": 0.0}";
                case SerializedPropertyType.Vector3:
                    return "3D vector object with x, y, z properties. Use format: {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0}";
                case SerializedPropertyType.Vector4:
                    return "4D vector object with x, y, z, w properties. Use format: {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0, \"w\": 0.0}";
                case SerializedPropertyType.Color:
                    return "Color object with r, g, b, a properties. Use format: {\"r\": 1.0, \"g\": 1.0, \"b\": 1.0, \"a\": 1.0}";
                case SerializedPropertyType.Enum:
                    return $"Enumeration value. Available options: {string.Join(", ", property.enumDisplayNames)}";
                case SerializedPropertyType.LayerMask:
                    return "Layer mask for collision detection";
                case SerializedPropertyType.Quaternion:
                    return "Rotation quaternion object with x, y, z, w properties. Use format: {\"x\": 0.0, \"y\": 0.0, \"z\": 0.0, \"w\": 1.0}";
                case SerializedPropertyType.Rect:
                    return "Rectangle (x, y, width, height)";
                case SerializedPropertyType.Bounds:
                    return "3D bounds (center and size)";
                default:
                    return $"Serialized property: {propertyName}";
            }
        }

        static string GetSerializedPropertyValue(SerializedProperty property)
        {
            try
            {
                // Handle arrays specially
                if (property.isArray && property.propertyType == SerializedPropertyType.Generic)
                {
                    var arraySize = property.arraySize;
                    if (arraySize == 0)
                        return "[]";

                    // For large arrays, just show count
                    if (arraySize > 10)
                        return $"Array[{arraySize}]";

                    // For small arrays, show element details
                    var elements = new List<string>();
                    for (int i = 0; i < Math.Min(arraySize, 5); i++)
                    {
                        var element = property.GetArrayElementAtIndex(i);
                        if (element.propertyType == SerializedPropertyType.ObjectReference)
                        {
                            var obj = element.objectReferenceValue;
                            elements.Add(obj != null ? $"{obj.name}" : "None");
                        }
                        else
                        {
                            elements.Add(GetSerializedPropertyValue(element));
                        }
                    }

                    if (arraySize > 5)
                    {
                        elements.Add($"... +{arraySize - 5} more");
                    }

                    return $"[{string.Join(", ", elements)}]";
                }

                switch (property.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        return property.intValue.ToString();
                    case SerializedPropertyType.Boolean:
                        return property.boolValue.ToString().ToLower();
                    case SerializedPropertyType.Float:
                        return property.floatValue.ToString("F2");
                    case SerializedPropertyType.String:
                        return $"\"{property.stringValue}\"";
                    case SerializedPropertyType.Color:
                        var c = property.colorValue;
                        return AssistantJsonHelper.Serialize(new { r = Math.Round(c.r, 2), g = Math.Round(c.g, 2), b = Math.Round(c.b, 2), a = Math.Round(c.a, 2) });
                    case SerializedPropertyType.ObjectReference:
                        return property.objectReferenceValue?.name ?? "None";
                    case SerializedPropertyType.LayerMask:
                        return property.intValue.ToString();
                    case SerializedPropertyType.Enum:
                        return property.enumDisplayNames[property.enumValueIndex];
                    case SerializedPropertyType.Vector2:
                        var v2 = property.vector2Value;
                        return AssistantJsonHelper.Serialize(new { x = Math.Round(v2.x, 2), y = Math.Round(v2.y, 2) });
                    case SerializedPropertyType.Vector3:
                        var v3 = property.vector3Value;
                        return AssistantJsonHelper.Serialize(new { x = Math.Round(v3.x, 2), y = Math.Round(v3.y, 2), z = Math.Round(v3.z, 2) });
                    case SerializedPropertyType.Vector4:
                        var v4 = property.vector4Value;
                        return AssistantJsonHelper.Serialize(new { x = Math.Round(v4.x, 2), y = Math.Round(v4.y, 2), z = Math.Round(v4.z, 2), w = Math.Round(v4.w, 2) });
                    case SerializedPropertyType.Rect:
                        var r = property.rectValue;
                        return $"({r.x:F2}, {r.y:F2}, {r.width:F2}, {r.height:F2})";
                    case SerializedPropertyType.ArraySize:
                        return $"Array[{property.intValue}]";
                    case SerializedPropertyType.Character:
                        return property.intValue.ToString();
                    case SerializedPropertyType.AnimationCurve:
                        return property.animationCurveValue?.keys.Length.ToString() ?? "0";
                    case SerializedPropertyType.Bounds:
                        var b = property.boundsValue;
                        return $"Center: ({b.center.x:F2}, {b.center.y:F2}, {b.center.z:F2}), Size: ({b.size.x:F2}, {b.size.y:F2}, {b.size.z:F2})";
                    case SerializedPropertyType.Quaternion:
                        var q = property.quaternionValue;
                        return AssistantJsonHelper.Serialize(new { x = Math.Round(q.x, 2), y = Math.Round(q.y, 2), z = Math.Round(q.z, 2), w = Math.Round(q.w, 2) });
                    case SerializedPropertyType.Generic:
                        // Generic could be various things including structs
                        return "<complex>";
                    default:
                        return "<unknown>";
                }
            }
            catch
            {
                return "<error>";
            }
        }

        static ComponentPropertyData[] GetSettableProperties(Component component)
        {
            var properties = new List<ComponentPropertyData>();

            // Use SerializedObject approach - this is what Unity's Inspector uses
            var serializedObject = new SerializedObject(component);
            serializedObject.Update();

            // Get iterator and enter the first level to access properties
            var serializedProperty = serializedObject.GetIterator();

            // Enter children on the first call to get to the actual properties
            if (serializedProperty.NextVisible(enterChildren: true))
            {
                do
                {
                    // Skip script property (m_Script) as it's not typically editable
                    if (!serializedProperty.editable)
                    {
                        continue;
                    }
                    if (serializedProperty.name == "m_Script") continue;

                    try
                    {
                        var propertyData = CreatePropertyDataFromSerializedProperty(serializedProperty, component);
                        if (propertyData.HasValue)
                        {
                            properties.Add(propertyData.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Error processing property {serializedProperty.name}: {ex.Message}");
                    }
                }
                while (serializedProperty.NextVisible(enterChildren: false));
            }

            return properties.ToArray();
        }
    }
}
