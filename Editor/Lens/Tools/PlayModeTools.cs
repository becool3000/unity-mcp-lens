#nullable disable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PlayModeTools
    {
        public const string ExitPlayModeDescription = @"Requests play-mode exit and optionally waits for Unity to settle.

Use this instead of Unity.RunCommand for cleanup after play-mode smoke tests. The tool marks play exit as an expected recoverable transition and returns compact final editor state.";

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
