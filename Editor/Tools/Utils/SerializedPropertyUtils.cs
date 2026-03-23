using System.Text.RegularExpressions;

namespace Unity.AI.Assistant.Tools.Editor
{
    internal static class SerializedPropertyUtils
    {
        // Converts a path like "someProperty/anArrayProperty[3]/aSubProperty/anotherArrayProp[7]/finalProperty"
        // To the expected Unity path:
        // "someProperty.anArrayProperty.Array.data[3].aSubProperty.anotherArrayProp.Array.data[7].finalProperty"
        public static string ConvertToUnityPropertyPath(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
                return propertyPath;

            // Replace '/' with '.'
            var path = propertyPath.Replace('/', '.');

            // Replace "propertyName[3]" with "propertyName.Array.data[3]"
            path = Regex.Replace(path, @"(\w+)\[(\d+)\]", "$1.Array.data[$2]");

            return path;
        }
    }
}
