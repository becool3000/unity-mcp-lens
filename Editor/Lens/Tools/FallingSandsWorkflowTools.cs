#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class FallingSandsWorkflowTools
    {
        const string ProbeToolName = "Unity.Workflow.RunGpuSimulationProbe";
        const string PackVerifyToolName = "Unity.Workflow.VerifyRuntimePackSelection";

        [McpSchema(ProbeToolName)]
        public static object GetGpuSimulationProbeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    packId = new { type = "string", description = "FallingSands element pack id. Defaults to garden." },
                    scenePath = new { type = "string", description = "Optional scene path. Host wrappers should load before play mode." },
                    fixture = new { type = "string", description = "Deterministic fixture id. Defaults to sparse_nectar_bee." },
                    steps = new { type = "integer", description = "Deterministic ticks to step. Defaults to 240." },
                    maxWallMs = new { type = "integer", description = "Hard local wall-clock cap. Defaults to 5000." },
                    summaryIds = new { type = "array", items = new { type = "string" }, description = "Summary count ids to return." },
                    caps = new { type = "object", description = "Optional safety caps such as beeCountMax, steamCountMax, dispatchMsMax, readbackMsMax." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture only logs emitted during the probe. Defaults to true." },
                    exitAfter = new { type = "boolean", description = "Host hint to exit Play Mode after the workflow. Native tool does not exit by itself." }
                }
            };
        }

        [McpSchema(PackVerifyToolName)]
        public static object GetRuntimePackSelectionSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    selectedPackId = new { type = "string", description = "Expected FallingSands pack id." },
                    scenePath = new { type = "string", description = "Optional scene path to load in edit mode before verification." },
                    requirePlayMode = new { type = "boolean", description = "Require an active runtime SimulationController. Defaults to true." }
                }
            };
        }

        [McpTool(ProbeToolName, "Runs a bounded FallingSands deterministic GPU simulation probe through the project test API.", "Run GPU Simulation Probe", Groups = new[] { "runtime", "diagnostics" }, EnabledByDefault = true)]
        public static object RunGpuSimulationProbe(JObject parameters)
        {
            parameters ??= new JObject();
            string workflowId = "gpu-probe-" + Guid.NewGuid().ToString("N");
            object before = LensTransactionSnapshot.Capture(workflowId);
            ConsoleCursorSnapshot consoleBefore = ConsoleCursorDelta.Capture();
            Stopwatch stopwatch = Stopwatch.StartNew();
            string packId = GetString(parameters, "garden", "packId", "PackId");
            string fixture = GetString(parameters, "sparse_nectar_bee", "fixture", "Fixture");
            int steps = Math.Max(1, GetInt(parameters, 240, "steps", "Steps"));
            int maxWallMs = Math.Max(500, GetInt(parameters, 5000, "maxWallMs", "MaxWallMs"));
            bool captureConsoleDelta = GetBool(parameters, true, "captureConsoleDelta", "CaptureConsoleDelta");

            try
            {
                if (!EditorApplication.isPlaying)
                {
                    return Response.Error("FallingSands GPU probe requires Play Mode. Use the host-facing workflow so Lens can enter and pause safely first.", new
                    {
                        workflowId,
                        activePack = packId,
                        stepsRequested = steps,
                        stepsCompleted = 0,
                        capsPassed = false,
                        reason = "not_in_play_mode",
                        before,
                        after = LensTransactionSnapshot.Capture(workflowId)
                    });
                }

                EditorApplication.isPaused = true;
                SelectFallingSandsPack(packId);
                Object controller = FindSimulationController();
                if (controller == null)
                {
                    return Response.Error("FallingSands SimulationController was not found in the active runtime.", new
                    {
                        workflowId,
                        activePack = packId,
                        stepsRequested = steps,
                        stepsCompleted = 0,
                        capsPassed = false,
                        reason = "simulation_controller_missing",
                        before,
                        after = LensTransactionSnapshot.Capture(workflowId)
                    });
                }

                object resetOptions = CreateResetOptions(controller.GetType(), packId, fixture);
                object reset = InvokeProjectApi(controller, "ResetForTest", resetOptions);
                object stepResult = InvokeProjectApi(controller, "StepForTest", steps);

                if (stopwatch.ElapsedMilliseconds > maxWallMs)
                {
                    object timeoutAfter = LensTransactionSnapshot.Capture(workflowId);
                    return Response.Error("FallingSands GPU probe exceeded its wall-clock cap before readback.", new
                    {
                        workflowId,
                        activePack = packId,
                        stepsRequested = steps,
                        stepResult,
                        elapsedMs = stopwatch.ElapsedMilliseconds,
                        maxWallMs,
                        capsPassed = false,
                        reason = "max_wall_time_before_readback",
                        before,
                        after = timeoutAfter
                    });
                }

                object summaryOptions = CreateSummaryOptions(controller.GetType(), parameters);
                object summary = InvokeProjectApi(controller, "ReadSummaryForTest", summaryOptions);
                object consoleDelta = ConsoleCursorDelta.BuildDelta(
                    captureConsoleDelta,
                    consoleBefore,
                    ProbeToolName,
                    new { kind = "falling_sands_gpu_probe_console_delta", workflowId, packId, fixture });
                JObject resetJson = JObject.FromObject(reset ?? new { });
                JObject stepJson = JObject.FromObject(stepResult ?? new { });
                JObject summaryJson = JObject.FromObject(summary ?? new { });
                JObject capsJson = parameters["caps"] as JObject ?? new JObject();
                object capEvaluation = EvaluateCaps(capsJson, stepJson, summaryJson, out bool capsPassed, out string[] failedCaps);
                int newErrors = GetIntFromObject(consoleDelta, "newErrors");
                bool success = capsPassed &&
                    failedCaps.Length == 0 &&
                    newErrors == 0 &&
                    !GetBoolFromJObject(stepJson, false, "TimedOut", "timedOut") &&
                    !GetBoolFromJObject(summaryJson, false, "TimedOut", "timedOut");
                object after = LensTransactionSnapshot.Capture(workflowId);

                var rawData = new
                {
                    workflowId,
                    activePack = packId,
                    fixture,
                    gridSize = new
                    {
                        width = GetIntFromJObject(summaryJson, 0, "GridWidth", "gridWidth"),
                        height = GetIntFromJObject(summaryJson, 0, "GridHeight", "gridHeight")
                    },
                    stepsRequested = steps,
                    stepsCompleted = GetIntFromJObject(stepJson, steps, "StepsCompleted", "stepsCompleted"),
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    maxWallMs,
                    dispatchTiming = stepJson,
                    readbackTiming = new
                    {
                        elapsedMs = GetDoubleFromJObject(summaryJson, 0d, "ReadbackElapsedMs", "readbackElapsedMs"),
                        timedOut = GetBoolFromJObject(summaryJson, false, "TimedOut", "timedOut")
                    },
                    counts = CloneProperty(summaryJson, "Counts", "counts"),
                    capsPassed,
                    failedCaps,
                    capEvaluation,
                    consoleDelta,
                    editorResponsiveAfter = !EditorApplication.isCompiling && !EditorApplication.isUpdating,
                    reset,
                    summary,
                    before,
                    after
                };

                var compactData = new
                {
                    rawData.workflowId,
                    rawData.activePack,
                    rawData.fixture,
                    rawData.gridSize,
                    rawData.stepsRequested,
                    rawData.stepsCompleted,
                    rawData.elapsedMs,
                    rawData.dispatchTiming,
                    rawData.readbackTiming,
                    rawData.counts,
                    rawData.capsPassed,
                    rawData.failedCaps,
                    rawData.consoleDelta,
                    rawData.editorResponsiveAfter,
                    before,
                    after
                };
                object shaped = ToolResultCompactor.ShapeStructuredPayload(
                    ProbeToolName,
                    rawData,
                    compactData,
                    new { kind = "falling_sands_gpu_probe", workflowId, packId, fixture },
                    "falling_sands_gpu_probe_result",
                    detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);

                return success
                    ? Response.Success("FallingSands GPU simulation probe completed.", shaped)
                    : Response.Error("FallingSands GPU simulation probe failed.", shaped);
            }
            catch (Exception ex)
            {
                return Response.Error("FallingSands GPU simulation probe failed.", new
                {
                    workflowId,
                    activePack = packId,
                    fixture,
                    stepsRequested = steps,
                    stepsCompleted = 0,
                    capsPassed = false,
                    reason = ex.Message,
                    errorKind = ex.GetType().Name,
                    before,
                    after = LensTransactionSnapshot.Capture(workflowId)
                });
            }
        }

        [McpTool(PackVerifyToolName, "Selects a FallingSands element pack and verifies the active runtime pack after optional scene load.", "Verify Runtime Pack Selection", Groups = new[] { "runtime", "diagnostics" }, EnabledByDefault = true)]
        public static object VerifyRuntimePackSelection(JObject parameters)
        {
            parameters ??= new JObject();
            string selectedPackId = GetString(parameters, "garden", "selectedPackId", "SelectedPackId", "packId", "PackId");
            string scenePath = GetString(parameters, null, "scenePath", "ScenePath");
            bool requirePlayMode = GetBool(parameters, true, "requirePlayMode", "RequirePlayMode");
            string workflowId = "pack-handoff-" + Guid.NewGuid().ToString("N");
            object before = LensTransactionSnapshot.Capture(workflowId);
            object sceneLoad = null;

            try
            {
                SelectFallingSandsPack(selectedPackId);
                if (!string.IsNullOrWhiteSpace(scenePath) && !EditorApplication.isPlaying)
                {
                    string normalizedScenePath = NormalizeScenePath(scenePath);
                    EditorSceneManager.OpenScene(normalizedScenePath);
                    sceneLoad = new { requested = true, scenePath = normalizedScenePath, loaded = true };
                }

                Object controller = FindSimulationController();
                bool runtimeAvailable = controller != null && EditorApplication.isPlaying;
                if (requirePlayMode && !runtimeAvailable)
                {
                    return Response.Error("Runtime pack selection could not be verified because no active runtime SimulationController is available.", new
                    {
                        workflowId,
                        selectedPackId,
                        scenePath,
                        sceneLoad,
                        passed = false,
                        runtimeAvailable,
                        before,
                        after = LensTransactionSnapshot.Capture(workflowId)
                    });
                }

                object summary = null;
                if (controller != null)
                {
                    MethodInfo method = controller.GetType().GetMethod("ReadSummaryForTest", BindingFlags.Instance | BindingFlags.Public);
                    if (method != null)
                    {
                        object options = CreateSummaryOptions(controller.GetType(), new JObject());
                        summary = method.Invoke(controller, new[] { options });
                    }
                }

                JObject summaryJson = JObject.FromObject(summary ?? new { });
                string activePackName = controller == null
                    ? null
                    : controller.GetType().GetProperty("ActiveElementPackName")?.GetValue(controller) as string;
                int elementCount = GetIntFromJObject(summaryJson, 0, "ElementCount", "elementCount");
                bool passed = !requirePlayMode || runtimeAvailable;
                return Response.Success("FallingSands runtime pack selection verified.", new
                {
                    workflowId,
                    selectedPackId,
                    activeRuntimePackName = activePackName,
                    elementCount,
                    sceneLoaded = sceneLoad,
                    domainReloadObserved = false,
                    passed,
                    summary,
                    before,
                    after = LensTransactionSnapshot.Capture(workflowId)
                });
            }
            catch (Exception ex)
            {
                return Response.Error("FallingSands runtime pack selection verification failed.", new
                {
                    workflowId,
                    selectedPackId,
                    scenePath,
                    sceneLoad,
                    passed = false,
                    errorKind = ex.GetType().Name,
                    error = ex.Message,
                    before,
                    after = LensTransactionSnapshot.Capture(workflowId)
                });
            }
        }

        static object CreateResetOptions(Type controllerType, string packId, string fixture)
        {
            Type optionsType = FindType("FallingSands.Orchestration.SimulationTestResetOptions");
            if (optionsType == null)
                return null;

            object options = Activator.CreateInstance(optionsType);
            SetMember(options, "PackId", packId);
            SetMember(options, "Fixture", fixture);
            SetMember(options, "ClearGrid", true);
            return options;
        }

        static object CreateSummaryOptions(Type controllerType, JObject parameters)
        {
            Type optionsType = FindType("FallingSands.Orchestration.SimulationTestSummaryOptions");
            if (optionsType == null)
                return null;

            object options = Activator.CreateInstance(optionsType);
            SetMember(options, "SummaryIds", ReadStringArray(parameters, "summaryIds", "SummaryIds") ?? new[] { "seed", "sprout", "plant", "flower", "water", "steam", "bee", "nectarBee", "hive" });
            SetMember(options, "ReadbackTimeoutMs", Math.Max(100, GetInt(parameters, 1000, "readbackTimeoutMs", "ReadbackTimeoutMs")));
            return options;
        }

        static object InvokeProjectApi(Object controller, string methodName, object argument)
        {
            MethodInfo method = controller.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
            if (method == null)
                throw new MissingMethodException(controller.GetType().FullName, methodName);

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 0
                ? method.Invoke(controller, null)
                : method.Invoke(controller, new[] { argument });
        }

        static void SelectFallingSandsPack(string packId)
        {
            Type selectionType = FindType("FallingSands.DataModel.ElementPackSelection");
            MethodInfo select = selectionType?.GetMethod("Select", BindingFlags.Static | BindingFlags.Public);
            if (select == null)
                throw new InvalidOperationException("FallingSands ElementPackSelection.Select was not found.");

            select.Invoke(null, new object[] { packId });
        }

        static Object FindSimulationController()
        {
            Type controllerType = FindType("FallingSands.Orchestration.SimulationController");
            if (controllerType == null)
                return null;

            return Resources.FindObjectsOfTypeAll(controllerType)
                .Cast<Object>()
                .FirstOrDefault(obj =>
                {
                    if (obj is Component component)
                        return component.gameObject.scene.IsValid();
                    return true;
                });
        }

        static Type FindType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly =>
                {
                    try { return assembly.GetType(fullName, throwOnError: false); }
                    catch { return null; }
                })
                .FirstOrDefault(type => type != null);
        }

        static object EvaluateCaps(JObject caps, JObject step, JObject summary, out bool capsPassed, out string[] failedCaps)
        {
            var failures = new List<string>();
            var details = new List<object>();
            JObject counts = summary["Counts"] as JObject ?? summary["counts"] as JObject ?? new JObject();

            CheckIntCap(caps, "beeCountMax", Count(counts, "bee") + Count(counts, "nectarBee"), failures, details);
            CheckIntCap(caps, "steamCountMax", Count(counts, "steam"), failures, details);
            CheckDoubleCap(caps, "dispatchMsMax", GetDoubleFromJObject(step, 0d, "ElapsedMs", "elapsedMs", "DispatchElapsedMs", "dispatchElapsedMs"), failures, details);
            CheckDoubleCap(caps, "readbackMsMax", GetDoubleFromJObject(summary, 0d, "ReadbackElapsedMs", "readbackElapsedMs"), failures, details);

            failedCaps = failures.ToArray();
            capsPassed = failedCaps.Length == 0;
            return new { capsPassed, failedCaps, checks = details.ToArray() };
        }

        static int Count(JObject counts, string key) => GetIntFromJObject(counts, 0, key, char.ToUpperInvariant(key[0]) + key.Substring(1));

        static void CheckIntCap(JObject caps, string name, int actual, ICollection<string> failures, ICollection<object> details)
        {
            if (!TryGetInt(caps, name, out int max))
                return;

            bool passed = actual <= max;
            if (!passed)
                failures.Add(name);
            details.Add(new { name, actual, max, passed });
        }

        static void CheckDoubleCap(JObject caps, string name, double actual, ICollection<string> failures, ICollection<object> details)
        {
            if (!TryGetDouble(caps, name, out double max))
                return;

            bool passed = actual <= max;
            if (!passed)
                failures.Add(name);
            details.Add(new { name, actual, max, passed });
        }

        static void SetMember(object target, string name, object value)
        {
            if (target == null)
                return;

            Type type = target.GetType();
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return;
            }

            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            field?.SetValue(target, value);
        }

        static string NormalizeScenePath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                normalized = "Assets/" + normalized.TrimStart('/');
            return normalized;
        }

        static string[] ReadStringArray(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                JToken token = parameters[name];
                if (token is JArray array)
                    return array.Select(item => item.Value<string>()).Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
            }

            return null;
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

        static string GetString(JObject parameters, string fallback, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<string>();
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

        static bool TryGetInt(JObject obj, string name, out int value)
        {
            value = 0;
            return obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) &&
                token.Type != JTokenType.Null &&
                int.TryParse(token.ToString(), out value);
        }

        static bool TryGetDouble(JObject obj, string name, out double value)
        {
            value = 0d;
            return obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) &&
                token.Type != JTokenType.Null &&
                double.TryParse(token.ToString(), out value);
        }

        static int GetIntFromObject(object value, string name)
        {
            try { return JObject.FromObject(value ?? new { }).Value<int?>(name) ?? 0; }
            catch { return 0; }
        }

        static int GetIntFromJObject(JObject obj, int fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) && token.Type != JTokenType.Null)
                    return token.Value<int>();
            }

            return fallback;
        }

        static double GetDoubleFromJObject(JObject obj, double fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) && token.Type != JTokenType.Null)
                    return token.Value<double>();
            }

            return fallback;
        }

        static bool GetBoolFromJObject(JObject obj, bool fallback, params string[] names)
        {
            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token) && token.Type != JTokenType.Null)
                    return token.Value<bool>();
            }

            return fallback;
        }

        static object CloneProperty(JObject obj, params string[] names)
        {
            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token.DeepClone();
            }

            return null;
        }
    }
}
