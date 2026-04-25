#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Project;
using Becool.UnityMcpLens.Editor.Models.Project;
using Becool.UnityMcpLens.Editor.Services.GameObjects;

namespace Becool.UnityMcpLens.Editor.Services.Project
{
    sealed class InputSystemProjectService
    {
        const string InputSystemPackageName = "com.unity.inputsystem";

        readonly UnityInputSystemProjectAdapter m_Adapter;

        public InputSystemProjectService(UnityInputSystemProjectAdapter adapter)
        {
            m_Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public ProjectOperationResult Diagnostics(InputSystemDiagnosticsRequest request, GameObjectToolTiming timing)
        {
            request ??= new InputSystemDiagnosticsRequest();
            request.MaxItems = Math.Max(1, request.MaxItems);

            object activeInputHandler;
            object defines;
            object package;
            object assembly;
            object devices = null;
            InputActionAssetsSummary assets = null;
            ProjectEditorLogSignalsResult editorLogSignals = null;
            ProjectCompatibilitySignals compatibilitySignals = null;
            ProjectPackageInfo compatibilityPackage = null;
            ProjectAssemblySignalsResult compatibilityAssemblies = null;

            using (timing.Measure("adapter"))
            {
                activeInputHandler = m_Adapter.ReadActiveInputHandler();
                defines = m_Adapter.ReadScriptingDefineSignals();
                package = m_Adapter.ReadInputSystemPackage();
                assembly = m_Adapter.ReadInputSystemAssemblyStatus();

                if (request.IncludeDevices)
                    devices = m_Adapter.ReadInputDevices(request.MaxItems);

                if (request.IncludeBindings || !string.IsNullOrWhiteSpace(request.AssetPath))
                    assets = m_Adapter.ReadInputActionAssets(request.AssetPath, request.IncludeBindings, request.MaxItems);

                if (request.IncludeCompatibilitySignals)
                    compatibilityPackage = m_Adapter.ReadPackageInfo(InputSystemPackageName);

                if (request.IncludeCompatibilitySignals)
                    compatibilityAssemblies = m_Adapter.ReadPackageAssemblySignals(InputSystemPackageName, request.MaxItems);

                if (request.IncludeEditorLogSignals)
                    editorLogSignals = m_Adapter.ReadEditorLogSignals(m_Adapter.GetInputSystemLogTerms(request.AssetPath), request.MaxItems);
            }

            if (request.IncludeCompatibilitySignals)
            {
                compatibilitySignals = BuildCompatibilitySignals(
                    compatibilityPackage,
                    compatibilityAssemblies,
                    assets?.assets,
                    editorLogSignals,
                    null);
            }

            return ProjectOperationResult.Ok("Retrieved Input System diagnostics.", new
            {
                activeInputHandler,
                defines,
                package,
                assembly,
                devices,
                inputActionAssets = assets,
                editorLogSignals,
                compatibilitySignals
            });
        }

        public ProjectOperationResult PackageCompatibility(PackageCompatibilityRequest request, GameObjectToolTiming timing)
        {
            request ??= new PackageCompatibilityRequest();
            request.MaxItems = Math.Max(1, request.MaxItems);
            if (string.IsNullOrWhiteSpace(request.PackageName))
            {
                return ProjectOperationResult.Error(
                    "packageName is required.",
                    "package_name_required",
                    new { errorKind = "package_name_required" });
            }

            ProjectPackageInfo package;
            ProjectEditorInfo editor;
            ProjectAssemblySignalsResult assemblySignals = null;
            ProjectEditorLogSignalsResult editorLogSignals = null;

            using (timing.Measure("adapter"))
            {
                package = m_Adapter.ReadPackageInfo(request.PackageName);
                editor = m_Adapter.ReadEditorInfo();

                if (request.IncludeAssemblySignals)
                    assemblySignals = m_Adapter.ReadPackageAssemblySignals(request.PackageName, request.MaxItems);

                if (request.IncludeEditorLogSignals)
                    editorLogSignals = m_Adapter.ReadEditorLogSignals(m_Adapter.GetPackageLogTerms(request.PackageName, assemblySignals), request.MaxItems);
            }

            return ProjectOperationResult.Ok("Retrieved package compatibility diagnostics.", new
            {
                package,
                editor,
                assemblySignals,
                compatibility = BuildCompatibilitySignals(package, assemblySignals, null, editorLogSignals, request.ExpectedVersion),
                editorLogSignals
            });
        }

        public ProjectOperationResult InspectInputActionsAsset(InputActionsInspectRequest request, GameObjectToolTiming timing)
        {
            request ??= new InputActionsInspectRequest();
            request.MaxItems = Math.Max(1, request.MaxItems);
            if (string.IsNullOrWhiteSpace(request.AssetPath))
            {
                return ProjectOperationResult.Error(
                    "assetPath is required.",
                    "asset_path_required",
                    new { errorKind = "asset_path_required" });
            }

            InputActionsInspectResult result;
            using (timing.Measure("adapter"))
            {
                result = m_Adapter.InspectInputActionAsset(request.AssetPath, request.IncludeBindings, request.MaxItems);
            }

            return ProjectOperationResult.Ok("Retrieved input actions asset diagnostics.", result);
        }

        public ProjectOperationResult PreviewActiveInputHandler(ActiveInputHandlerRequest request, GameObjectToolTiming timing)
        {
            if (!TryBuildPlan(request, timing, out var plan, out var error))
                return error;

            return ProjectOperationResult.Ok("Previewed active input handler setting.", plan);
        }

        public ProjectOperationResult SetActiveInputHandler(ActiveInputHandlerRequest request, GameObjectToolTiming timing)
        {
            if (!TryBuildPlan(request, timing, out var plan, out var error))
                return error;

            if (!plan.willModify)
                return ProjectOperationResult.Ok("No modifications applied to active input handler setting.", new
                {
                    applied = false,
                    plan.current,
                    plan.requested,
                    plan.restartRequired,
                    plan.expectedDefines,
                    plan.validationMessages
                });

            string applyError;
            ActiveInputHandlerState readback;
            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TrySetActiveInputHandler(plan.requested, out applyError))
                {
                    return ProjectOperationResult.Error(
                        $"Failed to set active input handler: {applyError}",
                        "apply_failed",
                        new { errorKind = "apply_failed", error = applyError });
                }

                if (request.Save)
                    m_Adapter.SaveProjectSettings();

                if (request.RequestScriptReload)
                    m_Adapter.RequestScriptReload();

                readback = m_Adapter.ReadActiveInputHandler();
            }

