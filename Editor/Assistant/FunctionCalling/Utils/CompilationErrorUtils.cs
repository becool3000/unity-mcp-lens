using System;
using System.Text.RegularExpressions;

namespace Unity.AI.Assistant.Editor.FunctionCalling
{
    /// <summary>
    /// Utility to parse an error message from a line to detailed info
    /// </summary>
    static class CompilationErrorUtils
    {
        static readonly Regex k_Pattern = new (@"^(?<file>.+)\((?<line>\d+),(?<column>\d+)\): error (?<message>.*)$", RegexOptions.Compiled);
        
        public class ParsedMessage
        {
            public string File { get; set; }
            public int Line { get; set; }
            public int Column { get; set; }
            public string Message { get; set; }
        }
        
        public static ParsedMessage Parse(string input)
        {
            var match = k_Pattern.Match(input);

            if (!match.Success)
                throw new FormatException("Input string is not in the expected format.");

            return new ParsedMessage
            {
                File = match.Groups["file"].Value,
                Line = int.Parse(match.Groups["line"].Value),
                Column = int.Parse(match.Groups["column"].Value),
                Message = match.Groups["message"].Value
            };
        }
    }
}
