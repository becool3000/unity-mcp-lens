using System;
using System.Linq;
using System.Reflection;
#if UNITY_6000_5_OR_NEWER
using UnityEngine.Assemblies;
#endif

namespace Unity.AI.Generators.Utils
{
    /// <summary>
    /// Utility methods for working with assemblies in a CoreCLR-compatible way.
    /// NOTE: This class can be removed once Unity 6000.5+ is the minimum supported version.
    /// At that point, call the CoreCLR-compatible APIs directly instead.
    /// </summary>
    static class AssemblyUtils
    {
        /// <summary>
        /// Gets all loaded assemblies in a CoreCLR-compatible way.
        /// </summary>
        public static Assembly[] GetLoadedAssemblies()
        {
#if UNITY_6000_5_OR_NEWER
            return CurrentAssemblies.GetLoadedAssemblies().ToArray();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        /// <summary>
        /// Loads an assembly from a file path in a CoreCLR-compatible way.
        /// </summary>
        public static Assembly LoadFromPath(string assemblyPath)
        {
            if (string.IsNullOrEmpty(assemblyPath))
                throw new ArgumentNullException(nameof(assemblyPath));

#if UNITY_6000_5_OR_NEWER
            return CurrentAssemblies.LoadFromPath(assemblyPath);
#else
            var assemblyName = AssemblyName.GetAssemblyName(assemblyPath);
            return Assembly.Load(assemblyName);
#endif
        }
    }
}
