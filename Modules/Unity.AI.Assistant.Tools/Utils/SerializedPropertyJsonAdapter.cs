using System;
using System.Text;
using Unity.Serialization.Json;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Tools.Editor
{
    class SerializedPropertyJsonAdapter : IJsonAdapter<SerializedProperty>
    {
        public class MaxLengthException : Exception
        {
            public int Length;
        }

        public int MaxArrayElements { get; set; } = -1;
        public int MaxDepth { get; set; } = -1;
        public int MaxLength { get; set; } = -1;
        public bool UseDisplayName { get; set; } = false;
        public bool IndicateDepthTruncation { get; set; } = true;

        public void Serialize(in JsonSerializationContext<SerializedProperty> context, SerializedProperty value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            // If this is the root object, we need an object scope
            if (context.Writer.AsUnsafe().Length == 0)
            {
                using (context.Writer.WriteObjectScope())
                {
                    WriteProperty(context, value, 0, true);
                }
            }
            else
            {
                WriteProperty(context, value, 0, true);
            }
        }

        public SerializedProperty Deserialize(in JsonDeserializationContext<SerializedProperty> context)
        {
            throw new NotImplementedException();
        }

        void WriteProperty(in JsonSerializationContext<SerializedProperty> context, SerializedProperty prop, int depth, bool includeName)
        {
            if (MaxDepth >= 0 && depth > MaxDepth)
                return;

            if (depth > 0 && MaxLength > 0)
            {
                var currentLength = context.Writer.AsUnsafe().Length;
                if (currentLength > MaxLength)
                    throw new MaxLengthException { Length = currentLength };
            }

            if (includeName)
                context.Writer.WriteKey(UseDisplayName ? prop.displayName : prop.name);

            // None: strings are also considered arrays of chars
            if (prop.isArray && prop.propertyType != SerializedPropertyType.String)
            {
                WriteArrayProperty(context, prop, depth);
                return;
            }

            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    context.Writer.WriteValue(prop.intValue);
                    break;

                case SerializedPropertyType.Boolean:
                    context.Writer.WriteValue(prop.boolValue);
                    break;

                case SerializedPropertyType.Float:
                    SafeWrite(context.Writer, prop.floatValue);
                    break;

                case SerializedPropertyType.String:
                    context.Writer.WriteValue(prop.stringValue);
                    break;

                case SerializedPropertyType.Color:
                    context.Writer.WriteValue(prop.colorValue.ToString());
                    break;

                case SerializedPropertyType.ObjectReference:
                    WriteObjectReference(context, prop.objectReferenceValue);
                    break;

                case SerializedPropertyType.Enum:
                    WriteEnumValue(context, prop);
                    break;

                case SerializedPropertyType.Vector2:
                    context.Writer.WriteValue(prop.vector2Value.ToString("F2"));
                    break;

                case SerializedPropertyType.Vector3:
                    context.Writer.WriteValue(prop.vector3Value.ToString("F2"));
                    break;

                case SerializedPropertyType.Vector4:
                    context.Writer.WriteValue(prop.vector4Value.ToString("F2"));
                    break;

                case SerializedPropertyType.Rect:
                    context.Writer.WriteValue(prop.rectValue.ToString());
                    break;

                case SerializedPropertyType.Quaternion:
                    context.Writer.WriteValue(prop.quaternionValue.ToString("F2"));
                    break;

                case SerializedPropertyType.LayerMask:
                    WriteLayerMask(context, prop);
                    break;

                case SerializedPropertyType.ArraySize:
                    context.Writer.WriteValue(prop.intValue);
                    break;

                case SerializedPropertyType.Character:
                    context.Writer.WriteValue(Convert.ToChar(prop.intValue).ToString());
                    break;

                case SerializedPropertyType.AnimationCurve:
                    context.Writer.WriteValue("(Type: AnimationCurve)");
                    break;

                case SerializedPropertyType.Bounds:
                    context.Writer.WriteValue(prop.boundsValue.ToString("F2"));
                    break;

                case SerializedPropertyType.Gradient:
                    context.Writer.WriteValue("(Type: Gradient)");
                    break;

                case SerializedPropertyType.ExposedReference:
                    WriteObjectReference(context, prop.exposedReferenceValue);
                    break;

                case SerializedPropertyType.FixedBufferSize:
                    context.Writer.WriteValue(prop.intValue);
                    break;

                case SerializedPropertyType.Vector2Int:
                    context.Writer.WriteValue(prop.vector2IntValue.ToString());
                    break;

                case SerializedPropertyType.Vector3Int:
                    context.Writer.WriteValue(prop.vector3IntValue.ToString());
                    break;

                case SerializedPropertyType.RectInt:
                    context.Writer.WriteValue(prop.rectIntValue.ToString());
                    break;

                case SerializedPropertyType.BoundsInt:
                    context.Writer.WriteValue(prop.boundsIntValue.ToString());
                    break;

                case SerializedPropertyType.ManagedReference:
                    WriteManagedReference(context, prop);
                    break;

                case SerializedPropertyType.Hash128:
                    context.Writer.WriteValue(prop.hash128Value.ToString());
                    break;

                case SerializedPropertyType.RenderingLayerMask:
                    WriteRenderingLayerMask(context, prop);
                    break;

                case SerializedPropertyType.Generic:
                default:
                    WriteGenericProperty(context, prop, depth);
                    break;
            }

            return;
        }

        void WriteManagedReference(in JsonSerializationContext<SerializedProperty> context, SerializedProperty prop)
        {
            context.Writer.WriteValue($"(Type: {prop.managedReferenceFieldTypename}, ID: {prop.managedReferenceId})");
        }

        void WriteGenericProperty(in JsonSerializationContext<SerializedProperty> context, SerializedProperty prop, int depth)
        {
            if (prop == null || !prop.hasChildren)
            {
                context.Writer.WriteNull();
                return;
            }

            // If the children properties are being truncated
            if (MaxDepth >= 0 && depth == MaxDepth && prop.hasChildren)
            {
                context.Writer.WriteValue("...");
                return;
            }

            // Specific case for ComponentPair
            var isComponentPair = prop.type == "ComponentPair";

            using (context.Writer.WriteObjectScope())
            {
                var iterator = prop.Copy();
                var endProp = iterator.GetEndProperty();
                var enterChildren = true;
                while (iterator.Next(enterChildren) && !SerializedProperty.EqualContents(iterator, endProp))
                {
                    // Specific case for ComponentPair to avoid recursing on components
                    if (isComponentPair && iterator.name == "data")
                        continue;

                    WriteProperty(context, iterator, depth + 1, true);
                    enterChildren = false;
                }
            }
        }

        void WriteLayerMask(in JsonSerializationContext<SerializedProperty> context, SerializedProperty prop)
        {
            var mask = prop.intValue;

            var sb = new StringBuilder();
            var first = true;

            // Unity supports up to 32 layers
            for (var i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    if (!first)
                        sb.Append(" | ");
                    else
                        first = false;

                    var layerName = LayerMask.LayerToName(i);
                    if (string.IsNullOrEmpty(layerName))
                        layerName = "Layer" + i;
                    sb.Append($"{layerName} ({i})");
                }
            }

            var maskString = sb.Length > 0 ? sb.ToString() : "None";
            context.Writer.WriteValue(maskString);
        }

        void WriteRenderingLayerMask(in JsonSerializationContext<SerializedProperty> context, SerializedProperty prop)
        {
            var mask = prop.intValue;

            var sb = new StringBuilder();
            var first = true;

            // Unity supports up to 32 layers
            for (var i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    if (!first)
                        sb.Append(" | ");
                    else
                        first = false;

                    var layerName = i < RenderingLayerMask.GetRenderingLayerCount() ? RenderingLayerMask.RenderingLayerToName(i) : null;
                    if (string.IsNullOrEmpty(layerName))
                        layerName = "Layer" + i;
                    sb.Append($"{layerName} ({i})");
                }
            }

            var maskString = sb.Length > 0 ? sb.ToString() : "None";
            context.Writer.WriteValue(maskString);
        }

        void WriteEnumValue(in JsonSerializationContext<SerializedProperty> context, SerializedProperty prop)
        {
            // Mixed value
            if (prop.enumValueIndex == -1)
            {
                var value = prop.enumValueFlag;
                if (value == 0)
                {
                    context.Writer.WriteValue("None");
                }
                else
                {
                    var sb = new StringBuilder();
                    var first = true;

                    for (var i = 0; i < prop.enumNames.Length; i++)
                    {
                        if ((value & (1 << i)) != 0)
                        {
                            if (!first)
                                sb.Append(" | ");
                            sb.Append(prop.enumNames[i]);
                            first = false;
                        }
                    }
                    context.Writer.WriteValue(sb.ToString());
                }
            }
            else
            {
                context.Writer.WriteValue(prop.enumNames[prop.enumValueIndex]);
            }
        }

        void WriteObjectReference(in JsonSerializationContext<SerializedProperty> context, Object value)
        {
            if (value == null)
            {
                context.Writer.WriteNull();
                return;
            }

            var name = value.name;
            var typeName = GetTypeName(value.GetType());
            var instanceID = value.GetInstanceID();

            context.Writer.WriteValue($"(Name: {name}, Type: {typeName}, InstanceID: {instanceID})");
        }

        void WriteArrayProperty(in JsonSerializationContext<SerializedProperty> context, SerializedProperty value, int depth)
        {
            using (context.Writer.WriteArrayScope())
            {
                var numElements = MaxArrayElements >= 0 ? Mathf.Min(value.arraySize, MaxArrayElements) : value.arraySize;
                for (var i = 0; i < numElements; ++i)
                {
                    var element = value.GetArrayElementAtIndex(i);
                    WriteProperty(context, element, depth + 1, false);
                }

                if (MaxArrayElements >= 0 && value.arraySize > MaxArrayElements)
                    context.Writer.WriteValue("... (truncated)");
            }
        }

        static void SafeWrite(JsonWriter writer, float value)
        {
            if (float.IsFinite(value))
                writer.WriteValue(value);
            else
                writer.WriteValue(value.ToString());
        }

        static string GetTypeName(Type type)
        {
            return type?.Name ?? "(null)";
        }
    }
}
