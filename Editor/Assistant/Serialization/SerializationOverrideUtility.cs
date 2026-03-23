using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Serialization
{
    static partial class SerializationOverrideUtility
    {
        public static
            IEnumerable<(string declaringType, string field, Func<SerializedProperty, object>
                @override)> GetOverrideMethodsFromAttribute()
        {
            return TypeCache
                .GetMethodsWithAttribute<SerializationOverrideAttribute>()
                .Where(methodInfo => methodInfo.IsStatic)
                .Where(ValidateParameters)
                .Select(FormatOutput);

            bool ValidateParameters(MethodInfo methodInfo)
            {
                var parameters = methodInfo.GetParameters();
                return parameters.Length == 1
                    && parameters[0].ParameterType == typeof(SerializedProperty);
            }

            Func<SerializedProperty, object> ToFunc(MethodInfo methodInfo) =>
                property => methodInfo.Invoke(null, new object[] {property});

            (string declaringType, string field, Func<SerializedProperty, object> func)
                FormatOutput(MethodInfo methodInfo)
            {
                var attr = methodInfo.GetCustomAttribute<SerializationOverrideAttribute>();
                return (attr.DeclaringType, attr.Field, ToFunc(methodInfo));
            }
        }

        public static ISerializationOverride CreateOverride(
            string declaringType,
            string field,
            Func<SerializedProperty, object> @override) =>
            new SerializationOverride(declaringType, field, @override);
    }
}
