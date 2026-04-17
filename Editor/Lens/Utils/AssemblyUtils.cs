using System;
using System.Linq;
using System.Reflection;
#if UNITY_6000_5_OR_NEWER
using UnityEngine.Assemblies;
#endif

namespace Becool.UnityMcpLens.Editor.Utils
{
    static class AssemblyUtils
    {
        public static Assembly[] GetLoadedAssemblies()
        {
#if UNITY_6000_5_OR_NEWER
            return CurrentAssemblies.GetLoadedAssemblies().ToArray();
#else
            return AppDomain.CurrentDomain.GetAssemblies();
#endif
        }

        public static string GetAssemblyPath(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

#if UNITY_6000_5_OR_NEWER
            return type.Assembly.GetLoadedAssemblyPath();
#else
            return type.Assembly.Location;
#endif
        }

        public static string GetAssemblyPath(Assembly assembly)
        {
            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));

#if UNITY_6000_5_OR_NEWER
            return assembly.GetLoadedAssemblyPath();
#else
            return assembly.Location;
#endif
        }

        public static Assembly LoadFromBytes(byte[] assemblyBytes)
        {
            if (assemblyBytes == null)
                throw new ArgumentNullException(nameof(assemblyBytes));

#if UNITY_6000_5_OR_NEWER
            return CurrentAssemblies.LoadFromBytes(assemblyBytes);
#else
            return Assembly.Load(assemblyBytes);
#endif
        }
    }
}
