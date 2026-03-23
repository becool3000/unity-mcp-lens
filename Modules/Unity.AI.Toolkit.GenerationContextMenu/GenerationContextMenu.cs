using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.GenerationContextMenu
{
    /// <summary>
    /// Use this attribute to register a method to be called when the user selects the "Generate" context menu item.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    class GenerateContextMenuAttribute : Attribute
    {
        /// <summary>
        /// The name of the method that will be called to validate if the context menu item should be enabled.
        /// This is a required parameter.
        /// </summary>
        internal string ValidateFunctionName { get; }

        /// <summary>
        /// The name of the method that will be called to check if the selected objects have generation history.
        /// This is an optional parameter.
        /// </summary>
        internal string HasGenerationsFunctionName { get; }

        /// <summary>
        /// Use this attribute to register a method to be called when the user selects the "Generate" context menu item.
        /// </summary>
        /// <param name="validateFunction">
        /// The name of the method that will be called to validate if the context menu item should be enabled.
        /// This is a required parameter.
        /// </param>
        /// <param name="hasGenerationsFunction">
        /// The name of the method that will be called to check if the selected objects have generation history.
        /// This is an optional parameter.
        /// </param>
        public GenerateContextMenuAttribute(string validateFunction, string hasGenerationsFunction = null)
        {
            ValidateFunctionName = validateFunction;
            HasGenerationsFunctionName = hasGenerationsFunction;
        }
    }

    [InitializeOnLoad, EditorBrowsable(EditorBrowsableState.Never)]
    static class GenerationContextMenu
    {
        static readonly List<(Action action, Func<bool> validateFunction, Func<bool> hasGenerationsFunction)> k_GenerateContextMenuActions = new();

        static GenerationContextMenu()
        {
            foreach (var methodInfo in TypeCache.GetMethodsWithAttribute(typeof(GenerateContextMenuAttribute)))
            {
                var attribute = methodInfo.GetCustomAttribute<GenerateContextMenuAttribute>();
                if (attribute == null)
                    continue;

                var action = (Action)Delegate.CreateDelegate(typeof(Action), null, methodInfo);
                if (string.IsNullOrEmpty(attribute.ValidateFunctionName))
                    throw new InvalidOperationException($"Validate function name is not provided for {methodInfo.DeclaringType!.Name}.{methodInfo.Name}");
                var validateFunction = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), methodInfo.DeclaringType!, attribute.ValidateFunctionName);

                Func<bool> hasGenerationsFunction = null;
                if (!string.IsNullOrEmpty(attribute.HasGenerationsFunctionName))
                    hasGenerationsFunction = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), methodInfo.DeclaringType!, attribute.HasGenerationsFunctionName);

                k_GenerateContextMenuActions.Add((action, validateFunction, hasGenerationsFunction));
            }
        }

        [MenuItem("Assets/Generate %g", false, 1)]
        static void Generate()
        {
            foreach (var (action, validateFunction, _) in k_GenerateContextMenuActions)
            {
                if (validateFunction())
                {
                    action();
                    return;
                }
            }
        }

        [MenuItem("Assets/Generate %g", true)]
        static bool ValidateGenerate()
        {
            foreach (var (_, validateFunction, hasGenerationsFunction) in k_GenerateContextMenuActions)
            {
                if (!validateFunction())
                    continue;

                if (hasGenerationsFunction == null)
                {
                    // If no hasGenerationsFunction provided, just test AiGeneratorsEnabled as before
                    if (Account.settings.AiGeneratorsEnabled)
                        return true;
                }
                else
                {
                    // If hasGenerationsFunction is provided, use the same logic as inspector button
                    if (Account.settings.AiGeneratorsEnabled || hasGenerationsFunction.Invoke())
                        return true;
                }
            }

            return false;
        }
    }
}
