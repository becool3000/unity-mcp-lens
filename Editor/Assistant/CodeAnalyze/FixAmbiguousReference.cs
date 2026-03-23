using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    internal class FixAmbiguousReference : CSharpFixProvider
    {
        static readonly string[] k_DiagnosticIds = { "CS0104" };

        public override bool CanFix(Diagnostic diagnostic)
        {
            return k_DiagnosticIds.Contains(diagnostic.Id);
        }

        public override SyntaxTree ApplyFix(SyntaxTree tree, Diagnostic diagnostic)
        {
            var diagnosticMessage = diagnostic.GetMessage();
            var aliasNameMatch = Regex.Match(diagnosticMessage, @"(?<=')[^']*(?=' is)");
            var nameSpaceToAddMatch = Regex.Match(diagnosticMessage, @"(?<=between ')[^']*(?=' and)");
            if (nameSpaceToAddMatch.Success && aliasNameMatch.Success)
            {
                return tree.AddUsingDirective(nameSpaceToAddMatch.Value, aliasNameMatch.Value);
            }

            return tree;
        }
    }
}
