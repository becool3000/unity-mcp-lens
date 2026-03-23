using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Utils;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    class ExpressionEvaluator
    {
        const string k_AssemblyName = "Unity.AI.Assistant.Agent.ExpressionEvaluator";
        const string k_ClassName = "ScriptedEvaluator";
        const string k_MethodName = "Evaluate";

        readonly List<MetadataReference> k_CachedReferences = GetRequiredReferences();

        public object Evaluate(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("Expression cannot be null or empty", nameof(expression));

            try
            {
                var syntaxTree = GenerateSyntaxTree(expression);
                var compilation = CreateCompilation(syntaxTree);
                var assemblyBytes = CompileToBytes(compilation);

                return ExecuteExpression(assemblyBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Evaluation failed: {ex.Message}", ex);
            }
        }

        static SyntaxTree GenerateSyntaxTree(string expression)
        {
            var expressionSyntax = ParseExpression(expression);

            var compilationUnit = CompilationUnit()
                .WithUsings(List(new[]
                {
                    UsingDirective(ParseName("System")),
                    UsingDirective(ParseName("System.Collections.Generic")),
                    UsingDirective(ParseName("System.Linq")),
                    UsingDirective(ParseName("UnityEngine"))
                }))
                .WithMembers(List(new MemberDeclarationSyntax[]
                {
                    CreateEvaluatorClass(expressionSyntax)
                }))
                .NormalizeWhitespace();

            return CSharpSyntaxTree.Create(compilationUnit);
        }

        static ClassDeclarationSyntax CreateEvaluatorClass(ExpressionSyntax expressionSyntax)
        {
            return ClassDeclaration(k_ClassName)
                .WithModifiers(TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.StaticKeyword)))
                .WithMembers(List(new MemberDeclarationSyntax[]
                {
                    CreateEvaluateMethod(expressionSyntax)
                }));
        }

        static MethodDeclarationSyntax CreateEvaluateMethod(ExpressionSyntax expressionSyntax)
        {
            return MethodDeclaration(
                    PredefinedType(Token(SyntaxKind.ObjectKeyword)),
                    Identifier(k_MethodName))
                .WithModifiers(TokenList(
                    Token(SyntaxKind.PublicKeyword),
                    Token(SyntaxKind.StaticKeyword)))
                .WithBody(Block(
                    ReturnStatement(expressionSyntax)));
        }

        CSharpCompilation CreateCompilation(SyntaxTree syntaxTree)
        {
            return CSharpCompilation.Create(
                k_AssemblyName,
                new[] { syntaxTree },
                k_CachedReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );
        }

        static List<MetadataReference> GetRequiredReferences()
        {
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(ValueType))),
                MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(Enumerable))),
                MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(Math))),
                MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(List<>))),
                MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(GameObject)))
            };

#if UNITY_6000_5_OR_NEWER
            var netStandardAssembly = AssemblyUtils.GetLoadedAssemblies()
                .FirstOrDefault(a => a.FullName == "netstandard, Version=2.1.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
#else
            var netStandardAssembly = Assembly.Load("netstandard, Version=2.1.0.0, Culture=neutral, PublicKeyToken=cc7b13ffcd2ddd51");
#endif
            if (netStandardAssembly != null)
            {
                var assemblyPath = AssemblyUtils.GetAssemblyPath(netStandardAssembly);
                if (!string.IsNullOrEmpty(assemblyPath))
                {
                    references.Add(MetadataReference.CreateFromFile(assemblyPath));
                }
            }

            return references;
        }

        static byte[] CompileToBytes(CSharpCompilation compilation)
        {
            using var memoryStream = new MemoryStream();
            var emitResult = compilation.Emit(memoryStream);

            if (!emitResult.Success)
            {
                var errors = GetCompilationErrors(emitResult);
                throw new InvalidOperationException($"Compilation failed:\n{errors}");
            }

            return memoryStream.ToArray();
        }

        static string GetCompilationErrors(EmitResult emitResult)
        {
            var failures = emitResult.Diagnostics
                .Where(diagnostic => diagnostic.IsWarningAsError ||
                                   diagnostic.Severity == DiagnosticSeverity.Error);

            return string.Join("\n", failures.Select(f => f.GetMessage()));
        }

        static object ExecuteExpression(byte[] assemblyBytes)
        {
            var assembly = AssemblyUtils.LoadFromBytes(assemblyBytes);
            var type = assembly.GetType(k_ClassName);
            var method = type?.GetMethod(k_MethodName);

            if (method == null)
                throw new InvalidOperationException($"Could not find method '{k_MethodName}' in generated assembly");

            return method.Invoke(null, null);
        }
    }
}
