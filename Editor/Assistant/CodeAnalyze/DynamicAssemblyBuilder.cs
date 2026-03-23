using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Unity.AI.Assistant.Utils;
using UnityEditor.Compilation;
using UnityEngine;
using Assembly = System.Reflection.Assembly;

namespace Unity.AI.Assistant.Editor.CodeAnalyze
{
    partial class DynamicAssemblyBuilder
    {
        // const string k_CompilationSuccessfulMessage = "Compilation successful";
        static readonly string[] k_CuratedAssemblyPrefixes = { "Assembly-CSharp", "UnityEngine", "UnityEditor", "Unity.", "netstandard" };
        
        static Dictionary<string, ReportDiagnostic> k_SupressedDiagnosticOptions = new()
        {
            { "CS0168", ReportDiagnostic.Suppress }, // The variable is declared but never used
            { "CS8321", ReportDiagnostic.Suppress }, // The local function is declared but never used
            { "CS0219", ReportDiagnostic.Suppress } // The variable is assigned but its value is never used
        };

        List<MetadataReference> m_References = new();
        List<CSharpFixProvider> m_FixProviders = new()
        {
            new FixMissingImports(),
            new FixMissingParenthesis(),
            new FixMissingBrace(),
            new FixMissingSquareBracket(),
            new FixMissingSemicolon(),
            new FixAmbiguousReference()
        };

        string m_AssemblyName;
        string m_DefaultNamespace;

        public DynamicAssemblyBuilder(string assemblyName, string defaultNamespace = null)
        {
            m_AssemblyName = assemblyName;
            m_DefaultNamespace = defaultNamespace;

            InitializeReferences();
        }

        public void AddReferences(List<string> additionalReferences)
        {
            foreach (var referencePath in additionalReferences)
            {
                m_References.Add(MetadataReference.CreateFromFile(referencePath));
            }
        }

        void InitializeReferences()
        {
            m_References.Clear();

            m_References.Add(MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(object)))); // mscorlib
            m_References.Add(MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(Enumerable)))); // System.Core
            m_References.Add(MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(Application)))); // UnityEngine.CoreModule
            m_References.Add(MetadataReference.CreateFromFile(AssemblyUtils.GetAssemblyPath(typeof(UnityEditor.Editor)))); // UnityEditor.CoreModule

            // Get assembly names defined in the Assets folder
            var assetsAssemblyNames = GetAssetsAssemblyNames();

            foreach (var assembly in AssemblyUtils.GetLoadedAssemblies())
            {
                if (!assembly.IsDynamic)
                {
                    var assemblyPath = AssemblyUtils.GetAssemblyPath(assembly);
                    if (!string.IsNullOrWhiteSpace(assemblyPath))
                    {
                        // Skip assemblies whose name matches the compilation target to avoid CS1704
                        if (assembly.GetName().Name == m_AssemblyName)
                            continue;

                        // Include curated assemblies or assemblies defined in the Assets folder
                        if (k_CuratedAssemblyPrefixes.Any(prefix => assembly.FullName.StartsWith(prefix)) ||
                            assetsAssemblyNames.Contains(assembly.GetName().Name))
                            m_References.Add(MetadataReference.CreateFromFile(assemblyPath));
                    }
                }
            }
        }

        static HashSet<string> GetAssetsAssemblyNames()
        {
            var assemblyNames = new HashSet<string>();

            // Get all player assemblies (includes both Editor and Runtime assemblies defined in Assets)
            var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            foreach (var assembly in assemblies)
            {
                // Check if the assembly's asmdef is inside the Assets folder
                var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                if (!string.IsNullOrEmpty(asmdefPath) && asmdefPath.StartsWith("Assets/"))
                {
                    assemblyNames.Add(assembly.name);
                }
            }

            // Also get editor assemblies
            var editorAssemblies = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            foreach (var assembly in editorAssemblies)
            {
                var asmdefPath = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assembly.name);
                if (!string.IsNullOrEmpty(asmdefPath) && asmdefPath.StartsWith("Assets/"))
                {
                    assemblyNames.Add(assembly.name);
                }
            }

            return assemblyNames;
        }

        public bool TryCompileCode(string code, out CompilationErrors compilationErrors, out CSharpCompilation compilation)
        {
            if (string.IsNullOrEmpty(code))
            {
                compilationErrors = new CompilationErrors();
                compilationErrors.Add("Compilation error: script is empty");
                compilation = null;
                return false;
            }

            compilation = Compile(code, out var tree);
            var diagnostics = compilation.GetDiagnostics();

            var updatedTree = tree;
            // Try to repair the tree if errors are detected
            bool hasError = diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            if (hasError)
            {
                foreach (var diagnostic in diagnostics)
                {
                    if (diagnostic.Severity != DiagnosticSeverity.Error)
                        continue;

                    foreach (var fix in m_FixProviders)
                    {
                        if (fix.CanFix(diagnostic))
                        {
                            updatedTree = fix.ApplyFix(updatedTree, diagnostic);
                            InternalLog.Log($"{fix.GetType().Name} was applied:\n{updatedTree.GetText()}");
                        }
                    }
                }

                if (updatedTree != tree)
                    compilation = compilation.ReplaceSyntaxTree(tree, updatedTree);
            }

            diagnostics = compilation.GetDiagnostics();
            compilationErrors = GetCompilationErrorDescriptions(diagnostics);

            return !diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        public CSharpCompilation Compile(string code, out SyntaxTree tree)
        {
            tree = SyntaxFactory.ParseSyntaxTree(code);
            if (m_DefaultNamespace != null)
                tree = tree.MoveToNamespace(m_DefaultNamespace);

            var compilation = CSharpCompilation.Create(m_AssemblyName)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(m_References)
                .AddSyntaxTrees(tree);

            return compilation;
        }

        internal Assembly LoadAssembly(MemoryStream compilationStream)
        {
            compilationStream.Seek(0, SeekOrigin.Begin);
            return AssemblyUtils.LoadFromBytes(compilationStream.ToArray());
        }

        static CompilationErrors GetCompilationErrorDescriptions(ImmutableArray<Diagnostic> diagnostics)
        {
            CompilationErrors errors = new();

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                    continue;

                var location = diagnostic.Location;
                if (location.IsInSource)
                {
                    var lineSpan = location.GetLineSpan();
                    errors.Add($"Error {diagnostic.Id}: {diagnostic.GetMessage()}", lineSpan.StartLinePosition.Line);
                }
            }

            return errors;
        }
    }
}
