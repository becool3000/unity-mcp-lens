using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor.Knowledge
{
    /// <summary>
    /// Static service for mapping Unity asset types to their corresponding descriptors.
    /// Uses cached instances for performance while maintaining a functional interface.
    /// </summary>
    static class AssetDescriptorResolver
    {
        static readonly Dictionary<Type, AssetDescriptor> k_DescriptorMap;
        static readonly HashSet<Type> k_DeclaredTypes = new HashSet<Type>();

        static AssetDescriptorResolver()
        {
            k_DescriptorMap = BuildDescriptorMap();
        }

        static Dictionary<Type, AssetDescriptor> BuildDescriptorMap()
        {
            var map = new Dictionary<Type, AssetDescriptor>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<AssetDescriptor>())
            {
                if (type.IsAbstract)
                    continue;

                var baseType = type.BaseType;
                if (baseType == null)
                    continue;

                if (baseType.GetGenericTypeDefinition() == typeof(AssetDescriptorBase<>))
                {
                    var genericArg = baseType.GetGenericArguments()[0];

                    var instance = (AssetDescriptor)Activator.CreateInstance(type);
                    map[genericArg] = instance;
                    k_DeclaredTypes.Add(genericArg);
                }
            }

            return map;
        }

        /// <summary>
        /// Gets the appropriate descriptor for the given asset object.
        /// </summary>
        /// <param name="assetObject">The asset object to get a descriptor for</param>
        /// <returns>The corresponding descriptor, or null if no mapping exists</returns>
        public static AssetDescriptor GetDescriptor(Object assetObject) =>
            assetObject != null ? GetDescriptor(assetObject.GetType()) : null;

        public static AssetDescriptor GetDescriptor<T>() where T : Object
        {
            return GetDescriptor(typeof(T));
        }

        static AssetDescriptor GetDescriptor(Type assetType)
        {
            if (assetType == null) return null;

            // Exact match
            if (k_DescriptorMap.TryGetValue(assetType, out var direct))
                return direct;

            // Walk up the inheritance chain to find the most specific mapped type
            for (var current = assetType; current != null; current = current.BaseType)
            {
                if (k_DescriptorMap.TryGetValue(current, out var descriptor))
                {
                    // Cache the resolved descriptor into the main map to fill blanks
                    k_DescriptorMap[assetType] = descriptor;
                    return descriptor;
                }
            }
            return null;
        }

        public static bool HasDescriptor(Object assetObject) => GetDescriptor(assetObject) != null;

        /// <summary>
        /// Returns a deterministic signature string that reflects the current descriptor mapping and versions.
        /// When this string changes between editor sessions, it indicates descriptors were added/removed or versions changed.
        /// </summary>
        public static string BuildSignature()
        {
            // Only include explicitly declared mappings to keep signature deterministic
            return string.Join("|",
                k_DeclaredTypes
                    .OrderBy(t => t.FullName)
                    .Select(t => $"{t.FullName}:{(k_DescriptorMap.TryGetValue(t, out var d) ? d.Version : "none")}"));
        }
    }
}
