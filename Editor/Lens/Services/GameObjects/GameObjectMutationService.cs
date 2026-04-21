#nullable disable
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.Adapters.Unity.GameObjects;
using Becool.UnityMcpLens.Editor.Models.GameObjects;

namespace Becool.UnityMcpLens.Editor.Services.GameObjects
{
    sealed class GameObjectMutationService
    {
        sealed class SimpleChangePlan
        {
            public bool success { get; set; }
            public string message { get; set; }
            public string errorKind { get; set; }
            public UnityGameObjectHandle target { get; set; }
            public GameObjectTargetSummary targetSummary { get; set; }
            public GameObjectInfo currentObject { get; set; }
            public UnityGameObjectSimpleChangeSet changeSet { get; } = new UnityGameObjectSimpleChangeSet();
            public List<GameObjectChangeEntry> changes { get; } = new List<GameObjectChangeEntry>();
            public List<ValidationMessage> validationMessages { get; } = new List<ValidationMessage>();

            public bool willModify => changes.Count > 0;

            public static SimpleChangePlan Error(string message, string errorKind)
            {
                return new SimpleChangePlan
                {
                    success = false,
                    message = message,
                    errorKind = errorKind
                };
            }
        }

        readonly UnityGameObjectAdapter m_Adapter;

        public GameObjectMutationService(UnityGameObjectAdapter adapter)
        {
            m_Adapter = adapter;
        }

        public GameObjectOperationResult ModifySimple(GameObjectSimpleModifyRequest request, GameObjectToolTiming timing)
        {
            var result = ApplySimple(request, timing);
            if (!result.success)
                return result;

            var data = result.data as GameObjectChangeApplyResult;
            return GameObjectOperationResult.Ok(result.message, data?.@object);
        }

        public GameObjectOperationResult PreviewSimple(GameObjectSimpleModifyRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildSimpleChangePlan(request, timing);
            if (!plan.success)
                return ToErrorResult(plan);

            return GameObjectOperationResult.Ok(
                plan.willModify
                    ? $"Previewed {plan.changes.Count} GameObject change(s) for '{plan.target.Name}'."
                    : $"No GameObject changes would be applied to '{plan.target.Name}'.",
                new GameObjectChangePreviewResult
                {
                    target = plan.targetSummary,
                    willModify = plan.willModify,
                    changes = plan.changes,
                    validationMessages = plan.validationMessages,
                    @object = plan.currentObject
                });
        }

        public GameObjectOperationResult ApplySimple(GameObjectSimpleModifyRequest request, GameObjectToolTiming timing)
        {
            var plan = BuildSimpleChangePlan(request, timing);
            if (!plan.success)
                return ToErrorResult(plan);

            if (!plan.willModify)
            {
                return GameObjectOperationResult.Ok(
                    $"No modifications applied to GameObject '{plan.target.Name}'.",
                    new GameObjectChangeApplyResult
                    {
                        target = plan.targetSummary,
                        applied = false,
                        changes = plan.changes,
                        @object = plan.currentObject
                    });
            }

            GameObjectInfo result;
            string error;
            using (timing.Measure("adapter"))
            {
                result = m_Adapter.ApplySimpleChanges(plan.target, plan.changeSet, out error);
            }

            if (result == null)
                return GameObjectOperationResult.Error(error, "apply_failed", BuildErrorData("apply_failed", error, plan.validationMessages));

            return GameObjectOperationResult.Ok(
                $"GameObject '{plan.target.Name}' modified successfully.",
                new GameObjectChangeApplyResult
                {
                    target = m_Adapter.ToTargetSummary(plan.target),
                    applied = true,
                    changes = plan.changes,
                    @object = result
                });
        }

