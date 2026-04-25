#nullable disable
using System;
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectComponentMutationService
    {
        sealed class ComponentMutationPlan
        {
            public bool success { get; set; }
            public string message { get; set; }
            public string errorKind { get; set; }
            public object errorData { get; set; }
            public UnityGameObjectHandle target { get; set; }
            public GameObjectTargetSummary targetSummary { get; set; }
            public GameObjectInfo currentObject { get; set; }
            public string operation { get; set; }
            public UnityComponentMutationStatus mutation { get; set; }

            public bool willModify => mutation?.willModify ?? false;

            public static ComponentMutationPlan Error(string message, string errorKind, object errorData = null)
            {
                return new ComponentMutationPlan
                {
                    success = false,
                    message = message,
                    errorKind = errorKind,
                    errorData = errorData
                };
            }
        }

        readonly UnityGameObjectAdapter m_GameObjectAdapter;
        readonly UnityComponentMutationAdapter m_ComponentMutationAdapter;

        public GameObjectComponentMutationService(UnityGameObjectAdapter gameObjectAdapter, UnityComponentMutationAdapter componentMutationAdapter)
        {
            m_GameObjectAdapter = gameObjectAdapter;
            m_ComponentMutationAdapter = componentMutationAdapter;
        }

        public GameObjectOperationResult Preview(GameObjectComponentMutationRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildPlan(request, timing, splitTool: true);
            if (!plan.success)
                return ToErrorResult(plan);

            return GameObjectOperationResult.Ok(
                plan.willModify
                    ? $"Previewed {plan.mutation.changes.Count} component change(s) for '{plan.target.Name}'."
                    : $"No component changes would be applied to '{plan.target.Name}'.",
                new GameObjectComponentMutationPreviewResult
                {
                    target = plan.targetSummary,
                    operation = plan.operation,
                    willModify = plan.willModify,
                    changes = plan.mutation.changes,
                    validationMessages = plan.mutation.validationMessages,
                    component = plan.mutation.component?.Info,
                    @object = plan.currentObject
                });
        }

        public GameObjectOperationResult Apply(GameObjectComponentMutationRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildPlan(request, timing, splitTool: true);
            if (!plan.success)
                return ToErrorResult(plan);

            if (!plan.willModify)
            {
                return GameObjectOperationResult.Ok(
                    $"No component changes applied to GameObject '{plan.target.Name}'.",
                    new GameObjectComponentMutationApplyResult
                    {
                        target = plan.targetSummary,
                        operation = plan.operation,
                        applied = false,
                        changes = plan.mutation.changes,
                        validationMessages = plan.mutation.validationMessages,
                        component = plan.mutation.component?.Info,
                        @object = plan.currentObject
                    });
            }

            UnityComponentMutationStatus applyResult;
            using (timing.Measure("adapter"))
            {
                applyResult = ApplyMutation(plan, request);
            }

            if (!applyResult.success)
                return ToErrorResult(plan, applyResult);

            return GameObjectOperationResult.Ok(
                applyResult.message ?? $"Component changes applied to GameObject '{plan.target.Name}'.",
                new GameObjectComponentMutationApplyResult
                {
                    target = m_GameObjectAdapter.ToTargetSummary(plan.target),
                    operation = plan.operation,
                    applied = true,
                    changes = applyResult.changes,
                    validationMessages = applyResult.validationMessages,
                    component = applyResult.component?.Info,
                    @object = m_GameObjectAdapter.ToGameObjectInfo(plan.target)
                });
        }

        public GameObjectOperationResult ApplyLegacy(GameObjectComponentMutationRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildPlan(request, timing, splitTool: false);
            if (!plan.success)
                return ToErrorResult(plan);

            UnityComponentMutationStatus applyResult = null;
            if (plan.willModify)
            {
                using (timing.Measure("adapter"))
                {
                    applyResult = ApplyMutation(plan, request);
                }

                if (!applyResult.success)
                    return ToErrorResult(plan, applyResult);
            }

            string message = request.operation switch
            {
                "add" => $"Component '{request.componentName}' added to '{plan.target.Name}'.",
                "remove" => $"Component '{request.componentName}' removed from '{plan.target.Name}'.",
                "setProperties" => $"Properties set for component '{request.componentName}' on '{plan.target.Name}'.",
                _ => $"Component operation '{request.operation}' completed on '{plan.target.Name}'."
            };

            return GameObjectOperationResult.Ok(message, m_GameObjectAdapter.GetLegacyGameObjectData(plan.target));
        }

        ComponentMutationPlan BuildPlan(GameObjectComponentMutationRequest request, GameObjectToolTiming timing, bool splitTool)
        {
            string operation = NormalizeOperation(request?.operation);
            if (string.IsNullOrEmpty(operation))
                return ComponentMutationPlan.Error("operation is required.", "missing_operation", BuildErrorData("missing_operation", "operation is required."));

            if (operation != "add" && operation != "remove" && operation != "setProperties")
                return ComponentMutationPlan.Error($"Unknown component operation: '{request?.operation}'.", "invalid_operation", BuildErrorData("invalid_operation", $"Unknown component operation: '{request?.operation}'."));

            if (request?.target == null || request.target.isNull || request.target.isEmptyString)
                return ComponentMutationPlan.Error("'target' parameter required for component mutation.", "missing_target", BuildErrorData("missing_target", "'target' parameter required for component mutation."));

            if (splitTool && request.target.text != null && request.target.text.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return ComponentMutationPlan.Error(
                    "Component mutation on prefab asset paths is not supported by GameObject split tools. Use Unity.ManageAsset.",
                    "prefab_asset_not_supported",
                    BuildErrorData("prefab_asset_not_supported", "Component mutation on prefab asset paths is not supported by GameObject split tools. Use Unity.ManageAsset."));
            }

            var componentValidation = ValidateComponentInputs(request, operation, splitTool);
            if (!componentValidation.success)
                return ComponentMutationPlan.Error(componentValidation.message, componentValidation.errorKind, BuildErrorData(componentValidation.errorKind, componentValidation.message));

            UnityGameObjectHandle target;
            using (timing.Measure("adapter"))
            {
                target = m_GameObjectAdapter.FindObject(request.target, request.searchMethod, request.searchInactive);
            }

            if (target == null)
            {
                string message = $"Target GameObject ('{request.target.text}') not found using method '{request.searchMethod ?? "default"}'.";
                return ComponentMutationPlan.Error(message, "target_not_found", BuildErrorData("target_not_found", message));
            }

            UnityComponentMutationStatus mutation;
            using (timing.Measure("adapter"))
            {
                mutation = operation switch
                {
                    "add" => m_ComponentMutationAdapter.PreviewAddComponent(target, request.componentName, request.componentProperties),
                    "remove" => m_ComponentMutationAdapter.PreviewRemoveComponent(target, request.componentName, request.componentIndex),
                    "setProperties" => m_ComponentMutationAdapter.PreviewSetProperties(target, request.componentName, request.componentIndex, request.componentProperties),
                    _ => UnityComponentMutationStatus.Error($"Unknown component operation: '{operation}'.", "invalid_operation")
                };
            }

            if (!mutation.success)
                return ComponentMutationPlan.Error(mutation.message, mutation.errorKind, BuildErrorData(mutation.errorKind, mutation.message, mutation.validationMessages, mutation.errors));

            GameObjectTargetSummary targetSummary;
            GameObjectInfo currentObject;
            using (timing.Measure("adapter"))
            {
                targetSummary = m_GameObjectAdapter.ToTargetSummary(target);
                currentObject = m_GameObjectAdapter.ToGameObjectInfo(target);
            }

            return new ComponentMutationPlan
            {
                success = true,
                operation = operation,
                target = target,
                targetSummary = targetSummary,
                currentObject = currentObject,
                mutation = mutation
            };
        }

        UnityComponentMutationStatus ApplyMutation(ComponentMutationPlan plan, GameObjectComponentMutationRequest request)
        {
            return plan.operation switch
            {
                "add" => m_ComponentMutationAdapter.ApplyAddComponent(plan.target, request.componentName, request.componentProperties),
                "remove" => m_ComponentMutationAdapter.ApplyRemoveComponent(plan.target, request.componentName, request.componentIndex),
                "setProperties" => m_ComponentMutationAdapter.ApplySetProperties(plan.target, request.componentName, request.componentIndex, request.componentProperties),
                _ => UnityComponentMutationStatus.Error($"Unknown component operation: '{plan.operation}'.", "invalid_operation")
            };
        }

        static UnityComponentMutationStatus ValidateComponentInputs(GameObjectComponentMutationRequest request, string operation, bool splitTool)
        {
            if (string.IsNullOrWhiteSpace(request.componentName))
            {
                string message = operation switch
                {
                    "add" when !splitTool => "Component type name ('componentName' or first element in 'components_to_add') is required.",
                    "remove" when !splitTool => "Component type name ('componentName' or first element in 'componentsToRemove') is required.",
                    "setProperties" when !splitTool => "'componentName' parameter is required.",
                    _ => "componentName is required."
                };
                return UnityComponentMutationStatus.Error(message, "missing_component_name");
            }

            if (request.componentIndex.HasValue && request.componentIndex.Value < 0)
                return UnityComponentMutationStatus.Error("componentIndex must be greater than or equal to 0.", "invalid_component_index");

            if (operation == "setProperties" && (request.componentProperties == null || !request.componentProperties.HasValues))
            {
                string message = splitTool
                    ? "componentProperties is required and cannot be empty for setProperties."
                    : "'componentProperties' dictionary for the specified component is required and cannot be empty.";
                return UnityComponentMutationStatus.Error(message, "missing_component_properties");
            }

            return UnityComponentMutationStatus.Ok();
        }

        static string NormalizeOperation(string operation)
        {
            if (string.IsNullOrWhiteSpace(operation))
                return null;

            if (string.Equals(operation, "set_properties", StringComparison.OrdinalIgnoreCase)
                || string.Equals(operation, "set_component_property", StringComparison.OrdinalIgnoreCase)
                || string.Equals(operation, "setproperties", StringComparison.OrdinalIgnoreCase))
                return "setProperties";

            return string.Equals(operation, "setProperties", StringComparison.Ordinal)
                ? "setProperties"
                : operation.ToLowerInvariant();
        }

        static GameObjectOperationResult ToErrorResult(ComponentMutationPlan plan, UnityComponentMutationStatus status = null)
        {
            string errorKind = status?.errorKind ?? plan.errorKind;
            string message = status?.message ?? plan.message;
            object errorData = status == null
                ? plan.errorData
                : BuildErrorData(errorKind, message, status.validationMessages, status.errors);
            return GameObjectOperationResult.Error(message, errorKind, errorData);
        }

        static object BuildErrorData(string errorKind, string message, List<ValidationMessage> validationMessages = null, List<string> errors = null)
        {
            return new
            {
                errorKind,
                code = errorKind,
                message,
                validationMessages = validationMessages ?? new List<ValidationMessage>(),
                errors = errors ?? new List<string>()
            };
        }
    }
}
