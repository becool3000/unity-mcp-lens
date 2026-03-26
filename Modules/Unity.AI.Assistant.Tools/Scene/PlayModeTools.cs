using System;
using System.Threading.Tasks;
using UnityEditor;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;

/*
 * Proposed Functions:
 *
 * enter_play_mode
 *      return whether or not it needed to do anything.
 *
 * exit_play_mode
 *      return whether or not it needed to do anything.
 *
 * play_and_record(time)
 *      # If ready in play mode, exit and restart.
 *      get_console_errors
 *      get_screenshot
 *
 * What if the user wants to show us something?
 *
 *
 */


namespace Unity.AI.Assistant.Tools.Editor
{
    class PlayModeTools
    {
        const string k_EnterPlayModeFunctionId = "Unity.EnterPlayMode";
        const string k_ExitPlayModeFunctionId = "Unity.ExitPlayMode";

        [Serializable]
        public class ToolOutput
        {
            public bool Success;
            public string Reason = string.Empty;
        }

        [
            AgentTool(
            "Enters Unity Editor play mode. Use this to test gameplay, scripts, and scene behavior. Note: Unsaved scene changes will be lost.",
            id: k_EnterPlayModeFunctionId,
            tags: FunctionCallingUtilities.k_PlayModeTag
            )
        ]
        internal static async Task<ToolOutput> EnterPlayMode(ToolExecutionContext context)
        {
            if (EditorApplication.isPlaying)
            {
                return new ToolOutput
                {
                    Success = false,
                    Reason = "Already in play mode"
                };
            }

            await context.Permissions.CheckPlayMode(IToolPermissions.PlayModeOperation.Enter);

            // Dispatch so that domain reload etc occurs after this call
            MainThread.DispatchAndForget(EditorApplication.EnterPlaymode);

            return new ToolOutput
            {
                Success = true,
                Reason = "Entered play mode successfully"
            };
        }

        [
            AgentTool(
                "Exits Unity Editor play mode and resets scene state. Use this to stop testing and return to edit mode.",
                id: k_ExitPlayModeFunctionId,
                tags: FunctionCallingUtilities.k_PlayModeTag
            )
        ]
        internal static async Task<ToolOutput> ExitPlayMode(ToolExecutionContext context)
        {
            if (!EditorApplication.isPlaying)
            {
                return new ToolOutput
                {
                    Success = false,
                    Reason = "Already out of play mode"
                };
            }

            await context.Permissions.CheckPlayMode(IToolPermissions.PlayModeOperation.Exit);
            EditorApplication.ExitPlaymode();

            return new ToolOutput
            {
                Success = true,
                Reason = "Exited play mode successfully"
            };
        }

    }
}
