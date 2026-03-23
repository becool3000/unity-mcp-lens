using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    internal class FixMissingImports : CSharpFixProvider
    {
        static readonly string[] k_DiagnosticIds = { "CS0246", "CS1061" };

        readonly Dictionary<string, string[]> k_NamespaceKeywords = new ()
        {
            { "System.Linq", new[] { "Where", "Select", "OrderBy", "Concat", "Any" } },
            { "System.Collections.Generic", new[] { "List<>", "Dictionary<>" } },
            { "Unity.AI.Navigation", new[] { "NavMeshSurface" } },
            { "UnityEditor", new[] { "MonoScript" } },
        };

        readonly Dictionary<string, string> k_WrongNamespaces = new ()
        {
            { "Unity.Cinemachine", "Cinemachine" },
        };

        public override bool CanFix(Diagnostic diagnostic)
        {
            if (!k_DiagnosticIds.Contains(diagnostic.Id))
                return false;

            var messages = diagnostic.GetMessage();
            foreach (var keywords in k_NamespaceKeywords.Values)
            {
                if (keywords.Any(keyword => messages.Contains($"'{keyword}'")))
                    return true;
            }

            foreach (var wrongNamespace in k_WrongNamespaces)
            {
                if (messages.Contains($"'{wrongNamespace.Value}'"))
                    return true;
            }

            return false;
        }

        public override SyntaxTree ApplyFix(SyntaxTree tree, Diagnostic diagnostic)
        {
            var messages = diagnostic.GetMessage();
            foreach (var namespaceKeywords in k_NamespaceKeywords)
            {
                var keywords = namespaceKeywords.Value;
                if (keywords.Any(keyword => messages.Contains($"'{keyword}'")))
                {
                    return tree.AddUsingDirective(namespaceKeywords.Key);
                }
            }

            foreach (var wrongNamespace in k_WrongNamespaces)
            {
                if (messages.Contains($"'{wrongNamespace.Value}'"))
                {
                    var cleanTree = tree.RemoveUsingDirective(wrongNamespace.Value);
                    return cleanTree.AddUsingDirective(wrongNamespace.Key);
                }
            }

            return tree;
        }
    }
}
