#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectLifecycleService
    {
        sealed class CreatePlan
        {
            public bool success { get; set; }
            public string message { get; set; }
            public string errorKind { get; set; }
            public object errorData { get; set; }
            public GameObjectCreateRequest request { get; set; }
            public UnityPrefabResolution prefab { get; set; }
            public UnityGameObjectHandle parent { get; set; }
            public GameObjectTargetSummary parentSummary { get; set; }
            public int? layerId { get; set; }
            public string source { get; set; }
            public List<GameObjectComponentInfo> plannedComponents { get; } = new List<GameObjectComponentInfo>();
            public List<GameObjectComponentAddSpec> componentsToApply { get; } = new List<GameObjectComponentAddSpec>();
            public List<ValidationMessage> validationMessages { get; } = new List<ValidationMessage>();

            public static CreatePlan Error(string message, string errorKind, object errorData = null)
            {
                return new CreatePlan
                {
                    success = false,
                    message = message,
                    errorKind = errorKind,
                    errorData = errorData
                };
            }
        }

        sealed class DeletePlan
        {
            public bool success { get; set; }
            public string message { get; set; }
            public string errorKind { get; set; }
            public object errorData { get; set; }
            public List<UnityGameObjectHandle> targets { get; set; } = new List<UnityGameObjectHandle>();
            public List<GameObjectTargetSummary> summaries { get; set; } = new List<GameObjectTargetSummary>();
            public List<ValidationMessage> validationMessages { get; } = new List<ValidationMessage>();

            public static DeletePlan Error(string message, string errorKind, object errorData = null)
            {
                return new DeletePlan
                {
                    success = false,
                    message = message,
                    errorKind = errorKind,
                    errorData = errorData
                };
            }
        }

        readonly UnityGameObjectLifecycleAdapter m_LifecycleAdapter;
        readonly UnityComponentMutationAdapter m_ComponentMutationAdapter;

        public GameObjectLifecycleService(UnityGameObjectLifecycleAdapter lifecycleAdapter, UnityComponentMutationAdapter componentMutationAdapter)
        {
            m_LifecycleAdapter = lifecycleAdapter;
            m_ComponentMutationAdapter = componentMutationAdapter;
        }

        public GameObjectOperationResult GetBuiltinAssets(GameObjectToolTiming timing)
        {
            object data;
            using (timing.Measure("adapter"))
            {
                data = m_LifecycleAdapter.GetBuiltinAssetsData();
            }

            return GameObjectOperationResult.Ok("Retrieved builtin GameObject asset hints.", data);
        }

        public GameObjectOperationResult PreviewCreate(GameObjectCreateRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildCreatePlan(request, timing, splitTool: true);
            if (!plan.success)
                return ToErrorResult(plan);

            return GameObjectOperationResult.Ok(
                $"Previewed GameObject create for '{plan.request.name}'.",
                new GameObjectCreatePreviewResult
                {
                    willCreate = true,
                    source = plan.source,
                    prefabPath = plan.prefab?.resolvedPath,
                    parent = plan.parentSummary,
                    plannedObject = BuildPlannedObject(plan),
                    components = plan.plannedComponents,
                    validationMessages = plan.validationMessages
                });
        }

        public GameObjectOperationResult ApplyCreate(GameObjectCreateRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildCreatePlan(request, timing, splitTool: true);
            if (!plan.success)
                return ToErrorResult(plan);

            var apply = ApplyCreatePlan(plan, timing);
            if (!apply.success)
                return ToErrorResult(apply.message, apply.errorKind, plan.validationMessages);

            return GameObjectOperationResult.Ok(
                apply.message,
                new GameObjectCreateApplyResult
                {
                    created = true,
                    source = apply.source,
                    savedAsPrefab = apply.savedAsPrefab,
                    prefabPath = apply.prefabPath,
                    @object = m_LifecycleAdapter.ToGameObjectInfo(apply.handle),
                    components = plan.plannedComponents,
                    validationMessages = plan.validationMessages
                });
        }

        public GameObjectOperationResult CreateLegacy(GameObjectCreateRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildCreatePlan(request, timing, splitTool: false);
            if (!plan.success)
                return ToErrorResult(plan);

            var apply = ApplyCreatePlan(plan, timing);
            if (!apply.success)
                return ToErrorResult(apply.message, apply.errorKind, plan.validationMessages);

            return GameObjectOperationResult.Ok(apply.message, m_LifecycleAdapter.GetLegacyGameObjectData(apply.handle));
        }

        public GameObjectOperationResult PreviewDelete(GameObjectDeleteRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildDeletePlan(request, timing, splitTool: true);
            if (!plan.success)
                return ToErrorResult(plan);

            return GameObjectOperationResult.Ok(
                plan.targets.Count == 1
                    ? $"Previewed delete for GameObject '{plan.summaries[0].name}'."
                    : $"Previewed delete for {plan.targets.Count} GameObjects.",
                new GameObjectDeletePreviewResult
                {
                    willDelete = plan.targets.Count > 0,
                    count = plan.targets.Count,
                    objects = plan.summaries,
                    validationMessages = plan.validationMessages
                });
        }

        public GameObjectOperationResult ApplyDelete(GameObjectDeleteRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildDeletePlan(request, timing, splitTool: true);
            if (!plan.success)
                return ToErrorResult(plan);

            UnityGameObjectDeleteApplyStatus apply;
            using (timing.Measure("adapter"))
            {
                apply = m_LifecycleAdapter.DeleteObjects(plan.targets);
            }

            if (!apply.success)
                return ToErrorResult(apply.message, apply.errorKind, plan.validationMessages);

            return GameObjectOperationResult.Ok(
                apply.message,
                new GameObjectDeleteApplyResult
                {
                    deleted = true,
                    count = apply.deletedObjects.Count,
                    objects = apply.deletedObjects
                });
        }

        public GameObjectOperationResult DeleteLegacy(GameObjectDeleteRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildDeletePlan(request, timing, splitTool: false);
            if (!plan.success)
                return ToErrorResult(plan);

            UnityGameObjectDeleteApplyStatus apply;
            using (timing.Measure("adapter"))
            {
                apply = m_LifecycleAdapter.DeleteObjects(plan.targets);
            }

            if (!apply.success)
                return ToErrorResult(apply.message, apply.errorKind, plan.validationMessages);

            return GameObjectOperationResult.Ok(apply.message, apply.legacyDeletedObjects);
        }

        CreatePlan BuildCreatePlan(GameObjectCreateRequest request, GameObjectToolTiming timing, bool splitTool)
        {
            if (request == null)
                return CreatePlan.Error("Create request is required.", "missing_request", BuildErrorData("missing_request", "Create request is required."));

            if (string.IsNullOrEmpty(request.name))
                return CreatePlan.Error("'name' parameter is required for 'create' action.", "missing_name", BuildErrorData("missing_name", "'name' parameter is required for 'create' action."));

            UnityPrefabResolution prefab;
            using (timing.Measure("adapter"))
            {
                prefab = m_LifecycleAdapter.ResolvePrefab(request);
            }

            if (!prefab.success)
                return CreatePlan.Error(prefab.message, prefab.errorKind, BuildErrorData(prefab.errorKind, prefab.message));

            if (!string.IsNullOrEmpty(request.primitiveType))
            {
                using (timing.Measure("adapter"))
                {
                    if (!m_LifecycleAdapter.TryParsePrimitiveType(request.primitiveType, out _))
                    {
                        string message = $"Invalid primitive type: '{request.primitiveType}'. Valid types: {string.Join(", ", m_LifecycleAdapter.GetPrimitiveTypes())}";
                        return CreatePlan.Error(message, "invalid_primitive_type", BuildErrorData("invalid_primitive_type", message));
                    }
                }
            }

            var plan = new CreatePlan
            {
                success = true,
                request = request,
                prefab = prefab,
                source = prefab.prefabAsset != null
                    ? "prefab"
                    : (!string.IsNullOrEmpty(request.primitiveType) ? "primitive" : "empty")
            };

            if (request.hasParent && request.parent != null && !request.parent.isNull && !request.parent.isEmptyString)
            {
                using (timing.Measure("adapter"))
                {
                    plan.parent = m_LifecycleAdapter.FindParent(request.parent);
                }

                if (plan.parent == null)
                {
                    string message = $"Parent specified ('{request.parent.text}') but not found.";
                    return CreatePlan.Error(message, "parent_not_found", BuildErrorData("parent_not_found", message));
                }

                using (timing.Measure("adapter"))
                {
                    plan.parentSummary = m_LifecycleAdapter.ToTargetSummary(plan.parent);
                }
            }

            if (!string.IsNullOrEmpty(request.layer))
            {
                int layerId;
                using (timing.Measure("adapter"))
                {
                    if (!m_LifecycleAdapter.TryResolveLayer(request.layer, out layerId))
                    {
                        string message = $"Invalid layer specified: '{request.layer}'. Use a valid layer name.";
                        if (splitTool)
                            return CreatePlan.Error(message, "invalid_layer", BuildErrorData("invalid_layer", message));

                        plan.validationMessages.Add(new ValidationMessage
                        {
                            severity = "warning",
                            code = "invalid_layer_defaulted",
                            message = $"Layer '{request.layer}' not found. Using default layer."
                        });
                        layerId = -1;
                    }
                }

                if (layerId != -1)
                    plan.layerId = layerId;
            }

            if (request.tag != null)
            {
                string tagToSet = string.IsNullOrEmpty(request.tag) ? "Untagged" : request.tag;
                using (timing.Measure("adapter"))
                {
                    if (!m_LifecycleAdapter.TagExists(tagToSet))
                    {
                        plan.validationMessages.Add(new ValidationMessage
                        {
                            severity = "info",
                            code = "tag_missing_will_create",
                            message = $"Tag '{tagToSet}' does not exist and will be created during apply."
                        });
                    }
                }
            }

            var componentValidation = ValidateCreateComponents(request, plan, splitTool);
            if (!componentValidation.success)
                return componentValidation;

            return plan;
        }

        DeletePlan BuildDeletePlan(GameObjectDeleteRequest request, GameObjectToolTiming timing, bool splitTool)
        {
            if (request?.target == null || request.target.isNull || request.target.isEmptyString)
            {
                string message = "Target GameObject is required for delete.";
                return DeletePlan.Error(message, "missing_target", BuildErrorData("missing_target", message));
            }

            var lookupRequest = new GameObjectDeleteRequest
            {
                target = request.target,
                searchMethod = request.searchMethod,
                searchInactive = request.searchInactive,
                findAll = splitTool ? true : request.findAll,
                legacyCompatibility = request.legacyCompatibility
            };

            List<UnityGameObjectHandle> targets;
            using (timing.Measure("adapter"))
            {
                targets = m_LifecycleAdapter.FindDeleteTargets(lookupRequest);
            }

            if (targets == null || targets.Count == 0)
            {
                string message = $"Target GameObject(s) ('{request.target.text}') not found using method '{request.searchMethod ?? "default"}'.";
                return DeletePlan.Error(message, "target_not_found", BuildErrorData("target_not_found", message));
            }

            var summaries = new List<GameObjectTargetSummary>();
            using (timing.Measure("adapter"))
            {
                summaries.AddRange(targets.Select(m_LifecycleAdapter.ToTargetSummary));
            }

            if (splitTool && !request.findAll && targets.Count > 1)
            {
                string message = $"Delete target '{request.target.text}' matched {targets.Count} GameObjects. Set findAll=true to delete all matches.";
                return DeletePlan.Error(message, "ambiguous_target", new
                {
                    errorKind = "ambiguous_target",
                    validationMessages = new List<ValidationMessage>
                    {
                        new ValidationMessage { severity = "error", code = "ambiguous_target", message = message }
                    },
                    candidates = summaries
                });
            }

            if (splitTool && !request.findAll && targets.Count == 1)
            {
                targets = new List<UnityGameObjectHandle> { targets[0] };
                summaries = new List<GameObjectTargetSummary> { summaries[0] };
            }

            return new DeletePlan
            {
                success = true,
                targets = targets,
                summaries = summaries
            };
        }

        CreatePlan ValidateCreateComponents(GameObjectCreateRequest request, CreatePlan plan, bool splitTool)
        {
            if (request.componentsToAdd == null || request.componentsToAdd.Count == 0)
                return plan;

            int index = 0;
            foreach (var spec in request.componentsToAdd)
            {
                if (string.IsNullOrWhiteSpace(spec?.componentName))
                {
                    string message = "Component type name is required in componentsToAdd.";
                    if (splitTool)
                        return CreatePlan.Error(message, "missing_component_name", BuildErrorData("missing_component_name", message));

                    plan.validationMessages.Add(new ValidationMessage
                    {
                        severity = "warning",
                        code = "invalid_component_format_skipped",
                        message = "Invalid component format in components_to_add was skipped."
                    });
                    index++;
                    continue;
                }

                Type componentType = m_ComponentMutationAdapter.FindType(spec.componentName);
                if (componentType == null)
                {
                    string message = $"Component type '{spec.componentName}' not found.";
                    return CreatePlan.Error(message, "component_type_not_found", BuildErrorData("component_type_not_found", message));
                }

                if (!typeof(Component).IsAssignableFrom(componentType) || componentType.IsAbstract || componentType.IsInterface)
                {
                    string message = $"Type '{spec.componentName}' is not a valid concrete Component.";
                    return CreatePlan.Error(message, "invalid_component_type", BuildErrorData("invalid_component_type", message));
                }

                if (componentType == typeof(Transform))
                    return CreatePlan.Error("Cannot add another Transform component.", "cannot_add_transform", BuildErrorData("cannot_add_transform", "Cannot add another Transform component."));

                plan.componentsToApply.Add(spec);
                plan.plannedComponents.Add(new GameObjectComponentInfo
                {
                    index = index,
                    typeName = componentType.FullName,
                    shortTypeName = componentType.Name,
                    name = spec.componentName,
                    missing = false
                });

                if (spec.properties != null && spec.properties.HasValues)
                {
                    plan.validationMessages.Add(new ValidationMessage
                    {
                        severity = "info",
                        code = "create_component_properties_apply_only",
                        message = $"Properties for component '{spec.componentName}' will be validated against the created component during apply."
                    });
                }
                index++;
            }

            return plan;
        }

        UnityGameObjectCreateApplyStatus ApplyCreatePlan(CreatePlan plan, GameObjectToolTiming timing)
        {
            UnityGameObjectCreateApplyStatus begin;
            using (timing.Measure("adapter"))
            {
                begin = m_LifecycleAdapter.BeginCreate(plan.request, plan.prefab, plan.parent, plan.layerId);
            }

            if (!begin.success)
                return begin;

            foreach (var component in plan.componentsToApply)
            {
                UnityComponentMutationStatus componentResult;
                using (timing.Measure("adapter"))
                {
                    componentResult = m_ComponentMutationAdapter.ApplyAddComponent(begin.handle, component.componentName, component.properties);
                }

                if (!componentResult.success)
                {
                    m_LifecycleAdapter.DestroyCreatedObject(begin.handle);
                    return UnityGameObjectCreateApplyStatus.Error(componentResult.message, componentResult.errorKind ?? "component_apply_failed");
                }
            }

            using (timing.Measure("adapter"))
            {
                return m_LifecycleAdapter.FinishCreate(plan.request, plan.prefab, begin);
            }
        }

        GameObjectInfo BuildPlannedObject(CreatePlan plan)
        {
            return new GameObjectInfo
            {
                name = plan.request.name,
                tag = plan.request.tag ?? "Untagged",
                layer = plan.layerId ?? 0,
                activeSelf = true,
                activeInHierarchy = true,
                isStatic = false,
                parentId = plan.parentSummary?.id,
                parentInstanceID = plan.parentSummary?.instanceID,
                transform = new GameObjectTransformInfo
                {
                    localPosition = plan.request.position ?? ZeroVector(),
                    localRotation = plan.request.rotation ?? ZeroVector(),
                    scale = plan.request.scale ?? OneVector()
                },
                componentNames = plan.plannedComponents.Select(component => component.typeName).ToList()
            };
        }

        static GameObjectOperationResult ToErrorResult(CreatePlan plan)
        {
            return GameObjectOperationResult.Error(plan.message, plan.errorKind, plan.errorData ?? BuildErrorData(plan.errorKind, plan.message, plan.validationMessages));
        }

        static GameObjectOperationResult ToErrorResult(DeletePlan plan)
        {
            return GameObjectOperationResult.Error(plan.message, plan.errorKind, plan.errorData ?? BuildErrorData(plan.errorKind, plan.message, plan.validationMessages));
        }

        static GameObjectOperationResult ToErrorResult(string message, string errorKind, List<ValidationMessage> validationMessages)
        {
            return GameObjectOperationResult.Error(message, errorKind, BuildErrorData(errorKind, message, validationMessages));
        }

        static object BuildErrorData(string errorKind, string message, List<ValidationMessage> validationMessages = null)
        {
            return new
            {
                errorKind,
                validationMessages = validationMessages != null && validationMessages.Count > 0
                    ? validationMessages
                    : new List<ValidationMessage>
                    {
                        new ValidationMessage
                        {
                            severity = "error",
                            code = errorKind,
                            message = message
                        }
                    }
            };
        }

        static Vector3Value ZeroVector()
        {
            return new Vector3Value { x = 0f, y = 0f, z = 0f };
        }

        static Vector3Value OneVector()
        {
            return new Vector3Value { x = 1f, y = 1f, z = 1f };
        }
    }
}