            bool applied = readback.rawValue == plan.requested.rawValue;
            if (!applied)
            {
                return ProjectOperationResult.Error(
                    "Active input handler setting did not match requested mode after apply.",
                    "readback_mismatch",
                    new
                    {
                        errorKind = "readback_mismatch",
                        requested = plan.requested,
                        readback
                    });
            }

            return ProjectOperationResult.Ok("Active input handler setting updated.", new
            {
                applied = true,
                previous = plan.current,
                current = readback,
                requested = plan.requested,
                saved = request.Save,
                scriptReloadRequested = request.RequestScriptReload,
                restartRequired = plan.restartRequired,
                expectedDefines = plan.expectedDefines,
                validationMessages = plan.validationMessages
            });
        }

        bool TryBuildPlan(
            ActiveInputHandlerRequest request,
            GameObjectToolTiming timing,
            out InputHandlerPlan plan,
            out ProjectOperationResult error)
        {
            plan = null;
            error = null;
            request ??= new ActiveInputHandlerRequest();

            var requested = m_Adapter.BuildRequestedInputHandler(request.Mode);
            if (requested.rawValue < 0)
            {
                using (timing.Measure("adapter"))
                {
                }

                error = ProjectOperationResult.Error(
                    "mode must be 'legacy', 'inputSystem', or 'both'.",
                    "invalid_mode",
                    new { errorKind = "invalid_mode", allowedModes = new[] { "legacy", "inputSystem", "both" } });
                return false;
            }

            ActiveInputHandlerState current;
            object expectedDefines;
            using (timing.Measure("adapter"))
            {
                current = m_Adapter.ReadActiveInputHandler();
                expectedDefines = m_Adapter.ReadScriptingDefineSignals();
            }

            bool willModify = current == null || current.rawValue != requested.rawValue;
            bool restartRequired = willModify;

            var validationMessages = new List<ProjectValidationMessage>();
            if (willModify && (requested.mode == "inputSystem" || requested.mode == "both"))
            {
                validationMessages.Add(new ProjectValidationMessage
                {
                    severity = "info",
                    code = "restart_required",
                    message = "Changing the active input handler usually requires an editor restart or script reload before scripting defines and devices fully settle."
                });
            }

            plan = new InputHandlerPlan
            {
                current = current,
                requested = requested,
                willModify = willModify,
                restartRequired = restartRequired,
                expectedDefines = expectedDefines,
                validationMessages = validationMessages
            };
            return true;
        }

