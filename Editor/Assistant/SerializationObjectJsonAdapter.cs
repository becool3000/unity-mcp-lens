using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Editor.Serialization;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Helper class to serialize any kind of Unity Object into a JSON string
    /// This is for ingestion/human and machine readability instead of file save/load
    /// </summary>
    class SerializationObjectJsonAdapter : IJsonAdapter<SerializedObject>
    {
        public class SerializationException : Exception
        {
            public int Depth;
        }

        int m_ObjectDepth = 0;
        int m_MaxObjectDepth = -1;
        int m_CurrentDepth;
        int m_MaxPropertyDepth = -1; // Limits how deep to go into properties.
        int MaxDepth = -1; // Keeps track how deep the deepest property is.
        Stack<int> m_Depths = new Stack<int>();
        HashSet<long> m_VisitedObjects = new HashSet<long>();
        HashSet<long> m_VisitedNodes = new HashSet<long>();

        public static int JsonOutputLimit = 0;

        // Fields to ignore when comparing and serializing components, they can be inferred from the parent object:
        public static readonly string[] ComponentFieldsToIgnore = { "m_GameObject", "m_PrefabAsset" };

        /// <summary>
        /// If the type of the variable should be included when writing the key, which is otherwise just the name of the object
        /// </summary>
        public bool OutputType { get; set; } = false;

        /// <summary>
        /// If OutputType is false, types that are not clear from the field name are output if this is true.
        /// </summary>
        public bool OutputNonObviousTypes { get; set; } = false;

        /// <summary>
        /// If a tooltip related to the variable should be included when writing the key
        /// </summary>
        public bool OutputTooltip { get; set; } = false;

        public bool OutputDirectory { get; set; } = false;

        public int MaxObjectDepth { get => m_MaxObjectDepth; set => m_MaxObjectDepth = value; }
        public int MaxPropertyDepth { get => m_MaxPropertyDepth; set => m_MaxPropertyDepth = value; }

        public string[] RootParameters { get; set; }

        public SerializedObject RootObject { get; set; }
        public bool UseDisplayName { get; set; }

        public bool IgnorePrefabInstance { get; set; }

        public ISerializationOverrideProvider OverrideProvider { get; set; }

        /// <summary>
        /// Returns the key for the top level object
        /// </summary>
        /// <param name="value">A serialized object to retrieve the name from</param>
        /// <returns>The name for the serialized object, which includes the type and tooltip</returns>
        public string GetObjectKey(SerializedObject value, bool includeInstanceID)
        {
            var instanceID = includeInstanceID ? $"(Instance ID: {value.targetObject.GetInstanceID()})" : string.Empty;

            string name;
            if (OutputType)
            {
                name = $"{value.targetObject.name} {instanceID}\n- Type: {value.targetObject.GetType().Name}";
            }
            else
            {
                var space = instanceID.Length > 0 ? " " : string.Empty;
                name = $"{value.targetObject.name}{space}{instanceID}";
            }

            var directory = AssetDatabase.GetAssetPath(value.targetObject);
            return string.IsNullOrEmpty(directory) || !OutputDirectory ? name : $"{name}\n- Path: {directory}";
        }

        void IJsonAdapter<SerializedObject>.Serialize(in JsonSerializationContext<SerializedObject> context, SerializedObject value)
        {
            m_VisitedObjects.Add(value.targetObject.GetHashCode());

            // get iter
            // perform function on it
            // Each iter that has children makes new object scope
            // Go back to pure SO adapter
            var iter = value.GetIterator();
            iter.Next(true);
            m_CurrentDepth = iter.depth;

            m_ObjectDepth++;

            ProcessSerializedProperty(context, iter, true);

            m_ObjectDepth--;

            if (m_ObjectDepth == 0)
            {
                m_VisitedNodes.Clear();
                m_VisitedObjects.Clear();
            }
        }

        SerializedObject IJsonAdapter<SerializedObject>.Deserialize(in JsonDeserializationContext<SerializedObject> context)
        {
            throw new System.NotImplementedException();
        }

        void ProcessSerializedProperty(in JsonSerializationContext<SerializedObject> context, SerializedProperty current, bool newObject)
        {
            var writer = context.Writer;

            // Check if property has children
            // If it does, create new object scope
            // Go through children writing keys and values
            // If the child is a generic type, we'll end up back here

            if (newObject)
            {
                using (writer.WriteObjectScope())
                {
                    do
                    {
                        if(Validate(current))
                            ProcessSerializedPropertyInner(context, current, true);
                    } while (current.Next(false));
                }
            }
            else
            {
                do
                {
                    if(Validate(current))
                        ProcessSerializedPropertyInner(context, current, true);
                } while (current.Next(false));
            }

            return;

            bool Validate(SerializedProperty property)
            {
                if(m_MaxPropertyDepth >= 0 && m_ObjectDepth + property.depth > m_MaxPropertyDepth)
                    return false;

                MaxDepth = Math.Max(MaxDepth, m_ObjectDepth + property.depth);

                // We never want to serialize the m_GameObject field on components, it just leads to duplication:
                if (property.serializedObject.targetObject is Component &&
                    ComponentFieldsToIgnore.Contains(property.name))
                {
                    return false;
                }

                // Don't output everything from these types, it's too much data and will be in the file contents we send:
                if (RootObject.targetObject is VisualTreeAsset or Shader && !property.editable)
                {
                    return false;
                }

                // Never serialize massive arrays:
                if (property.isArray && property.arraySize > 1000)
                {
                    return false;
                }

                return RootObject != property.serializedObject || RootParameters is null
                                                               || RootParameters.Contains(property.name);
            }
        }

        void ProcessSerializedPropertyInner(in JsonSerializationContext<SerializedObject> context, SerializedProperty current, bool writeKey = false)
        {
            // If the output becomes really long, abort:
            if (JsonOutputLimit > 0 && context.Writer.AsUnsafe().Length > JsonOutputLimit)
            {
                m_ObjectDepth = 0;
                m_VisitedNodes.Clear();
                m_VisitedObjects.Clear();
                throw new SerializationException { Depth = MaxDepth };
            }

            if (current.depth < m_CurrentDepth)
            {
                return;
            }

            if (current.propertyType == SerializedPropertyType.ManagedReference && m_VisitedNodes.Contains(current.managedReferenceId))
            {
                return;
            }

            // Current.name is slow, get it once only:
            var currentName = current.name;

            if (currentName == "m_PrefabInstance" && IgnorePrefabInstance)
            {
                return;
            }

            var writer = context.Writer;
            if (writeKey)
            {
                var key = UseDisplayName ? current.displayName : currentName;
                var type = current.propertyType.ToString();

                if (current.propertyType == SerializedPropertyType.Generic && current.isArray)
                {
                    type = $"Array({PrettifyString(current.arrayElementType)})";
                }

                var shouldOutputType = OutputType;
                var useTypeAsKey = false;
                if (current.propertyType is SerializedPropertyType.ObjectReference
                    or SerializedPropertyType.ExposedReference)
                {
                    if (current.objectReferenceValue != null)
                    {
                        var referenceType = current.objectReferenceValue.GetType();
                        type = referenceType.Name;

                        if (!shouldOutputType && OutputNonObviousTypes)
                        {
                            shouldOutputType = referenceType != typeof(object) &&
                                               referenceType != typeof(Object) &&
                                               !currentName.ToLowerInvariant().Contains(type.ToLowerInvariant());

                            // For components, avoid writing things like "component - Transform" inside component arrays, just write the type:
                            if (shouldOutputType && current.propertyPath.Contains("Array") && referenceType.IsSubclassOf(typeof(Component)))
                                useTypeAsKey = true;
                        }
                    }
                    else
                    {
                        type = PrettifyString(current.type);
                    }
                }

                if (useTypeAsKey)
                    key = type;
                else if (shouldOutputType)
                    key += $" - {type}";

                if (OutputTooltip && !string.IsNullOrEmpty(current.tooltip))
                    key += $" - {current.tooltip}";

                // Override for GameObject's component list
                if (type == "Array(ComponentPair)")
                    key = "Components";

                writer.WriteKey(key);
            }

            m_CurrentDepth++;

            var serializationOverride = OverrideProvider.Find(
                current.serializedObject.targetObject.GetType().FullName, currentName);

            if (serializationOverride is not null)
            {
                var value = serializationOverride.Override(current);
                switch (value)
                {
                    case bool b:
                        writer.WriteValue(b);
                        break;
                    case char c:
                        writer.WriteValue(c);
                        break;
                    case double d:
                        SafeNumberWrite(writer, d);
                        break;
                    case float f:
                        SafeNumberWrite(writer, f);
                        break;
                    case int i:
                        writer.WriteValue(i);
                        break;
                    case long l:
                        writer.WriteValue(l);
                        break;
                    case string s:
                        writer.WriteValue(s);
                        break;
                    case uint ui:
                        writer.WriteValue(ui);
                        break;
                    case ulong ul:
                        writer.WriteValue(ul);
                        break;
                }
            }
            else
            {
                switch (current.propertyType)
                {
                    case SerializedPropertyType.Generic:
                    {
                        if (current.isArray)
                        {
                            using (writer.WriteArrayScope())
                            {
                                var length = current.arraySize;
                                for (var i = 0; i < length; i++)
                                {
                                    var arrayElement = current.GetArrayElementAtIndex(i);
                                    ProcessSerializedPropertyInner(context, arrayElement);
                                }
                            }
                        }
                        else
                        {
                            if (current.hasChildren)
                            {
                                var childProp = current.Copy();
                                childProp.Next(true);
                                ProcessSerializedProperty(context, childProp, true);
                            }
                            else
                                writer.WriteValue("Generic no children");
                        }
                    }
                        break;
                    case SerializedPropertyType.Integer:
                        writer.WriteValue(current.intValue);
                        break;
                    case SerializedPropertyType.Boolean:
                        writer.WriteValue(current.boolValue ? 1 : 0);
                        break;
                    case SerializedPropertyType.Float:
                        SafeNumberWrite(writer, current.floatValue);
                        break;
                    case SerializedPropertyType.String:
                        writer.WriteValue(current.stringValue);
                        break;
                    case SerializedPropertyType.Color:
                        writer.WriteValue(current.colorValue.ToString());
                        break;
                    case SerializedPropertyType.ObjectReference:
                    {
                        var objectReference = current.objectReferenceValue;

                        if (objectReference != null)
                        {
                            var instanceID = objectReference.GetInstanceID();

                            if (!m_VisitedObjects.Contains(instanceID))
                            {
                                if (m_MaxObjectDepth > -1 && m_ObjectDepth > m_MaxObjectDepth)
                                {
                                    context.SerializeValue($"{objectReference.name}");
                                }
                                else
                                {
                                    m_Depths.Push(m_CurrentDepth);
                                    var SO = new SerializedObject(objectReference);
                                    context.SerializeValue(SO);
                                    m_CurrentDepth = m_Depths.Pop();
                                }
                            }
                            else
                            {
                                context.SerializeValue(
                                    $"Already serialized - {objectReference.name} ({objectReference.GetInstanceID()})");
                            }
                        }
                        else
                        {
                            writer.WriteValue("null");
                        }
                    }
                        break;
                    case SerializedPropertyType.LayerMask:
                        writer.WriteValue(current.intValue);
                        break;
                    case SerializedPropertyType.Enum:
                    {
                        if (current.enumValueIndex >= 0 && current.enumValueIndex < current.enumDisplayNames.Length)
                        {
                            writer.WriteValue(current.enumDisplayNames[current.enumValueIndex]);
                        }
                        else
                        {
                            writer.WriteValue(current.enumValueFlag);
                        }
                    }
                        break;
                    case SerializedPropertyType.Vector2:
                        writer.WriteValue(current.vector2Value.ToString("F2"));
                        break;
                    case SerializedPropertyType.Vector3:
                        writer.WriteValue(current.vector3Value.ToString("F2"));
                        break;
                    case SerializedPropertyType.Vector4:
                        writer.WriteValue(current.vector4Value.ToString("F2"));
                        break;
                    case SerializedPropertyType.Rect:
                        writer.WriteValue(current.rectValue.ToString());
                        break;
                    case SerializedPropertyType.ArraySize:
                        writer.WriteValue(current.intValue);
                        break;
                    case SerializedPropertyType.Character:
                        // Todo: Better support
                        writer.WriteValue($"Character - {current.boxedValue}");
                        break;
                    case SerializedPropertyType.AnimationCurve:
                        // Todo: Better support
                        writer.WriteValue($"Animation curve - {current.animationCurveValue}");
                        break;
                    case SerializedPropertyType.Bounds:
                        writer.WriteValue($"{current.boundsValue}");
                        break;
                    case SerializedPropertyType.Gradient:
                        // Todo: Better support
                        writer.WriteValue($"Gradient - {current.gradientValue}");
                        break;
                    case SerializedPropertyType.Quaternion:
                        writer.WriteValue(current.quaternionValue.ToString("F2"));
                        break;
                    case SerializedPropertyType.ExposedReference:
                    {
                        var objectReference = current.objectReferenceValue;
                        if (objectReference != null)
                        {
                            var instanceID = objectReference.GetInstanceID();
                            if (!m_VisitedObjects.Contains(instanceID))
                            {
                                if (m_MaxObjectDepth > -1 && m_ObjectDepth > m_MaxObjectDepth)
                                {
                                    context.SerializeValue($"{objectReference.name}");
                                }
                                else
                                {
                                    m_Depths.Push(m_CurrentDepth);
                                    var SO = new SerializedObject(objectReference);
                                    context.SerializeValue(SO);
                                    m_CurrentDepth = m_Depths.Pop();
                                }
                            }
                            else
                            {
                                context.SerializeValue(
                                    $"Already serialized - {objectReference.name} ({objectReference.GetInstanceID()})");
                            }
                        }
                        else
                        {
                            writer.WriteValue("null");
                        }
                    }
                        break;
                    case SerializedPropertyType.FixedBufferSize:
                        writer.WriteValue(current.intValue);
                        break;
                    case SerializedPropertyType.Vector2Int:
                        writer.WriteValue(current.vector2IntValue.ToString());
                        break;
                    case SerializedPropertyType.Vector3Int:
                        writer.WriteValue(current.vector3IntValue.ToString());
                        break;
                    case SerializedPropertyType.RectInt:
                        writer.WriteValue(current.rectIntValue.ToString());
                        break;
                    case SerializedPropertyType.BoundsInt:
                        writer.WriteValue(current.boundsIntValue.ToString());
                        break;
                    case SerializedPropertyType.ManagedReference:
                    {
                        var refId = current.managedReferenceId;
                        var visited = false;

                        if (!m_VisitedNodes.Contains(refId))
                        {
                            m_VisitedNodes.Add(current.managedReferenceId);
                            if (current.hasChildren)
                            {
                                visited = true;
                                var childProp = current.Copy();
                                childProp.Next(true);
                                ProcessSerializedProperty(context, childProp, true);
                            }
                        }

                        if (!visited)
                        {
                            var boxedValue = current.boxedValue;
                            writer.WriteValue($"Managed reference ID: {boxedValue}");
                        }

                    }
                        break;
                    case SerializedPropertyType.Hash128:
                        writer.WriteValue(current.hash128Value.ToString());
                        break;
                    default:
                        writer.WriteValue($"unsupported - {current.propertyType}");
                        break;
                }
            }

            m_CurrentDepth--;
        }

        static void SafeNumberWrite(JsonWriter writer, float value)
        {
            if (float.IsFinite(value))
                writer.WriteValue(value);
            else
                writer.WriteValue(value.ToString());
        }

        static void SafeNumberWrite(JsonWriter writer, double value)
        {
            if (double.IsFinite(value))
                writer.WriteValue(value);
            else
                writer.WriteValue(value.ToString());
        }

        static string PrettifyString(string toPrettify)
        {
            if (toPrettify.StartsWith("PPtr<"))
                return toPrettify.Substring(5, toPrettify.Length - 6);

            return toPrettify;
        }
    }
}
