using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    class PublicMethodCallWalker : CSharpSyntaxWalker
    {
        SemanticModel m_SemanticModel;
        public List<string> PublicMethodCalls { get; } = new();

        public PublicMethodCallWalker(SemanticModel semanticModel)
        {
            m_SemanticModel = semanticModel;
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var symbolInfo = m_SemanticModel.GetSymbolInfo(node);
            if (symbolInfo.Symbol is IMethodSymbol methodSymbol &&
                methodSymbol.DeclaredAccessibility == Accessibility.Public)
            {
                string fullMethodName = GetFullMethodName(methodSymbol);
                if (!string.IsNullOrEmpty(fullMethodName))
                {
                    if (!PublicMethodCalls.Contains(fullMethodName))
                        PublicMethodCalls.Add(fullMethodName);
                }
            }

            base.VisitInvocationExpression(node);
        }

        string GetFullMethodName(IMethodSymbol methodSymbol)
        {
            var parts = new List<string> { methodSymbol.Name };

            // Add containing type(s)
            var containingType = methodSymbol.ContainingType;
            while (containingType != null)
            {
                parts.Add(containingType.Name);
                containingType = containingType.ContainingType;
            }

            // Add namespace
            if (methodSymbol.ContainingNamespace is { IsGlobalNamespace: false })
            {
                var namespaceParts = methodSymbol.ContainingNamespace.ToDisplayString().Split('.');
                parts.AddRange(namespaceParts.Reverse());
            }

            // Reverse the list and join with dots
            parts.Reverse();
            return string.Join(".", parts);
        }
    }
}
