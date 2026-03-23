using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor.RunCommand
{
    [UnityEditor.InitializeOnLoad]
    static class RunCommandUtils
    {
        const string k_DynamicAssemblyName = "Unity.AI.Assistant.Bridge.Editor";
        const string k_DynamicCommandNamespace = "Unity.AI.Assistant.Agent.Dynamic.Extension.Editor";
        const string k_DynamicCommandClassName = "CommandScript";

        const string k_DynamicCommandFullClassName = k_DynamicCommandNamespace + "." + k_DynamicCommandClassName;

        const string k_DummyCommandScript =
            "\nusing UnityEngine;\nusing UnityEditor;\n\ninternal class CommandScript : IRunCommand\n{\n    public void Execute(ExecutionResult result) {}\n}";
        
        static readonly DynamicAssemblyBuilder m_Builder = new(k_DynamicAssemblyName, k_DynamicCommandNamespace);

        public static DynamicAssemblyBuilder Builder => m_Builder;
        
        static RunCommandUtils()
        {
            // Warm up call
            Task.Run(() => m_Builder.Compile(k_DummyCommandScript, out _));
        }

        internal static AgentRunCommand BuildRunCommand(string commandScript)
        {
            var compilationSuccessful =
                m_Builder.TryCompileCode(commandScript, out var compilationLogs, out var compilation);
            
            var updatedScript = compilation.GetSourceCode();
            var runCommand =
                new AgentRunCommand() { CompilationErrors = compilationLogs, Script = updatedScript };

            if (runCommand.HasUnauthorizedNamespaceUsage())
            {
                runCommand.CompilationSuccess = false;
            }
            else if (compilationSuccessful)
            {
                runCommand.CompilationSuccess = true;
                runCommand.Initialize(compilation);
            }
            else
            {
                InternalLog.LogWarning($"Unable to compile the command:\n{compilationLogs}");
            }

            return runCommand;
        }

        internal static ExecutionResult Execute(AgentRunCommand command, string title = "")
        {
            using var stream = new MemoryStream();
            var result = command.Compilation.Emit(stream);
            if (!result.Success)
            {
                return new ExecutionResult(title) { SuccessfullyStarted = false };
            }

            stream.Seek(0, SeekOrigin.Begin);
            var agentAssembly = m_Builder.LoadAssembly(stream);
            var commandInstance = CreateRunCommandInstance(agentAssembly);
            command.SetInstance(commandInstance);

            command.Execute(out var executionResult, title);

            return executionResult;
        }

        internal static IRunCommand CreateRunCommandInstance(Assembly dynamicAssembly)
        {
            var type = dynamicAssembly.GetType(k_DynamicCommandFullClassName);
            if (type == null)
                return null;

            return Activator.CreateInstance(type) as IRunCommand;
        }
    }
}
