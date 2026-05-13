#nullable disable
using System;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PlayModeStepVerifierTools
    {
        const string ToolName = "Unity.PlayMode.StepVerifier";
        const string Description = @"Runs a bounded paused Play Mode verification step.

This tool does not free-run real-time simulation by default. The Lens host should enter Play Mode first, then this tool pauses immediately, applies exactly warmupSteps + steps editor/player steps, captures compact runtime and console evidence, and exits or restores pause state according to arguments.";

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    steps = new { type = "integer", description = "Verification steps to run after warmup. Defaults to 1." },
                    warmupSteps = new { type = "integer", description = "Paused steps to run before counted verification steps. Defaults to 0." },
                    exitAfter = new { type = "boolean", description = "Exit Play Mode after stepping. Defaults to true." },
                    restorePreviousState = new { type = "boolean", description = "If true, restore the previous playing/paused state instead of always exiting. Defaults to false." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture only console entries emitted during the verifier. Defaults to true." },
                    failOnNewConsoleErrors = new { type = "boolean", description = "Fail the verifier when new console errors appear. Defaults to true." },
                    allowRealtimeRun = new { type = "boolean", description = "Explicit opt-in to allow unpaused real-time waiting. Defaults to false." },
                    timeoutMs = new { type = "integer", description = "Hard local timeout for paused stepping and cleanup. Defaults to 30000." }
                }
            };
        }

        [McpTool(ToolName, Description, "Play Mode Step Verifier", Groups = new[] { "runtime", "editor" }, EnabledByDefault = true)]
        public static async Task<object> StepVerifier(JObject parameters)
        {
            parameters ??= new JObject();
            var args = Normalize(parameters);
            string workflowId = "step-verifier-" + Guid.NewGuid().ToString("N");
            object before = LensTransactionSnapshot.Capture(workflowId);
            ConsoleCursorSnapshot consoleBefore = ConsoleCursorDelta.Capture();
            PlayModeRuntimeProbeData probeBefore = EditorToolStateHelpers.BuildRuntimeProbeData();
            bool wasPlaying = EditorApplication.isPlaying;
            bool wasPaused = EditorApplication.isPaused;
            bool enteredPlayMode = wasPlaying;
            bool timedOut = false;
            bool paused = false;
            int warmupCompleted = 0;
            int stepsCompleted = 0;
            string failureReason = null;
            object cleanup = null;
            DateTime deadlineUtc = DateTime.UtcNow.AddMilliseconds(args.TimeoutMs);

            try
            {
                if (!EditorApplication.isPlaying)
                {
                    return Response.Error("StepVerifier requires Play Mode. Use the host-facing Unity.PlayMode.StepVerifier so Lens can enter safely before stepping.", new
                    {
                        enteredPlayMode = false,
                        paused = false,
                        stepsRequested = args.Steps,
                        stepsCompleted = 0,
                        warmupSteps = args.WarmupSteps,
                        warmupCompleted = 0,
                        runtimeAdvanced = false,
                        timedOut = false,
                        editorResponsiveAfter = true,
                        before,
                        after = LensTransactionSnapshot.Capture(workflowId)
                    });
                }

                enteredPlayMode = true;
                if (!args.AllowRealtimeRun)
                    EditorApplication.isPaused = true;
                paused = EditorApplication.isPaused;

                int totalSteps = args.WarmupSteps + args.Steps;
                for (int i = 0; i < totalSteps; i++)
                {
                    if (DateTime.UtcNow >= deadlineUtc)
                    {
                        timedOut = true;
                        failureReason = "StepVerifier timed out while applying paused editor steps.";
                        break;
                    }

                    if (!EditorApplication.isPlaying)
                    {
                        failureReason = "Play Mode exited before requested steps completed.";
                        break;
                    }

                    if (!args.AllowRealtimeRun && !EditorApplication.isPaused)
                        EditorApplication.isPaused = true;

                    EditorApplication.Step();
                    if (i < args.WarmupSteps)
                        warmupCompleted++;
                    else
                        stepsCompleted++;

                    await Task.Yield();
                }

                PlayModeRuntimeProbeData probeAfterSteps = EditorToolStateHelpers.BuildRuntimeProbeData();
                bool runtimeAdvanced = RuntimeAdvanced(probeBefore, probeAfterSteps) || (warmupCompleted + stepsCompleted) > 0;
                object consoleDelta = ConsoleCursorDelta.BuildDelta(
                    args.CaptureConsoleDelta,
                    consoleBefore,
                    ToolName,
                    new { kind = "step_verifier_console_delta", workflowId });
                int newErrors = GetConsoleDeltaInt(consoleDelta, "newErrors");
                int newWarnings = GetConsoleDeltaInt(consoleDelta, "newWarnings");
                bool newConsoleErrorsDetected = args.FailOnNewConsoleErrors && newErrors > 0;

                cleanup = await CleanupAsync(args, wasPlaying, wasPaused, deadlineUtc);
                object after = LensTransactionSnapshot.Capture(workflowId);
                bool editorResponsiveAfter = !EditorApplication.isCompiling &&
                    !EditorApplication.isUpdating &&
                    !EditorApplication.isPlayingOrWillChangePlaymode;
                bool success = !timedOut &&
                    failureReason == null &&
                    !newConsoleErrorsDetected &&
                    stepsCompleted == args.Steps &&
                    (!args.AllowRealtimeRun || runtimeAdvanced);

                var rawData = new
                {
                    workflowId,
                    enteredPlayMode,
                    paused,
                    allowRealtimeRun = args.AllowRealtimeRun,
                    stepsRequested = args.Steps,
                    stepsCompleted,
                    warmupSteps = args.WarmupSteps,
                    warmupCompleted,
                    totalStepsRequested = args.Steps + args.WarmupSteps,
                    totalStepsCompleted = stepsCompleted + warmupCompleted,
                    runtimeAdvanced,
                    timedOut,
                    editorResponsiveAfter,
                    failOnNewConsoleErrors = args.FailOnNewConsoleErrors,
                    newConsoleErrorsDetected,
                    newErrors,
                    newWarnings,
                    reason = failureReason,
                    probeBefore,
                    probeAfter = probeAfterSteps,
                    consoleDelta,
                    before,
                    after,
                    cleanup
                };

                var compactData = new
                {
                    rawData.workflowId,
                    rawData.enteredPlayMode,
                    rawData.paused,
                    rawData.allowRealtimeRun,
                    rawData.stepsRequested,
                    rawData.stepsCompleted,
                    rawData.warmupSteps,
                    rawData.warmupCompleted,
                    rawData.runtimeAdvanced,
                    rawData.timedOut,
                    rawData.editorResponsiveAfter,
                    rawData.newConsoleErrorsDetected,
                    rawData.newErrors,
                    rawData.newWarnings,
                    rawData.reason,
                    rawData.consoleDelta,
                    rawData.cleanup,
                    before,
                    after
                };

                object shaped = ToolResultCompactor.ShapeStructuredPayload(
                    ToolName,
                    rawData,
                    compactData,
                    new { kind = "play_mode_step_verifier", workflowId },
                    "play_mode_step_verifier_result",
                    detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);

                return success
                    ? Response.Success("Play Mode stepped verification completed.", shaped)
                    : Response.Error("Play Mode stepped verification did not complete cleanly.", shaped);
            }
            catch (Exception ex)
            {
                object after = LensTransactionSnapshot.Capture(workflowId);
                return Response.Error("Play Mode StepVerifier failed.", new
                {
                    workflowId,
                    errorKind = ex.GetType().Name,
                    error = ex.Message,
                    enteredPlayMode,
                    paused,
                    stepsRequested = args.Steps,
                    stepsCompleted,
                    warmupSteps = args.WarmupSteps,
                    warmupCompleted,
                    runtimeAdvanced = false,
                    timedOut,
                    editorResponsiveAfter = true,
                    before,
                    after,
                    cleanup
                });
            }
        }

        sealed class StepVerifierParams
        {
            public int Steps;
            public int WarmupSteps;
            public bool ExitAfter;
            public bool RestorePreviousState;
            public bool CaptureConsoleDelta;
            public bool FailOnNewConsoleErrors;
            public bool AllowRealtimeRun;
            public int TimeoutMs;
        }

        static StepVerifierParams Normalize(JObject parameters)
        {
            return new StepVerifierParams
            {
                Steps = Math.Max(0, GetInt(parameters, 1, "steps", "Steps")),
                WarmupSteps = Math.Max(0, GetInt(parameters, 0, "warmupSteps", "WarmupSteps")),
                ExitAfter = GetBool(parameters, true, "exitAfter", "ExitAfter"),
                RestorePreviousState = GetBool(parameters, false, "restorePreviousState", "RestorePreviousState"),
                CaptureConsoleDelta = GetBool(parameters, true, "captureConsoleDelta", "CaptureConsoleDelta"),
                FailOnNewConsoleErrors = GetBool(parameters, true, "failOnNewConsoleErrors", "FailOnNewConsoleErrors"),
                AllowRealtimeRun = GetBool(parameters, false, "allowRealtimeRun", "AllowRealtimeRun"),
                TimeoutMs = Math.Max(1000, Math.Min(120000, GetInt(parameters, 30000, "timeoutMs", "TimeoutMs")))
            };
        }

        static async Task<object> CleanupAsync(StepVerifierParams args, bool wasPlaying, bool wasPaused, DateTime deadlineUtc)
        {
            if (args.RestorePreviousState)
            {
                if (!wasPlaying && EditorApplication.isPlaying)
                {
                    EditorApplication.isPaused = false;
                    EditorApplication.isPlaying = false;
                    await WaitForPlayModeExitAsync(deadlineUtc);
                    return new { mode = "restore_previous_state", requestedExit = true, restoredPause = false, finalIsPlaying = EditorApplication.isPlaying, finalIsPaused = EditorApplication.isPaused };
                }

                if (EditorApplication.isPlaying)
                    EditorApplication.isPaused = wasPaused;
                return new { mode = "restore_previous_state", requestedExit = false, restoredPause = wasPaused, finalIsPlaying = EditorApplication.isPlaying, finalIsPaused = EditorApplication.isPaused };
            }

            if (args.ExitAfter && EditorApplication.isPlaying)
            {
                EditorApplication.isPaused = false;
                EditorApplication.isPlaying = false;
                await WaitForPlayModeExitAsync(deadlineUtc);
                return new { mode = "exit_after", requestedExit = true, finalIsPlaying = EditorApplication.isPlaying, finalIsPaused = EditorApplication.isPaused };
            }

            if (EditorApplication.isPlaying && !args.AllowRealtimeRun)
                EditorApplication.isPaused = true;

            return new { mode = "leave_paused", requestedExit = false, finalIsPlaying = EditorApplication.isPlaying, finalIsPaused = EditorApplication.isPaused };
        }

        static async Task WaitForPlayModeExitAsync(DateTime deadlineUtc)
        {
            while (EditorApplication.isPlayingOrWillChangePlaymode && DateTime.UtcNow < deadlineUtc)
                await Task.Delay(100);
        }

        static bool RuntimeAdvanced(PlayModeRuntimeProbeData before, PlayModeRuntimeProbeData after)
        {
            if (after == null || !after.IsAvailable)
                return false;

            if (before == null || !before.IsAvailable)
                return after.HasAdvancedFrames || after.UpdateCount > 0 || after.FixedUpdateCount > 0 || after.FrameCount > 0;

            return after.UpdateCount > before.UpdateCount ||
                after.FixedUpdateCount > before.FixedUpdateCount ||
                after.FrameCount > before.FrameCount ||
                after.UnscaledTime > before.UnscaledTime ||
                after.RuntimeTime > before.RuntimeTime ||
                after.FixedTime > before.FixedTime;
        }

        static int GetConsoleDeltaInt(object consoleDelta, string name)
        {
            try
            {
                return JObject.FromObject(consoleDelta ?? new { }).Value<int?>(name) ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value))
                    return value;
            }

            return null;
        }

        static int GetInt(JObject parameters, int fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }
    }
}
