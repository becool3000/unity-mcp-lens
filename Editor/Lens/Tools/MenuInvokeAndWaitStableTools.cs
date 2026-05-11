#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class MenuInvokeAndWaitStableTools
    {
        const string ToolName = "Unity.Menu.InvokeAndWaitStable";

        static readonly HashSet<string> k_MenuPathBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "File/Quit"
        };

        [McpSchema(ToolName)]
        public static object GetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    menuPath = new { type = "string", description = "Exact Unity editor menu path to invoke." },
                    expectedScenePath = new { type = "string", description = "Optional scene path expected to be loaded after invocation." },
                    waitForAssetRefresh = new { type = "boolean", description = "Wait for compiling/importing/building/play-transition blockers to clear. Defaults to true." },
                    waitForSceneDirtyClear = new { type = "boolean", description = "Wait for open loaded scenes to have no dirty flag. Defaults to false." },
                    timeoutMs = new { type = "integer", description = "Maximum wait time in milliseconds. Defaults to 60000." },
                    captureConsoleDelta = new { type = "boolean", description = "Capture error-count delta before and after invocation. Defaults to true." }
                },
                required = new[] { "menuPath" }
            };
        }

        [McpTool(ToolName,
            "Invokes a Unity editor menu item and waits for editor/import/scene stability evidence before returning.",
            "Invoke Menu And Wait Stable",
            Groups = new[] { "console", "editor" },
            EnabledByDefault = true)]
        public static async Task<object> InvokeAndWaitStable(JObject @params)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(ToolName, "invoke_and_wait_stable", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                string menuPath;
                string expectedScenePath;
                bool waitForAssetRefresh;
                bool waitForSceneDirtyClear;
                int timeoutMs;
                bool captureConsoleDelta;

                using (timing.Measure("normalization"))
                {
                    menuPath = GetString(@params, "menuPath", "MenuPath");
                    expectedScenePath = NormalizeScenePath(GetString(@params, "expectedScenePath", "ExpectedScenePath"));
                    waitForAssetRefresh = GetBool(@params, true, "waitForAssetRefresh", "WaitForAssetRefresh");
                    waitForSceneDirtyClear = GetBool(@params, false, "waitForSceneDirtyClear", "WaitForSceneDirtyClear");
                    timeoutMs = Math.Clamp(GetInt(@params, 60000, "timeoutMs", "TimeoutMs"), 1000, 600000);
                    captureConsoleDelta = GetBool(@params, true, "captureConsoleDelta", "CaptureConsoleDelta");
                }

                using (timing.Measure("service"))
                {
                    data = await InvokeAsync(menuPath, expectedScenePath, waitForAssetRefresh, waitForSceneDirtyClear, timeoutMs, captureConsoleDelta);
                    string serialized = JsonConvert.SerializeObject(data, Formatting.None);
                    success = serialized.IndexOf("\"status\":\"ready\"", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        serialized.IndexOf("\"timedOut\":true", StringComparison.OrdinalIgnoreCase) < 0 &&
                        serialized.IndexOf("\"consoleErrorsDetected\":true", StringComparison.OrdinalIgnoreCase) < 0 &&
                        serialized.IndexOf("\"expectedSceneLoaded\":false", StringComparison.OrdinalIgnoreCase) < 0;
                    errorKind = success ? null : ExtractReason(serialized) ?? "menu_invoke_wait_failed";
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message,
                    finalState = EditorToolStateHelpers.BuildEditorState()
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success("Menu invocation completed and editor stability checks passed.", ToolResultCompactor.ShapeStructuredPayload(
                        ToolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "menu_invoke_wait_stable_full_result" },
                        "menu_invoke_wait_stable",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error("MENU_INVOKE_WAIT_STABLE_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static async Task<object> InvokeAsync(
            string menuPath,
            string expectedScenePath,
            bool waitForAssetRefresh,
            bool waitForSceneDirtyClear,
            int timeoutMs,
            bool captureConsoleDelta)
        {
            if (string.IsNullOrWhiteSpace(menuPath))
                return Failed("menu_path_required", "menuPath is required.");
            if (k_MenuPathBlacklist.Contains(menuPath))
                return Failed("menu_path_blocked", $"Execution of menu item '{menuPath}' is blocked for safety reasons.");

            int initialConsoleErrorCount = captureConsoleDelta ? EditorToolStateHelpers.CountConsoleErrors() : -1;
            bool invoked = EditorApplication.ExecuteMenuItem(menuPath);
            var attempts = new List<object>();
            bool timedOut = false;
            bool stable = !waitForAssetRefresh || EditorToolStateHelpers.IsStable();
            bool sceneDirtyClear = !waitForSceneDirtyClear || GetDirtyScenePaths().Length == 0;

            if (invoked && (waitForAssetRefresh || waitForSceneDirtyClear))
            {
                DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                while (DateTime.UtcNow < deadline)
                {
                    string[] dirtyScenes = GetDirtyScenePaths();
                    stable = !waitForAssetRefresh || EditorToolStateHelpers.IsStable();
                    sceneDirtyClear = !waitForSceneDirtyClear || dirtyScenes.Length == 0;
                    var attempt = new
                    {
                        utc = DateTime.UtcNow.ToString("O"),
                        stable,
                        sceneDirtyClear,
                        dirtyScenePaths = dirtyScenes,
                        editorState = EditorToolStateHelpers.BuildEditorState()
                    };
                    attempts.Add(attempt);

                    if (stable && sceneDirtyClear)
                        break;

                    await Task.Delay(250);
                }

                timedOut = !(stable && sceneDirtyClear);
            }

            string normalizedExpectedScenePath = NormalizeScenePath(expectedScenePath);
            bool expectedSceneLoaded = string.IsNullOrWhiteSpace(normalizedExpectedScenePath) ||
                GetLoadedScenePaths().Contains(normalizedExpectedScenePath, StringComparer.OrdinalIgnoreCase);
            int finalConsoleErrorCount = captureConsoleDelta ? EditorToolStateHelpers.CountConsoleErrors() : -1;
            int newConsoleErrorCount = captureConsoleDelta && initialConsoleErrorCount >= 0 && finalConsoleErrorCount >= 0
                ? Math.Max(0, finalConsoleErrorCount - initialConsoleErrorCount)
                : 0;

            return new
            {
                status = invoked ? "ready" : "failed",
                reason = invoked ? null : "menu_not_found_or_disabled",
                menuPath,
                invoked,
                expectedScenePath = normalizedExpectedScenePath,
                expectedSceneLoaded,
                waitForAssetRefresh,
                waitForSceneDirtyClear,
                timeoutMs,
                timedOut,
                stable,
                sceneDirtyClear,
                dirtyScenePaths = GetDirtyScenePaths(),
                loadedScenePaths = GetLoadedScenePaths(),
                consoleDelta = captureConsoleDelta
                    ? new
                    {
                        initialConsoleErrorCount,
                        finalConsoleErrorCount,
                        newConsoleErrorCount,
                        consoleErrorsDetected = newConsoleErrorCount > 0
                    }
                    : null,
                consoleErrorsDetected = newConsoleErrorCount > 0,
                finalState = EditorToolStateHelpers.BuildEditorState(),
                attempts = attempts.ToArray()
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray attempts = root["attempts"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                reason = root["reason"],
                menuPath = root["menuPath"],
                invoked = root["invoked"],
                expectedScenePath = root["expectedScenePath"],
                expectedSceneLoaded = root["expectedSceneLoaded"],
                waitForAssetRefresh = root["waitForAssetRefresh"],
                waitForSceneDirtyClear = root["waitForSceneDirtyClear"],
                timeoutMs = root["timeoutMs"],
                timedOut = root["timedOut"],
                stable = root["stable"],
                sceneDirtyClear = root["sceneDirtyClear"],
                dirtyScenePaths = root["dirtyScenePaths"],
                loadedScenePaths = root["loadedScenePaths"],
                consoleDelta = root["consoleDelta"],
                consoleErrorsDetected = root["consoleErrorsDetected"],
                finalState = root["finalState"],
                attemptCount = attempts.Count
            };
        }

        static object Failed(string reason, string message)
        {
            return new
            {
                status = "failed",
                reason,
                message,
                finalState = EditorToolStateHelpers.BuildEditorState()
            };
        }

        static string ExtractReason(string serialized)
        {
            try
            {
                return JObject.Parse(serialized)["reason"]?.Value<string>();
            }
            catch
            {
                return null;
            }
        }

        static string[] GetLoadedScenePaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && !string.IsNullOrWhiteSpace(scene.path))
                    paths.Add(NormalizeScenePath(scene.path));
            }

            return paths.ToArray();
        }

        static string[] GetDirtyScenePaths()
        {
            var paths = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.IsValid() && scene.isLoaded && scene.isDirty)
                    paths.Add(string.IsNullOrWhiteSpace(scene.path) ? scene.name : NormalizeScenePath(scene.path));
            }

            return paths.ToArray();
        }

        static string NormalizeScenePath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Replace('\\', '/').Trim();
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
            return GetToken(parameters, names)?.Value<string>();
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
