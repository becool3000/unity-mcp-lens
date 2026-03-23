using System.Linq;
using Microsoft.CodeAnalysis;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    internal class FixMissingSquareBracket : CSharpFixProvider
    {
        static readonly string[] k_DiagnosticIds = { "CS1003" };

        public override bool CanFix(Diagnostic diagnostic)
        {
            return k_DiagnosticIds.Contains(diagnostic.Id);
        }

        public override SyntaxTree ApplyFix(SyntaxTree tree, Diagnostic diagnostic)
        {
            return tree.InsertAtLocation(diagnostic.Location, "]");
        }
    }
}
