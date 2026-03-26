using System;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Assistant.Agent.Dynamic.Extension.Editor;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.Editor.RunCommand;
using Unity.AI.Assistant.FunctionCalling;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
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
                throw new InvalidOperationException(string.Format(k_CommandExecutionFailedMessage, logs));
            }

            var hasWarningsOrErrors = executionResult.Logs != null && executionResult.Logs.Any(log =>
                log.LogType == LogType.Warning ||
                log.LogType == LogType.Error ||
                log.LogType == LogType.Exception);

            if (hasWarningsOrErrors)
            {
                var logs = string.IsNullOrEmpty(formattedLogs) ? "No logs available" : formattedLogs;
                throw new InvalidOperationException(string.Format(k_CommandExecutionWarningsMessage, logs));
            }

            return new ExecutionOutput
            {
                IsExecutionSuccessful = true,
                ExecutionId = executionResult.Id,
                ExecutionLogs = formattedLogs
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
