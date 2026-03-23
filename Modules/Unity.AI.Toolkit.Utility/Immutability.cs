using System;
using System.Collections.Generic;
using System.Reflection;

namespace Unity.AI.Toolkit.Utility
{
    /// <summary>
    /// APIs that help with providing state immutability
    /// </summary>
    static class Immutability
    {
        static readonly Dictionary<Type, ConstructorInfo> k_ConstructorCache = new();
        static readonly Dictionary<Type, MethodInfo> k_CloneMethodCache = new();

        public static T Clone<T>(T original)
        {
            if (original is ICloneable cloneable)
                return (T)cloneable.Clone();

            return CloneObjectByCopy(original);
        }

        /// <summary>
        /// Clone a state, ideally by using its copy constructor.
        /// Fairly generic and not extremely performant, but provides some level of immutability.
        /// </summary>
        /// <param name="original"></param>
        /// <returns></returns>
        public static T CloneObjectByCopy<T>(T original)
        {
            var result = CloneObjectByCopy(original);
            return result ?? original;
        }

        public static object CloneObjectByCopy(object original)
        {
            if (original == null)
                return null;

            Type type = original.GetType();
            ConstructorInfo copyConstructor;
            if (!k_ConstructorCache.TryGetValue(type, out copyConstructor))
            {
                copyConstructor = type.GetConstructor(new[] { type });
                k_ConstructorCache[type] = copyConstructor;
            }

            if (copyConstructor == null)
                return CloneObjectByMembers(original);

            return copyConstructor.Invoke(new[] { original });
        }

        public static object CloneObjectByMembers(object original)
        {
            if (original == null)
                return null;

            MethodInfo cloneMethod;
            Type type = original.GetType();
            if (!k_CloneMethodCache.TryGetValue(type, out cloneMethod))
            {
                cloneMethod = type.GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
                k_CloneMethodCache[type] = cloneMethod;
            }

            if (cloneMethod == null)
                throw new InvalidOperationException("Could not find the MemberwiseClone method.");

            return cloneMethod.Invoke(original, null);
        }
    }
}
