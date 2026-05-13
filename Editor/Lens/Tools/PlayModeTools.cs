#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PlayModeTools
    {
        public const string ExitPlayModeDescription = @"Requests play-mode exit and optionally waits for Unity to settle.

Use this instead of Unity.RunCommand for cleanup after play-mode smoke tests. The tool marks play exit as an expected recoverable transition and returns compact final editor state.";
        public const string SetPlayModeDescription = @"Requests a high-level play-mode transition and reports compact transition, runtime-advance, and console-error evidence.

Use this instead of Unity.ManageEditor Play/Stop or Unity.RunCommand play-mode snippets for smoke workflows.";
        public const string EnterReadyDescription = @"Requests Play Mode entry and waits until the runtime probe is ready for runtime tools.

When called through the Lens host, this workflow survives reconnect-prone domain reloads and reports request, editor-stability, runtime-advance, and console-delta evidence.";

        [McpSchema("Unity.Editor.ExitPlayMode")]
        public static object GetExitPlayModeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    waitForStableEditor = new { type = "boolean", description = "Wait until the editor leaves play mode and reaches a stable state." },
                    timeoutMs = new { type = "integer", description = "Timeout in milliseconds while waiting for play-mode exit and editor stability." },
                    pollIntervalMs = new { type = "integer", description = "Polling interval in milliseconds for wait-based exit checks." },
                    stablePollCount = new { type = "integer", description = "Consecutive stable polls required before reporting a stable editor." },
                    postStableDelayMs = new { type = "integer", description = "Additional settle delay in milliseconds after stable polls are reached." },
                    unpauseBeforeExit = new { type = "boolean", description = "Clear EditorApplication.isPaused before requesting play-mode exit." }
                }
            };
        }

        [McpTool("Unity.Editor.ExitPlayMode", ExitPlayModeDescription, "Exit Play Mode", Groups = new[] { "runtime", "editor" }, EnabledByDefault = true)]
        public static async Task<object> ExitPlayMode(JObject @params)
        {
            ExitPlayModeParams parameters = NormalizeExitPlayModeParams(@params);
            var timing = new ToolOperationTiming("Unity.Editor.ExitPlayMode", "exit_play_mode", 0);
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                    parameters.TimeoutMs = Math.Max(1000, parameters.TimeoutMs);
                    parameters.PollIntervalMs = Math.Max(50, parameters.PollIntervalMs);
                    parameters.StablePollCount = Math.Max(1, parameters.StablePollCount);
                    parameters.PostStableDelayMs = Math.Max(0, parameters.PostStableDelayMs);
                }

                using (timing.Measure("service"))
                {
                    data = await RequestExitAsync(parameters);
                    success = JsonConvert.SerializeObject(data).IndexOf("\"waitTimedOut\":true", StringComparison.OrdinalIgnoreCase) < 0;
                    errorKind = success ? null : "exit_play_mode_timeout";
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new { errorKind, error = ex.Message, finalState = BuildEditorState() };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Play-mode exit completed.", data)
                    : Response.Error("Play-mode exit did not reach a stable state.", data);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        [McpSchema(ToolPackCatalog.EditorSetPlayModeToolName)]
        public static object GetSetPlayModeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    mode = new { type = "string", description = "Requested mode: enter or exit." },
                    stopFirst = new { type = "boolean", description = "Exit play mode first before entering play mode." },
                    waitForRuntimeAdvance = new { type = "boolean", description = "Wait for runtime probe advancement when play mode can be observed in the current domain." },
                    warmupSeconds = new { type = "number", description = "Additional warmup seconds after runtime advancement is observed." },
                    timeoutSeconds = new { type = "integer", description = "Timeout in seconds for wait-based transition checks." },
                    unpauseBeforeExit = new { type = "boolean", description = "Clear EditorApplication.isPaused before requesting play-mode exit." }
                },
                required = new[] { "mode" }
            };
        }

        [McpSchema("Unity.PlayMode.EnterReady")]
        public static object GetEnterReadySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    scenePath = new { type = "string", description = "Optional Assets-relative .unity scene path to load before entering play mode." },
                    timeoutMs = new { type = "integer", description = "Timeout in milliseconds for play entry and runtime readiness." },
                    pollIntervalMs = new { type = "integer", description = "Host polling interval in milliseconds while waiting for reconnect and runtime readiness." },
                    warmupFrames = new { type = "integer", description = "Approximate additional warmup frames after runtime advancement is observed." },
                    warmupSeconds = new { type = "number", description = "Additional warmup seconds after runtime advancement is observed." },
                    stopFirst = new { type = "boolean", description = "Exit play mode first before entering play mode." },
                    clearPause = new { type = "boolean", description = "Clear EditorApplication.isPaused before exit or enter cleanup checks." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture project console error-count delta around play entry." }
                }
            };
        }

        [McpTool(ToolPackCatalog.EditorSetPlayModeToolName, SetPlayModeDescription, "Set Play Mode", Groups = new[] { "runtime", "editor" }, EnabledByDefault = true)]
        public static async Task<object> SetPlayMode(JObject @params)
        {
            var parameters = NormalizeSetPlayModeParams(@params);
            var timing = new ToolOperationTiming(ToolPackCatalog.EditorSetPlayModeToolName, parameters.Mode, 0);
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                    parameters.TimeoutSeconds = Math.Max(1, parameters.TimeoutSeconds);
                    parameters.WarmupSeconds = Math.Max(0d, parameters.WarmupSeconds);
                }

                using (timing.Measure("service"))
                {
                    data = await RequestSetPlayModeAsync(parameters);
                    string serialized = JsonConvert.SerializeObject(data, Formatting.None);
                    success = serialized.IndexOf("\"readyForFollowUp\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                    errorKind = success ? null : DetermineSetPlayModeErrorKind(serialized);
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    errorKind,
                    error = ex.Message,
                    finalState = EditorToolStateHelpers.BuildEditorState(),
                    consoleErrorCount = EditorToolStateHelpers.CountConsoleErrors()
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Play-mode transition completed.", data)
                    : Response.Error("Play-mode transition did not complete cleanly.", data);
                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        [McpTool("Unity.PlayMode.EnterReady", EnterReadyDescription, "Enter Play Mode Ready", Groups = new[] { "runtime", "editor" }, EnabledByDefault = true)]
        public static async Task<object> EnterReady(JObject @params)
        {
            JObject setPlayModeParams = ConvertEnterReadyParams(@params);
            return await SetPlayMode(setPlayModeParams);
        }

        sealed class SetPlayModeParams
        {
            public string Mode { get; set; }
            public bool StopFirst { get; set; }
            public bool WaitForRuntimeAdvance { get; set; }
            public double WarmupSeconds { get; set; }
            public int TimeoutSeconds { get; set; }
            public bool UnpauseBeforeExit { get; set; }
        }

        static SetPlayModeParams NormalizeSetPlayModeParams(JObject parameters)
        {
            parameters ??= new JObject();
            string mode = (GetToken(parameters, "mode", "Mode")?.Value<string>() ?? "enter").Trim().ToLowerInvariant();
            return new SetPlayModeParams
            {
                Mode = mode,
                StopFirst = GetBool(parameters, false, "stopFirst", "StopFirst"),
                WaitForRuntimeAdvance = GetBool(parameters, true, "waitForRuntimeAdvance", "WaitForRuntimeAdvance"),
                WarmupSeconds = GetDouble(parameters, 1.0d, "warmupSeconds", "WarmupSeconds"),
                TimeoutSeconds = GetInt(parameters, 60, "timeoutSeconds", "TimeoutSeconds"),
                UnpauseBeforeExit = GetBool(parameters, true, "unpauseBeforeExit", "UnpauseBeforeExit")
            };
        }

        static JObject ConvertEnterReadyParams(JObject parameters)
        {
            parameters ??= new JObject();
            var setPlayModeParams = (JObject)parameters.DeepClone();
            setPlayModeParams["mode"] = "enter";
            setPlayModeParams["waitForRuntimeAdvance"] = true;

            int timeoutMs = GetInt(parameters, 0, "timeoutMs", "TimeoutMs");
            if (timeoutMs > 0)
                setPlayModeParams["timeoutSeconds"] = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000.0d));

            if (GetToken(parameters, "clearPause", "ClearPause") != null)
                setPlayModeParams["unpauseBeforeExit"] = GetBool(parameters, true, "clearPause", "ClearPause");

            int warmupFrames = GetInt(parameters, 0, "warmupFrames", "WarmupFrames");
            double warmupSeconds = GetDouble(parameters, -1d, "warmupSeconds", "WarmupSeconds");
            if (warmupFrames > 0)
                warmupSeconds = Math.Max(0d, Math.Max(warmupSeconds, warmupFrames / 60.0d));
            if (warmupSeconds >= 0d)
                setPlayModeParams["warmupSeconds"] = warmupSeconds;

            return setPlayModeParams;
        }

        static async Task<object> RequestSetPlayModeAsync(SetPlayModeParams parameters)
        {
            ConsoleCursorSnapshot consoleBefore = ConsoleCursorDelta.Capture();
            if (parameters.Mode != "enter" && parameters.Mode != "exit")
            {
                return BuildSetPlayModeResult(
                    parameters,
                    refused: true,
                    requested: false,
                    reconnectExpected: false,
                    transitionState: "invalid_mode",
                    transitionRecoveryNotes: "mode must be 'enter' or 'exit'.",
                    runtimeAdvance: null,
                    exitResult: null,
                    attempts: Array.Empty<object>(),
                    consoleBefore: consoleBefore);
            }

            if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                return BuildSetPlayModeResult(
                    parameters,
                    refused: true,
                    requested: false,
                    reconnectExpected: false,
                    transitionState: BuildPipeline.isBuildingPlayer ? "building_player" : EditorApplication.isCompiling ? "compiling" : "updating",
                    transitionRecoveryNotes: "Unity is busy; play-mode transitions are refused while building, compiling, or importing.",
                    runtimeAdvance: null,
                    exitResult: null,
                    attempts: Array.Empty<object>(),
                    consoleBefore: consoleBefore);
            }

            object exitResult = null;
            bool requested = false;
            bool reconnectExpected = false;
            string transitionState;
            string transitionRecoveryNotes = null;
            object runtimeAdvance = null;
            var attempts = new List<object>();

            if (parameters.Mode == "exit")
            {
                var exitParams = new ExitPlayModeParams
                {
                    WaitForStableEditor = true,
                    TimeoutMs = parameters.TimeoutSeconds * 1000,
                    PollIntervalMs = 250,
                    StablePollCount = 2,
                    PostStableDelayMs = 250,
                    UnpauseBeforeExit = parameters.UnpauseBeforeExit
                };
                exitResult = await RequestExitAsync(exitParams);
                requested = JsonConvert.SerializeObject(exitResult).IndexOf("\"requested\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                transitionState = JsonConvert.SerializeObject(exitResult).IndexOf("\"waitTimedOut\":true", StringComparison.OrdinalIgnoreCase) >= 0
                    ? "exit_timed_out"
                    : "exited_play_mode";
                return BuildSetPlayModeResult(parameters, false, requested, requested, transitionState, transitionRecoveryNotes, runtimeAdvance, exitResult, attempts, consoleBefore);
            }

            if (parameters.StopFirst && (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                var exitParams = new ExitPlayModeParams
                {
                    WaitForStableEditor = true,
                    TimeoutMs = parameters.TimeoutSeconds * 1000,
                    PollIntervalMs = 250,
                    StablePollCount = 2,
                    PostStableDelayMs = 250,
                    UnpauseBeforeExit = parameters.UnpauseBeforeExit
                };
                exitResult = await RequestExitAsync(exitParams);
            }

            if (EditorApplication.isPlaying)
            {
                transitionState = "already_playing";
                runtimeAdvance = parameters.WaitForRuntimeAdvance
                    ? await WaitForRuntimeAdvanceAsync(parameters.TimeoutSeconds, parameters.WarmupSeconds, attempts)
                    : null;
                return BuildSetPlayModeResult(parameters, false, false, false, transitionState, transitionRecoveryNotes, runtimeAdvance, exitResult, attempts, consoleBefore);
            }

            BridgeStatusTracker.MarkTransition("entering_play_mode", "set_play_mode_enter", Math.Max(5.0, parameters.TimeoutSeconds));
            requested = true;

            if (CanWaitForPlayTransitionInCurrentDomain())
            {
                EditorApplication.isPlaying = true;
                runtimeAdvance = parameters.WaitForRuntimeAdvance
                    ? await WaitForRuntimeAdvanceAsync(parameters.TimeoutSeconds, parameters.WarmupSeconds, attempts)
                    : null;
                transitionState = !parameters.WaitForRuntimeAdvance || RuntimeAdvanceSucceeded(runtimeAdvance)
                    ? "entered_play_mode"
                    : "enter_requested";
                reconnectExpected = false;
            }
            else
            {
                RequestPlayModeAfterResponse();
                transitionState = "enter_requested_after_response";
                reconnectExpected = true;
                transitionRecoveryNotes = "Play mode is scheduled after this response; a reconnect and follow-up readiness poll may be needed.";
            }

            return BuildSetPlayModeResult(parameters, false, requested, reconnectExpected, transitionState, transitionRecoveryNotes, runtimeAdvance, exitResult, attempts, consoleBefore);
        }

        static object BuildSetPlayModeResult(
            SetPlayModeParams parameters,
            bool refused,
            bool requested,
            bool reconnectExpected,
            string transitionState,
            string transitionRecoveryNotes,
            object runtimeAdvance,
            object exitResult,
            IReadOnlyCollection<object> attempts,
            ConsoleCursorSnapshot consoleBefore = null)
        {
            object consoleDelta = ConsoleCursorDelta.BuildDelta(
                true,
                consoleBefore,
                ToolPackCatalog.EditorSetPlayModeToolName,
                new { kind = "set_play_mode_console_delta", requestedMode = parameters.Mode, transitionState });
            int consoleErrorCount = GetConsoleDeltaInt(consoleDelta, "finalConsoleErrorCount");
            int newConsoleErrorCount = GetConsoleDeltaInt(consoleDelta, "newErrors");
            bool consoleErrorsDetected = GetConsoleDeltaBool(consoleDelta, "consoleErrorsDetected");
            bool staleConsoleErrorsPresent = GetConsoleDeltaBool(consoleDelta, "staleErrorsPresent");
            bool runtimeAdvanced = RuntimeAdvanceSucceeded(runtimeAdvance);
            bool timedOut = JsonConvert.SerializeObject(runtimeAdvance ?? new { }).IndexOf("\"timedOut\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                transitionState?.IndexOf("timed_out", StringComparison.OrdinalIgnoreCase) >= 0;
            bool transitionPending = IsSetPlayModeTransitionPending(transitionState);
            bool readyForRuntimeTools = parameters.Mode == "enter" &&
                !refused &&
                !timedOut &&
                !consoleErrorsDetected &&
                !transitionPending &&
                (!parameters.WaitForRuntimeAdvance || runtimeAdvanced);
            bool readyForFollowUp = IsSetPlayModeReadyForFollowUp(
                parameters,
                refused,
                transitionState,
                runtimeAdvanced,
                timedOut,
                consoleErrorsDetected);
            var rawData = new
            {
                requestedMode = parameters.Mode,
                refused,
                requested,
                stopFirst = parameters.StopFirst,
                waitForRuntimeAdvance = parameters.WaitForRuntimeAdvance,
                warmupSeconds = parameters.WarmupSeconds,
                timeoutSeconds = parameters.TimeoutSeconds,
                reconnectExpected,
                transitionState,
                transitionRecoveryNotes,
                transitionPending,
                runtimeAdvanced,
                readyForRuntimeTools,
                readyForFollowUp,
                runtimeAdvance,
                exitResult,
                timedOut,
                consoleErrorCount,
                newConsoleErrorCount,
                staleConsoleErrorsPresent,
                consoleDelta,
                consoleErrorsDetected,
                finalState = EditorToolStateHelpers.BuildEditorState(),
                attempts = attempts?.ToArray() ?? Array.Empty<object>()
            };
            var compactData = new
            {
                rawData.requestedMode,
                rawData.refused,
                rawData.requested,
                rawData.stopFirst,
                rawData.waitForRuntimeAdvance,
                rawData.warmupSeconds,
                rawData.timeoutSeconds,
                rawData.reconnectExpected,
                rawData.transitionState,
                rawData.transitionRecoveryNotes,
                rawData.transitionPending,
                rawData.runtimeAdvanced,
                rawData.readyForRuntimeTools,
                rawData.readyForFollowUp,
                rawData.runtimeAdvance,
                rawData.timedOut,
                rawData.consoleErrorCount,
                rawData.newConsoleErrorCount,
                rawData.staleConsoleErrorsPresent,
                rawData.consoleDelta,
                rawData.consoleErrorsDetected,
                rawData.finalState,
                attemptCount = attempts?.Count ?? 0
            };

            return ToolResultCompactor.ShapeStructuredPayload(
                ToolPackCatalog.EditorSetPlayModeToolName,
                rawData,
                compactData,
                new
                {
                    kind = "set_play_mode_attempts",
                    requestedMode = parameters.Mode,
                    transitionState,
                    attemptCount = attempts?.Count ?? 0
                },
                "editor_set_play_mode_result",
                detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);
        }

        static string DetermineSetPlayModeErrorKind(string serializedResult)
        {
            if (serializedResult.IndexOf("\"refused\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                return "set_play_mode_refused";
            if (serializedResult.IndexOf("\"timedOut\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                return "set_play_mode_timeout";
            if (serializedResult.IndexOf("\"consoleErrorsDetected\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                return "set_play_mode_console_errors";
            if (serializedResult.IndexOf("\"transitionPending\":true", StringComparison.OrdinalIgnoreCase) >= 0)
                return "set_play_mode_pending";

            return "set_play_mode_not_ready";
        }

        static int GetConsoleDeltaInt(object consoleDelta, string name, int defaultValue = 0)
        {
            try
            {
                return JObject.FromObject(consoleDelta ?? new { }).Value<int?>(name) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        static bool GetConsoleDeltaBool(object consoleDelta, string name, bool defaultValue = false)
        {
            try
            {
                return JObject.FromObject(consoleDelta ?? new { }).Value<bool?>(name) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        static bool IsSetPlayModeTransitionPending(string transitionState)
        {
            return string.Equals(transitionState, "enter_requested", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transitionState, "enter_requested_after_response", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transitionState, "exiting_play_mode", StringComparison.OrdinalIgnoreCase);
        }

        static bool IsSetPlayModeReadyForFollowUp(
            SetPlayModeParams parameters,
            bool refused,
            string transitionState,
            bool runtimeAdvanced,
            bool timedOut,
            bool consoleErrorsDetected)
        {
            if (refused || timedOut || consoleErrorsDetected || IsSetPlayModeTransitionPending(transitionState))
                return false;

            if (parameters.Mode == "exit")
                return string.Equals(transitionState, "exited_play_mode", StringComparison.OrdinalIgnoreCase);

            if (parameters.Mode == "enter")
            {
                if (!string.Equals(transitionState, "entered_play_mode", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(transitionState, "already_playing", StringComparison.OrdinalIgnoreCase))
                    return false;

                return !parameters.WaitForRuntimeAdvance || runtimeAdvanced;
            }

            return false;
        }

        static async Task<object> WaitForRuntimeAdvanceAsync(int timeoutSeconds, double warmupSeconds, IList<object> attempts)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(Math.Max(1, timeoutSeconds));
            double previousUnscaledTime = -1d;
            while (DateTime.UtcNow < deadline)
            {
                var probe = EditorToolStateHelpers.BuildRuntimeProbeData();
                bool timeAdvanced = previousUnscaledTime >= 0d && probe.UnscaledTime > previousUnscaledTime;
                bool advanced = EditorApplication.isPlaying &&
                    probe.IsAvailable &&
                    (probe.HasAdvancedFrames || probe.UpdateCount >= 10 || timeAdvanced);
                attempts.Add(new
                {
                    utc = DateTime.UtcNow.ToString("O"),
                    isPlaying = EditorApplication.isPlaying,
                    runtimeProbeAvailable = probe.IsAvailable,
                    runtimeProbeHasAdvancedFrames = probe.HasAdvancedFrames,
                    runtimeProbeUpdateCount = probe.UpdateCount,
                    runtimeProbeUnscaledTime = probe.UnscaledTime,
                    runtimeAdvancedByTime = timeAdvanced,
                    advanced
                });

                if (advanced)
                {
                    if (warmupSeconds > 0d)
                        await Task.Delay((int)Math.Round(warmupSeconds * 1000d));
                    BridgeStatusTracker.MarkReady();
                    return new
                    {
                        success = true,
                        timedOut = false,
                        warmupSeconds,
                        finalProbe = EditorToolStateHelpers.BuildRuntimeProbeData()
                    };
                }

                previousUnscaledTime = probe.UnscaledTime;
                await Task.Delay(250);
            }

            return new
            {
                success = false,
                timedOut = true,
                warmupSeconds,
                finalProbe = EditorToolStateHelpers.BuildRuntimeProbeData()
            };
        }

        static bool RuntimeAdvanceSucceeded(object runtimeAdvance)
        {
            return runtimeAdvance != null &&
                JsonConvert.SerializeObject(runtimeAdvance, Formatting.None).IndexOf("\"success\":true", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool CanWaitForPlayTransitionInCurrentDomain()
        {
#if UNITY_2019_3_OR_NEWER
            return EditorSettings.enterPlayModeOptionsEnabled &&
                (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
#else
            return false;
#endif
        }

        static void RequestPlayModeAfterResponse()
        {
            EditorApplication.CallbackFunction requestPlay = null;
            double playRequestAfter = EditorApplication.timeSinceStartup + 0.25d;
            requestPlay = () =>
            {
                if (EditorApplication.timeSinceStartup < playRequestAfter)
                    return;

                EditorApplication.update -= requestPlay;
                if (!EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorApplication.isPlaying = true;
            };
            EditorApplication.update += requestPlay;
        }

        static ExitPlayModeParams NormalizeExitPlayModeParams(JObject parameters)
        {
            parameters ??= new JObject();
            return new ExitPlayModeParams
            {
                WaitForStableEditor = GetBool(parameters, true, "waitForStableEditor", "WaitForStableEditor"),
                TimeoutMs = GetInt(parameters, 30000, "timeoutMs", "TimeoutMs"),
                PollIntervalMs = GetInt(parameters, 250, "pollIntervalMs", "PollIntervalMs"),
                StablePollCount = GetInt(parameters, 2, "stablePollCount", "StablePollCount"),
                PostStableDelayMs = GetInt(parameters, 250, "postStableDelayMs", "PostStableDelayMs"),
                UnpauseBeforeExit = GetBool(parameters, true, "unpauseBeforeExit", "UnpauseBeforeExit")
            };
        }

        static JToken GetToken(JObject parameters, params string[] names)
        {
            if (parameters == null)
                return null;

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

        static double GetDouble(JObject parameters, double fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<double>();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static async Task<object> RequestExitAsync(ExitPlayModeParams parameters)
        {
            bool wasPlaying = EditorApplication.isPlaying;
            bool wasPaused = EditorApplication.isPaused;
            bool wasTransitioning = EditorApplication.isPlayingOrWillChangePlaymode;
            var attempts = new List<object>();

            if (wasPlaying || wasTransitioning)
            {
                BridgeStatusTracker.MarkTransition("exiting_play_mode", "exit_play_mode", Math.Max(5.0, parameters.TimeoutMs / 1000.0));
                if (parameters.UnpauseBeforeExit && EditorApplication.isPaused)
                    EditorApplication.isPaused = false;

                EditorApplication.isPlaying = false;
            }

            bool waitTimedOut = false;
            int stablePolls = 0;
            int waitedMs = 0;
            if (parameters.WaitForStableEditor)
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(parameters.TimeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    var state = BuildEditorState();
                    attempts.Add(state);
                    if (IsStable(state))
                    {
                        stablePolls++;
                        if (stablePolls >= parameters.StablePollCount)
                        {
                            if (parameters.PostStableDelayMs > 0)
                            {
                                await Task.Delay(parameters.PostStableDelayMs);
                                waitedMs += parameters.PostStableDelayMs;
                            }

                            BridgeStatusTracker.MarkReady();
                            break;
                        }
                    }
                    else
                    {
                        stablePolls = 0;
                    }

                    await Task.Delay(parameters.PollIntervalMs);
                    waitedMs += parameters.PollIntervalMs;
                }

                waitTimedOut = stablePolls < parameters.StablePollCount;
            }

            var finalState = BuildEditorState();
            return new
            {
                requested = wasPlaying || wasTransitioning,
                wasPlaying,
                wasPaused,
                wasTransitioning,
                unpauseBeforeExit = parameters.UnpauseBeforeExit,
                waitForStableEditor = parameters.WaitForStableEditor,
                waitTimedOut,
                waitedMilliseconds = waitedMs,
                stablePollCountReached = stablePolls,
                transitionState = wasPlaying || wasTransitioning
                    ? waitTimedOut ? "exiting_play_mode" : "exited_play_mode"
                    : "already_stopped",
                reconnectExpected = wasPlaying || wasTransitioning,
                finalState,
                attempts = attempts.ToArray()
            };
        }

        static object BuildEditorState()
        {
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                timeSinceStartup = EditorApplication.timeSinceStartup
            };
        }

        static bool IsStable(object state)
        {
            string json = JsonConvert.SerializeObject(state);
            return json.IndexOf("\"isPlaying\":false", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   json.IndexOf("\"isPlayingOrWillChangePlaymode\":false", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   json.IndexOf("\"isCompiling\":false", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   json.IndexOf("\"isUpdating\":false", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static int GetUtf8ByteCount(string value) => System.Text.Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
