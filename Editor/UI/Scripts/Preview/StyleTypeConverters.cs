using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    /// <summary>
    /// Unified type converter registry for converting string values to Unity UI Toolkit style types.
    /// Used by both USS parsing and inline style attribute parsing to ensure consistent behavior.
    /// </summary>
    internal static class StyleTypeConverters
    {
        static readonly Dictionary<Type, Func<string, object>> s_StyleConverters = new()
        {
            // Unity Style Types - use StyleValueParsers for consistency
            [typeof(StyleLength)] = StyleValueParsers.ParseStyleLength,
            [typeof(StyleFloat)] = StyleValueParsers.ParseStyleFloat,
            [typeof(StyleInt)] = StyleValueParsers.ParseStyleInt,
            [typeof(StyleColor)] = StyleValueParsers.ParseStyleColor,
            [typeof(StyleKeyword)] = StyleValueParsers.ParseStyleKeyword,
            [typeof(StyleBackground)] = StyleValueParsers.ParseStyleBackground,
            [typeof(StyleTextShadow)] = StyleValueParsers.ParseStyleTextShadow,
            [typeof(StyleBackgroundSize)] = StyleValueParsers.ParseStyleBackgroundSize,
            [typeof(StyleBackgroundPosition)] = StyleValueParsers.ParseStyleBackgroundPosition,
            [typeof(StyleRotate)] = StyleValueParsers.ParseStyleRotate,
            [typeof(StyleTransformOrigin)] = StyleValueParsers.ParseStyleTransformOrigin,

            // Unity Style Enums - use StyleValueParsers for consistency
            [typeof(StyleEnum<DisplayStyle>)] = StyleValueParsers.ParseStyleEnum<DisplayStyle>,
            [typeof(StyleEnum<FlexDirection>)] = StyleValueParsers.ParseStyleEnum<FlexDirection>,
            [typeof(StyleEnum<Justify>)] = StyleValueParsers.ParseStyleEnum<Justify>,
            [typeof(StyleEnum<Align>)] = StyleValueParsers.ParseStyleEnum<Align>,
            [typeof(StyleEnum<Wrap>)] = StyleValueParsers.ParseStyleEnum<Wrap>,
            [typeof(StyleEnum<Position>)] = StyleValueParsers.ParseStyleEnum<Position>,
            [typeof(StyleEnum<Overflow>)] = StyleValueParsers.ParseStyleEnum<Overflow>,
            [typeof(StyleEnum<Visibility>)] = StyleValueParsers.ParseStyleEnum<Visibility>,
            [typeof(StyleEnum<WhiteSpace>)] = StyleValueParsers.ParseStyleEnum<WhiteSpace>,
            [typeof(StyleEnum<FontStyle>)] = StyleValueParsers.ParseStyleEnum<FontStyle>,
            [typeof(StyleEnum<TextAnchor>)] = StyleValueParsers.ParseStyleEnum<TextAnchor>,
            [typeof(StyleEnum<ScaleMode>)] = StyleValueParsers.ParseScaleMode,

            // Basic types for UXML attributes (non-style properties)
            [typeof(bool)] = value => bool.TryParse(value, out var result) && result,
            [typeof(int)] = value => int.TryParse(value, out var result) ? result : 0,
            [typeof(float)] = value => float.TryParse(value, out var result) ? result : 0f,
            [typeof(double)] = value => double.TryParse(value, out var result) ? result : 0.0,
            [typeof(string)] = value => value,
            [typeof(Color)] = value => TryParseColor(value, out var color) ? color : Color.white,
            [typeof(Length)] = value => TryParseLength(value, out var length) ? length : Length.Auto(),
            [typeof(DisplayStyle)] = value => Enum.TryParse<DisplayStyle>(value, true, out var result) ? result : DisplayStyle.Flex,
            [typeof(FlexDirection)] = value => TryParseFlexDirection(value, out var result) ? result : FlexDirection.Column,
            [typeof(PickingMode)] = value => Enum.TryParse<PickingMode>(value, true, out var result) ? result : PickingMode.Position,
        };

        /// <summary>
        /// Get the complete dictionary of type converters for use by processors
        /// </summary>
        public static Dictionary<Type, Func<string, object>> GetConverters()
        {
            return s_StyleConverters;
        }

        /// <summary>
        /// Convert a string value to the specified target type
        /// </summary>
        public static object ConvertValue(string value, Type targetType)
        {
            if (string.IsNullOrEmpty(value))
                return GetDefaultValue(targetType);

            var actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (s_StyleConverters.TryGetValue(actualType, out var converter))
            {
                return converter(value);
            }

            // Handle generic enums not in our converter list
            if (actualType.IsEnum)
            {
                if (Enum.TryParse(actualType, value, true, out var enumValue))
                    return enumValue;

                var normalizedValue = value.Replace("-", "").Replace("_", "");
                if (Enum.TryParse(actualType, normalizedValue, true, out var enumValue2))
                    return enumValue2;
            }

            // Fallback to default conversion
            try
            {
                return Convert.ChangeType(value, actualType);
            }
            catch
            {
                return GetDefaultValue(targetType);
            }
        }

        static object GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        #region Basic Type Parsers (for UXML attributes)

        static bool TryParseColor(string value, out Color color)
        {
            color = Color.white;

            if (string.IsNullOrEmpty(value))
                return false;

            value = value.Trim().ToLower();

            if (value.StartsWith("#"))
            {
                return ColorUtility.TryParseHtmlString(value, out color);
            }

            // Basic named colors
            switch (value)
            {
                case "white": color = Color.white; return true;
                case "black": color = Color.black; return true;
                case "red": color = Color.red; return true;
                case "green": color = Color.green; return true;
                case "blue": color = Color.blue; return true;
                case "yellow": color = Color.yellow; return true;
                case "cyan": color = Color.cyan; return true;
                case "magenta": color = Color.magenta; return true;
                case "gray": case "grey": color = Color.gray; return true;
                case "transparent": color = Color.clear; return true;
            }

            return false;
        }

        static bool TryParseLength(string value, out Length length)
        {
            length = Length.Auto();

            if (string.IsNullOrEmpty(value))
                return false;

            value = value.Trim().ToLower();

            if (value == "auto")
            {
                length = Length.Auto();
                return true;
            }

            if (value.EndsWith("px"))
            {
                if (float.TryParse(value.Substring(0, value.Length - 2), out var pixels))
                {
                    length = new Length(pixels, LengthUnit.Pixel);
                    return true;
                }
            }
            else if (value.EndsWith("%"))
            {
                if (float.TryParse(value.Substring(0, value.Length - 1), out var percent))
                {
                    length = new Length(percent, LengthUnit.Percent);
                    return true;
                }
            }
            else if (float.TryParse(value, out var number))
            {
                length = new Length(number, LengthUnit.Pixel);
                return true;
            }

            return false;
        }

        static bool TryParseFlexDirection(string value, out FlexDirection flexDirection)
        {
            value = value.Replace("-", "").ToLowerInvariant();
            return Enum.TryParse(value, true, out flexDirection);
        }

        #endregion
    }
}