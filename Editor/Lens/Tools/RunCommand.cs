using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Tools.RunCommandSupport;
using Becool.UnityMcpLens.Editor.Utils;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Tools
{
    /// <summary>
    /// Handles compilation and execution of C# scripts in the Unity environment.
    /// Combines validation and execution through MCP-owned Lens command support.
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
                            failureStage = new { type = "string", description = "The phase where execution failed: validation, compilation, execution, result_serialization, transport_unknown, or unknown" },
                            errorKind = new { type = "string", description = "Stable machine-readable failure classification when available" },
                            exceptionType = new { type = "string", description = "Captured exception type when available" },
                            exceptionMessage = new { type = "string", description = "Captured exception message when available" },
                            compilationLogsDetailRef = new { type = "object", description = "Detail ref for full compilation logs when omitted or truncated" },
                            executionLogsDetailRef = new { type = "object", description = "Detail ref for full execution logs when omitted or truncated" },
                            consoleLogsDetailRef = new { type = "object", description = "Detail ref for full captured console logs when omitted or truncated" },
                            logCounts = new { type = "object", description = "Counts of execution log, warning, and error entries" },
                            validationSummary = new { type = "object", description = "Structured validation summary for the command" },
                            playStateRestored = new { type = "boolean", description = "Whether the tool restored the pre-execution play pause state" },
                            localFixedCodeChanged = new { type = "boolean", description = "Whether Lens locally rewrote the submitted command before compilation" },
                            localFixedCodeDetailRef = new { type = "object", description = "Detail ref for the locally rewritten command when omitted from the inline response" },
                            localFixedCodeIncluded = new { type = "boolean", description = "Whether localFixedCode is included inline" },
                            returnedData = new { description = "Structured result returned from ExecutionResult.ReturnResult when included inline." },
                            returnedDataIncluded = new { type = "boolean", description = "Whether returnedData is included inline." },
                            returnedDataBytes = new { type = "integer", description = "UTF-8 byte count of the serialized returnedData payload." },
                            returnedDataDetailRef = new { type = "object", description = "Detail ref for structured returnedData when omitted from the inline response." },
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
        public static Task<object> HandleCommand(RunCommandParams parameters)
        {
            string code = parameters?.Code;
            if (string.IsNullOrWhiteSpace(code))
            {
                return Task.FromResult<object>(Response.Error("CODE_REQUIRED: code parameter cannot be empty."));
            }

            string mode = string.IsNullOrWhiteSpace(parameters?.Mode)
                ? "execute"
                : parameters.Mode.Trim().ToLowerInvariant();
            if (mode != "execute" && mode != "validate")
            {
                return Task.FromResult<object>(Response.Error("INVALID_MODE: mode must be 'execute' or 'validate'."));
            }

            bool wasPlaying = EditorApplication.isPlaying;
            bool wasPaused = EditorApplication.isPaused;
            int stepsRequested = Math.Max(0, parameters?.StepFrames ?? 0);
            bool shouldPauseForExecution = wasPlaying && ((parameters?.PausePlayMode ?? false) || stepsRequested > 0);
            bool restorePauseState = parameters?.RestorePauseState ?? true;
            bool includeLocalFixedCode = parameters?.IncludeLocalFixedCode ?? false;
            bool pauseApplied = false;
            int stepsApplied = 0;
            bool responseSuccess = false;
            string responseMessage;
            object responseData = null;
            LensCompileOutput validationResult = default;
            bool validationCompleted = false;
            LensExecutionOutput executionResult = default;
            string failureStage = "unknown";
            string errorKind = null;
            string exceptionType = null;
            string exceptionMessage = null;

            try
            {
                // Step 1: Validate the code using MCP-owned Lens command support.
                validationResult = LensRunCommandValidator.Validate(code);
                validationCompleted = true;

                if (!validationResult.IsCompilationSuccessful)
                {
                    failureStage = "compilation";
                    errorKind = validationResult.HasUnauthorizedNamespaceUsage ? "unauthorized_namespace" : "compilation_failed";
                    responseMessage = "COMPILATION_FAILED: Code failed to compile.";
                    var compilationLog = ShapeLogText("compilation_logs", validationResult.CompilationLogs, mode, out var compilationLogDetailRef, out var compilationLogTruncated, out var compilationLogBytes);
                    var localFixedCodeDetailRef = includeLocalFixedCode
                        ? null
                        : CreateLocalFixedCodeDetailRef(validationResult.LocalFixedCode, code, mode);
                    responseData = new
                    {
                        isCompilationSuccessful = false,
                        isExecutionSuccessful = false,
                        compilationLogs = compilationLog,
                        compilationLogsDetailRef = compilationLogDetailRef,
                        compilationLogsTruncated = compilationLogTruncated,
                        compilationLogsBytes = compilationLogBytes,
                        executionLogs = string.Empty,
                        consoleLogs = string.Empty,
                        localFixedCode = includeLocalFixedCode ? validationResult.LocalFixedCode : null,
                        localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                        localFixedCodeIncluded = includeLocalFixedCode,
                        localFixedCodeDetailRef = localFixedCodeDetailRef,
                        returnedData = (object)null,
                        returnedDataIncluded = false,
                        returnedDataBytes = 0,
                        returnedDataDetailRef = (object)null,
                        result = responseMessage,
                        failureStage,
                        errorKind,
                        exceptionType = (string)null,
                        exceptionMessage = (string)null,
                        logCounts = new { execution = 0, warnings = 0, errors = 0 },
                        validationSummary = new
                        {
                            isCompilationSuccessful = false,
                            localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                            compilationLogLength = validationResult.CompilationLogs?.Length ?? 0,
                            compilationLogBytes,
                            hasUnauthorizedNamespaceUsage = validationResult.HasUnauthorizedNamespaceUsage
                        }
                    };
                    goto ReturnResponse;
                }

                if (mode == "validate")
                {
                    failureStage = null;
                    responseSuccess = true;
                    responseMessage = "Command validation succeeded.";
                    var compilationLog = ShapeLogText("compilation_logs", validationResult.CompilationLogs, mode, out var compilationLogDetailRef, out var compilationLogTruncated, out var compilationLogBytes);
                    responseData = new
                    {
                        mode,
                        isCompilationSuccessful = true,
                        isExecutionSuccessful = false,
                        executionSkipped = true,
                        compilationLogs = compilationLog,
                        compilationLogsDetailRef = compilationLogDetailRef,
                        compilationLogsTruncated = compilationLogTruncated,
                        compilationLogsBytes = compilationLogBytes,
                        executionLogs = string.Empty,
                        consoleLogs = string.Empty,
                        localFixedCode = validationResult.LocalFixedCode,
                        returnedData = (object)null,
                        returnedDataIncluded = false,
                        returnedDataBytes = 0,
                        returnedDataDetailRef = (object)null,
                        result = responseMessage,
                        failureStage = (string)null,
                        errorKind = (string)null,
                        exceptionType = (string)null,
                        exceptionMessage = (string)null,
                        logCounts = new { execution = 0, warnings = 0, errors = 0 },
                        validationSummary = new
                        {
                            isCompilationSuccessful = true,
                            localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                            compilationLogLength = validationResult.CompilationLogs?.Length ?? 0,
                            compilationLogBytes,
                            hasUnauthorizedNamespaceUsage = false
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

                executionResult = LensRunCommandExecutor.Execute(validationResult.Command, parameters?.Title);

                // Return combined result
                failureStage = executionResult.IsExecutionSuccessful ? null : "execution";
                errorKind = executionResult.ErrorKind;
                exceptionType = null;
                exceptionMessage = null;
                var resultMessage = executionResult.IsExecutionSuccessful
                    ? "Command executed successfully."
                    : "Command execution failed.";

                responseSuccess = executionResult.IsExecutionSuccessful;
                responseMessage = resultMessage;
                var shapedCompilationLogs = ShapeLogText("compilation_logs", validationResult.CompilationLogs, mode, out var compilationLogsDetailRef, out var compilationLogsTruncated, out var compilationLogsBytes);
                var shapedExecutionLogs = ShapeLogText("execution_logs", executionResult.ExecutionLogs, mode, out var executionLogsDetailRef, out var executionLogsTruncated, out var executionLogsBytes);
                var shapedConsoleLogs = ShapeLogText("console_logs", executionResult.ConsoleLogs, mode, out var consoleLogsDetailRef, out var consoleLogsTruncated, out var consoleLogsBytes);
                if (!TryShapeReturnedData(
                        executionResult.ReturnedData,
                        mode,
                        out object returnedData,
                        out bool returnedDataIncluded,
                        out int returnedDataBytes,
                        out object returnedDataDetailRef,
                        out string returnedDataError))
                {
                    failureStage = "result_serialization";
                    errorKind = "result_serialization_failed";
                    exceptionType = typeof(JsonException).FullName;
                    exceptionMessage = returnedDataError;
                    responseSuccess = false;
                    responseMessage = $"RESULT_SERIALIZATION_FAILED: {returnedDataError}";
                    responseData = new
                    {
                        isCompilationSuccessful = true,
                        isExecutionSuccessful = false,
                        mode,
                        executionSkipped = false,
                        executionId = executionResult.ExecutionId,
                        compilationLogs = shapedCompilationLogs,
                        compilationLogsDetailRef,
                        compilationLogsTruncated,
                        compilationLogsBytes,
                        executionLogs = shapedExecutionLogs,
                        executionLogsDetailRef,
                        executionLogsTruncated,
                        executionLogsBytes,
                        consoleLogs = shapedConsoleLogs,
                        consoleLogsDetailRef,
                        consoleLogsTruncated,
                        consoleLogsBytes,
                        localFixedCode = includeLocalFixedCode ? validationResult.LocalFixedCode : null,
                        localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                        localFixedCodeIncluded = includeLocalFixedCode,
                        localFixedCodeDetailRef = includeLocalFixedCode
                            ? null
                            : CreateLocalFixedCodeDetailRef(validationResult.LocalFixedCode, code, mode),
                        returnedData = (object)null,
                        returnedDataIncluded = false,
                        returnedDataBytes = 0,
                        returnedDataDetailRef = (object)null,
                        result = responseMessage,
                        failureStage,
                        errorKind,
                        exceptionType,
                        exceptionMessage,
                        logCounts = new
                        {
                            execution = executionResult.LogCount,
                            warnings = executionResult.WarningCount,
                            errors = executionResult.ErrorCount
                        },
                        validationSummary = new
                        {
                            isCompilationSuccessful = true,
                            localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                            compilationLogLength = validationResult.CompilationLogs?.Length ?? 0,
                            compilationLogBytes = compilationLogsBytes,
                            hasUnauthorizedNamespaceUsage = false
                        }
                    };
                    goto ReturnResponse;
                }

                responseData = new
                {
                    isCompilationSuccessful = true,
                    isExecutionSuccessful = executionResult.IsExecutionSuccessful,
                    mode,
                    executionSkipped = false,
                    executionId = executionResult.ExecutionId,
                    compilationLogs = shapedCompilationLogs,
                    compilationLogsDetailRef,
                    compilationLogsTruncated,
                    compilationLogsBytes,
                    executionLogs = shapedExecutionLogs,
                    executionLogsDetailRef,
                    executionLogsTruncated,
                    executionLogsBytes,
                    consoleLogs = shapedConsoleLogs,
                    consoleLogsDetailRef,
                    consoleLogsTruncated,
                    consoleLogsBytes,
                    localFixedCode = includeLocalFixedCode ? validationResult.LocalFixedCode : null,
                    localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                    localFixedCodeIncluded = includeLocalFixedCode,
                    localFixedCodeDetailRef = includeLocalFixedCode
                        ? null
                        : CreateLocalFixedCodeDetailRef(validationResult.LocalFixedCode, code, mode),
                    returnedData,
                    returnedDataIncluded,
                    returnedDataBytes,
                    returnedDataDetailRef,
                    result = executionResult.IsExecutionSuccessful ? resultMessage : executionResult.FailureReason ?? resultMessage,
                    failureStage,
                    errorKind,
                    exceptionType,
                    exceptionMessage,
                    logCounts = new
                    {
                        execution = executionResult.LogCount,
                        warnings = executionResult.WarningCount,
                        errors = executionResult.ErrorCount
                    },
                    validationSummary = new
                    {
                        isCompilationSuccessful = true,
                        localFixedCodeChanged = !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                        compilationLogLength = validationResult.CompilationLogs?.Length ?? 0,
                        compilationLogBytes = compilationLogsBytes,
                        hasUnauthorizedNamespaceUsage = false
                    }
                };
            }
            catch (Exception e)
            {
                failureStage = validationCompleted ? "execution" : "validation";
                errorKind = validationCompleted ? "execution_exception" : "validation_exception";
                exceptionType = e.GetType().FullName;
                exceptionMessage = e.Message;
                responseMessage = $"UNEXPECTED_ERROR: {e.GetType().Name}: {e.Message}";
                var shapedCompilationLogs = ShapeLogText("compilation_logs", validationCompleted ? validationResult.CompilationLogs : string.Empty, mode, out var compilationLogsDetailRef, out var compilationLogsTruncated, out var compilationLogsBytes);
                var shapedExecutionLogs = ShapeLogText("execution_logs", executionResult.ExecutionLogs ?? string.Empty, mode, out var executionLogsDetailRef, out var executionLogsTruncated, out var executionLogsBytes);
                var shapedConsoleLogs = ShapeLogText("console_logs", executionResult.ConsoleLogs ?? string.Empty, mode, out var consoleLogsDetailRef, out var consoleLogsTruncated, out var consoleLogsBytes);
                responseData = new
                {
                    isCompilationSuccessful = validationCompleted && validationResult.IsCompilationSuccessful,
                    isExecutionSuccessful = false,
                    executionId = executionResult.ExecutionId,
                    compilationLogs = shapedCompilationLogs,
                    compilationLogsDetailRef,
                    compilationLogsTruncated,
                    compilationLogsBytes,
                    executionLogs = shapedExecutionLogs,
                    executionLogsDetailRef,
                    executionLogsTruncated,
                    executionLogsBytes,
                    consoleLogs = shapedConsoleLogs,
                    consoleLogsDetailRef,
                    consoleLogsTruncated,
                    consoleLogsBytes,
                    localFixedCode = validationCompleted ? validationResult.LocalFixedCode : string.Empty,
                    returnedData = (object)null,
                    returnedDataIncluded = false,
                    returnedDataBytes = 0,
                    returnedDataDetailRef = (object)null,
                    result = responseMessage,
                    failureStage,
                    errorKind,
                    exceptionType,
                    exceptionMessage,
                    logCounts = new
                    {
                        execution = executionResult.LogCount,
                        warnings = executionResult.WarningCount,
                        errors = executionResult.ErrorCount
                    },
                    validationSummary = new
                    {
                        isCompilationSuccessful = validationCompleted && validationResult.IsCompilationSuccessful,
                        localFixedCodeChanged = validationCompleted && !string.Equals(validationResult.LocalFixedCode, code, StringComparison.Ordinal),
                        compilationLogLength = validationCompleted ? (validationResult.CompilationLogs?.Length ?? 0) : 0,
                        compilationLogBytes = compilationLogsBytes,
                        hasUnauthorizedNamespaceUsage = validationCompleted && validationResult.HasUnauthorizedNamespaceUsage
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

            return Task.FromResult(BuildResponse(responseSuccess, responseMessage, responseData));
        }

        static object CreateLocalFixedCodeDetailRef(string localFixedCode, string originalCode, string mode)
        {
            if (string.IsNullOrEmpty(localFixedCode))
                return null;

            int rawBytes = PayloadBudgeting.GetUtf8ByteCount(localFixedCode);
            return ToolResultCompactor.CreateStoredDetailRef(
                ToolName,
                localFixedCode,
                rawBytes,
                new
                {
                    kind = "local_fixed_code",
                    mode,
                    changed = !string.Equals(localFixedCode, originalCode, StringComparison.Ordinal)
                });
        }

        static string ShapeLogText(
            string kind,
            string text,
            string mode,
            out object detailRef,
            out bool truncated,
            out int bytes)
        {
            text ??= string.Empty;
            bytes = PayloadBudgeting.GetUtf8ByteCount(text);
            string preview = PayloadBudgeting.CreateTextPreview(text, 80, Math.Min(4096, PayloadBudgetPolicy.MaxToolResultBytes), out truncated);
            detailRef = truncated
                ? ToolResultCompactor.CreateStoredDetailRef(
                    ToolName,
                    text,
                    bytes,
                    new
                    {
                        kind,
                        mode,
                        sha256 = PayloadBudgeting.ComputeSha256(text)
                    })
                : null;
            return preview;
        }

        static bool TryShapeReturnedData(
            object returnedData,
            string mode,
            out object inlineValue,
            out bool included,
            out int bytes,
            out object detailRef,
            out string error)
        {
            inlineValue = null;
            included = false;
            bytes = 0;
            detailRef = null;
            error = null;

            if (returnedData == null)
                return true;

            try
            {
                string serialized = JsonConvert.SerializeObject(returnedData, Formatting.None);
                bytes = PayloadBudgeting.GetUtf8ByteCount(serialized);
                if (bytes <= PayloadBudgetPolicy.MaxToolResultBytes)
                {
                    inlineValue = JsonConvert.DeserializeObject(serialized);
                    included = true;
                    return true;
                }

                detailRef = ToolResultCompactor.CreateStoredDetailRef(
                    ToolName,
                    returnedData,
                    bytes,
                    new
                    {
                        kind = "returned_data",
                        mode,
                        sha256 = PayloadBudgeting.ComputeSha256(serialized)
                    });

                if (detailRef == null)
                {
                    inlineValue = JsonConvert.DeserializeObject(serialized);
                    included = true;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
