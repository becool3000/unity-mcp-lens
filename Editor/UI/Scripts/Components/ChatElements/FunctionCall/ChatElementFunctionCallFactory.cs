using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    static class FunctionCallRendererFactory
    {
        static readonly Lazy<Dictionary<string, Type>> k_RendererMap = new (BuildRendererMap);
        static readonly Lazy<Dictionary<string, bool>> k_EmphasizedMap = new (BuildEmphasizedMap);

        public static IFunctionCallRenderer CreateFunctionCallRenderer(string functionId)
        {
            var map = k_RendererMap.Value;

            if (!map.TryGetValue(functionId, out var elementType))
                elementType = typeof(DefaultFunctionCallRenderer);

            return (IFunctionCallRenderer)Activator.CreateInstance(elementType)!;
        }

        public static bool IsEmphasized(string functionId)
        {
            if (string.IsNullOrEmpty(functionId))
                return false;

            return k_EmphasizedMap.Value.TryGetValue(functionId, out var emphasized) && emphasized;
        }

        static Dictionary<string, Type> BuildRendererMap()
        {
            var map = new Dictionary<string, Type>();

            var types = TypeCache.GetTypesDerivedFrom<IFunctionCallRenderer>();

            foreach (var type in types)
            {
                if (type.IsAbstract)
                    continue;

                foreach (var attr in type.GetCustomAttributes<FunctionCallRendererAttribute>())
                {
                    if (!map.TryAdd(attr.FunctionId, type))
                        throw new InvalidOperationException($"A renderer for {attr.FunctionId} is already registered");
                }
            }

            return map;
        }

        static Dictionary<string, bool> BuildEmphasizedMap()
        {
            var map = new Dictionary<string, bool>();

            var types = TypeCache.GetTypesDerivedFrom<IFunctionCallRenderer>();

            foreach (var type in types)
            {
                if (type.IsAbstract)
                    continue;

                foreach (var attr in type.GetCustomAttributes<FunctionCallRendererAttribute>())
                    map[attr.FunctionId] = attr.Emphasized;
            }

            return map;
        }
    }
}
