using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    /// <summary>
    /// Utility class for parsing color values from strings using Unity's Color class reflection
    /// </summary>
    internal static class ColorParser
    {
        static readonly Dictionary<string, Color> k_NamedColors = new();
        static bool s_Initialized;

        static ColorParser()
        {
            InitializeNamedColors();
        }

        static void InitializeNamedColors()
        {
            if (s_Initialized) return;

            var colorType = typeof(Color);
            var colorProperties = colorType.GetProperties(BindingFlags.Public | BindingFlags.Static)
                .Where(p => p.PropertyType == typeof(Color) && p.CanRead);

            foreach (var property in colorProperties)
            {
                try
                {
                    var color = (Color)property.GetValue(null);
                    var name = property.Name.ToLowerInvariant();
                    k_NamedColors[name] = color;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to initialize color {property.Name}: {ex.Message}");
                }
            }

            k_NamedColors["grey"] = Color.gray;
            k_NamedColors["transparent"] = Color.clear;

            s_Initialized = true;
        }

        public static bool TryParseColor(string value, out Color color)
        {
            color = Color.clear;

            if (string.IsNullOrEmpty(value))
                return false;

            value = value.Trim();

            if (value.StartsWith("#"))
            {
                return ColorUtility.TryParseHtmlString(value, out color);
            }

            if (value.StartsWith("rgba(") || value.StartsWith("rgb("))
            {
                return TryParseRgbaColor(value, out color);
            }

            var lowerValue = value.ToLowerInvariant();
            if (k_NamedColors.TryGetValue(lowerValue, out color))
            {
                return true;
            }

            return false;
        }

        public static Color ParseColor(string value, Color defaultColor = default)
        {
            return TryParseColor(value, out var color) ? color : defaultColor;
        }

        static bool TryParseRgbaColor(string value, out Color color)
        {
            color = Color.clear;

            try
            {
                var isRgba = value.StartsWith("rgba(");
                var content = value.Substring(value.IndexOf('(') + 1).TrimEnd(')');
                var parts = content.Split(',').Select(p => p.Trim()).ToArray();

                if (parts.Length >= 3)
                {
                    var r = float.Parse(parts[0], CultureInfo.InvariantCulture) / 255f;
                    var g = float.Parse(parts[1], CultureInfo.InvariantCulture) / 255f;
                    var b = float.Parse(parts[2], CultureInfo.InvariantCulture) / 255f;
                    var a = isRgba && parts.Length >= 4 ? float.Parse(parts[3], CultureInfo.InvariantCulture) : 1f;

                    color = new Color(r, g, b, a);
                    return true;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            return false;
        }
    }
}
