using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using Unity.AI.Assistant.Editor.RunCommand;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class RunCommandTool
    {
        internal const string k_FunctionId = "Unity.RunCommand";
        internal const string k_CodeRequiredMessage = "Code parameter cannot be empty.";
        internal const string k_CommandBuildFailedMessage = "Failed to build agent command.";
        
        internal const string k_CommandExecutionFailedMessage = "Execution failed:\n{0}";
        internal const string k_CommandExecutionWarningsMessage = "Command was executed partially, but reported warnings or errors:\n{0}\nConsider reverting changes that may have happened if you retry.";

        [Serializable]
        public struct ExecutionOutput
        {
            [JsonProperty("isExecutionSuccessful")]
            public bool IsExecutionSuccessful;

            [JsonProperty("executionId")]
            public int ExecutionId;

            [JsonProperty("executionLogs")]
            public string ExecutionLogs;

            [JsonProperty("consoleLogs")]
            public string ConsoleLogs;

            [JsonProperty("failureStage")]
            public string FailureStage;

            [JsonProperty("exceptionType")]
            public string ExceptionType;

            [JsonProperty("exceptionMessage")]
            public string ExceptionMessage;

            [JsonProperty("resultMessage")]
            public string ResultMessage;
        }

        [AgentTool(
            "Execute a C# script in the Unity environment. The script will be compiled and executed, returning the results.",
            k_FunctionId,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_CodeExecutionTag)]
        public static async Task<ExecutionOutput> ExecuteCommand(
            ToolExecutionContext context,
            [Parameter("The C# script code to execute. Should implement IRunCommand interface or be a valid C# script.")]
            string code,
            [Parameter("Title for the execution command")]
            string title)
        {
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException(k_CodeRequiredMessage);

            var agentCommand = RunCommandUtils.BuildRunCommand(code);
            if (agentCommand == null)
                throw new InvalidOperationException(k_CommandBuildFailedMessage);

            await context.Permissions.CheckCodeExecution(code);

            if (agentCommand.Unsafe)
            {
                var approvalInteraction = new UnsafeCommandApprovalInteraction();
                var approved = await context.Interactions.WaitForUser(approvalInteraction);

                if (!approved)
                    throw new OperationCanceledException("User declined to execute the unsafe command.");
            }
            
            var executionResult = RunCommandUtils.Execute(agentCommand, title);
            var formattedLogs = FormatLogs(executionResult);

            if (!executionResult.SuccessfullyStarted)
            {
                var logs = string.IsNullOrEmpty(formattedLogs) ? "No logs available" : formattedLogs;
                return new ExecutionOutput
                {
                    IsExecutionSuccessful = false,
                    ExecutionId = executionResult.Id,
                    ExecutionLogs = formattedLogs,
                    ConsoleLogs = executionResult.ConsoleLogs,
                    FailureStage = "execution",
                    ExceptionType = nameof(InvalidOperationException),
                    ExceptionMessage = string.Format(k_CommandExecutionFailedMessage, logs),
                    ResultMessage = "Command execution failed to start."
                };
            }

            var hasStructuredWarningsOrErrors = executionResult.Logs != null && executionResult.Logs.Any(log =>
                log.LogType == LogType.Warning ||
                log.LogType == LogType.Error ||
                log.LogType == LogType.Exception);
            var hasConsoleWarningsOrErrors = !string.IsNullOrWhiteSpace(executionResult.ConsoleLogs);

            if (hasStructuredWarningsOrErrors || hasConsoleWarningsOrErrors)
            {
                var logs = string.IsNullOrEmpty(formattedLogs) ? "No logs available" : formattedLogs;
                if (hasConsoleWarningsOrErrors)
                {
                    logs = string.IsNullOrEmpty(logs)
                        ? executionResult.ConsoleLogs
                        : $"{logs}\n{executionResult.ConsoleLogs.TrimEnd()}";
                }

                return new ExecutionOutput
                {
                    IsExecutionSuccessful = false,
                    ExecutionId = executionResult.Id,
                    ExecutionLogs = formattedLogs,
                    ConsoleLogs = executionResult.ConsoleLogs,
                    FailureStage = "execution",
                    ExceptionType = "CommandExecutionReportedWarnings",
                    ExceptionMessage = string.Format(k_CommandExecutionWarningsMessage, logs),
                    ResultMessage = "Command execution reported warnings or errors."
                };
            }

            return new ExecutionOutput
            {
                IsExecutionSuccessful = true,
                ExecutionId = executionResult.Id,
                ExecutionLogs = formattedLogs,
                ConsoleLogs = executionResult.ConsoleLogs,
                FailureStage = null,
                ExceptionType = null,
                ExceptionMessage = null,
                ResultMessage = "Command executed successfully."
            };
        }

        static string FormatLogs(ExecutionResult executionResult)
        {
            if (executionResult?.Logs == null || executionResult.Logs.Count == 0)
                return string.Empty;

            return string.Join("\n", executionResult.GetFormattedLogs());
        }
    }
}
