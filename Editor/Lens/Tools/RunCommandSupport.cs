using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Becool.UnityMcpLens.Editor.Utils;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools.RunCommandSupport
{
    public interface IRunCommand
    {
        void Execute(ExecutionResult result);
    }

    [Serializable]
    public struct ExecutionLog
    {
        public string Log;
        public LogType LogType;

        public ExecutionLog(string logTemplate, LogType logType, object[] references = null)
        {
            LogType = logType;
            Log = ExecutionResult.FormatLogTemplate(logTemplate, references);
        }
    }

    [Serializable]
    public class ExecutionResult
    {
        public static readonly Regex PlaceholderRegex = new(@"^(\d+)(?:,(-?\d+))?(?::(.+))?$", RegexOptions.Compiled);

        int m_UndoGroup;

        public int Id = 1;
        public readonly string CommandName;
        public List<ExecutionLog> Logs = new();
        public string ConsoleLogs;
        public bool SuccessfullyStarted;

        public ExecutionResult(string commandName) => CommandName = commandName;

        public void RegisterObjectCreation(Object objectCreated)
        {
            if (objectCreated != null)
                Undo.RegisterCreatedObjectUndo(objectCreated, $"{objectCreated.name} was created");
        }

        public void RegisterObjectCreation(Component component)
        {
            if (component != null)
                Undo.RegisterCreatedObjectUndo(component, $"{component} was attached to {component.gameObject.name}");
        }

        public void RegisterObjectModification(Object objectToRegister, string operationDescription = "")
        {
            if (objectToRegister == null)
                return;

            if (!string.IsNullOrEmpty(operationDescription))
                Undo.RecordObject(objectToRegister, operationDescription);
            else
                Undo.RegisterCompleteObjectUndo(objectToRegister, $"{objectToRegister.name} was modified");
        }

        public void DestroyObject(Object objectToDestroy)
        {
            if (objectToDestroy == null)
                return;

            if (EditorUtility.IsPersistent(objectToDestroy))
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(objectToDestroy));
            }
            else if (!EditorApplication.isPlaying)
            {
                Undo.DestroyObjectImmediate(objectToDestroy);
            }
            else
            {
                Object.Destroy(objectToDestroy);
            }
        }

        public void Start()
        {
            SuccessfullyStarted = true;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(CommandName ?? "Run command execution");
            m_UndoGroup = Undo.GetCurrentGroup();
            Application.logMessageReceived += HandleConsoleLog;
        }

        public void End()
        {
            Application.logMessageReceived -= HandleConsoleLog;
            Undo.CollapseUndoOperations(m_UndoGroup);
        }

        public void Log(string log, params object[] references) => Logs.Add(new ExecutionLog(log, LogType.Log, references));
        public void LogWarning(string log, params object[] references) => Logs.Add(new ExecutionLog(log, LogType.Warning, references));
        public void LogError(string log, params object[] references) => Logs.Add(new ExecutionLog(log, LogType.Error, references));

        internal static string FormatLogTemplate(string logTemplate, object[] arguments)
        {
            if (string.IsNullOrEmpty(logTemplate) || arguments == null || arguments.Length == 0)
                return logTemplate;

            var builder = new StringBuilder(logTemplate.Length + 16);
            for (var index = 0; index < logTemplate.Length; index++)
            {
                var current = logTemplate[index];
                if (current == '{')
                {
                    if (index + 1 < logTemplate.Length && logTemplate[index + 1] == '{')
                    {
                        builder.Append('{');
                        index++;
                        continue;
                    }

                    var endIndex = logTemplate.IndexOf('}', index + 1);
                    if (endIndex < 0)
                    {
                        builder.Append(current);
                        continue;
                    }

                    var placeholder = logTemplate.Substring(index + 1, endIndex - index - 1);
                    builder.Append(TryFormatPlaceholder(placeholder, arguments, out var formatted) ? formatted : "{" + placeholder + "}");
                    index = endIndex;
                    continue;
                }

                if (current == '}' && index + 1 < logTemplate.Length && logTemplate[index + 1] == '}')
                {
                    builder.Append('}');
                    index++;
                    continue;
                }

                builder.Append(current);
            }

            return builder.ToString();
        }

        static bool TryFormatPlaceholder(string placeholder, object[] arguments, out string formatted)
        {
            formatted = null;
            var match = PlaceholderRegex.Match(placeholder ?? string.Empty);
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out var index) || index < 0 || index >= arguments.Length)
                return false;

            var alignment = 0;
            if (match.Groups[2].Success)
                int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out alignment);

            var scalar = FormatArgumentToken(arguments[index], match.Groups[3].Success ? match.Groups[3].Value : null);
            formatted = alignment == 0
                ? scalar
                : alignment < 0
                    ? scalar.PadRight(-alignment)
                    : scalar.PadLeft(alignment);
            return true;
        }

        static string FormatArgumentToken(object argument, string format)
        {
            if (argument is Object unityObject)
                return unityObject == null ? "[null]" : $"[{unityObject.name}|InstanceID:{unityObject.GetInstanceID()}]";

            if (argument == null)
                return "null";

            try
            {
                if (argument is IFormattable formattable)
                    return $"[{formattable.ToString(format, CultureInfo.InvariantCulture)}]";
            }
            catch (FormatException)
            {
            }

            return $"[{argument}]";
        }

        void HandleConsoleLog(string logString, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                ConsoleLogs += $"{type}: {logString}\n";
        }

        public List<string> GetFormattedLogs()
        {
            return Logs == null
                ? new List<string>()
                : Logs.Where(log => !string.IsNullOrEmpty(log.Log)).Select(log => $"[{log.LogType}] {log.Log}").ToList();
        }
    }

    [Serializable]
    struct LensCompileOutput
    {
        public bool IsCompilationSuccessful;
        public string CompilationLogs;
        public string LocalFixedCode;
    }

    [Serializable]
    struct LensExecutionOutput
    {
        public bool IsExecutionSuccessful;
        public int ExecutionId;
        public string ExecutionLogs;
    }

    static class LensRunCommandValidator
    {
        public static LensCompileOutput Validate(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Code parameter cannot be empty.");

            var success = LensRunCommandCompiler.TryCompileCode(code, out var errors, out var compilation, out var localFixedCode);
            return new LensCompileOutput
            {
                IsCompilationSuccessful = success,
                CompilationLogs = errors.ToString(),
                LocalFixedCode = localFixedCode ?? compilation?.SyntaxTrees.FirstOrDefault()?.GetText().ToString() ?? code
            };
        }
    }

    static class LensRunCommandExecutor
    {
        const string k_DynamicCommandFullClassName = LensRunCommandCompiler.DynamicCommandNamespace + ".CommandScript";

        public static LensExecutionOutput Execute(string code, string title)
        {
            var agentCommand = LensRunCommandCompiler.BuildRunCommand(code);
            if (agentCommand == null)
                throw new InvalidOperationException("Failed to build Lens command.");

            if (!agentCommand.CompilationSuccess)
                throw new InvalidOperationException($"Command compilation failed:\n{agentCommand.CompilationErrors}");

            var executionResult = ExecuteCompiled(agentCommand, title);
            var formattedLogs = executionResult?.Logs == null ? string.Empty : string.Join("\n", executionResult.GetFormattedLogs());

            if (executionResult == null || !executionResult.SuccessfullyStarted)
                throw new InvalidOperationException($"Execution failed:\n{(string.IsNullOrEmpty(formattedLogs) ? "No logs available" : formattedLogs)}");

            if (executionResult.Logs != null && executionResult.Logs.Any(log =>
                    log.LogType == LogType.Warning ||
                    log.LogType == LogType.Error ||
                    log.LogType == LogType.Exception))
            {
                throw new InvalidOperationException(
                    $"Command was executed partially, but reported warnings or errors:\n{(string.IsNullOrEmpty(formattedLogs) ? "No logs available" : formattedLogs)}\nConsider reverting changes that may have happened if you retry.");
            }

            return new LensExecutionOutput
            {
                IsExecutionSuccessful = true,
                ExecutionId = executionResult.Id,
                ExecutionLogs = formattedLogs
            };
        }

        static ExecutionResult ExecuteCompiled(LensRunCommand command, string title)
        {
            using var stream = new MemoryStream();
            var result = command.Compilation.Emit(stream);
            if (!result.Success)
                return new ExecutionResult(title) { SuccessfullyStarted = false };

            stream.Seek(0, SeekOrigin.Begin);
            var assembly = AssemblyUtils.LoadFromBytes(stream.ToArray());
            var commandType = assembly.GetType(k_DynamicCommandFullClassName);
            var instance = commandType == null ? null : Activator.CreateInstance(commandType) as IRunCommand;
            command.SetInstance(instance);
            command.Execute(out var executionResult, title);
            return executionResult;
        }
    }

    class LensRunCommand
    {
        IRunCommand m_Instance;
        bool m_IsUnsafe;

        public string Script { get; set; }
        public CompilationErrors CompilationErrors { get; set; }
        public bool Unsafe => m_IsUnsafe;
        public bool CompilationSuccess;
        internal CSharpCompilation Compilation { get; set; }

        public void Initialize(CSharpCompilation compilation)
        {
            Compilation = compilation;
            m_IsUnsafe = LensRunCommandCompiler.HasUnsafeCalls(compilation);
        }

        public bool HasUnauthorizedNamespaceUsage() => LensRunCommandCompiler.HasUnauthorizedNamespaceUsage(Script);

        public void SetInstance(IRunCommand commandInstance) => m_Instance = commandInstance;

        public bool Execute(out ExecutionResult executionResult, string title)
        {
            executionResult = new ExecutionResult(title);
            if (m_Instance == null)
                return false;

            executionResult.Start();
            try
            {
                m_Instance.Execute(executionResult);
                if (Unsafe)
                    AssetDatabase.Refresh();
            }
            catch (Exception ex)
            {
                executionResult.LogError(ex.ToString());
            }
            finally
            {
                executionResult.End();
            }

            return true;
        }
    }

    class CompilationErrors
    {
        readonly List<string> m_Errors = new();

        public void Add(string message, int line = -1)
        {
            m_Errors.Add(line >= 0 ? $"- Error {message} (Line: {line + 1})" : $"- Error {message}");
        }

        public override string ToString() => string.Join("\n", m_Errors);
    }

    static class LensRunCommandCompiler
    {
        internal const string DynamicAssemblyName = "Unity.MCP.Lens.DynamicCommand.Editor";
        internal const string DynamicCommandNamespace = "Becool.UnityMcpLens.Editor.Tools.RunCommandSupport";

        static readonly string[] k_UnauthorizedNamespaces = { "System.Net", "System.Diagnostics", "System.Runtime.InteropServices", "System.Reflection" };
        static readonly string[] k_UnsafeMethods =
        {
            "UnityEditor.AssetDatabase.DeleteAsset",
            "UnityEditor.FileUtil.DeleteFileOrDirectory",
            "System.IO.File.Delete",
            "System.IO.Directory.Delete",
            "System.IO.File.Move",
            "System.IO.Directory.Move"
        };

        static readonly object s_ReferenceLock = new();
        static List<MetadataReference> s_References;

        public static LensRunCommand BuildRunCommand(string code)
        {
            var success = TryCompileCode(code, out var errors, out var compilation, out var localFixedCode);
            var command = new LensRunCommand
            {
                CompilationErrors = errors,
                Script = localFixedCode
            };

            if (command.HasUnauthorizedNamespaceUsage())
            {
                command.CompilationSuccess = false;
            }
            else if (success)
            {
                command.CompilationSuccess = true;
                command.Initialize(compilation);
            }

            return command;
        }

        public static bool TryCompileCode(string code, out CompilationErrors errors, out CSharpCompilation compilation, out string localFixedCode)
        {
            errors = new CompilationErrors();
            localFixedCode = code;
            compilation = null;

            if (string.IsNullOrWhiteSpace(code))
            {
                errors.Add("Compilation error: script is empty");
                return false;
            }

            var tree = ParseIntoCommandNamespace(code);
            localFixedCode = tree.GetText().ToString();
            compilation = CSharpCompilation.Create(DynamicAssemblyName)
                .WithOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                .AddReferences(GetReferences())
                .AddSyntaxTrees(tree);

            var diagnostics = compilation.GetDiagnostics();
            errors = BuildCompilationErrors(diagnostics);
            return !diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        }

        static SyntaxTree ParseIntoCommandNamespace(string code)
        {
            var tree = SyntaxFactory.ParseSyntaxTree(code);
            var root = tree.GetCompilationUnitRoot();
            var existingNamespace = root.Members.OfType<NamespaceDeclarationSyntax>().FirstOrDefault();
            if (existingNamespace != null)
            {
                var updated = existingNamespace.Name.ToString() == DynamicCommandNamespace
                    ? root
                    : root.ReplaceNode(existingNamespace, existingNamespace.WithName(SyntaxFactory.ParseName(DynamicCommandNamespace)));
                return CSharpSyntaxTree.Create((CompilationUnitSyntax)updated.NormalizeWhitespace());
            }

            var typeDeclarations = root.Members.OfType<BaseTypeDeclarationSyntax>().ToArray();
            if (typeDeclarations.Length == 0)
                return tree;

            var strippedRoot = (CompilationUnitSyntax)root.RemoveNodes(typeDeclarations, SyntaxRemoveOptions.KeepNoTrivia);
            var namespaceDeclaration = SyntaxFactory.NamespaceDeclaration(SyntaxFactory.ParseName(DynamicCommandNamespace))
                .AddMembers(typeDeclarations.Cast<MemberDeclarationSyntax>().ToArray());
            return CSharpSyntaxTree.Create(strippedRoot.AddMembers(namespaceDeclaration).NormalizeWhitespace());
        }

        static List<MetadataReference> GetReferences()
        {
            lock (s_ReferenceLock)
            {
                if (s_References != null)
                    return s_References;

                var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
                void AddAssembly(Assembly assembly)
                {
                    try
                    {
                        if (assembly == null || assembly.IsDynamic)
                            return;

                        var path = AssemblyUtils.GetAssemblyPath(assembly);
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && !references.ContainsKey(path))
                            references[path] = MetadataReference.CreateFromFile(path);
                    }
                    catch
                    {
                    }
                }

                AddAssembly(typeof(object).Assembly);
                AddAssembly(typeof(Enumerable).Assembly);
                AddAssembly(typeof(Application).Assembly);
                AddAssembly(typeof(UnityEditor.Editor).Assembly);
                AddAssembly(typeof(IRunCommand).Assembly);

                foreach (var assembly in AssemblyUtils.GetLoadedAssemblies())
                    AddAssembly(assembly);

                s_References = references.Values.ToList();
                return s_References;
            }
        }

        static CompilationErrors BuildCompilationErrors(ImmutableArray<Diagnostic> diagnostics)
        {
            var errors = new CompilationErrors();
            foreach (var diagnostic in diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            {
                var location = diagnostic.Location;
                var line = location.IsInSource ? location.GetLineSpan().StartLinePosition.Line : -1;
                errors.Add($"{diagnostic.Id}: {diagnostic.GetMessage()}", line);
            }

            return errors;
        }

        public static bool HasUnauthorizedNamespaceUsage(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return false;

            var root = SyntaxFactory.ParseSyntaxTree(script).GetCompilationUnitRoot();
            foreach (var usingDirective in root.Usings)
            {
                var namespaceName = usingDirective.Name?.ToString();
                if (k_UnauthorizedNamespaces.Any(disallowed => namespaceName != null && namespaceName.StartsWith(disallowed, StringComparison.Ordinal)))
                    return true;
            }

            return root.DescendantNodes()
                .OfType<MemberAccessExpressionSyntax>()
                .Select(node => node.ToString())
                .Any(name => k_UnauthorizedNamespaces.Any(disallowed => name.StartsWith(disallowed, StringComparison.Ordinal)));
        }

        public static bool HasUnsafeCalls(CSharpCompilation compilation)
        {
            var tree = compilation.SyntaxTrees.FirstOrDefault();
            if (tree == null)
                return false;

            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
                    continue;

                var name = GetFullMethodName(method);
                if (k_UnsafeMethods.Contains(name, StringComparer.Ordinal))
                    return true;
            }

            return false;
        }

        static string GetFullMethodName(IMethodSymbol method)
        {
            var parts = new List<string> { method.Name };
            var type = method.ContainingType;
            while (type != null)
            {
                parts.Add(type.Name);
                type = type.ContainingType;
            }

            if (method.ContainingNamespace is { IsGlobalNamespace: false })
                parts.AddRange(method.ContainingNamespace.ToDisplayString().Split('.').Reverse());

            parts.Reverse();
            return string.Join(".", parts);
        }
    }
}
