using System;
using Newtonsoft.Json;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class CompilerTool
    {
        internal const string k_FunctionId = "Unity.Muse.Chat.Backend.Socket.Tools.Compiler";

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

        [AgentTool("Let's you try to compile code on the frontend from the backend",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_CodeCorrectionTag)]
        public static CompileOutput Compiler([Parameter("The code to attempt to compile")] string code)
        {
            var compilationSuccessful = new DynamicAssemblyBuilder("Unity.Assistant.CodeGen")
                .TryCompileCode(code, out var compilationErrors, out var compilation);

            return new CompileOutput
            {
                IsCompilationSuccessful = compilationSuccessful,
                CompilationLogs = compilationErrors.ToString(),
                LocalFixedCode = compilation.GetSourceCode()
            };
        }
    }
}
