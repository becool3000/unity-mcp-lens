using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class CodeSyntaxHighlight
    {
        internal static readonly string k_ColorAzure = EditorGUIUtility.isProSkin ? "#7893e5" : "#1d2c98";
        internal static readonly string k_ColorLavender = EditorGUIUtility.isProSkin ? "#bc92f9" : "#653ba3";
        internal static readonly string k_ColorTurquoise = EditorGUIUtility.isProSkin ? "#68c99e" : "#2b8856";
        internal static readonly string k_ColorSand = EditorGUIUtility.isProSkin ? "#c2a473" : "#aa6932";
        internal static readonly string k_ColorLime = EditorGUIUtility.isProSkin ? "#91c275" : "#4e7d34";
        internal static readonly string k_ColorPink = EditorGUIUtility.isProSkin ? "#EC94C0" : "#B22E6A";
        internal static readonly string k_ColorGreen = EditorGUIUtility.isProSkin ? "#ACEB96" : "#346500";

        public static string HighlightCSharp(string sourceCode)
        {
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = (CompilationUnitSyntax)tree.GetRoot();
            var formattedCode = new StringBuilder();

            foreach (var token in root.DescendantTokens())
            {
                foreach (var trivia in token.LeadingTrivia)
                {
                    formattedCode.Append(ProcessTrivia(trivia));
                }

                formattedCode.Append(ProcessToken(token));

                foreach (var trivia in token.TrailingTrivia)
                {
                    formattedCode.Append(ProcessTrivia(trivia));
                }
            }

            return formattedCode.ToString();
        }

        public static string HighlightUXML(string xml)
        {
            // Highlight colors
            var tagColor = k_ColorLavender;
            var attrNameColor = k_ColorLime;
            var attrValueColor = k_ColorSand;
            var commentColor = k_ColorLime;

            // Placeholder-safe color tag helper
            string EncodeColorTag(string content, string color)
            {
                return $"[[LT]]color[[EQ]]{color}[[GT]]{content}[[LT]]/color[[GT]]";
            }

            // 1. Protect comments and strings first
            var protectedParts = new List<string>();
            xml = Regex.Replace(xml, @"<!--(.*?)-->|""[^""]*""", m =>
            {
                protectedParts.Add(m.Value);
                return $"[[PROTECTED_{protectedParts.Count - 1}]]";
            }, RegexOptions.Singleline);

            // 2. Highlight tag names and brackets, including '/>' as a single token
            xml = Regex.Replace(xml, @"(<\/?)([\w:]+)(\s*[^>]*?)(\/?>)", m =>
                $"{EncodeColorTag(m.Groups[1].Value, tagColor)}" +
                $"{EncodeColorTag(m.Groups[2].Value, tagColor)}" +
                $"{m.Groups[3].Value}" +
                $"{EncodeColorTag(m.Groups[4].Value, tagColor)}");

            // 3. Highlight namespace prefix in xmlns:ui=
            xml = Regex.Replace(xml, @"(xmlns:)([\w\-]+)(=)", m =>
                $"{EncodeColorTag(m.Groups[1].Value, attrNameColor)}{EncodeColorTag(m.Groups[2].Value, tagColor)}{m.Groups[3].Value}");

            // 4. Highlight attribute names before '=' (including hyphens)
            xml = Regex.Replace(xml, @"\b([\w\-:]+)(=)", m =>
                $"{EncodeColorTag(m.Groups[1].Value, attrNameColor)}{m.Groups[2].Value}");

            // 5. Restore protected comments and strings with correct coloring
            xml = Regex.Replace(xml, @"\[\[PROTECTED_(\d+)\]\]", m =>
            {
                int idx = int.Parse(m.Groups[1].Value);
                var value = protectedParts[idx];
                if (value.StartsWith("<!--"))
                    return EncodeColorTag(value, commentColor);
                return EncodeColorTag(value, attrValueColor);
            });

            // Final: decode placeholder tags
            xml = xml.Replace("[[LT]]", "<")
                .Replace("[[GT]]", ">")
                .Replace("[[EQ]]", "=");

            return xml;
        }

        public static string HighlightUSS(string uss)
        {
            // Highlight colors
            var selectorColor = k_ColorLavender;
            var propertyColor = k_ColorAzure;
            var stringColor = k_ColorSand;
            var commentColor = k_ColorLime;
            var numberColor = k_ColorPink;
            var hexColor = k_ColorGreen;
            var functionColor = k_ColorTurquoise;

            // Placeholder-safe color tag helper
            string EncodeColorTag(string content, string color)
                => $"[[LT]]color[[EQ]]{color}[[GT]]{content}[[LT]]/color[[GT]]";

            // 1. Extract and protect comments and strings
            var protectedParts = new List<string>();
            uss = Regex.Replace(uss, @"/\*[\s\S]*?\*/|(['""])(?:\\.|(?!\1).)*\1", m =>
            {
                protectedParts.Add(m.Value);
                return $"[[PROTECTED_{protectedParts.Count - 1}]]";
            });

            // 2. Highlight hex color values (e.g., #ffffff)
            uss = Regex.Replace(uss, @"(?<=:\s*)(#([a-fA-F0-9]{3,8}))\b", m =>
                EncodeColorTag(m.Value, hexColor));

            // 3. Highlight property names before colon (e.g., color:)
            uss = Regex.Replace(uss, @"(^|\s|;)(--[a-zA-Z0-9_-]+|[a-zA-Z0-9_-]+)(\s*:)", m =>
                $"{m.Groups[1].Value}{EncodeColorTag(m.Groups[2].Value, propertyColor)}{m.Groups[3].Value}");

            // 4. Highlight numeric values inside CSS functions (except var())
            uss = Regex.Replace(uss, @"\b([a-zA-Z-]+)\(([^)]*)\)", m =>
            {
                var functionName = m.Groups[1].Value;
                var content = m.Groups[2].Value;
                if (functionName.Equals("var", StringComparison.OrdinalIgnoreCase))
                    return m.Value;
                var highlightedContent = Regex.Replace(content, @"-?\b\d+(\.\d+)?\b",
                    numMatch => EncodeColorTag(numMatch.Value, numberColor));
                return $"{functionName}({highlightedContent})";
            });

            // 5. Highlight numeric values in property values (e.g., min-width: 480px;)
            uss = Regex.Replace(uss, @"(?<=:\s*)-?\d+(\.\d+)?(?=\s*(px|%)?\b)", m =>
                EncodeColorTag(m.Value, numberColor));

            // 6. Highlight value inside var(...)
            uss = Regex.Replace(uss, @"var\(([^)]+)\)", m =>
                $"var({EncodeColorTag(m.Groups[1].Value, selectorColor)})");

            // 7. Highlight 'function(' keyword in property values
            uss = Regex.Replace(uss, @"(?<=:\s*)\b([a-zA-Z-]+)\b(?=\()", m =>
                EncodeColorTag(m.Value, functionColor));

            // 8. Highlight selectors and each pseudo-class in a chain individually, including universal (*) and pseudo-selectors (e.g., :root)
            uss = Regex.Replace(uss, @"(?<!:\s*)(^|[{\s,])((?:\.|#)?[a-zA-Z_][\w\-]*|\*|:[a-zA-Z_][\w\-]*)(:[a-zA-Z_][\w\-]*)*", m =>
            {
                var prefix = m.Groups[1].Value;
                var selector = m.Groups[2].Value;
                var pseudoClasses = m.Value.Substring(prefix.Length + selector.Length);
                var result = $"{prefix}{EncodeColorTag(selector, selectorColor)}";
                if (!string.IsNullOrEmpty(pseudoClasses))
                {
                    result += Regex.Replace(pseudoClasses, @":[a-zA-Z_][\w\-]*", pc =>
                        $":{EncodeColorTag(pc.Value.Substring(1), selectorColor)}");
                }
                return result;
            });

            // 9. Restore protected comments and strings with correct coloring
            uss = Regex.Replace(uss, @"\[\[PROTECTED_(\d+)\]\]", m =>
            {
                int idx = int.Parse(m.Groups[1].Value);
                var value = protectedParts[idx];
                if (value.StartsWith("/*"))
                    return EncodeColorTag(value, commentColor);
                return EncodeColorTag(value, stringColor);
            });

            // Final: decode placeholder tags
            uss = uss.Replace("[[LT]]", "<")
                .Replace("[[GT]]", ">")
                .Replace("[[EQ]]", "=");

            return uss;
        }

        static string ProcessToken(SyntaxToken token)
        {
            var tokenText = token.Text;
            var kind = token.Kind();

            if (SyntaxFacts.IsKeywordKind(kind))
                return tokenText.RichColor(k_ColorAzure);
            if (IsTypeIdentifier(token) || IsMethodReturnType(token) || IsClassDeclaration(token))
                return tokenText.RichColor(k_ColorLavender);
            if (IsMethodDeclaration(token))
                return tokenText.RichColor(k_ColorTurquoise);
            if (kind is SyntaxKind.StringLiteralToken or SyntaxKind.InterpolatedStringTextToken)
                return tokenText.RichColor(k_ColorSand);

            return tokenText;
        }

        static string ProcessTrivia(SyntaxTrivia trivia)
        {
            if (trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
                return trivia.ToFullString().RichColor(k_ColorLime);

            return trivia.ToFullString();
        }

        static bool IsClassDeclaration(SyntaxToken token)
        {
            return token.Parent is ClassDeclarationSyntax classNode && token == classNode.Identifier;
        }

        static bool IsMethodDeclaration(SyntaxToken token)
        {
            return token.Parent is MethodDeclarationSyntax methodNode && token == methodNode.Identifier;
        }

        static bool IsTypeIdentifier(SyntaxToken token)
        {
            return token.Parent is IdentifierNameSyntax identifierName &&
                identifierName.Parent is VariableDeclarationSyntax;
        }

        static bool IsMethodReturnType(SyntaxToken token) =>
            token.Parent is PredefinedTypeSyntax or IdentifierNameSyntax
            && token.Parent.Parent is MethodDeclarationSyntax methodDeclaration
            && methodDeclaration.ReturnType == token.Parent;
    }
}
