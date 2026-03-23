using System;
using Newtonsoft.Json;
using Unity.AI.Assistant.Editor.RunCommand;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class RunCommandValidatorTool
    {
        internal const string k_FunctionId = "Unity.RunCommandValidator";
        internal const string k_CodeRequiredMessage = "Code parameter cannot be empty.";

        [Serializable]
        public struct CompileOutput
        {
            [JsonProperty("isCompilationSuccessful")] // must match Python code
            public bool IsCompilationSuccessful;

            [JsonProperty("compilationLogs")] // must match Python code
            public string CompilationLogs;

            [JsonProperty("localFixedCode")] // must match Python code
            public string LocalFixedCode;
        }

        [AgentTool("Validate that a run command is valid. Can be compiled and executed.", k_FunctionId, ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_CodeCorrectionTag)]
        public static CompileOutput RunCommandValidator([Parameter("The code to attempt to compile")] string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException(k_CodeRequiredMessage);

            var compilationSuccessful = RunCommandUtils.Builder.TryCompileCode(code, out var compilationErrors, out var compilation);
            
            return new CompileOutput
            {
                IsCompilationSuccessful = compilationSuccessful,
                CompilationLogs = compilationErrors.ToString(),
                LocalFixedCode = compilation.GetSourceCode()
            };
        }
    }
}
