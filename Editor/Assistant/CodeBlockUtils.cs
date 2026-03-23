using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.AI.Assistant.Editor
{
    enum CodeChangeType { None, Added, Removed }

    struct DiffItem
    {
        public CodeChangeType Type;
        public string Line;
    }

    struct DiffResult
    {
        public string[] Lines;
        public Dictionary<int, CodeChangeType> LineChanges;
    }

    static class CodeBlockUtils
    {
        static readonly Regex k_UsingsRegex = new(@"\s*using\s+([\w\.]+)\s*;", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly Regex k_ClassRegex = new Regex(@"^.*?\s*class\s+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        static readonly IList<string> k_UsingTemp = new List<string>();
        static readonly IList<string> k_ContentTemp = new List<string>();

        public static string Format(string source, string defaultClassName = "CodeExport")
        {
            var output = new StringBuilder();
            output.Append(AssistantConstants.GetDisclaimerHeader(CodeFormat.CSharp));

            k_UsingTemp.Clear();
            k_UsingTemp.Add("System");
            k_UsingTemp.Add("UnityEditor");
            k_UsingTemp.Add("UnityEngine");

            bool hasClass = false;
            bool isBehaviorClass = false;

            // Anlyze the code block
            k_ContentTemp.Clear();
            string[] lines = source.Split("\n");
            int indent = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var usingMatch = k_UsingsRegex.Match(line);
                if (usingMatch.Success)
                {
                    // Filter out usings, they have to be outside of the class
                    string usingValue = usingMatch.Groups[1].Value;
                    if (!k_UsingTemp.Contains(usingValue))
                    {
                        k_UsingTemp.Add(usingValue);
                    }

                    continue;
                }

                if (k_ClassRegex.IsMatch(line))
                {
                    hasClass = true;
                }

                if (line.IndexOf(": MonoBehaviour", StringComparison.Ordinal) > 0 || line.IndexOf("GetComponent<", StringComparison.Ordinal) > 0)
                {
                    isBehaviorClass = true;
                }

                k_ContentTemp.Add(line);
            }

            // Now re-construct the code with the result of the above analysis
            for (var i = 0; i < k_UsingTemp.Count; i++)
            {
                output.AppendLine($"using {k_UsingTemp[i]};");
            }

            if (!hasClass)
            {
                indent++;
                string classInherit = isBehaviorClass ? " : MonoBehaviour" : "";
                output.AppendLine($"class {defaultClassName}{classInherit}");
                output.AppendLine("{");
            }

            string codeIndent = new string(' ', indent * 4);
            for (var i = 0; i < k_ContentTemp.Count; i++)
            {
                output.Append(codeIndent);
                output.AppendLine(k_ContentTemp[i]);
            }

            if (!hasClass)
            {
                output.AppendLine("}");
            }

            return output.ToString();
        }

        public static string AddDisclaimer(string codeFormat, string source)
        {
            var output = new StringBuilder();
            output.Append(AssistantConstants.GetDisclaimerHeader(codeFormat));
            output.Append(source);
            return output.ToString();
        }

        public static string ExtractClassName(string source)
        {
            var tree = CSharpSyntaxTree.ParseText(source);
            var root = (CompilationUnitSyntax)tree.GetRoot();

            var classNodes = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .ToList();

            var prioritizedTypes = new HashSet<string> { "MonoBehaviour", "ScriptableObject", "IComponentData" };
            var className = classNodes.FirstOrDefault(c =>
                c.BaseList != null &&
                c.BaseList.Types.Any(t => prioritizedTypes.Contains(t.Type.ToString()))
            ) ?? classNodes.FirstOrDefault();

            return className?.Identifier.Text;
        }

        public static bool IsShaderType(string codeType)
        {
            foreach (var type in AssistantConstants.ShaderCodeBlockTypes)
            {
                if (codeType.IndexOf(type, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// The output blends removed/added lines and unchanged code lines, based on a common minimal diff algorithm.
        /// See use of CalculateLongestCommonSubsequence().
        /// </summary>
        public static DiffResult CreateDiffCodeLines(string oldCode, string newCode)
        {
            var oldLines = oldCode.Split(new[] { AssistantConstants.NewLineCRLF, AssistantConstants.NewLineLF }, StringSplitOptions.None);
            var newLines = newCode.Split(new[] { AssistantConstants.NewLineCRLF, AssistantConstants.NewLineLF }, StringSplitOptions.None);

            var diff = CalculateLongestCommonSubsequence(oldLines, newLines);
            var resultLines = new List<string>();
            var lineChanges = new Dictionary<int, CodeChangeType>();
            int lineNumber = 1;

            foreach (var item in diff)
            {
                switch (item.Type)
                {
                    case CodeChangeType.None:
                        resultLines.Add(item.Line);
                        lineNumber++;
                        break;

                    case CodeChangeType.Removed:
                        resultLines.Add(item.Line);
                        lineChanges[lineNumber] = CodeChangeType.Removed;
                        lineNumber++;
                        break;

                    case CodeChangeType.Added:
                        resultLines.Add(item.Line);
                        lineChanges[lineNumber] = CodeChangeType.Added;
                        lineNumber++;
                        break;
                }
            }

            return new DiffResult
            {
                Lines = resultLines.ToArray(),
                LineChanges = lineChanges
            };
        }

        /// <summary>
        /// Calculates a line-by-line minimal diff using the Longest Common Subsequence (LCS) algorithm.
        ///
        /// The algorithm, a common diff tool standard, identifies longest sequence of lines that appear in both
        /// old and new code AND in the same order. Now we know unchanged lines, then all other lines can be marked as
        /// added (in new only) or removed (in old only).
        /// </summary>
        static List<DiffItem> CalculateLongestCommonSubsequence(string[] oldLines, string[] newLines)
        {
            int m = oldLines.Length;
            int n = newLines.Length;

            // Create LCS table; dynamic programming approach breaking problem into subproblems
            int[,] lcs = new int[m + 1, n + 1];

            for (int i = 1; i <= m; i++)
            {
                for (int j = 1; j <= n; j++)
                {
                    if (oldLines[i - 1] == newLines[j - 1])
                    {
                        lcs[i, j] = lcs[i - 1, j - 1] + 1;
                    }
                    else
                    {
                        lcs[i, j] = Math.Max(lcs[i - 1, j], lcs[i, j - 1]);
                    }
                }
            }

            // Backtrack through LCS table to reconstruct the actual line-by-line differences
            var diff = new List<DiffItem>();
            int x = m, y = n;

            while (x > 0 || y > 0)
            {
                if (x > 0 && y > 0 && oldLines[x - 1] == newLines[y - 1])
                {
                    diff.Add(new DiffItem { Type = CodeChangeType.None, Line = oldLines[x - 1] });
                    x--;
                    y--;
                }
                else if (y > 0 && (x == 0 || lcs[x, y - 1] >= lcs[x - 1, y]))
                {
                    diff.Add(new DiffItem { Type = CodeChangeType.Added, Line = newLines[y - 1] });
                    y--;
                }
                else if (x > 0)
                {
                    diff.Add(new DiffItem { Type = CodeChangeType.Removed, Line = oldLines[x - 1] });
                    x--;
                }
            }

            diff.Reverse();
            return diff;
        }
    }
}
