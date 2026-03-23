#if !UNITY_6000_3_OR_NEWER
using System;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Bridge.Editor
{
    static partial class UssValidator
    {
        static Type s_StyleSheetImporterImplType;
        static MethodInfo s_ImportMethod;
        static bool s_ReflectionInitialized;
        static bool s_ReflectionAvailable;

        static bool InitializeReflection()
        {
            if (s_ReflectionInitialized)
                return s_ReflectionAvailable;

            s_ReflectionInitialized = true;

            try
            {
                var uiElementsEditorAssembly = typeof(UnityEditor.UIElements.ColorField).Assembly;

                s_StyleSheetImporterImplType = uiElementsEditorAssembly.GetType("UnityEditor.UIElements.StyleSheets.StyleSheetImporterImpl");

                if (s_StyleSheetImporterImplType == null)
                {
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        s_StyleSheetImporterImplType = assembly.GetType("UnityEditor.UIElements.StyleSheets.StyleSheetImporterImpl");
                        if (s_StyleSheetImporterImplType != null)
                            break;
                    }
                }

                if (s_StyleSheetImporterImplType != null)
                {
                    s_ImportMethod = s_StyleSheetImporterImplType.GetMethod("Import",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(StyleSheet), typeof(string) },
                        null);
                }

                s_ReflectionAvailable = s_StyleSheetImporterImplType != null && s_ImportMethod != null;
                return s_ReflectionAvailable;
            }
            catch (Exception)
            {
                s_ReflectionAvailable = false;
                return false;
            }
        }

        internal static string ValidateUss(string content)
        {
            if (!InitializeReflection())
            {
                return ValidateUssBasic(content);
            }

            var errorCollector = new StringBuilder();

            try
            {
                var importer = Activator.CreateInstance(s_StyleSheetImporterImplType);
                var stylesheet = ScriptableObject.CreateInstance<StyleSheet>();

                try
                {
                    s_ImportMethod.Invoke(importer, new object[] { stylesheet, content });

                    var errorsProperty = s_StyleSheetImporterImplType.GetProperty("importErrors",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    if (errorsProperty != null)
                    {
                        var errors = errorsProperty.GetValue(importer);
                        if (errors != null)
                        {
                            var enumerableErrors = errors as System.Collections.IEnumerable;
                            if (enumerableErrors != null)
                            {
                                foreach (var error in enumerableErrors)
                                {
                                    errorCollector.AppendLine($"USS import error: {error}");
                                }
                            }
                        }
                    }

                    return errorCollector.ToString();
                }
                catch (TargetInvocationException ex)
                {
                    return $"Exception during import: {ex.InnerException?.Message ?? ex.Message}";
                }
                catch (Exception ex)
                {
                    return $"Exception during import: {ex.Message}\n{ex.StackTrace}";
                }
                finally
                {
                    Object.DestroyImmediate(stylesheet);
                }
            }
            catch (Exception ex)
            {
                return $"Exception creating importer: {ex.Message}";
            }
        }

        static string ValidateUssBasic(string content)
        {
            var errors = new StringBuilder();

            try
            {
                int braceCount = 0;
                int lineNumber = 1;
                for (int i = 0; i < content.Length; i++)
                {
                    char c = content[i];
                    if (c == '\n') lineNumber++;
                    else if (c == '{') braceCount++;
                    else if (c == '}')
                    {
                        braceCount--;
                        if (braceCount < 0)
                        {
                            errors.AppendLine($"USS syntax error: Unexpected '}}' at line {lineNumber}");
                            braceCount = 0;
                        }
                    }
                }

                if (braceCount > 0)
                {
                    errors.AppendLine($"USS syntax error: Missing {braceCount} closing brace(s)");
                }
            }
            catch (Exception ex)
            {
                errors.AppendLine($"Exception during basic validation: {ex.Message}");
            }

            return errors.ToString();
        }
    }
}
#endif
