#nullable disable
using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Project;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Models.Project;
using Becool.UnityMcpLens.Editor.Services.GameObjects;
using Becool.UnityMcpLens.Editor.Services.Project;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class ProjectInputSystemTools
    {
        const string DiagnosticsToolName = "Unity.InputSystem.Diagnostics";
        const string PreviewActiveInputHandlerToolName = "Unity.ProjectSettings.PreviewActiveInputHandler";
        const string SetActiveInputHandlerToolName = "Unity.ProjectSettings.SetActiveInputHandler";

        const string DiagnosticsDescription = @"Reads Input System and active-input-handler diagnostics without mutation.

Uses package metadata, reflection, input action JSON parsing, and editor log scanning so it remains safe when com.unity.inputsystem is absent or broken.";

        const string PreviewActiveInputHandlerDescription = @"Previews a Unity ProjectSettings active input handler change without mutation.

Modes: legacy, inputSystem, both.";

        const string SetActiveInputHandlerDescription = @"Sets Unity ProjectSettings active input handler through editor-authored PlayerSettings mutation.

Modes: legacy, inputSystem, both. Usually requires an editor restart or script reload before defines and devices fully settle.";

        static readonly UnityInputSystemProjectAdapter Adapter = new UnityInputSystemProjectAdapter();
        static readonly InputSystemProjectService Service = new InputSystemProjectService(Adapter);

        [McpSchema(DiagnosticsToolName)]
        public static object GetDiagnosticsSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    assetPath = new { type = "string", description = "Optional .inputactions asset path to inspect." },
                    includeDevices = new { type = "boolean", description = "Reflect loaded Input System devices when available." },
                    includeBindings = new { type = "boolean", description = "Include compact binding rows for inspected .inputactions assets." },
                    includeEditorLogSignals = new { type = "boolean", description = "Scan recent Editor.log tail for Input System related errors and warnings." },
                    maxItems = new { type = "integer", description = "Maximum list items per section. Defaults to 8." },
                    includeDetails = new { type = "boolean", description = "Store the full structured diagnostics payload behind a detail ref when supported." }
                }
            };
        }

        [McpSchema(PreviewActiveInputHandlerToolName)]
        public static object GetPreviewActiveInputHandlerSchema()
        {
            return BuildActiveInputHandlerSchema();
        }

        [McpSchema(SetActiveInputHandlerToolName)]
        public static object GetSetActiveInputHandlerSchema()
        {
            return BuildActiveInputHandlerSchema();
        }

        [McpTool(DiagnosticsToolName, DiagnosticsDescription, "Input System Diagnostics", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object Diagnostics(JObject @params)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(DiagnosticsToolName, "diagnostics", GetUtf8ByteCount(@params.ToString(Formatting.None)));
            ProjectOperationResult result;
            string errorKind = null;

            try
            {
                InputSystemDiagnosticsRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeDiagnosticsRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = Service.Diagnostics(request, timing);
                }

                if (request.IncludeDetails && result.success)
                    result.data = AddDetailRef(DiagnosticsToolName, result.data);
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = ProjectOperationResult.Error($"Internal error reading Input System diagnostics: {ex.Message}", errorKind);
            }

            return ShapeResponse(DiagnosticsToolName, result, timing, errorKind, compactSuccessData: true);
        }

        [McpTool(PreviewActiveInputHandlerToolName, PreviewActiveInputHandlerDescription, "Preview Active Input Handler", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object PreviewActiveInputHandler(JObject @params)
        {
            return HandleActiveInputHandlerTool(PreviewActiveInputHandlerToolName, "preview_active_input_handler", @params, apply: false);
        }

        [McpTool(SetActiveInputHandlerToolName, SetActiveInputHandlerDescription, "Set Active Input Handler", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object SetActiveInputHandler(JObject @params)
        {
            return HandleActiveInputHandlerTool(SetActiveInputHandlerToolName, "set_active_input_handler", @params, apply: true);
        }

        static object HandleActiveInputHandlerTool(string toolName, string action, JObject @params, bool apply)
        {
            @params ??= new JObject();
            var timing = new GameObjectToolTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            ProjectOperationResult result;
            string errorKind = null;

            try
            {
                ActiveInputHandlerRequest request;
                using (timing.Measure("normalization"))
                {
                    request = NormalizeActiveInputHandlerRequest(@params);
                }

                using (timing.Measure("service"))
                {
                    result = apply
                        ? Service.SetActiveInputHandler(request, timing)
                        : Service.PreviewActiveInputHandler(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = ProjectOperationResult.Error($"Internal error processing active input handler request: {ex.Message}", errorKind);
            }

            return ShapeResponse(toolName, result, timing, errorKind, compactSuccessData: false);
        }

        static object BuildActiveInputHandlerSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    mode = new
                    {
                        type = "string",
                        description = "Requested active input handler mode.",
                        @enum = new[] { "legacy", "inputSystem", "both" }
                    },
                    save = new { type = "boolean", description = "Save project settings after apply. Defaults to true." },
                    requestScriptReload = new { type = "boolean", description = "Request a script compilation/reload after apply." }
                },
                required = new[] { "mode" }
            };
        }

        static InputSystemDiagnosticsRequest NormalizeDiagnosticsRequest(JObject parameters)
        {
            return new InputSystemDiagnosticsRequest
            {
                AssetPath = GetString(parameters, "assetPath", "AssetPath"),
                IncludeDevices = GetBool(parameters, false, "includeDevices", "IncludeDevices"),
                IncludeBindings = GetBool(parameters, false, "includeBindings", "IncludeBindings"),
                IncludeEditorLogSignals = GetBool(parameters, false, "includeEditorLogSignals", "IncludeEditorLogSignals"),
                IncludeDetails = GetBool(parameters, false, "includeDetails", "IncludeDetails"),
                MaxItems = Math.Max(1, GetInt(parameters, 8, "maxItems", "MaxItems"))
            };
        }

        static ActiveInputHandlerRequest NormalizeActiveInputHandlerRequest(JObject parameters)
        {
            return new ActiveInputHandlerRequest
            {
                Mode = GetString(parameters, "mode", "Mode"),
                Save = GetBool(parameters, true, "save", "Save"),
                RequestScriptReload = GetBool(parameters, false, "requestScriptReload", "RequestScriptReload")
            };
        }

        static object ShapeResponse(string toolName, ProjectOperationResult result, GameObjectToolTiming timing, string fallbackErrorKind, bool compactSuccessData)
        {
            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.success
                    ? Response.Success(result.message, compactSuccessData ? ToolResultCompactor.ShapeJsonPayload(toolName, result.message, result.data) : result.data)
                    : Response.Error(result.message, result.errorData ?? new { errorKind = result.errorKind ?? fallbackErrorKind });

                timing.SetResponseBytes(GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.success, result.success ? null : result.errorKind ?? fallbackErrorKind);
            return response;
        }

        static object AddDetailRef(string toolName, object data)
        {
            string serialized = JsonConvert.SerializeObject(data, Formatting.None);
            int rawBytes = GetUtf8ByteCount(serialized);
            var detailRef = ToolResultCompactor.CreateStoredDetailRef(
                toolName,
                data,
                rawBytes,
                new
                {
                    kind = "input_system_diagnostics",
                    rawBytes
                });

            if (detailRef == null)
                return data;

            var shaped = JObject.FromObject(data);
            shaped["detailRef"] = JToken.FromObject(detailRef);
            return shaped;
        }

        static string GetString(JObject parameters, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                    return token?.Type == JTokenType.Null ? null : token?.ToString();
            }

            return null;
        }

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                    return token.Type == JTokenType.Boolean ? token.Value<bool>() : bool.TryParse(token.ToString(), out bool parsed) ? parsed : defaultValue;
            }

            return defaultValue;
        }

        static int GetInt(JObject parameters, int defaultValue, params string[] names)
        {
            foreach (string name in names)
            {
                if (parameters.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out var token))
                    return token.Type == JTokenType.Integer ? token.Value<int>() : int.TryParse(token.ToString(), out int parsed) ? parsed : defaultValue;
            }

            return defaultValue;
        }

        static int GetUtf8ByteCount(string value) => Encoding.UTF8.GetByteCount(value ?? string.Empty);
    }
}
