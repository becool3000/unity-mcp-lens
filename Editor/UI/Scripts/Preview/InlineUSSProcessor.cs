using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    /// <summary>
    /// A USS processor that directly applies styles to elements using reflection to discover all available IStyle properties.
    /// This approach bypasses Unity's internal StyleSheet system and applies styles directly.
    /// </summary>
    internal class InlineUSSProcessor
    {
        // Regex patterns for parsing USS
        static readonly Regex k_RulePattern = new(PreviewConstants.UssRulePattern, RegexOptions.Multiline | RegexOptions.Compiled);
        static readonly Regex k_PropertyPattern = new(PreviewConstants.UssPropertyPattern, RegexOptions.Multiline | RegexOptions.Compiled);
        static readonly Regex k_UssVariablePattern = new(@"var\(--([^,)]+)(?:,([^)]+))?\)", RegexOptions.Compiled);

        // Cache for reflection data
        static Dictionary<string, PropertyInfo> s_StyleProperties;

        // USS variables storage
        Dictionary<string, string> m_UssVariables = new();

        static InlineUSSProcessor()
        {
            InitializeReflectionCache();
        }

        /// <summary>
        /// Apply USS styles directly to a visual element and its descendants
        /// </summary>
        public void ApplyStylesToElement(VisualElement rootElement, string ussContent)
        {
            if (string.IsNullOrEmpty(ussContent) || rootElement == null)
                return;

            try
            {
                ussContent = RemoveComments(ussContent);

                // First pass: extract USS variables from :root
                ExtractUssVariables(ussContent);

                var ruleMatches = k_RulePattern.Matches(ussContent);

                foreach (Match ruleMatch in ruleMatches)
                {
                    var selectorText = ruleMatch.Groups[1].Value.Trim();
                    var propertiesText = ruleMatch.Groups[2].Value.Trim();

                    if (string.IsNullOrEmpty(selectorText) || string.IsNullOrEmpty(propertiesText))
                        continue;

                    foreach (var selector in selectorText.Split(','))
                    {
                        var sel = selector.Trim();
                        if (string.IsNullOrEmpty(sel) || sel == ":root")
                            continue;

                        ApplyRuleToElements(rootElement, sel, propertiesText);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to apply USS styles: {ex.Message}");
            }
        }

        void ExtractUssVariables(string ussContent)
        {
            var ruleMatches = k_RulePattern.Matches(ussContent);

            foreach (Match ruleMatch in ruleMatches)
            {
                var selectorText = ruleMatch.Groups[1].Value.Trim();
                var propertiesText = ruleMatch.Groups[2].Value.Trim();

                if (selectorText == ":root")
                {
                    var propertyMatches = k_PropertyPattern.Matches(propertiesText);
                    foreach (Match propertyMatch in propertyMatches)
                    {
                        var propertyName = propertyMatch.Groups[1].Value.Trim();
                        var propertyValue = propertyMatch.Groups[2].Value.Trim();

                        if (propertyName.StartsWith("--"))
                        {
                            m_UssVariables[propertyName] = propertyValue;
                        }
                    }
                    break;
                }
            }
        }

        string SubstituteUssVariables(string value)
        {
            return k_UssVariablePattern.Replace(value, match =>
            {
                var variableName = "--" + match.Groups[1].Value.Trim();
                var fallbackValue = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";

                if (m_UssVariables.TryGetValue(variableName, out var variableValue))
                {
                    return variableValue;
                }

                return string.IsNullOrEmpty(fallbackValue) ? match.Value : fallbackValue;
            });
        }

        static void InitializeReflectionCache()
        {
            s_StyleProperties = new Dictionary<string, PropertyInfo>();

            var styleProperties = typeof(IStyle).GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in styleProperties)
            {
                if (prop.CanWrite)
                {
                    s_StyleProperties[prop.Name.ToLowerInvariant()] = prop;

                    var kebabName = ConvertToKebabCase(prop.Name);
                    if (kebabName != prop.Name.ToLowerInvariant())
                    {
                        s_StyleProperties[kebabName] = prop;
                    }
                }
            }


        }

        static string RemoveComments(string ussContent)
        {
            return Regex.Replace(ussContent, PreviewConstants.UssCommentPattern, "", RegexOptions.Singleline);
        }

        void ApplyRuleToElements(VisualElement rootElement, string selectorText, string propertiesText)
        {
            try
            {
                var matchingElements = FindElementsBySelector(rootElement, selectorText);
                if (matchingElements.Count == 0)
                {
                    return;
                }

                var propertyMatches = k_PropertyPattern.Matches(propertiesText);
                foreach (var element in matchingElements)
                {
                    foreach (Match propertyMatch in propertyMatches)
                    {
                        var propertyName = propertyMatch.Groups[1].Value.Trim().ToLowerInvariant();
                        var propertyValue = propertyMatch.Groups[2].Value.Trim();

                        // Substitute USS variables before applying the property
                        propertyValue = SubstituteUssVariables(propertyValue);

                        ApplyStyleProperty(element, propertyName, propertyValue);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to apply rule '{selectorText}': {ex.Message}");
            }
        }

        static List<VisualElement> FindElementsBySelector(VisualElement rootElement, string selector)
        {
            var results = new List<VisualElement>();
            selector = selector.Trim();

            // Check for descendant selectors (space-separated)
            if (selector.Contains(' '))
            {
                return FindElementsByDescendantSelector(rootElement, selector);
            }

            if (selector.StartsWith("."))
            {
                var classNames = selector.Substring(1).Split('.', StringSplitOptions.RemoveEmptyEntries);

                if (classNames.Length == 1)
                {
                    rootElement.Query(className: classNames[0]).ForEach(element => results.Add(element));
                }
                else
                {
                    // Start with elements that have the first class to get a smaller initial set
                    var candidateElements = new List<VisualElement>();
                    rootElement.Query(className: classNames[0]).ForEach(element => candidateElements.Add(element));

                    // Filter the smaller set for the remaining classes
                    foreach (var element in candidateElements)
                    {
                        var hasAllRemainingClasses = true;
                        for (int i = 1; i < classNames.Length; i++)
                        {
                            if (!element.ClassListContains(classNames[i]))
                            {
                                hasAllRemainingClasses = false;
                                break;
                            }
                        }

                        if (hasAllRemainingClasses)
                        {
                            results.Add(element);
                        }
                    }
                }

            }
            else if (selector.StartsWith("#"))
            {
                var element = rootElement.Q(selector.Substring(1));
                if (element != null) results.Add(element);
            }
            else if (selector == "*")
            {
                rootElement.Query<VisualElement>().ForEach(element => results.Add(element));
            }

            return results;
        }

        static List<VisualElement> FindElementsByDescendantSelector(VisualElement rootElement, string selector)
        {
            var results = new List<VisualElement>();
            var parts = selector.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length < 2) return results;

            // Start with elements matching the first selector
            var currentElements = FindElementsBySelector(rootElement, parts[0]);

            // For each subsequent selector part, find descendants
            for (int i = 1; i < parts.Length; i++)
            {
                var nextElements = new List<VisualElement>();

                foreach (var currentElement in currentElements)
                {
                    var descendants = FindElementsBySelector(currentElement, parts[i]);
                    nextElements.AddRange(descendants);
                }

                currentElements = nextElements;
            }

            return currentElements;
        }

        void ApplyStyleProperty(VisualElement element, string propertyName, string propertyValue)
        {
            if (TryApplyShorthandProperty(element, propertyName, propertyValue))
            {
                return;
            }

            if (s_StyleProperties.TryGetValue(propertyName, out var property))
            {
                try
                {
                    var convertedValue = ConvertValueToStyleType(propertyValue, property.PropertyType);
                    if (convertedValue != null)
                    {
                        property.SetValue(element.style, convertedValue);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to apply style property {propertyName}: {propertyValue} - {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"Unknown style property: {propertyName}");
            }
        }

        bool TryApplyShorthandProperty(VisualElement element, string propertyName, string propertyValue)
        {
            switch (propertyName)
            {
                case "padding":
                    ApplyPaddingShorthand(element, propertyValue);
                    return true;
                case "margin":
                    ApplyMarginShorthand(element, propertyValue);
                    return true;
                case "border-width":
                    ApplyBorderWidthShorthand(element, propertyValue);
                    return true;
                case "border-color":
                    ApplyBorderColorShorthand(element, propertyValue);
                    return true;
                case "border-radius":
                    ApplyBorderRadiusShorthand(element, propertyValue);
                    return true;
                case "border-style":
                    // Unity doesn't support individual border styles per side
                    // Skip this property since Unity uses solid borders only
                    return true;
                case "background-color":
                    // Map background-color to backgroundColor (StyleColor)
                    ApplyIndividualProperty(element, "backgroundcolor", propertyValue);
                    return true;
                case "background-image":
                    // Map background-image to backgroundImage (StyleBackground)
                    ApplyIndividualProperty(element, "backgroundimage", propertyValue);
                    return true;
                case "-unity-font-style-and-weight":
                    // Map Unity-specific property
                    ApplyIndividualProperty(element, "unityfontstyleandweight", propertyValue);
                    return true;
                case "-unity-text-align":
                    ApplyIndividualProperty(element, "unitytextalign", propertyValue);
                    return true;
                case "-unity-background-scale-mode":
                    ApplyIndividualProperty(element, "unitybackgroundscalemode", propertyValue);
                    return true;
                case "-unity-background-image-tint-color":
                    ApplyIndividualProperty(element, "unitybackgroundimagetintcolor", propertyValue);
                    return true;
                default:
                    return false;
            }
        }

        void ApplyPaddingShorthand(VisualElement element, string value)
        {
            var values = ParseShorthandValues(value);
            ApplyBoxValues(element, values, "padding");
        }

        void ApplyMarginShorthand(VisualElement element, string value)
        {
            var values = ParseShorthandValues(value);
            ApplyBoxValues(element, values, "margin");
        }

        void ApplyBorderWidthShorthand(VisualElement element, string value)
        {
            var values = ParseShorthandValues(value);
            ApplyBoxValues(element, values, "border", "width");
        }

        void ApplyBorderColorShorthand(VisualElement element, string value)
        {
            var values = ParseShorthandValues(value);
            ApplyBoxValues(element, values, "border", "color");
        }

        void ApplyBorderRadiusShorthand(VisualElement element, string value)
        {
            var values = ParseShorthandValues(value);
            ApplyBorderRadiusValues(element, values);
        }

        void ApplyBorderRadiusValues(VisualElement element, string[] values)
        {
            // Unity uses borderTopLeftRadius, borderTopRightRadius, etc.
            var radiusProperties = new[] { "bordertopleftradius", "bordertoprightradius", "borderbottomrightradius", "borderbottomleftradius" };

            for (int i = 0; i < 4; i++)
            {
                ApplyIndividualProperty(element, radiusProperties[i], values[i]);
            }
        }

        static string[] ParseShorthandValues(string value)
        {
            var parts = value.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            switch (parts.Length)
            {
                case 1:
                    // All sides same value
                    return new[] { parts[0], parts[0], parts[0], parts[0] };
                case 2:
                    // top/bottom, left/right
                    return new[] { parts[0], parts[1], parts[0], parts[1] };
                case 3:
                    // top, left/right, bottom
                    return new[] { parts[0], parts[1], parts[2], parts[1] };
                case 4:
                    // top, right, bottom, left
                    return parts;
                default:
                    // Fallback to first value for all sides
                    return new[] { parts[0], parts[0], parts[0], parts[0] };
            }
        }

        void ApplyBoxValues(VisualElement element, string[] values, string prefix, string suffix = "")
        {
            var sides = new[] { "Top", "Right", "Bottom", "Left" };

            for (int i = 0; i < 4; i++)
            {
                // Build proper camelCase property name for Unity (e.g., borderTopWidth)
                var propertyName = string.IsNullOrEmpty(suffix)
                    ? $"{prefix}{sides[i]}".ToLowerInvariant()
                    : $"{prefix}{sides[i]}{char.ToUpper(suffix[0])}{suffix.Substring(1)}";

                // Convert to lowercase for lookup in our cache (our cache stores lowercase keys)
                propertyName = propertyName.ToLowerInvariant();

                ApplyIndividualProperty(element, propertyName, values[i]);
            }
        }

        void ApplyIndividualProperty(VisualElement element, string propertyName, string propertyValue)
        {
            // Substitute USS variables in the property value
            propertyValue = SubstituteUssVariables(propertyValue);

            if (s_StyleProperties.TryGetValue(propertyName, out var property))
            {
                try
                {
                    var convertedValue = ConvertValueToStyleType(propertyValue, property.PropertyType);
                    if (convertedValue != null)
                    {
                        property.SetValue(element.style, convertedValue);
                    }
                    else
                    {
                        Debug.LogWarning($"Failed to convert value for {propertyName}: {propertyValue}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to apply style property {propertyName}: {propertyValue} - {ex.Message}");
                }
            }
            else
            {
                Debug.LogWarning($"Property '{propertyName}' not found in Unity style properties");
            }
        }

        static string ConvertToKebabCase(string camelCase)
        {
            return Regex.Replace(camelCase, "([a-z])([A-Z])", "$1-$2").ToLowerInvariant();
        }

        static object ConvertValueToStyleType(string value, Type targetType)
        {
            return StyleTypeConverters.ConvertValue(value, targetType);
        }
    }
}