        static ProjectCompatibilitySignals BuildCompatibilitySignals(
            ProjectPackageInfo package,
            ProjectAssemblySignalsResult assemblySignals,
            IEnumerable<InputActionsInspectResult> assets,
            ProjectEditorLogSignalsResult editorLogSignals,
            string expectedVersion)
        {
            var issues = new List<ProjectDiagnosticIssue>();

            if (package == null || !package.installed)
            {
                issues.Add(new ProjectDiagnosticIssue
                {
                    severity = "error",
                    code = "package_not_installed",
                    message = $"Package '{package?.requestedName ?? "(unknown)"}' is not installed."
                });
            }

            if (!string.IsNullOrWhiteSpace(package?.manifestVersion) &&
                !string.IsNullOrWhiteSpace(package?.registeredVersion) &&
                !string.Equals(package.manifestVersion, package.registeredVersion, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ProjectDiagnosticIssue
                {
                    severity = "warning",
                    code = "manifest_registered_version_mismatch",
                    message = $"Manifest version '{package.manifestVersion}' does not match registered version '{package.registeredVersion}' for '{package.requestedName}'."
                });
            }

            if (!string.IsNullOrWhiteSpace(expectedVersion))
            {
                if (!string.IsNullOrWhiteSpace(package?.manifestVersion) &&
                    !string.Equals(package.manifestVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ProjectDiagnosticIssue
                    {
                        severity = "warning",
                        code = "expected_manifest_version_mismatch",
                        message = $"Manifest version '{package.manifestVersion}' does not match expected version '{expectedVersion}' for '{package.requestedName}'."
                    });
                }

                if (!string.IsNullOrWhiteSpace(package?.registeredVersion) &&
                    !string.Equals(package.registeredVersion, expectedVersion, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new ProjectDiagnosticIssue
                    {
                        severity = "warning",
                        code = "expected_registered_version_mismatch",
                        message = $"Registered version '{package.registeredVersion}' does not match expected version '{expectedVersion}' for '{package.requestedName}'."
                    });
                }
            }

            foreach (var signal in assemblySignals?.assemblies ?? Array.Empty<ProjectAssemblySignal>())
            {
                if (!signal.loaded)
                {
                    issues.Add(new ProjectDiagnosticIssue
                    {
                        severity = "error",
                        code = "assembly_not_loaded",
                        message = $"Assembly '{signal.name}' is not loaded for package '{package?.requestedName}'."
                    });
                    continue;
                }

                if (!signal.typeLoadOk)
                {
                    issues.Add(new ProjectDiagnosticIssue
                    {
                        severity = "error",
                        code = "assembly_type_load_failed",
                        message = $"Assembly '{signal.name}' reported a type-load failure: {signal.error ?? "unknown error"}"
                    });
                }
            }

            foreach (var asset in assets ?? Array.Empty<InputActionsInspectResult>())
            {
                foreach (var issue in asset.issues ?? new List<ProjectDiagnosticIssue>())
                {
                    issues.Add(new ProjectDiagnosticIssue
                    {
                        severity = issue.severity,
                        code = issue.code,
                        message = $"Asset '{asset.path}': {issue.message}"
                    });
                }
            }

            foreach (var issue in BuildEditorLogIssues(editorLogSignals?.signals))
                issues.Add(issue);

            return new ProjectCompatibilitySignals
            {
                status = ComputeStatus(issues),
                issues = issues
            };
        }

        static string ComputeStatus(IEnumerable<ProjectDiagnosticIssue> issues)
        {
            var severities = (issues ?? Array.Empty<ProjectDiagnosticIssue>())
                .Select(issue => (issue?.severity ?? string.Empty).Trim().ToLowerInvariant())
                .ToArray();
            if (severities.Any(severity => severity == "error"))
                return "error";
            if (severities.Any(severity => severity == "warning"))
                return "warning";

            return "ok";
        }

        static IEnumerable<ProjectDiagnosticIssue> BuildEditorLogIssues(IEnumerable<ProjectEditorLogSignal> signals)
        {
            return (signals ?? Array.Empty<ProjectEditorLogSignal>())
                .Where(signal => !string.IsNullOrWhiteSpace(signal?.message))
                .GroupBy(
                    signal => NormalizeLogMessage(signal.message),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => CreateEditorLogIssue(group.First().message, group.Count()))
                .ToArray();
        }

        static ProjectDiagnosticIssue CreateEditorLogIssue(string message, int repeatCount)
        {
            bool benignSkip = IsBenignInputSystemAssemblySkip(message);
            string suffix = repeatCount > 1 ? $" (repeated {repeatCount}x)" : string.Empty;

            return new ProjectDiagnosticIssue
            {
                severity = benignSkip ? "info" : GuessLogSeverity(message),
                code = benignSkip ? "editor_log_signal_info" : "editor_log_signal",
                message = $"{message}{suffix}"
            };
        }

        static string NormalizeLogMessage(string message)
        {
            return (message ?? string.Empty).Trim();
        }

        static bool IsBenignInputSystemAssemblySkip(string message)
        {
            string normalized = NormalizeLogMessage(message);
            return normalized.IndexOf("Unity.InputSystem.IntegrationTests.dll", StringComparison.OrdinalIgnoreCase) >= 0 &&
                normalized.IndexOf("Loading of assembly skipped", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static string GuessLogSeverity(string message)
        {
            string normalized = (message ?? string.Empty).Trim();
            if (normalized.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("exception", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "error";
            }

            return "warning";
        }
    }
}
