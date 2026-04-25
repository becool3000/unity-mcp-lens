#nullable disable
using System;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Project;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Models.Project;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Services.Project;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class ProjectInputSystemTools
    {
        const string DiagnosticsToolName = "Unity.InputSystem.Diagnostics";
        const string PackageCompatibilityToolName = "Unity.Project.PackageCompatibility";
        const string InspectInputActionsAssetToolName = "Unity.InputActions.InspectAsset";
        const string PreviewActiveInputHandlerToolName = "Unity.ProjectSettings.PreviewActiveInputHandler";
        const string SetActiveInputHandlerToolName = "Unity.ProjectSettings.SetActiveInputHandler";

        const string DiagnosticsDescription = @"Reads Input System and active-input-handler diagnostics without mutation.

Uses package metadata, reflection, input action JSON parsing, and editor log scanning so it remains safe when com.unity.inputsystem is absent or broken.";

        const string PackageCompatibilityDescription = @"Reads package compatibility, assembly, and editor-log diagnostics without mutation.

Returns package install/version state, editor metadata, assembly load/type-load signals, and filtered Editor.log matches when requested.";

        const string InspectInputActionsAssetDescription = @"Reads a .inputactions asset without mutation.

Returns compact map/action/binding counts, wrapper-generation metadata, binding rows when requested, and derived asset issues.";

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
                    includeCompatibilitySignals = new { type = "boolean", description = "Include compact package, assembly, asset, and editor-log compatibility issues." },
                    maxItems = new { type = "integer", description = "Maximum list items per section. Defaults to 8." },
                    includeDetails = new { type = "boolean", description = "Store the full structured diagnostics payload behind a detail ref when supported." }
                }
            };
        }

        [McpSchema(PackageCompatibilityToolName)]
        public static object GetPackageCompatibilitySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    packageName = new { type = "string", description = "Package name to inspect, for example com.unity.inputsystem." },
                    expectedVersion = new { type = "string", description = "Optional expected version to compare against manifest and registered package versions." },
                    includeEditorLogSignals = new { type = "boolean", description = "Include compact Editor.log matches for the requested package." },
                    includeAssemblySignals = new { type = "boolean", description = "Include package assembly load/type-load signals. Defaults to true." },
                    maxItems = new { type = "integer", description = "Maximum list items per section. Defaults to 8." }
                },
                required = new[] { "packageName" }
            };
        }

        [McpSchema(InspectInputActionsAssetToolName)]
        public static object GetInspectInputActionsAssetSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    assetPath = new { type = "string", description = "Input actions asset path to inspect." },
                    includeBindings = new { type = "boolean", description = "Include compact binding rows. Defaults to false." },
                    maxItems = new { type = "integer", description = "Maximum returned binding rows. Defaults to 8." }
                },
                required = new[] { "assetPath" }
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
            var timing = new ToolOperationTiming(DiagnosticsToolName, "diagnostics", GetUtf8ByteCount(@params.ToString(Formatting.None)));
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

        [McpTool(PackageCompatibilityToolName, PackageCompatibilityDescription, "Package Compatibility", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object PackageCompatibility(JObject @params)
        {
            return HandleProjectDiagnosticsTool(
                PackageCompatibilityToolName,
                "package_compatibility",
                @params,
                NormalizePackageCompatibilityRequest,
                (request, timing) => Service.PackageCompatibility(request, timing),
                "Internal error reading package compatibility diagnostics: {0}");
        }

        [McpTool(InspectInputActionsAssetToolName, InspectInputActionsAssetDescription, "Inspect Input Actions Asset", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static object InspectInputActionsAsset(JObject @params)
        {
            return HandleProjectDiagnosticsTool(
                InspectInputActionsAssetToolName,
                "inspect_input_actions_asset",
                @params,
                NormalizeInputActionsInspectRequest,
                (request, timing) => Service.InspectInputActionsAsset(request, timing),
                "Internal error reading input actions asset diagnostics: {0}");
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
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
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
                IncludeCompatibilitySignals = GetBool(parameters, false, "includeCompatibilitySignals", "IncludeCompatibilitySignals"),
                IncludeDetails = GetBool(parameters, false, "includeDetails", "IncludeDetails"),
                MaxItems = Math.Max(1, GetInt(parameters, 8, "maxItems", "MaxItems"))
            };
        }

        static PackageCompatibilityRequest NormalizePackageCompatibilityRequest(JObject parameters)
        {
            return new PackageCompatibilityRequest
            {
                PackageName = GetString(parameters, "packageName", "PackageName"),
                ExpectedVersion = GetString(parameters, "expectedVersion", "ExpectedVersion"),
                IncludeEditorLogSignals = GetBool(parameters, false, "includeEditorLogSignals", "IncludeEditorLogSignals"),
                IncludeAssemblySignals = GetBool(parameters, true, "includeAssemblySignals", "IncludeAssemblySignals"),
                MaxItems = Math.Max(1, GetInt(parameters, 8, "maxItems", "MaxItems"))
            };
        }

        static InputActionsInspectRequest NormalizeInputActionsInspectRequest(JObject parameters)
        {
            return new InputActionsInspectRequest
            {
                AssetPath = GetString(parameters, "assetPath", "AssetPath"),
                IncludeBindings = GetBool(parameters, false, "includeBindings", "IncludeBindings"),
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

        static object ShapeResponse(string toolName, ProjectOperationResult result, ToolOperationTiming timing, string fallbackErrorKind, bool compactSuccessData)
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

        static object HandleProjectDiagnosticsTool<TRequest>(
            string toolName,
            string action,
            JObject @params,
            Func<JObject, TRequest> normalize,
            Func<TRequest, ToolOperationTiming, ProjectOperationResult> execute,
            string errorFormat)
        {
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, action, GetUtf8ByteCount(@params.ToString(Formatting.None)));
            ProjectOperationResult result;
            string errorKind = null;

            try
            {
                TRequest request;
                using (timing.Measure("normalization"))
                {
                    request = normalize(@params);
                }

                using (timing.Measure("service"))
                {
                    result = execute(request, timing);
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = ProjectOperationResult.Error(string.Format(errorFormat, ex.Message), errorKind);
            }

            return ShapeResponse(toolName, result, timing, errorKind, compactSuccessData: true);
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