        SimpleChangePlan BuildSimpleChangePlan(GameObjectSimpleModifyRequest request, GameObjectToolTiming timing)
        {
            UnityGameObjectHandle target;
            using (timing.Measure("adapter"))
            {
                target = m_Adapter.FindObject(request?.target, request?.searchMethod);
            }

            if (target == null)
            {
                return SimpleChangePlan.Error(
                    $"Target GameObject ('{request?.target?.text}') not found using method '{request?.searchMethod ?? "default"}'.",
                    "target_not_found");
            }

            GameObjectMutableState state;
            GameObjectTargetSummary targetSummary;
            GameObjectInfo currentObject;
            using (timing.Measure("adapter"))
            {
                state = m_Adapter.GetMutableState(target);
                targetSummary = m_Adapter.ToTargetSummary(target);
                currentObject = m_Adapter.ToGameObjectInfo(target);
            }

            var plan = new SimpleChangePlan
            {
                success = true,
                target = target,
                targetSummary = targetSummary,
                currentObject = currentObject
            };

            if (!string.IsNullOrEmpty(request.name) && state.name != request.name)
            {
                plan.changeSet.name = request.name;
                AddChange(plan, "name", state.name, request.name);
            }

            if (request.hasSetActive && state.activeSelf != request.setActive)
            {
                plan.changeSet.setActive = request.setActive;
                AddChange(plan, "activeSelf", state.activeSelf, request.setActive);
            }

            if (request.hasTag)
            {
                string tagToSet = string.IsNullOrEmpty(request.tag) ? "Untagged" : request.tag;
                if (state.tag != tagToSet)
                {
                    plan.changeSet.tag = request.tag;
                    plan.changeSet.hasTag = true;
                    AddChange(plan, "tag", state.tag, tagToSet);

                    using (timing.Measure("adapter"))
                    {
                        if (!m_Adapter.TagExists(tagToSet))
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
            }

            if (!string.IsNullOrEmpty(request.layer))
            {
                int layerId;
                using (timing.Measure("adapter"))
                {
                    if (!m_Adapter.TryResolveLayer(request.layer, out layerId))
                    {
                        return SimpleChangePlan.Error(
                            $"Invalid layer specified: '{request.layer}'. Use a valid layer name.",
                            "invalid_layer");
                    }
                }

                if (state.layer != layerId)
                {
                    plan.changeSet.layer = layerId;
                    AddChange(plan, "layer", state.layer, layerId);
                }
            }

            if (request.position != null)
            {
                Vector3Value resolvedPosition;
                using (timing.Measure("adapter"))
                {
                    resolvedPosition = m_Adapter.ResolvePosition(target, request.position, request.positionType);
                }

                if (!state.localPosition.SameValue(resolvedPosition))
                {
                    plan.changeSet.localPosition = resolvedPosition;
                    AddChange(plan, "localPosition", state.localPosition, resolvedPosition);
                }
            }

            if (request.rotation != null && !state.localRotation.SameValue(request.rotation))
            {
                plan.changeSet.localRotation = request.rotation;
                AddChange(plan, "localRotation", state.localRotation, request.rotation);
            }

            if (request.scale != null && !state.localScale.SameValue(request.scale))
            {
                plan.changeSet.localScale = request.scale;
                AddChange(plan, "localScale", state.localScale, request.scale);
            }

            if (request.hasParent)
            {
                UnityGameObjectHandle parent = null;
                bool parentCleared = request.parent == null || request.parent.isNull || request.parent.isEmptyString;

                if (!parentCleared)
                {
                    using (timing.Measure("adapter"))
                    {
                        parent = m_Adapter.FindObject(request.parent, "by_id_or_name_or_path", true);
                    }

                    if (parent == null)
                        return SimpleChangePlan.Error($"New parent ('{request.parent.text}') not found.", "parent_not_found");

                    using (timing.Measure("adapter"))
                    {
                        if (m_Adapter.WouldCreateParentLoop(target, parent))
                        {
                            return SimpleChangePlan.Error(
                                $"Cannot parent '{target.Name}' to '{parent.Name}', as it would create a hierarchy loop.",
                                "parent_loop");
                        }
                    }
                }

                GameObjectTargetSummary currentParent;
                bool parentChanged;
                using (timing.Measure("adapter"))
                {
                    currentParent = m_Adapter.GetParentSummary(target);
                    parentChanged = !m_Adapter.HasParent(target, parent);
                }

                if (parentChanged)
                {
                    plan.changeSet.hasParent = true;
                    plan.changeSet.parent = parent;
                    AddChange(plan, "parent", currentParent, parent == null ? null : m_Adapter.ToTargetSummary(parent));
                }
            }

            return plan;
        }

        static GameObjectOperationResult ToErrorResult(SimpleChangePlan plan)
        {
            return GameObjectOperationResult.Error(
                plan.message,
                plan.errorKind,
                BuildErrorData(plan.errorKind, plan.message, plan.validationMessages));
        }

        static object BuildErrorData(string errorKind, string message, List<ValidationMessage> validationMessages)
        {
            if (validationMessages == null || validationMessages.Count == 0)
            {
                validationMessages = new List<ValidationMessage>
                {
                    new ValidationMessage
                    {
                        severity = "error",
                        code = errorKind,
                        message = message
                    }
                };
            }

            return new
            {
                errorKind,
                validationMessages
            };
        }

        static void AddChange(SimpleChangePlan plan, string field, object before, object after)
        {
            plan.changes.Add(new GameObjectChangeEntry
            {
                field = field,
                before = before,
                after = after
            });
        }
    }
}
