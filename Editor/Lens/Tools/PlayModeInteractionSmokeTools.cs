#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PlayModeInteractionSmokeTools
    {
        public const string ToolName = "Unity.PlayMode.InteractionSmoke";
        const int DefaultWaitMs = 10000;
        const int MaxWaitMs = 120000;
        const int MaxStepWaitMs = 10000;
        const int MaxStepFrames = 600;

        public const string Description = @"Runs a bounded manual-style Play Mode interaction smoke.

The tool is verification equipment, not a test suite. It can enter Play Mode, invoke UI controls, queue pointer or keyboard input, wait bounded frames/milliseconds, read runtime snapshots, assert active state, capture Game view evidence, capture console deltas, and exit Play Mode by default.";

        [McpSchema(ToolName)]
        public static object GetInteractionSmokeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    scenePath = new { type = "string", description = "Optional Assets-relative .unity scene path to load before entering Play Mode." },
                    enterPlayMode = new { type = "boolean", description = "Enter Play Mode before running steps. Defaults to true." },
                    exitAfter = new { type = "boolean", description = "Exit Play Mode after steps complete. Defaults to true." },
                    waitMs = new { type = "integer", description = "Play-mode entry/exit wait budget in milliseconds. Defaults to 10000." },
                    consoleCount = new { type = "integer", description = "Maximum grouped console rows to include before/after verification. Defaults to 20." },
                    failFast = new { type = "boolean", description = "Stop after the first failed step. Defaults to false." },
                    steps = new
                    {
                        type = "array",
                        description = "Bounded manual-style interaction steps.",
                        items = new
                        {
                            type = "object",
                            properties = new
                            {
                                type = new { type = "string", description = "Step type: ui_control, pointer, key, wait, snapshot, assert_active, or capture_game_view." },
                                label = new { type = "string", description = "Optional human-readable step label." },
                                target = new { type = "string", description = "Target object, UI element, or component snapshot target." },
                                targetPath = new { type = "string", description = "Optional relative child path for active-state assertions." },
                                searchMethod = new { type = "string", description = "How to resolve target. Defaults to by_id_or_name_or_path for assertions." },
                                includeInactive = new { type = "boolean", description = "Include inactive objects when resolving targets." },
                                action = new { type = "string", description = "UI action or key action. Key actions: tap, press, release." },
                                value = new { description = "Value passed to compatible delegated tools such as setSlider/toggle." },
                                screenX = new { type = "number", description = "Pointer screen-space X coordinate." },
                                screenY = new { type = "number", description = "Pointer screen-space Y coordinate." },
                                key = new { type = "string", description = "Keyboard key name for key steps, for example C or Escape." },
                                keys = new { type = "array", items = new { type = "string" }, description = "Keyboard key names for key steps." },
                                holdFrames = new { type = "integer", description = "Frames to hold a tap key press before release." },
                                waitFrames = new { type = "integer", description = "Frames to wait after the step." },
                                waitMs = new { type = "integer", description = "Milliseconds to wait for wait steps." },
                                expectedActive = new { type = "boolean", description = "Expected activeInHierarchy state for assert_active steps." },
                                active = new { type = "boolean", description = "Alias for expectedActive in assert_active steps." },
                                componentType = new { type = "string", description = "Component type for snapshot steps." },
                                outputPath = new { type = "string", description = "Output path for capture_game_view steps." }
                            },
                            required = new[] { "type" }
                        }
                    }
                },
                required = new[] { "steps" }
            };
        }

        [McpTool(ToolName, Description, "Play Mode Interaction Smoke", Groups = new[] { "runtime", "diagnostics" }, EnabledByDefault = true)]
        public static async Task<object> InteractionSmoke(JObject @params)
        {
            JObject parameters = @params ?? new JObject();
            var timing = new ToolOperationTiming(ToolName, "interaction_smoke", PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            object response;
            bool transportSuccess = false;
            string errorKind = null;

            try
            {
                if (!TryGetStepArray(parameters, out JArray steps, out object validationError))
                {
                    errorKind = "invalid_steps";
                    response = Response.Error("INTERACTION_SMOKE_STEPS_REQUIRED", validationError);
                    timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                    timing.Record(false, errorKind);
                    return response;
                }

                if (!TryValidateStepTypes(steps, out validationError))
                {
                    errorKind = "invalid_step_type";
                    response = Response.Error("INTERACTION_SMOKE_INVALID_STEP", validationError);
                    timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
                    timing.Record(false, errorKind);
                    return response;
                }

                object data = await ExecuteAsync(parameters, steps);
                JObject dataObject = SafeJObject(data);
                bool transportError = dataObject.Value<bool?>("transportError") ?? false;
                bool passed = dataObject.Value<bool?>("passed") ?? false;
                transportSuccess = !transportError;
                errorKind = transportError ? dataObject.Value<string>("errorKind") ?? "interaction_smoke_transport_error" : null;

                if (transportError)
                {
                    response = Response.Error(dataObject.Value<string>("code") ?? "INTERACTION_SMOKE_PLAYMODE_ENTRY_FAILED", data);
                }
                else
                {
                    object shaped = ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "playmode_interaction_smoke_full_result" },
                        "playmode_interaction_smoke",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);
                    response = Response.Success(
                        passed ? "Play Mode interaction smoke passed." : "Play Mode interaction smoke completed with failed steps.",
                        shaped);
                }
            }
            catch (Exception ex)
            {
                transportSuccess = false;
                errorKind = ex.GetType().Name;
                response = Response.Error("INTERACTION_SMOKE_FAILED", new
                {
                    errorKind,
                    error = ex.Message,
                    editor = BuildEditorState()
                });
            }

            timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            timing.Record(transportSuccess, errorKind);
            return response;
        }

        static async Task<object> ExecuteAsync(JObject parameters, JArray steps)
        {
            bool enterPlayMode = GetBool(parameters, true, "enterPlayMode", "EnterPlayMode");
            bool exitAfter = GetBool(parameters, true, "exitAfter", "ExitAfter");
            bool failFast = GetBool(parameters, false, "failFast", "FailFast");
            int waitMs = Math.Clamp(GetInt(parameters, DefaultWaitMs, "waitMs", "WaitMs"), 1000, MaxWaitMs);
            int consoleCount = Math.Clamp(GetInt(parameters, 20, "consoleCount", "ConsoleCount"), 1, 100);
            bool wasPlaying = EditorApplication.isPlaying;
            bool startedPlaying = false;
            bool stoppedEarly = false;
            var stepRows = new List<object>();
            var cleanupWarnings = new List<object>();
            var capturePaths = new List<string>();

            object dirtyBefore = SceneTools.GetDirtyState(new JObject());
            ConsoleCursorSnapshot consoleBeforeSnapshot = ConsoleCursorDelta.Capture();
            object consoleBefore = ReadConsoleSummary(consoleCount);
            object enterResult = null;
            object exitResult = null;

            if (enterPlayMode)
            {
                JObject enterParams = new()
                {
                    ["timeoutMs"] = waitMs,
                    ["captureConsoleDelta"] = true
                };
                string scenePath = GetString(parameters, "scenePath", "ScenePath");
                if (!string.IsNullOrWhiteSpace(scenePath))
                    enterParams["scenePath"] = scenePath;

                enterResult = await PlayModeTools.EnterReady(enterParams);
                if (!ResponseSucceeded(enterResult))
                {
                    return new
                    {
                        transportError = true,
                        code = "INTERACTION_SMOKE_PLAYMODE_ENTRY_FAILED",
                        errorKind = "playmode_entry_failed",
                        message = "Play Mode interaction smoke failed while entering Play Mode.",
                        enterPlayMode,
                        enterResult,
                        editor = BuildEditorState(),
                        consoleBefore,
                        dirtyEvidence = new { before = dirtyBefore, after = SceneTools.GetDirtyState(new JObject()) },
                        saveState = NoSaveState()
                    };
                }

                startedPlaying = !wasPlaying && EditorApplication.isPlaying;
            }
            else if (!EditorApplication.isPlaying)
            {
                return new
                {
                    transportError = true,
                    code = "INTERACTION_SMOKE_REQUIRES_PLAY_MODE",
                    errorKind = "not_in_play_mode",
                    message = "Play Mode interaction smoke requires Play Mode when enterPlayMode=false.",
                    enterPlayMode,
                    enterResult,
                    editor = BuildEditorState(),
                    consoleBefore,
                    dirtyEvidence = new { before = dirtyBefore, after = SceneTools.GetDirtyState(new JObject()) },
                    saveState = NoSaveState()
                };
            }

            for (int index = 0; index < steps.Count; index++)
            {
                JObject step = (JObject)steps[index];
                object row = await ExecuteStepAsync(index, step);
                stepRows.Add(row);
                JObject rowObject = SafeJObject(row);
                if (rowObject.Value<bool?>("success") == false && failFast)
                {
                    stoppedEarly = true;
                    break;
                }
            }

            foreach (object row in stepRows)
            {
                foreach (string path in ExtractCapturePaths(row))
                    capturePaths.Add(path);
            }

            if (exitAfter)
            {
                exitResult = await PlayModeTools.SetPlayMode(new JObject
                {
                    ["mode"] = "exit",
                    ["timeoutSeconds"] = Math.Max(1, (int)Math.Ceiling(waitMs / 1000.0d)),
                    ["unpauseBeforeExit"] = true
                });
                if (!ResponseSucceeded(exitResult))
                {
                    cleanupWarnings.Add(new
                    {
                        kind = "exit_play_mode_failed",
                        message = "Unity.Editor.SetPlayMode exit did not complete cleanly.",
                        exitResult
                    });
                }
            }

            object consoleAfter = ReadConsoleSummary(consoleCount, consoleBeforeSnapshot.Cursor);
            object consoleDelta = ConsoleCursorDelta.BuildDelta(
                true,
                consoleBeforeSnapshot,
                ToolName,
                new { kind = "playmode_interaction_smoke_console_delta" });
            object dirtyAfter = SceneTools.GetDirtyState(new JObject());
            int failedCount = stepRows.Count(row => SafeJObject(row).Value<bool?>("success") == false);
            bool cleanupPassed = cleanupWarnings.Count == 0;
            bool passed = failedCount == 0 && cleanupPassed;

            return new
            {
                status = passed ? "passed" : "completed_with_failures",
                passed,
                transportError = false,
                enterPlayMode,
                exitAfter,
                failFast,
                requestedStepCount = steps.Count,
                executedStepCount = stepRows.Count,
                failedStepCount = failedCount,
                stoppedEarly,
                wasPlaying,
                startedPlaying,
                editor = BuildEditorState(),
                lifecycle = new
                {
                    enterResult,
                    exitResult,
                    cleanupWarnings = cleanupWarnings.ToArray()
                },
                stepRows = stepRows.ToArray(),
                capturePaths = capturePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                consoleDelta,
                consoleSnapshots = new
                {
                    before = consoleBefore,
                    after = consoleAfter
                },
                dirtyEvidence = new
                {
                    before = dirtyBefore,
                    after = dirtyAfter
                },
                saveState = NoSaveState()
            };
        }

        static async Task<object> ExecuteStepAsync(int index, JObject step)
        {
            string type = NormalizeStepType(GetString(step, "type", "Type"));
            string label = GetString(step, "label", "Label") ?? $"{type}_{index}";
            try
            {
                return type switch
                {
                    "ui_control" => await DelegatedStep(index, label, type, "Unity.UI.InvokeControl", step, async payload => await UiDiagnosticsTools.InvokeControl(payload.ToObject<UiInvokeControlParams>() ?? new UiInvokeControlParams())),
                    "pointer" => await DelegatedStep(index, label, type, "Unity.PlayMode.PointerInputSmoke", step, RuntimeDiagnosticsTools.PointerInputSmoke),
                    "snapshot" => DelegatedStep(index, label, type, "Unity.Runtime.GetComponentSnapshot", step, payload => RuntimeGetComponentSnapshotTools.GetComponentSnapshot(payload)),
                    "capture_game_view" => await DelegatedStep(index, label, type, "Unity.UI.CaptureGameView", step, payload => UiDiagnosticsTools.CaptureGameView(payload.ToObject<CaptureGameViewParams>() ?? new CaptureGameViewParams())),
                    "wait" => await WaitStep(index, label, step),
                    "key" => await KeyStep(index, label, step),
                    "assert_active" => AssertActiveStep(index, label, step),
                    _ => BuildStepRow(index, label, type, false, "unsupported_step_type", $"Unsupported step type '{type}'.", parameters: BuildParameterSummary(step))
                };
            }
            catch (Exception ex)
            {
                return BuildStepRow(index, label, type, false, ex.GetType().Name, ex.Message, parameters: BuildParameterSummary(step));
            }
        }

        static async Task<object> DelegatedStep(int index, string label, string type, string tool, JObject step, Func<JObject, Task<object>> call)
        {
            JObject payload = StepPayload(step);
            object result = await call(payload);
            bool success = ResponsePassed(result);
            return BuildStepRow(
                index,
                label,
                type,
                success,
                success ? "completed" : "failed",
                SafeJObject(result).Value<string>("message") ?? SafeJObject(result).Value<string>("error") ?? $"{tool} completed.",
                tool,
                BuildParameterSummary(payload),
                result);
        }

        static object DelegatedStep(int index, string label, string type, string tool, JObject step, Func<JObject, object> call)
        {
            JObject payload = StepPayload(step);
            object result = call(payload);
            bool success = ResponsePassed(result);
            return BuildStepRow(
                index,
                label,
                type,
                success,
                success ? "completed" : "failed",
                SafeJObject(result).Value<string>("message") ?? SafeJObject(result).Value<string>("error") ?? $"{tool} completed.",
                tool,
                BuildParameterSummary(payload),
                result);
        }

        static async Task<object> WaitStep(int index, string label, JObject step)
        {
            int waitMs = Math.Clamp(GetInt(step, 0, "waitMs", "WaitMs"), 0, MaxStepWaitMs);
            int waitFrames = Math.Clamp(GetInt(step, 0, "waitFrames", "WaitFrames", "frames", "Frames"), 0, MaxStepFrames);
            await AdvanceFramesAsync(waitFrames);
            if (waitMs > 0)
                await Task.Delay(waitMs);

            return BuildStepRow(index, label, "wait", true, "completed", "Bounded wait completed.", evidence: new
            {
                waitMs,
                waitFrames,
                editor = BuildEditorState()
            }, parameters: BuildParameterSummary(step));
        }

        static async Task<object> KeyStep(int index, string label, JObject step)
        {
            string action = (GetString(step, "action", "Action") ?? "tap").Trim().ToLowerInvariant();
            string[] keys = ExtractKeys(step);
            if (keys.Length == 0)
            {
                return BuildStepRow(index, label, "key", false, "missing_keys", "Key step requires key or keys.", parameters: BuildParameterSummary(step));
            }

            int holdFrames = Math.Clamp(GetInt(step, 1, "holdFrames", "HoldFrames"), 0, MaxStepFrames);
            int waitFrames = Math.Clamp(GetInt(step, 1, "waitFrames", "WaitFrames"), 0, MaxStepFrames);
            var deliveries = new List<object>();

            switch (action)
            {
                case "tap":
                    deliveries.Add(QueueKeyboardState(keys, "press"));
                    await AdvanceFramesAsync(Math.Max(1, holdFrames));
                    deliveries.Add(QueueKeyboardState(Array.Empty<string>(), "release"));
                    break;
                case "press":
                    deliveries.Add(QueueKeyboardState(keys, "press"));
                    break;
                case "release":
                    deliveries.Add(QueueKeyboardState(Array.Empty<string>(), "release"));
                    break;
                default:
                    return BuildStepRow(index, label, "key", false, "unsupported_key_action", $"Unsupported key action '{action}'. Use tap, press, or release.", parameters: BuildParameterSummary(step));
            }

            await AdvanceFramesAsync(waitFrames);
            bool unsupported = deliveries.Select(SafeJObject).Any(row => string.Equals(row.Value<string>("status"), "unsupported", StringComparison.OrdinalIgnoreCase));
            bool success = !unsupported && deliveries.Select(SafeJObject).All(row => row.Value<bool?>("succeeded") == true);
            string status = unsupported ? "unsupported" : success ? "delivered" : "failed";
            return BuildStepRow(index, label, "key", success, status, success ? "Keyboard input delivered." : "Keyboard input was not delivered.", evidence: new
            {
                action,
                keys,
                holdFrames,
                waitFrames,
                deliveryMode = "input_system_keyboard_state",
                deliveries = deliveries.ToArray(),
                editor = BuildEditorState()
            }, parameters: BuildParameterSummary(step));
        }

        static object AssertActiveStep(int index, string label, JObject step)
        {
            bool expected = GetBool(step, true, "expectedActive", "ExpectedActive", "active", "Active");
            GameObject target = ResolveStepGameObject(step, out string error);
            if (target == null)
            {
                return BuildStepRow(index, label, "assert_active", false, "target_not_found", error, parameters: BuildParameterSummary(step));
            }

            bool actual = target.activeInHierarchy;
            bool passed = actual == expected;
            return BuildStepRow(index, label, "assert_active", passed, passed ? "passed" : "failed", passed ? "Active-state assertion passed." : "Active-state assertion failed.", evidence: new
            {
                target = BuildGameObjectEvidence(target),
                expectedActive = expected,
                actualActiveInHierarchy = actual,
                actualActiveSelf = target.activeSelf
            }, parameters: BuildParameterSummary(step));
        }

        static object QueueKeyboardState(string[] keyNames, string phase)
        {
            var result = new KeyboardQueueResult
            {
                phase = phase ?? "queue",
                requestedKeys = keyNames ?? Array.Empty<string>(),
                deliveryMode = "immediate_update"
            };

            Type inputSystemType = Type.GetType("UnityEngine.InputSystem.InputSystem,Unity.InputSystem");
            Type keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard,Unity.InputSystem");
            Type keyboardStateType = Type.GetType("UnityEngine.InputSystem.LowLevel.KeyboardState,Unity.InputSystem");
            Type keyType = Type.GetType("UnityEngine.InputSystem.Key,Unity.InputSystem");
            if (inputSystemType == null || keyboardType == null || keyboardStateType == null || keyType == null)
            {
                result.status = "unsupported";
                result.error = "Input System keyboard types are not loaded.";
                return result;
            }

            result.available = true;
            object keyboard = keyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (keyboard == null)
            {
                result.status = "unsupported";
                result.error = "Keyboard.current is null.";
                return result;
            }

            if (!TryBuildKeyArray(keyType, keyNames, out Array keyArray, out string[] normalizedKeys, out string keyError))
            {
                result.status = "failed";
                result.error = keyError;
                return result;
            }

            result.normalizedKeys = normalizedKeys;
            try
            {
                object state = CreateKeyboardState(keyboardStateType, keyType, keyArray);
                MethodInfo queueStateEvent = inputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "QueueStateEvent" && method.IsGenericMethodDefinition && method.GetParameters().Length >= 2);
                if (queueStateEvent == null)
                {
                    result.status = "unsupported";
                    result.error = "InputSystem.QueueStateEvent<TState> could not be resolved.";
                    return result;
                }

                MethodInfo generic = queueStateEvent.MakeGenericMethod(keyboardStateType);
                var parameters = generic.GetParameters();
                object[] args = parameters.Length >= 3 ? new[] { keyboard, state, (object)(-1d) } : new[] { keyboard, state };
                generic.Invoke(null, args);
                inputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(method => method.Name == "Update" && method.GetParameters().Length == 0)
                    ?.Invoke(null, Array.Empty<object>());
                result.processed = true;
                result.succeeded = true;
                result.status = "delivered";
            }
            catch (Exception ex)
            {
                result.processed = true;
                result.succeeded = false;
                result.status = "failed";
                result.error = ex.InnerException?.Message ?? ex.Message;
            }

            return result;
        }

        static object CreateKeyboardState(Type keyboardStateType, Type keyType, Array keyArray)
        {
            ConstructorInfo arrayConstructor = keyboardStateType.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(ctor =>
                {
                    ParameterInfo[] parameters = ctor.GetParameters();
                    return parameters.Length == 1 && parameters[0].ParameterType.IsArray;
                });
            if (arrayConstructor != null)
                return arrayConstructor.Invoke(new object[] { keyArray });

            object state = Activator.CreateInstance(keyboardStateType);
            MethodInfo pressMethod = keyboardStateType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => (method.Name == "Press" || method.Name == "Set") && method.GetParameters().Any(parameter => parameter.ParameterType == keyType));
            if (pressMethod == null)
                return state;

            foreach (object key in keyArray)
            {
                ParameterInfo[] parameters = pressMethod.GetParameters();
                object[] args = parameters.Length >= 2 && parameters[1].ParameterType == typeof(bool)
                    ? new[] { key, (object)true }
                    : new[] { key };
                pressMethod.Invoke(state, args);
            }

            return state;
        }

        static bool TryBuildKeyArray(Type keyType, string[] keyNames, out Array keyArray, out string[] normalizedKeys, out string error)
        {
            var keys = new List<object>();
            var labels = new List<string>();
            foreach (string keyName in keyNames ?? Array.Empty<string>())
            {
                if (!TryParseKey(keyType, keyName, out object keyValue, out string normalized, out error))
                {
                    keyArray = Array.CreateInstance(keyType, 0);
                    normalizedKeys = labels.ToArray();
                    return false;
                }

                keys.Add(keyValue);
                labels.Add(normalized);
            }

            keyArray = Array.CreateInstance(keyType, keys.Count);
            for (int i = 0; i < keys.Count; i++)
                keyArray.SetValue(keys[i], i);
            normalizedKeys = labels.ToArray();
            error = null;
            return true;
        }

        static bool TryParseKey(Type keyType, string raw, out object keyValue, out string normalized, out string error)
        {
            string value = (raw ?? string.Empty).Trim();
            normalized = value;
            if (string.IsNullOrWhiteSpace(value))
            {
                keyValue = null;
                error = "Key name is empty.";
                return false;
            }

            string[] candidates = BuildKeyCandidates(value);
            foreach (string candidate in candidates)
            {
                try
                {
                    keyValue = Enum.Parse(keyType, candidate, ignoreCase: true);
                    normalized = candidate;
                    error = null;
                    return true;
                }
                catch
                {
                }
            }

            keyValue = null;
            error = $"Input System key '{value}' could not be resolved.";
            return false;
        }

        static string[] BuildKeyCandidates(string value)
        {
            string compact = value.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty);
            var candidates = new List<string>
            {
                value,
                compact
            };

            if (string.Equals(compact, "esc", StringComparison.OrdinalIgnoreCase))
                candidates.Add("Escape");
            if (compact.Length == 1 && char.IsDigit(compact[0]))
                candidates.Add("Digit" + compact);
            if (compact.Length == 1 && char.IsLetter(compact[0]))
                candidates.Add(compact.ToUpperInvariant());
            if (compact.EndsWith("arrow", StringComparison.OrdinalIgnoreCase))
                candidates.Add(compact);
            if (compact.StartsWith("arrow", StringComparison.OrdinalIgnoreCase) && compact.Length > 5)
                candidates.Add(compact.Substring(5) + "Arrow");

            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static async Task AdvanceFramesAsync(int frames)
        {
            int count = Math.Clamp(frames, 0, MaxStepFrames);
            for (int i = 0; i < count; i++)
            {
                if (EditorApplication.isPlaying && EditorApplication.isPaused)
                    EditorApplication.Step();

                await Task.Delay(EditorApplication.isPaused ? 50 : 20);
            }
        }

        static GameObject ResolveStepGameObject(JObject step, out string error)
        {
            error = null;
            JToken targetToken = GetToken(step, "target", "Target");
            string targetPath = GetString(step, "targetPath", "TargetPath");
            if ((targetToken == null || targetToken.Type == JTokenType.Null || string.IsNullOrWhiteSpace(targetToken.ToString())) && !string.IsNullOrWhiteSpace(targetPath))
                targetToken = new JValue(targetPath);

            if (targetToken == null || targetToken.Type == JTokenType.Null || string.IsNullOrWhiteSpace(targetToken.ToString()))
            {
                error = "target or targetPath is required.";
                return null;
            }

            string searchMethod = GetString(step, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path";
            bool includeInactive = GetBool(step, true, "includeInactive", "IncludeInactive");
            var findParams = new JObject
            {
                ["search_inactive"] = includeInactive,
                ["includeInactive"] = includeInactive
            };
            GameObject root = ObjectsHelper.FindObject(targetToken, searchMethod, findParams);
            if (root == null)
            {
                error = $"Target '{targetToken}' could not be resolved.";
                return null;
            }

            if (string.IsNullOrWhiteSpace(targetPath) || targetToken.ToString().Equals(targetPath, StringComparison.OrdinalIgnoreCase) || targetPath == ".")
                return root;

            Transform child = FindRelativeChild(root.transform, targetPath);
            if (child == null)
            {
                error = $"Target path '{targetPath}' could not be resolved under '{UiDiagnosticsHelper.GetHierarchyPath(root.transform)}'.";
                return null;
            }

            return child.gameObject;
        }

        static Transform FindRelativeChild(Transform root, string path)
        {
            if (root == null || string.IsNullOrWhiteSpace(path) || path == ".")
                return root;

            string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Transform current = root;
            foreach (string segment in segments)
            {
                if (string.Equals(segment, current.name, StringComparison.OrdinalIgnoreCase))
                    continue;

                Transform next = null;
                for (int i = 0; i < current.childCount; i++)
                {
                    Transform child = current.GetChild(i);
                    if (string.Equals(child.name, segment, StringComparison.OrdinalIgnoreCase))
                    {
                        next = child;
                        break;
                    }
                }

                if (next == null)
                    return null;
                current = next;
            }

            return current;
        }

        static JObject StepPayload(JObject step)
        {
            JObject payload = (JObject)step.DeepClone();
            foreach (string key in new[] { "type", "Type", "label", "Label" })
                payload.Remove(key);
            return payload;
        }

        static object BuildStepRow(int index, string label, string type, bool success, string status, string message, string tool = null, object parameters = null, object result = null, object evidence = null)
        {
            return new
            {
                index,
                label,
                type,
                tool,
                success,
                status,
                message,
                parameters,
                result,
                evidence
            };
        }

        static object BuildGameObjectEvidence(GameObject gameObject)
        {
            return gameObject == null ? null : new
            {
                name = gameObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                instanceId = gameObject.GetInstanceID(),
                activeSelf = gameObject.activeSelf,
                activeInHierarchy = gameObject.activeInHierarchy
            };
        }

        static object BuildEditorState()
        {
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode
            };
        }

        static object ReadConsoleSummary(int count, int? cursor = null)
        {
            return ReadConsole.HandleCommand(new ReadConsoleParams
            {
                Action = ConsoleAction.Get,
                Types = new[] { ConsoleLogType.Error, ConsoleLogType.Warning, ConsoleLogType.Exception, ConsoleLogType.Assert },
                Count = count,
                Cursor = cursor,
                Format = ConsoleOutputFormat.Summary,
                ExcludeMcpNoise = true,
                IncludeStacktrace = false
            });
        }

        static bool TryGetStepArray(JObject parameters, out JArray steps, out object error)
        {
            steps = GetToken(parameters, "steps", "Steps") as JArray;
            if (steps == null)
            {
                error = new { message = "steps must be an array.", stepsPresent = false };
                return false;
            }

            if (steps.Count == 0)
            {
                error = new { message = "steps must contain at least one step.", stepsPresent = true, stepCount = 0 };
                return false;
            }

            error = null;
            return true;
        }

        static bool TryValidateStepTypes(JArray steps, out object error)
        {
            for (int index = 0; index < steps.Count; index++)
            {
                if (steps[index] is not JObject step)
                {
                    error = new { message = "Each step must be an object.", index };
                    return false;
                }

                string type = NormalizeStepType(GetString(step, "type", "Type"));
                if (string.IsNullOrWhiteSpace(type))
                {
                    error = new { message = "Each step must include type.", index };
                    return false;
                }

                if (!IsKnownStepType(type))
                {
                    error = new { message = $"Unsupported step type '{type}'.", index, type };
                    return false;
                }
            }

            error = null;
            return true;
        }

        static bool IsKnownStepType(string type)
        {
            return type == "ui_control" ||
                   type == "pointer" ||
                   type == "key" ||
                   type == "wait" ||
                   type == "snapshot" ||
                   type == "assert_active" ||
                   type == "capture_game_view";
        }

        static string NormalizeStepType(string type)
        {
            string value = (type ?? string.Empty).Trim().ToLowerInvariant();
            return value switch
            {
                "ui" or "invoke_control" or "ui_invoke" => "ui_control",
                "pointer_input" or "mouse" => "pointer",
                "keyboard" => "key",
                "delay" => "wait",
                "component_snapshot" or "runtime_snapshot" => "snapshot",
                "active" or "assertactive" => "assert_active",
                "capture" or "game_view_capture" => "capture_game_view",
                _ => value
            };
        }

        static string[] ExtractKeys(JObject step)
        {
            var keys = new List<string>();
            string key = GetString(step, "key", "Key");
            if (!string.IsNullOrWhiteSpace(key))
                keys.Add(key);

            if (GetToken(step, "keys", "Keys") is JArray array)
            {
                foreach (JToken token in array)
                {
                    string value = token?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        keys.Add(value);
                }
            }

            return keys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static object BuildParameterSummary(JObject parameters)
        {
            if (parameters == null)
                return null;

            JObject clone = (JObject)parameters.DeepClone();
            if (clone["steps"] is JArray steps && steps.Count > 8)
            {
                int omitted = steps.Count - 8;
                clone["steps"] = new JArray(steps.Take(8).Select(step => step.DeepClone()));
                clone["omittedStepCount"] = omitted;
            }

            return clone;
        }

        static object BuildCompactData(object data)
        {
            JObject root = SafeJObject(data);
            if (root["stepRows"] is JArray rows && rows.Count > 20)
            {
                int omitted = rows.Count - 20;
                root["stepRows"] = new JArray(rows.Take(20).Select(row => row.DeepClone()));
                root["omittedStepRowCount"] = omitted;
            }

            return root;
        }

        static string[] ExtractCapturePaths(object value)
        {
            JObject root = SafeJObject(value);
            var paths = new List<string>();
            foreach (JToken token in root.DescendantsAndSelf())
            {
                if (token is not JProperty property)
                    continue;

                if (property.Value.Type != JTokenType.String)
                    continue;

                string name = property.Name ?? string.Empty;
                if (name.IndexOf("path", StringComparison.OrdinalIgnoreCase) < 0 &&
                    name.IndexOf("output", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                string path = property.Value.ToString();
                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    paths.Add(path);
                }
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static object NoSaveState()
        {
            return new
            {
                requested = false,
                attempted = false,
                saved = false,
                message = "not_requested_runtime_interaction_smoke"
            };
        }

        static bool ResponsePassed(object result)
        {
            JObject root = SafeJObject(result);
            if (!ResponseSucceeded(root))
                return false;

            JToken data = root["data"];
            if (data is JObject dataObject && dataObject.TryGetValue("passed", StringComparison.OrdinalIgnoreCase, out JToken passed))
                return passed.Type != JTokenType.Boolean || passed.Value<bool>();

            if (data is JObject shapedData &&
                shapedData["data"] is JObject nestedData &&
                nestedData.TryGetValue("passed", StringComparison.OrdinalIgnoreCase, out JToken nestedPassed))
            {
                return nestedPassed.Type != JTokenType.Boolean || nestedPassed.Value<bool>();
            }

            return true;
        }

        static bool ResponseSucceeded(object result) => ResponseSucceeded(SafeJObject(result));

        static bool ResponseSucceeded(JObject root)
        {
            if (root == null)
                return false;
            if (root.TryGetValue("success", StringComparison.OrdinalIgnoreCase, out JToken success))
                return success.Type == JTokenType.Boolean && success.Value<bool>();
            return false;
        }

        static JObject SafeJObject(object value)
        {
            if (value == null)
                return new JObject();
            if (value is JObject jObject)
                return (JObject)jObject.DeepClone();
            try
            {
                return JObject.FromObject(value);
            }
            catch (Exception ex)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["errorKind"] = ex.GetType().Name,
                    ["error"] = ex.Message
                };
            }
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

        static string GetString(JObject parameters, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString().Trim();
        }

        static bool GetBool(JObject parameters, bool fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return fallback;
            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();
            return bool.TryParse(token.ToString(), out bool value) ? value : fallback;
        }

        static int GetInt(JObject parameters, int fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return fallback;
            return int.TryParse(token.ToString(), out int value) ? value : fallback;
        }

        sealed class KeyboardQueueResult
        {
            public string phase;
            public string status = "failed";
            public bool available;
            public bool processed;
            public bool succeeded;
            public string deliveryMode;
            public string[] requestedKeys = Array.Empty<string>();
            public string[] normalizedKeys = Array.Empty<string>();
            public string error;
        }
    }
}
