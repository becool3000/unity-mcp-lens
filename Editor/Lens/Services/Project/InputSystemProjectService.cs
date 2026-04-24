#nullable disable
using System;
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Project;
using Becool.UnityMcpLens.Editor.Models.Project;
using Becool.UnityMcpLens.Editor.Services.GameObjects;

namespace Becool.UnityMcpLens.Editor.Services.Project
{
    sealed class InputSystemProjectService
    {
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
            object assets = null;
            object editorLogSignals = null;

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

                if (request.IncludeEditorLogSignals)
                    editorLogSignals = m_Adapter.ReadEditorLogSignals(request.MaxItems);
            }

            return ProjectOperationResult.Ok("Retrieved Input System diagnostics.", new
            {
                activeInputHandler,
                defines,
                package,
                assembly,
                devices,
                inputActionAssets = assets,
                editorLogSignals
            });
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

            var validationMessages = new List<ProjectValidationMessage>();
            if (requested.mode == "inputSystem" || requested.mode == "both")
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
                willModify = current == null || current.rawValue != requested.rawValue,
                restartRequired = true,
                expectedDefines = expectedDefines,
                validationMessages = validationMessages
            };
            return true;
        }
    }
}
