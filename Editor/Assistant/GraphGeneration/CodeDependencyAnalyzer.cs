using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.GraphGeneration
{
    /// <summary>
    /// Roslyn-based code dependency analyzer. Runs on a background thread.
    /// Uses Microsoft.CodeAnalysis.CSharp directly (no Unity API calls) so it is safe off the main thread.
    /// </summary>
    class CodeDependencyAnalyzer
    {
        public List<CodeDependencyInfo> AnalyzeCodeDependencies(IReadOnlyList<string> csFilePaths)
        {
            var dependencies = new List<CodeDependencyInfo>();

            try
            {
                InternalLog.Log("[AssistantGraphGenerator] Starting code dependency analysis...");

                InternalLog.Log($"[AssistantGraphGenerator] Found {csFilePaths.Count} C# files to analyze");

                int filesProcessed = 0;
                foreach (var filePath in csFilePaths)
                {
                    try
                    {
                        var code = File.ReadAllText(filePath);
                        var tree = CSharpSyntaxTree.ParseText(code);
                        var root = tree.GetRoot();
                        if (root == null) continue;

                        AnalyzeNode(root, filePath, dependencies);
                        filesProcessed++;
                    }
                    catch (Exception ex)
                    {
                        InternalLog.LogWarning($"[AssistantGraphGenerator] Error analyzing {filePath}: {ex.Message}");
                    }
                }

                InternalLog.Log($"[AssistantGraphGenerator] Analyzed {filesProcessed}/{csFilePaths.Count} files, found {dependencies.Count} dependencies");
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[AssistantGraphGenerator] Code dependency analysis failed: {ex.Message}");
            }

            return dependencies;
        }

        static string ConvertToUnityPath(string absolutePath)
        {
            var normalized = absolutePath.Replace("\\", "/");
            var assetsSegmentIndex = normalized.IndexOf("/Assets/", StringComparison.Ordinal);
            if (assetsSegmentIndex >= 0)
                return normalized.Substring(assetsSegmentIndex + 1);
            if (normalized.StartsWith("Assets/", StringComparison.Ordinal))
                return normalized;
            var packagesSegmentIndex = normalized.IndexOf("/Packages/", StringComparison.Ordinal);
            if (packagesSegmentIndex >= 0)
                return normalized.Substring(packagesSegmentIndex + 1);
            if (normalized.StartsWith("Packages/", StringComparison.Ordinal))
                return normalized;
            return normalized;
        }

        static int GetLineNumber(SyntaxNode node)
        {
            if (node == null) return 0;
            try
            {
                var lineSpan = node.GetLocation().GetLineSpan();
                return lineSpan.StartLinePosition.Line + 1;
            }
            catch
            {
                return 0;
            }
        }

        static void AddDependency(List<CodeDependencyInfo> dependencies, CodeDependencyType depType, string filePath, string toTypeName, SyntaxNode locationNode)
        {
            if (string.IsNullOrEmpty(toTypeName)) return;
            var unityPath = ConvertToUnityPath(filePath);
            dependencies.Add(new CodeDependencyInfo
            {
                type = depType,
                from = unityPath,
                to = toTypeName,
                references = new List<CodeReference> { new CodeReference { sourceFile = unityPath, lineNumber = GetLineNumber(locationNode) } }
            });
        }

        static void AnalyzeNode(SyntaxNode node, string filePath, List<CodeDependencyInfo> dependencies)
        {
            if (node == null) return;

            try
            {
                switch (node)
                {
                    case ClassDeclarationSyntax classDecl:
                    case StructDeclarationSyntax structDecl:
                    case InterfaceDeclarationSyntax interfaceDecl:
                    case RecordDeclarationSyntax recordDecl:
                        AnalyzeTypeDeclaration(node, filePath, dependencies);
                        break;
                }

                foreach (var child in node.ChildNodes())
                {
                    AnalyzeNode(child, filePath, dependencies);
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[AssistantGraphGenerator] Error in AnalyzeNode: {ex.Message}");
            }
        }

        static void AnalyzeTypeDeclaration(SyntaxNode node, string filePath, List<CodeDependencyInfo> dependencies)
        {
            var typeName = GetTypeDeclarationName(node);
            if (string.IsNullOrEmpty(typeName)) return;

            foreach (var child in node.ChildNodes())
            {
                switch (child)
                {
                    case BaseListSyntax baseList:
                        AnalyzeBaseList(baseList, typeName, filePath, dependencies);
                        break;
                    case FieldDeclarationSyntax fieldDecl:
                        AnalyzeFieldDeclaration(fieldDecl, typeName, filePath, dependencies);
                        break;
                    case PropertyDeclarationSyntax propertyDecl:
                        AnalyzePropertyDeclaration(propertyDecl, typeName, filePath, dependencies);
                        break;
                    case MethodDeclarationSyntax methodDecl:
                        AnalyzeMethodDeclaration(methodDecl, typeName, filePath, dependencies);
                        break;
                }
            }
        }

        static string GetTypeDeclarationName(SyntaxNode node)
        {
            return node switch
            {
                ClassDeclarationSyntax c => c.Identifier.ValueText,
                StructDeclarationSyntax s => s.Identifier.ValueText,
                InterfaceDeclarationSyntax i => i.Identifier.ValueText,
                RecordDeclarationSyntax r => r.Identifier.ValueText,
                _ => null
            };
        }

        static void AnalyzeBaseList(BaseListSyntax baseList, string typeName, string filePath, List<CodeDependencyInfo> dependencies)
        {
            foreach (var baseType in baseList.Types)
            {
                if (baseType is not SimpleBaseTypeSyntax simpleBase) continue;
                var baseName = simpleBase.Type.ToString();
                if (string.IsNullOrEmpty(baseName)) continue;

                var depType = baseName.StartsWith("I") && baseName.Length > 1 && char.IsUpper(baseName[1])
                    ? CodeDependencyType.Implements
                    : CodeDependencyType.InheritsFrom;

                AddDependency(dependencies, depType, filePath, baseName, baseType);
            }
        }

        static void AnalyzeFieldDeclaration(FieldDeclarationSyntax fieldDecl, string typeName, string filePath, List<CodeDependencyInfo> dependencies)
        {
            var fieldTypeName = fieldDecl.Declaration?.Type?.ToString();
            if (string.IsNullOrEmpty(fieldTypeName)) return;

            AddDependency(dependencies, CodeDependencyType.Declares, filePath, fieldTypeName, fieldDecl);
        }

        static void AnalyzePropertyDeclaration(PropertyDeclarationSyntax propertyDecl, string typeName, string filePath, List<CodeDependencyInfo> dependencies)
        {
            var propertyTypeName = propertyDecl.Type?.ToString();
            if (string.IsNullOrEmpty(propertyTypeName)) return;

            AddDependency(dependencies, CodeDependencyType.Declares, filePath, propertyTypeName, propertyDecl);
        }

        static void AnalyzeMethodDeclaration(MethodDeclarationSyntax methodDecl, string typeName, string filePath, List<CodeDependencyInfo> dependencies)
        {
            var returnTypeName = methodDecl.ReturnType?.ToString();
            if (!string.IsNullOrEmpty(returnTypeName) && returnTypeName != "void")
                AddDependency(dependencies, CodeDependencyType.Uses, filePath, returnTypeName, methodDecl);

            foreach (var parameter in methodDecl.ParameterList.Parameters)
            {
                var paramTypeName = parameter.Type?.ToString();
                if (string.IsNullOrEmpty(paramTypeName)) continue;
                AddDependency(dependencies, CodeDependencyType.Uses, filePath, paramTypeName, parameter);
            }
        }

    }
}
