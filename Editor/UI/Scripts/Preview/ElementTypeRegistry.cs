using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    internal static class ElementTypeRegistry
    {
        static readonly Dictionary<string, Type> k_ElementTypes = new();
        static bool s_Initialized;

        static ElementTypeRegistry()
        {
            Initialize();
        }

        public static Type GetElementType(string tagName)
        {
            EnsureInitialized();
            return k_ElementTypes.TryGetValue(tagName.ToLower(), out var type) ? type : typeof(VisualElement);
        }

        static void EnsureInitialized()
        {
            if (!s_Initialized)
            {
                Initialize();
            }
        }

        static void Initialize()
        {
            var assemblies = GetUIToolkitAssemblies();

            DiscoverViaUxmlFactory(assemblies);
            DiscoverViaUxmlElementAttribute(assemblies);
            DiscoverViaVisualElementTypes(assemblies);

            s_Initialized = true;
        }

        static Assembly[] GetUIToolkitAssemblies()
        {
            return AssemblyUtils.GetLoadedAssemblies()
                .Where(a => IsUIToolkitAssembly(a.GetName().Name))
                .ToArray();
        }

        static bool IsUIToolkitAssembly(string name)
        {
            return name.Contains(PreviewConstants.UnityEngineUIElementsModule) ||
                   name.Contains(PreviewConstants.UnityEditorUIElementsModule) ||
                   name == PreviewConstants.UnityEngine ||
                   name == PreviewConstants.UnityEditor;
        }

        static void DiscoverViaUxmlFactory(Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                try
                {
                    var factoryTypes = assembly.GetTypes()
                        .Where(IsUxmlFactory)
                        .ToArray();

                    foreach (var factoryType in factoryTypes)
                    {
                        ProcessFactory(factoryType);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to scan assembly {assembly.GetName().Name}: {ex.Message}");
                }
            }
        }

        static bool IsUxmlFactory(Type type)
        {
            return type.Name.EndsWith(PreviewConstants.FactorySuffix) &&
                   !type.IsAbstract &&
                   !type.IsInterface &&
                   type.GetInterfaces().Any(i => i.Name.Contains(PreviewConstants.UxmlFactoryInterface));
        }

        static void ProcessFactory(Type factoryType)
        {
            try
            {
                var factory = Activator.CreateInstance(factoryType);
                var uxmlName = GetUxmlName(factory, factoryType);

                if (!string.IsNullOrEmpty(uxmlName))
                {
                    var elementType = GetElementTypeFromFactory(factoryType);
                    if (elementType != null)
                    {
                        RegisterElement(uxmlName, elementType);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to process factory {factoryType.Name}: {ex.Message}");
            }
        }

        static string GetUxmlName(object factory, Type factoryType)
        {
            var nameProperty = factoryType.GetProperty("uxmlName", BindingFlags.Public | BindingFlags.Instance);
            var nameField = factoryType.GetField("uxmlName", BindingFlags.Public | BindingFlags.Instance);

            return (nameProperty?.GetValue(factory) ?? nameField?.GetValue(factory)) as string;
        }

        static Type GetElementTypeFromFactory(Type factoryType)
        {
            var elementType = GetElementTypeFromGenericParameters(factoryType);
            return elementType ?? InferElementTypeFromFactoryName(factoryType);
        }

        static Type GetElementTypeFromGenericParameters(Type factoryType)
        {
            var baseType = factoryType.BaseType;
            while (baseType?.IsGenericType == true)
            {
                var genericArgs = baseType.GetGenericArguments();
                if (genericArgs.Length > 0 && genericArgs[0].IsSubclassOf(typeof(VisualElement)))
                {
                    return genericArgs[0];
                }
                baseType = baseType.BaseType;
            }
            return null;
        }

        static Type InferElementTypeFromFactoryName(Type factoryType)
        {
            if (!factoryType.Name.EndsWith(PreviewConstants.FactorySuffix)) return null;

            var elementName = factoryType.Name.Substring(0, factoryType.Name.Length - PreviewConstants.FactorySuffix.Length);
            return factoryType.Assembly.GetType($"{PreviewConstants.UnityEngineUIElements}.{elementName}") ??
                   factoryType.Assembly.GetType($"{PreviewConstants.UnityEditorUIElements}.{elementName}");
        }

        static void DiscoverViaUxmlElementAttribute(Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                try
                {
                    var typesWithAttribute = assembly.GetTypes()
                        .Where(HasUxmlElementAttribute)
                        .ToArray();

                    foreach (var type in typesWithAttribute)
                    {
                        ProcessUxmlElementType(type);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to scan assembly for UxmlElement: {assembly.GetName().Name}: {ex.Message}");
                }
            }
        }

        static bool HasUxmlElementAttribute(Type type)
        {
            return type.IsSubclassOf(typeof(VisualElement)) &&
                   !type.IsAbstract &&
                   type.GetCustomAttributes().Any(attr => attr.GetType().Name.Contains(PreviewConstants.UxmlElementAttribute));
        }

        static void ProcessUxmlElementType(Type type)
        {
            try
            {
                var uxmlElementAttr = type.GetCustomAttributes()
                    .FirstOrDefault(attr => attr.GetType().Name.Contains(PreviewConstants.UxmlElementAttribute));

                if (uxmlElementAttr != null)
                {
                    var nameProperty = uxmlElementAttr.GetType().GetProperty("name");
                    var uxmlName = nameProperty?.GetValue(uxmlElementAttr) as string ?? type.Name;

                    RegisterElement(uxmlName, type);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to process UxmlElement type {type.Name}: {ex.Message}");
            }
        }

        static void DiscoverViaVisualElementTypes(Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                try
                {
                    var visualElementTypes = assembly.GetTypes()
                        .Where(IsInstantiableVisualElement)
                        .ToArray();

                    foreach (var type in visualElementTypes)
                    {
                        RegisterElement(type.Name, type);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to scan assembly for VisualElement types: {assembly.GetName().Name}: {ex.Message}");
                }
            }
        }

        static bool IsInstantiableVisualElement(Type type)
        {
            return type.IsSubclassOf(typeof(VisualElement)) &&
                   !type.IsAbstract &&
                   type.IsPublic &&
                   type.GetConstructor(Type.EmptyTypes) != null;
        }

        static void RegisterElement(string name, Type type)
        {
            var key = name.ToLower();
            k_ElementTypes.TryAdd(key, type);
        }
    }
}
