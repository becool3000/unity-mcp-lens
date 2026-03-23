using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Preview
{
    internal static class StyleValueParsers
    {
        const string k_ProjectDatabasePrefix = "project://database/";
        static readonly Regex k_UrlPattern = new(@"url\(\s*(?:['""]([^'""]+)['""]|([^)\s]+))\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static object ParseStyleLength(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value == "auto")
                return new StyleLength(StyleKeyword.Auto);

            if (value.EndsWith("px"))
            {
                var numberPart = value.Substring(0, value.Length - 2);
                if (float.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
                {
                    return new StyleLength(pixels);
                }
            }
            else if (value.EndsWith("%"))
            {
                var numberPart = value.Substring(0, value.Length - 1);
                if (float.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
                {
                    return new StyleLength(new Length(percent, LengthUnit.Percent));
                }
            }
            else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return new StyleLength(number);
            }

            return new StyleLength(StyleKeyword.Auto);
        }

        public static object ParseStyleFloat(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value.EndsWith("px"))
            {
                var numberPart = value.Substring(0, value.Length - 2);
                if (float.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
                {
                    return new StyleFloat(pixels);
                }
            }
            else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return new StyleFloat(result);
            }

            return new StyleFloat(0f);
        }

        public static object ParseStyleInt(string value)
        {
            if (int.TryParse(value.Trim(), out var result))
            {
                return new StyleInt(result);
            }
            return new StyleInt(0);
        }

        public static object ParseStyleColor(string value)
        {
            var color = ColorParser.ParseColor(value, Color.white);
            return new StyleColor(color);
        }

        public static object ParseStyleKeyword(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "auto" => StyleKeyword.Auto,
                "none" => StyleKeyword.None,
                "initial" => StyleKeyword.Initial,
                "null" => StyleKeyword.Null,
                _ => StyleKeyword.Auto
            };
        }

        public static object ParseScaleMode(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "scale-to-fit" => new StyleEnum<ScaleMode>(ScaleMode.ScaleToFit),
                "stretch-to-fill" => new StyleEnum<ScaleMode>(ScaleMode.StretchToFill),
                "scale-and-crop" => new StyleEnum<ScaleMode>(ScaleMode.ScaleAndCrop),
                _ => new StyleEnum<ScaleMode>(ScaleMode.ScaleToFit)
            };
        }

        public static object ParseStyleBackground(string value)
        {
            if (string.IsNullOrEmpty(value))
                return new StyleBackground(StyleKeyword.None);

            value = value.Trim();

            if (string.IsNullOrEmpty(value) || value == "none")
                return new StyleBackground(StyleKeyword.None);

            value = DecodeXmlEntities(value);

            var match = k_UrlPattern.Match(value);
            if (!match.Success)
                return new StyleBackground(StyleKeyword.None);

            var url = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            var assetPath = ExtractAssetPath(url);
            if (string.IsNullOrEmpty(assetPath))
                return new StyleBackground(StyleKeyword.None);

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            return texture != null
                ? new StyleBackground(texture)
                : new StyleBackground(StyleKeyword.None);
        }

        static string DecodeXmlEntities(string value)
        {
            return value
                .Replace("&quot;", "\"")
                .Replace("&amp;", "&")
                .Replace("&lt;", "<")
                .Replace("&gt;", ">")
                .Replace("&apos;", "'");
        }

        static string ExtractAssetPath(string url)
        {
            if (url.StartsWith(k_ProjectDatabasePrefix))
                url = url.Substring(k_ProjectDatabasePrefix.Length);

            var queryIndex = url.IndexOf('?');
            if (queryIndex >= 0)
                url = url.Substring(0, queryIndex);

            var fragmentIndex = url.IndexOf('#');
            if (fragmentIndex >= 0)
                url = url.Substring(0, fragmentIndex);

            return url;
        }

        public static object ParseStyleTextShadow(string value)
        {
            value = value.Trim();

            if (value == "none")
            {
                return new StyleTextShadow(StyleKeyword.None);
            }

            try
            {
                var parts = value.Split(' ');
                if (parts.Length >= 2)
                {
                    var offsetX = ParseLengthValue(parts[0]);
                    var offsetY = ParseLengthValue(parts[1]);
                    var blurRadius = parts.Length > 2 ? ParseLengthValue(parts[2]) : 0f;

                    var color = Color.black;
                    for (int i = 3; i < parts.Length; i++)
                    {
                        if (parts[i].StartsWith("#") || parts[i].StartsWith("rgba") || parts[i].StartsWith("rgb"))
                        {
                            var colorStyle = ParseStyleColor(string.Join(" ", parts.Skip(i)));
                            if (colorStyle is StyleColor styleColor)
                            {
                                color = styleColor.value;
                            }
                            break;
                        }
                    }

                    var textShadow = new TextShadow
                    {
                        offset = new Vector2(offsetX, offsetY),
                        blurRadius = blurRadius,
                        color = color
                    };

                    return new StyleTextShadow(textShadow);
                }
            }
            catch
            {
                // Fall back to no shadow on parsing errors
            }

            return new StyleTextShadow(StyleKeyword.None);
        }

        public static object ParseStyleBackgroundSize(string value)
        {
            value = value.Trim().ToLowerInvariant();

            return value switch
            {
                "auto" => new StyleBackgroundSize(StyleKeyword.Auto),
                "cover" => new StyleBackgroundSize(new BackgroundSize { sizeType = BackgroundSizeType.Cover }),
                "contain" => new StyleBackgroundSize(new BackgroundSize { sizeType = BackgroundSizeType.Contain }),
                _ => new StyleBackgroundSize(StyleKeyword.Auto)
            };
        }

        public static object ParseStyleRotate(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value == "none" || value == "initial")
            {
                return new StyleRotate(StyleKeyword.Initial);
            }

            var angle = ParseAngleValue(value);
            return new StyleRotate(new Rotate(angle));
        }

        public static object ParseStyleTransformOrigin(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value == "initial")
            {
                return new StyleTransformOrigin(StyleKeyword.Initial);
            }

            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 1)
            {
                var x = ParseTransformOriginLength(parts[0]);
                var y = new Length(50, LengthUnit.Percent);
                return new StyleTransformOrigin(new TransformOrigin(x, y));
            }
            else if (parts.Length >= 2)
            {
                var x = ParseTransformOriginLength(parts[0]);
                var y = ParseTransformOriginLength(parts[1]);
                return new StyleTransformOrigin(new TransformOrigin(x, y));
            }

            return new StyleTransformOrigin(new TransformOrigin(new Length(0, LengthUnit.Pixel), new Length(0, LengthUnit.Pixel)));
        }

        static Length ParseTransformOriginLength(string value)
        {
            return ParseLengthWithKeywords(value);
        }


        public static object ParseStyleBackgroundPosition(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value == "initial")
                return new StyleBackgroundPosition(StyleKeyword.Initial);

            return new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Center));
        }

        public static object ParseStyleEnum<T>(string value) where T : struct, Enum
        {
            var normalizedValue = value.Replace("-", "").Replace("_", "");
            if (Enum.TryParse<T>(normalizedValue, true, out var enumValue))
            {
                return new StyleEnum<T>(enumValue);
            }

            return new StyleEnum<T>(default(T));
        }

        static Length ParseLengthWithKeywords(string value)
        {
            value = value.Trim().ToLowerInvariant();
            return value switch
            {
                "center" => new Length(50, LengthUnit.Percent),
                _ => ParseLength(value)
            };
        }

        static Length ParseLength(string value)
        {
            var styleLength = (StyleLength)ParseStyleLength(value);
            return styleLength.keyword == StyleKeyword.Auto ? new Length(0, LengthUnit.Pixel) : styleLength.value;
        }

        static float ParseLengthValue(string value)
        {
            value = value.Trim().ToLowerInvariant();
            if (value.EndsWith("px"))
            {
                value = value.Substring(0, value.Length - 2);
            }

            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }

            return 0f;
        }

        static Angle ParseAngleValue(string value)
        {
            value = value.Trim().ToLowerInvariant();

            if (value.EndsWith("deg"))
            {
                var numberPart = value.Substring(0, value.Length - 3);
                if (float.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out var degrees))
                {
                    return new Angle(degrees, AngleUnit.Degree);
                }
            }
            else if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                return new Angle(number, AngleUnit.Degree);
            }

            return new Angle(0, AngleUnit.Degree);
        }

    }
}
