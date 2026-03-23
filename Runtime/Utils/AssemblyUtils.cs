using System;
using System.Reflection;
using System.Linq;
#if UNITY_6000_5_OR_NEWER && !UNITY_EDITOR
using UnityEngine.Assemblies;
#endif

namespace Unity.AI.Assistant.Runtime.Utils
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
        /// This method centralizes the UNITY_6000_5_OR_NEWER check to avoid
        /// preprocessor directives scattered throughout the codebase.
        /// </summary>
        /// <returns>Array of all loaded assemblies.</returns>
        public static Assembly[] GetLoadedAssemblies()
        {
#if UNITY_6000_5_OR_NEWER && !UNITY_EDITOR
            return CurrentAssemblies.GetLoadedAssemblies().ToArray();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }
    }
}
