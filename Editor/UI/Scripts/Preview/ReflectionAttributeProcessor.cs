using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    /// <summary>
    /// Reflection-based attribute processor that dynamically discovers and applies UXML attributes
    /// to VisualElement instances using Unity's property system and naming conventions.
    /// </summary>
    internal static class ReflectionAttributeProcessor
    {
        static readonly Dictionary<(Type, string), PropertyInfo> k_PropertyCache = new();
        static readonly Dictionary<(Type, string), FieldInfo> k_FieldCache = new();

        public static void ApplyAttribute(VisualElement element, string attributeName, string attributeValue)
        {
            if (element == null || string.IsNullOrEmpty(attributeName) || attributeValue == null)
            {
                return;
            }

            try
            {
                if (TryApplyBuiltInAttribute(element, attributeName, attributeValue))
                {
                    return;
                }

                if (TryApplyReflectedProperty(element, attributeName, attributeValue))
                {
                    return;
                }

                Debug.LogWarning($"Unsupported UXML attribute: {attributeName}='{attributeValue}' on {element.GetType().Name}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error applying attribute {attributeName}='{attributeValue}' to {element.GetType().Name}: {ex.Message}");
            }
        }

        static bool TryApplyBuiltInAttribute(VisualElement element, string attributeName, string attributeValue)
        {
            switch (attributeName.ToLowerInvariant())
            {
                case "name":
                    element.name = attributeValue;
                    return true;

                case "class":
                    ApplyClasses(element, attributeValue);
                    return true;

                case "tooltip":
                    element.tooltip = attributeValue;
                    return true;

                case "style":
                    ApplyBasicInlineStyle(element, attributeValue);
                    return true;


                default:
                    return false;
            }
        }

        static bool TryApplyReflectedProperty(VisualElement element, string attributeName, string attributeValue)
        {
            var elementType = element.GetType();
            var propertyNames = GetPropertyNameVariations(attributeName);

            foreach (var propertyName in propertyNames)
            {
                var property = GetCachedProperty(elementType, propertyName);
                if (property != null && property.CanWrite)
                {
                    if (TrySetProperty(element, property, attributeValue))
                    {
                        return true;
                    }
                }

                var field = GetCachedField(elementType, propertyName);
                if (field != null && !field.IsInitOnly)
                {
                    if (TrySetField(element, field, attributeValue))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        static List<string> GetPropertyNameVariations(string attributeName)
        {
            var variations = new List<string> { attributeName };

            if (attributeName.Length > 0)
            {
                variations.Add(char.ToUpper(attributeName[0]) + attributeName.Substring(1));
            }

            if (attributeName.Contains("-"))
            {
                var camelCase = ConvertToCamelCase(attributeName);
                variations.Add(camelCase);
                if (camelCase.Length > 0)
                {
                    variations.Add(char.ToUpper(camelCase[0]) + camelCase.Substring(1));
                }
            }

            switch (attributeName.ToLowerInvariant())
            {
                case "text":
                    variations.AddRange(new[] { "value", "Value" });
                    break;
                case "value":
                    variations.AddRange(new[] { "text", "Text" });
                    break;
                case "src":
                    variations.AddRange(new[] { "image", "Image", "sprite", "Sprite", "texture", "Texture" });
                    break;
                case "placeholder-text":
                    variations.AddRange(new[] { "placeholderText", "PlaceholderText" });
                    break;
                case "picking-mode":
                    variations.AddRange(new[] { "pickingMode", "PickingMode" });
                    break;
            }

            return variations;
        }

        static string ConvertToCamelCase(string hyphenated)
        {
            var parts = hyphenated.Split('-');
            var result = parts[0].ToLowerInvariant();

            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length > 0)
                {
                    result += char.ToUpper(parts[i][0]) + parts[i].Substring(1).ToLowerInvariant();
                }
            }

            return result;
        }

        static PropertyInfo GetCachedProperty(Type type, string propertyName)
        {
            var key = (type, propertyName);
            if (k_PropertyCache.TryGetValue(key, out var cached))
                return cached;

            var property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            k_PropertyCache[key] = property;
            return property;
        }

        static FieldInfo GetCachedField(Type type, string fieldName)
        {
            var key = (type, fieldName);
            if (k_FieldCache.TryGetValue(key, out var cached))
                return cached;

            var field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            k_FieldCache[key] = field;
            return field;
        }

        static bool TrySetProperty(VisualElement element, PropertyInfo property, string value)
        {
            try
            {
                var propertyType = property.PropertyType;
                var convertedValue = ConvertValue(value, propertyType);

                if (convertedValue != null)
                {
                    property.SetValue(element, convertedValue);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to set property {property.Name}: {ex.Message}");
            }

            return false;
        }

        static bool TrySetField(VisualElement element, FieldInfo field, string value)
        {
            try
            {
                var fieldType = field.FieldType;
                var convertedValue = ConvertValue(value, fieldType);

                if (convertedValue != null)
                {
                    field.SetValue(element, convertedValue);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to set field {field.Name}: {ex.Message}");
            }

            return false;
        }

        static object ConvertValue(string value, Type targetType)
        {
            return StyleTypeConverters.ConvertValue(value, targetType);
        }

        static void ApplyClasses(VisualElement element, string classValue)
        {
            var classes = classValue.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var className in classes)
            {
                element.AddToClassList(className.Trim());
            }
        }

        static void ApplyBasicInlineStyle(VisualElement element, string styleValue)
        {
            var properties = styleValue.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var property in properties)
            {
                var parts = property.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var propName = parts[0].Trim().ToLowerInvariant();
                    var propValue = parts[1].Trim();
                    ApplyBasicStyleProperty(element, propName, propValue);
                }
            }
        }

        static void ApplyBasicStyleProperty(VisualElement element, string property, string value)
        {
            if (TryApplyShorthandProperty(element, property, value))
                return;

            var propertyVariations = GetStylePropertyNameVariations(property);

            foreach (var propName in propertyVariations)
            {
                var styleProperty = typeof(IStyle).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (styleProperty != null && styleProperty.CanWrite)
                {
                    try
                    {
                        var convertedValue = ConvertValue(value, styleProperty.PropertyType);
                        if (convertedValue != null)
                        {
                            styleProperty.SetValue(element.style, convertedValue);
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to set style property {propName}: {ex.Message}");
                    }
                }
            }

            Debug.LogWarning($"Unsupported inline style property: {property}");
        }

        static List<string> GetStylePropertyNameVariations(string ussProperty)
        {
            var variations = new List<string>();

            variations.Add(ussProperty);

            var camelCase = ConvertToCamelCase(ussProperty);
            variations.Add(camelCase);

            if (camelCase.Length > 0)
            {
                variations.Add(char.ToUpper(camelCase[0]) + camelCase.Substring(1));
            }

            return variations;
        }

        static bool TryApplyShorthandProperty(VisualElement element, string property, string value)
        {
            switch (property.ToLowerInvariant())
            {
                case "margin":
                    return TryApplyToMultipleStyleProperties(element, value,
                        "marginTop", "marginRight", "marginBottom", "marginLeft");

                case "padding":
                    return TryApplyToMultipleStyleProperties(element, value,
                        "paddingTop", "paddingRight", "paddingBottom", "paddingLeft");

                case "border-width":
                    return TryApplyToMultipleStyleProperties(element, value,
                        "borderTopWidth", "borderRightWidth", "borderBottomWidth", "borderLeftWidth");

                case "border-color":
                    return TryApplyToMultipleStyleProperties(element, value,
                        "borderTopColor", "borderRightColor", "borderBottomColor", "borderLeftColor");

                case "border-radius":
                    return TryApplyToMultipleStyleProperties(element, value,
                        "borderTopLeftRadius", "borderTopRightRadius", "borderBottomLeftRadius", "borderBottomRightRadius");

                default:
                    return false;
            }
        }

        static bool TryApplyToMultipleStyleProperties(VisualElement element, string value, params string[] propertyNames)
        {
            bool anySucceeded = false;

            foreach (var propName in propertyNames)
            {
                var styleProperty = typeof(IStyle).GetProperty(propName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                if (styleProperty != null && styleProperty.CanWrite)
                {
                    try
                    {
                        var convertedValue = ConvertValue(value, styleProperty.PropertyType);
                        if (convertedValue != null)
                        {
                            styleProperty.SetValue(element.style, convertedValue);
                            anySucceeded = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Failed to set shorthand style property {propName}: {ex.Message}");
                    }
                }
            }


            return anySucceeded;
        }

    }
}
