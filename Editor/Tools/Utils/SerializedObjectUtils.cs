using System;
using System.Collections.Generic;
using Unity.Serialization.Json;
using UnityEditor;

namespace Unity.AI.Assistant.Tools.Editor
{
    internal static class SerializedObjectUtils
    {
        const int k_MaxSerializationDepthSearch = 8;

        /// <summary>
        /// Returns a string summary of the given object
        /// </summary>
        /// <param name="serializedObject">The serialized object to serialize to Json</param>
        /// <param name="propertyPath">A path to a specific property. Empty or null otherwise.</param>
        /// <param name="useDisplayName">Write field using their beautified display name.</param>
        /// <param name="maxDepth">Maximum property depth (inclusive).</param>
        /// <param name="maxArrayElements">Max number of elements to write for array properties. -1 for no limitation.</param>
        /// <param name="maxLength">The maximum character count of the output. Best depth will be chosen to maximize information while staying below this count (except for depth 0). -1 for not limitation.</param>
        /// <returns>A string summary of the given object and its components</returns>
        public static (string , int) ToJson(this SerializedObject serializedObject, string propertyPath = null, bool useDisplayName = false, int maxDepth = -1, int maxArrayElements = -1, int maxLength = -1)
        {
            if (serializedObject == null)
                return (string.Empty, -1);

            var objectAdapter = new SerializedObjectJsonAdapter();

            var propertyAdapter = new SerializedPropertyJsonAdapter();
            propertyAdapter.UseDisplayName = useDisplayName;
            propertyAdapter.MaxDepth = maxDepth;
            propertyAdapter.MaxArrayElements = maxArrayElements;
            propertyAdapter.MaxLength = maxLength;

            var parameters = new JsonSerializationParameters
            {
                DisableSerializedReferences = true,
                UserDefinedAdapters = new List<IJsonAdapter>
                {
                    objectAdapter,
                    propertyAdapter
                },
                Minified = true
            };

            SerializedProperty serializedProperty = null;
            if (!string.IsNullOrEmpty(propertyPath))
            {
                var correctedPropertyPath = SerializedPropertyUtils.ConvertToUnityPropertyPath(propertyPath);
                serializedProperty = serializedObject.FindProperty(correctedPropertyPath);
                if (serializedProperty == null)
                    throw new ArgumentException($"Property {propertyPath} not found.");
            }

            var depthSearchMax = maxDepth > 0 ? maxDepth : k_MaxSerializationDepthSearch;

            var (json, depth, found) = BinarySearchUtils.BinarySearch(
                generate: GetJson,
                minSearchValue: 0,
                maxSearchValue: depthSearchMax
            );

            return (json, depth);

            // Helper method to use typed serialization methods (using a generic object would not work)
            string GetJson(int depth)
            {
                propertyAdapter.MaxDepth = depth;
                return serializedProperty != null ?
                    JsonSerialization.ToJson(serializedProperty, parameters) :
                    JsonSerialization.ToJson(serializedObject, parameters);
            }
        }
    }
}
