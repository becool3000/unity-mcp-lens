using UnityEngine;

namespace Unity.AI.Assistant.Editor.Utils
{
    class CodeUtils
    {
        /// <summary>
        /// Returns the given string wrapped in comment syntax for the specified code format.
        /// </summary>
        /// <param name="src"> The string to be commented. </param>
        /// <param name="codeFormat"> The code format (e.g., "csharp", "xml", "json", etc.). You can use the constants defined in the CodeFormat class. </param>
        /// <param name="indentLevel"> THe indentation level. </param>
        /// <param name="indentSize"> The number of spaces per indentation level. </param>
        /// <returns> The commented string. </returns>
        internal static string GetCommentedLines(string src, string codeFormat = CodeFormat.CSharp, int indentLevel = 0, int indentSize = 4)
        {
            var indentText = "".PadLeft(indentLevel * indentSize);

            if (codeFormat == null)
            {
                Debug.LogWarning("Code format is null, returning an empty string.");
                return string.Empty;
            }

            return codeFormat.ToLower() switch
            {
                CodeFormat.Json => string.Empty,
                CodeFormat.Xml or CodeFormat.Uxml or CodeFormat.Html or CodeFormat.Xaml  => $"{indentText}<!--\n{indentText}{src.Replace("\n", "\n" + indentText)}\n{indentText}-->\n",
                CodeFormat.Yaml or CodeFormat.Yml or CodeFormat.Sh or CodeFormat.Bash or CodeFormat.Python => $"{indentText}# {src.Replace("\n", "\n" + indentText + "# ")}\n",
                _ => $"{indentText}/*\n{indentText}{src.Replace("\n", "\n" + indentText)}\n{indentText}*/\n"
            };
        }
    }
}
