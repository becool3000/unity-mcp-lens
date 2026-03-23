using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.AI.Assistant.Runtime.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.FunctionCalling
{
    class AttributeBasedFunctionSource : IFunctionSource
    {
        /// <summary>
        ///     <see cref="AgentToolAttribute"/> that meet the requirements for being an agent tool.
        /// </summary>
        public LocalAssistantFunction[] GetFunctions()
        {
            var methods = GetMethodsWithAttribute<AgentToolAttribute>()
                .Where(methodInfo =>
                {
                    if (!methodInfo.IsStatic)
                    {
                        Debug.LogWarning(
                            $"Method \"{methodInfo.Name}\" in \"{methodInfo.DeclaringType?.FullName}\" failed" +
                            $"validation. This means it does not have the appropriate function signature for" +
                            $"the given attribute {nameof(AgentToolAttribute)}");
                        return false;
                    }

                    return true;
                })
                .Select(method =>
                {
                    var attribute = method.GetCustomAttribute<AgentToolAttribute>();

                    var parameters = method.GetParameters();
                    return new LocalAssistantFunction(
                        method,
                        attribute.Description,
                        attribute.Id,
                        attribute.AssistantMode,
                        attribute.Tags,
                        attribute.ToolCallEnvironment
                    );
                })
                .ToArray();

            // Checks all tool IDs are unique
            var uniqueIds = new HashSet<string>();
            foreach (var cachedFunction in methods)
            {
                var toolId = cachedFunction.FunctionDefinition.FunctionId;
                if (!uniqueIds.Add(toolId))
                    Debug.LogError($"Tool ID '{cachedFunction.FunctionDefinition.FunctionId}' should be unique.");
            }

            return methods;

            static IEnumerable<MethodInfo> GetMethodsWithAttribute<T>() where T : Attribute
            {
#if UNITY_EDITOR
                return UnityEditor.TypeCache.GetMethodsWithAttribute<T>();
#else
                return AssemblyUtils.GetLoadedAssemblies()
                    .SelectMany(assembly => assembly.GetTypes())
                    .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                        BindingFlags.Static | BindingFlags.Instance))
                    .Where(method => method.GetCustomAttribute<T>() != null);
#endif
            }
        }
    }
}
