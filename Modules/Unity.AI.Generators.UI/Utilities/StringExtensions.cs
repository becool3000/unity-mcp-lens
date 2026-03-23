using System.IO;
using System.Text;

namespace Unity.AI.Generators.UI.Utilities
{
    static class StringExtensions
    {
        public static string AddSpaceBeforeCapitalLetters(this string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var result = new StringBuilder(input.Length * 2);
            var previousWasUppercase = false;

            for (var i = 0; i < input.Length; i++)
            {
                var current = input[i];
                var isUppercase = char.IsUpper(current);

                // Add a space before an uppercase letter if:
                // 1. It's not the first character
                // 2. The previous character wasn't uppercase (meaning this is the start of a new word)
                // 3. Or if the next character is lowercase (this is the end of an acronym)
                if (isUppercase && i > 0 && !previousWasUppercase)
                    result.Append(' ');

                result.Append(current);
                previousWasUppercase = isUppercase;
            }

            return result.ToString();
        }

        public static string GetFileExtension(this string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return string.Empty;

            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            var firstDotIndex = fileName.IndexOf('.');
            return firstDotIndex switch
            {
                -1 => string.Empty,
                0 => fileName,
                _ => fileName[firstDotIndex..]
            };
        }
    }
}
