using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles compilation and execution of C# scripts in the Unity environment.
    /// Combines validation and execution into a single operation by delegating to
    /// RunCommandValidatorTool and RunCommandTool.
    /// </summary>
    public static class RunCommand
    {
        /// <summary>
        /// Human-readable description of the Unity.RunCommand tool functionality and usage.
        /// </summary>
        public const string Description =
            @"Compile and execute a C# script in the Unity Editor.

This tool first validates that the code can be compiled, then executes it if compilation succeeds.
Args: code (required), title (optional).
Returns: compilation status, execution status, logs, and results.

This is a powerful tool that allows you to programmatically control virtually every aspect of the game, including physics, input, graphics, gameplay logic, project setting and package management.

### The Golden Template
```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // 1. Your logic here
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

        // 2. Register changes for Undo/Redo and tracking
        result.RegisterObjectCreation(cube);

        // 3. Log the result
        result.Log(""Created {0}"", cube);
    }
}
```
### Rules for Success
1. **Class Name is Mandatory**: The class MUST be named `CommandScript`. Using any other name will cause a NullReferenceException or execution failure.
2. **Use `internal` Accessibility**: Always use `internal class CommandScript`. Using `public` will cause an ""Inconsistent Accessibility"" compilation error.
3. **Use the `result` Object**:
   - **Creation**: Use `result.RegisterObjectCreation(obj)` after creating objects.
   - **Modification**: Use `result.RegisterObjectModification(obj)` BEFORE changing properties.
   - **Deletion**: Use `result.DestroyObject(obj)` instead of `Object.DestroyImmediate`.
   - **Logging**:
     - `result.Log(""Created {0}"", obj)` - Log with object references using `{0}`, `{1}`, etc.
     - `result.LogWarning(""Warning message"")` - Log warnings
     - `result.LogError(""Error message"")` - Log errors
4. **Avoid Top-Level Statements**: Always wrap your code in the class structure above.

";

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The output schema object defining the structure of successful responses.</returns>
        [McpOutputSchema("Unity.RunCommand")]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the operation succeeded" },
                    message = new { type = "string", description = "Human-readable message about the operation" },
                    data = new
                    {
                        type = "object",
                        description = "Execution result data",
                        properties = new
                        {
                            isCompilationSuccessful = new { type = "boolean", description = "Whether the code compiled successfully" },
                            isExecutionSuccessful = new { type = "boolean", description = "Whether the code executed successfully" },
                            executionId = new { type = "integer", description = "ID of the execution" },
                            compilationLogs = new { type = "string", description = "Logs from the compilation process" },
                            executionLogs = new { type = "string", description = "Logs from the execution process" },
                            consoleLogs = new { type = "string", description = "Warnings, errors, and exceptions captured from the Unity console during execution" },
                            localFixedCode = new { type = "string", description = "Code with local fixes applied (if any)" },
                            result = new { type = "string", description = "Human-readable result message" },
                            failureStage = new { type = "string", description = "The phase where execution failed: validation, execution, result_serialization, or unknown" },
                            exceptionType = new { type = "string", description = "Captured exception type when available" },
                            exceptionMessage = new { type = "string", description = "Captured exception message when available" },
                            validationSummary = new { type = "object", description = "Structured validation summary for the command" },
                            playStateRestored = new { type = "boolean", description = "Whether the tool restored the pre-execution play pause state" },
                            playModeExecution = new
                            {
                                type = "object",
                                description = "Optional play-mode pause and step summary for this execution.",
                                properties = new
                                {
                                    wasPlaying = new { type = "boolean", description = "Whether the editor was already in play mode before execution." },
                                    wasPaused = new { type = "boolean", description = "Whether play mode was already paused before execution." },
                                    pauseApplied = new { type = "boolean", description = "Whether the tool paused play mode before execution." },
                                    stepsRequested = new { type = "integer", description = "How many play-mode steps were requested before execution." },
                                    stepsApplied = new { type = "integer", description = "How many play-mode steps were applied before execution." },
                                    isPausedAfter = new { type = "boolean", description = "Whether play mode was paused after execution cleanup completed." }
                                }
                            }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// The MCP tool name used to identify this tool in the registry.
        /// </summary>
        public const string ToolName = "Unity.RunCommand";

        /// <summary>
        /// Main handler for script compilation and execution.
        /// </summary>
        /// <param name="parameters">Parameters containing the script code and optional title.</param>
        /// <returns>A response object indicating success or failure with compilation and execution details.</returns>
        [McpTool(ToolName, Description, Groups = new string[] { "core", "scripting" }, EnabledByDefault = true)]
        public static async Task<object> HandleCommand(RunCommandParams parameters)
        {
            string code = parameters?.Code;
            if (string.IsNullOrWhiteSpace(code))
            {
                return Response.Error("CODE_REQUIRED: code parameter cannot be empty.");
            }

            bool wasPlaying = EditorApplication.isPlaying;
            bool wasPaused = EditorApplication.isPaused;
            int stepsRequested = Math.Max(0, parameters?.StepFrames ?? 0);
            bool shouldPauseForExecution = wasPlaying && ((parameters?.PausePlayMode ?? false) || stepsRequested > 0);
            bool restorePauseState = parameters?.RestorePauseState ?? true;
            bool pauseApplied = false;
            int stepsApplied = 0;
            bool responseSuccess = false;
            string responseMessage;
            object responseData = null;
            RunCommandValidatorTool.CompileOutput validationResult = default;
            bool validationCompleted = false;
            RunCommandTool.ExecutionOutput executionResult = default;
            string failureStage = "unknown";
            string exceptionType = null;
            string exceptionMessage = null;

            try
            {
                // Step 1: Validate the code using RunCommandValidatorTool
                validationResult = RunCommandValidatorTool.RunCommandValidator(code);
                validationCompleted = true;

                if (!validationResult.IsCompilationSuccessful)
                {
                    failureStage = "validation";
                    responseMessage = "COMPILATION_FAILED: Code failed to compile.";
                    responseData = new
                    {
                        isCompilationSuccessful = false,
                        isExecutionSuccessful = false,
                        compilationLogs = validationResult.CompilationLogs,
                        executionLogs = string.Empty,
                        consoleLogs = string.Empty,
                        localFixedCode = validationResult.LocalFixedCode,
                        result = responseMessage,
                        failureStage,
                        exceptionType = (string)null,
                        exceptionMessage = (string)null,
                        validationSummary = new
                        {
                            isCompilationSuccessful = false,
                            localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                            compilationLogLength = validationResult.CompilationLogs?.Length ?? 0
                        }
                    };
                    goto ReturnResponse;
                }

                if (shouldPauseForExecution)
                {
                    EditorApplication.isPaused = true;
                    pauseApplied = EditorApplication.isPaused;
                }

                if (stepsRequested > 0 && EditorApplication.isPlaying)
                {
                    for (int stepIndex = 0; stepIndex < stepsRequested; stepIndex++)
                    {
                        if (!EditorApplication.isPlaying)
                        {
                            break;
                        }

                        EditorApplication.Step();
                        stepsApplied += 1;
                    }
                }

                // Step 2: Execute the command using RunCommandTool
                // Create a ToolExecutionContext for MCP calls using the factory
                var toolParams = new JObject
                {
                    ["code"] = code,
                    ["title"] = parameters?.Title ?? string.Empty
                };
                var context = ToolExecutionContextFactory.CreateForExternalCall(
                    RunCommandTool.k_FunctionId,
                    toolParams);

                executionResult = await RunCommandTool.ExecuteCommand(
                    context,
                    code,
                    parameters?.Title);

                // Return combined result
                failureStage = executionResult.IsExecutionSuccessful ? null : "execution";
                exceptionType = null;
                exceptionMessage = null;
                var resultMessage = executionResult.IsExecutionSuccessful
                    ? "Command executed successfully."
                    : "Command execution failed.";

                responseSuccess = executionResult.IsExecutionSuccessful;
                responseMessage = resultMessage;
                responseData = new
                {
                    isCompilationSuccessful = true,
                    isExecutionSuccessful = executionResult.IsExecutionSuccessful,
                    executionId = executionResult.ExecutionId,
                    compilationLogs = validationResult.CompilationLogs,
                    executionLogs = executionResult.ExecutionLogs,
                    consoleLogs = string.Empty,
                    localFixedCode = validationResult.LocalFixedCode,
                    result = resultMessage,
                    failureStage,
                    exceptionType,
                    exceptionMessage,
                    validationSummary = new
                    {
                        isCompilationSuccessful = true,
                        localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                        compilationLogLength = validationResult.CompilationLogs?.Length ?? 0
                    }
                };
            }
            catch (Exception e)
            {
                failureStage = validationCompleted ? "execution" : "validation";
                exceptionType = e.GetType().FullName;
                exceptionMessage = e.Message;
                responseMessage = $"UNEXPECTED_ERROR: {e.GetType().Name}: {e.Message}";
                responseData = new
                {
                    isCompilationSuccessful = validationCompleted && validationResult.IsCompilationSuccessful,
                    isExecutionSuccessful = false,
                    executionId = executionResult.ExecutionId,
                    compilationLogs = validationCompleted ? validationResult.CompilationLogs : string.Empty,
                    executionLogs = executionResult.ExecutionLogs ?? string.Empty,
                    consoleLogs = string.Empty,
                    localFixedCode = validationCompleted ? validationResult.LocalFixedCode : string.Empty,
                    result = responseMessage,
                    failureStage,
                    exceptionType,
                    exceptionMessage,
                    validationSummary = new
                    {
                        isCompilationSuccessful = validationCompleted && validationResult.IsCompilationSuccessful,
                        localFixedCodeChanged = validationCompleted && !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                        compilationLogLength = validationCompleted ? (validationResult.CompilationLogs?.Length ?? 0) : 0
                    }
                };
            }
            finally
            {
                if (restorePauseState && shouldPauseForExecution && wasPlaying)
                {
                    EditorApplication.isPaused = wasPaused;
                }
            }

        ReturnResponse:
            object BuildResponse(bool success, string message, object data)
            {
                var playModeExecution = new
                {
                    wasPlaying,
                    wasPaused,
                    pauseApplied,
                    stepsRequested,
                    stepsApplied,
                    isPausedAfter = EditorApplication.isPaused
                };

                object combinedData = CombineResponseData(data, playModeExecution);
                return success
                    ? Response.Success(message, combinedData)
                    : Response.Error(message, combinedData);
            }

            object CombineResponseData(object data, object playModeExecution)
            {
                JObject combined = data != null
                    ? JObject.FromObject(data)
                    : new JObject();

                combined["playStateRestored"] = JToken.FromObject(!shouldPauseForExecution || !restorePauseState || EditorApplication.isPaused == wasPaused);
                combined["playModeExecution"] = JToken.FromObject(playModeExecution);
                return combined;
            }

            return BuildResponse(responseSuccess, responseMessage, responseData);
        }
    }
}
