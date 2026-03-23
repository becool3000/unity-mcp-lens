using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using ExpressionEvaluator = Unity.AI.Assistant.Editor.CodeAnalyze.ExpressionEvaluator;

namespace Unity.AI.Assistant.Editor.RunCommand
{
    static class RunCommandCodeAnalyzer
    {
        static readonly string[] k_UnauthorizedNamespaces = { "System.Net", "System.Diagnostics", "System.Runtime.InteropServices", "System.Reflection" };

        static string[] k_UnsafeMethods = new[]
        {
            "UnityEditor.AssetDatabase.DeleteAsset",
            "UnityEditor.FileUtil.DeleteFileOrDirectory",
            "System.IO.File.Delete",
            "System.IO.Directory.Delete",
            "System.IO.File.Move",
            "System.IO.Directory.Move"
        };

        public static RunCommandMetadata AnalyzeCommandAndExtractMetadata(CSharpCompilation compilation)
        {
            var result = new RunCommandMetadata();

            var commandTree = compilation.SyntaxTrees.FirstOrDefault();
            if (commandTree == null)
                return result;

            var model = compilation.GetSemanticModel(commandTree);
            var root = commandTree.GetCompilationUnitRoot();

            var runCommandInterfaceSymbol = compilation.GetTypeByMetadataName(typeof(IRunCommand).FullName);
            if (runCommandInterfaceSymbol == null)
                return result;

            var walker = new PublicMethodCallWalker(model);
            walker.Visit(root);

            foreach (var methodCall in walker.PublicMethodCalls)
            {
                if (k_UnsafeMethods.Contains(methodCall))
                {
                    result.IsUnsafe = true;
                    break;
                }
            }

            return result;
        }

        public static bool HasUnauthorizedNamespaceUsage(string script)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(script);
            return tree.ContainsNamespaces(k_UnauthorizedNamespaces);
        }
    }
}
