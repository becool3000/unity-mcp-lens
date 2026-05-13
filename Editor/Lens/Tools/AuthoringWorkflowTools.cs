#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class AuthoringWorkflowTools
    {
        const string AuthorSceneObjectToolName = "Unity.Workflow.AuthorSceneObject";
        const string AuthorPrefabToolName = "Unity.Workflow.AuthorPrefab";
        const string ConfigureExistingComponentToolName = "Unity.Workflow.ConfigureExistingComponent";
        const string RunPlayModeVerificationToolName = "Unity.Workflow.RunPlayModeVerification";

        const string DescriptionSuffix = @"

Workflow wrappers preserve partial results, run reuse discovery before mutation, use preview/apply phases internally, and keep durable authoring separate from runtime verification.";

        [McpSchema(AuthorSceneObjectToolName)]
        public static object GetAuthorSceneObjectSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Feature or object intent. Used for reuse discovery before authoring." },
                    context = new { type = "string", description = "Optional project/scene constraints for reuse discovery." },
                    apply = new { type = "boolean", description = "When false, only preview and report the workflow. Defaults to false." },
                    name = new { type = "string", description = "Scene object name to create." },
                    objectKind = new { type = "string", description = "Object kind: empty, primitive, camera, light, canvas, eventSystem." },
                    primitiveType = new { type = "string", description = "Primitive type when objectKind=primitive." },
                    prefabPath = new { type = "string", description = "Optional prefab asset to instantiate instead of creating a new object." },
                    parent = new { description = "Optional parent GameObject target." },
                    tag = new { type = "string", description = "Optional tag." },
                    layer = new { description = "Optional layer name or index." },
                    position = new { description = "Local/world position as {x,y,z} or [x,y,z]." },
                    rotation = new { description = "Euler rotation as {x,y,z} or [x,y,z]." },
                    scale = new { description = "Scale as {x,y,z} or [x,y,z]." },
                    componentsToAdd = new { type = "array", items = new { description = "Component name or component descriptor." } },
                    componentEdits = new { type = "array", description = "Preview/apply component mutation rows after creation.", items = new { type = "object" } },
                    serializedAssignments = new { type = "array", description = "Scene serialized property assignments for Unity.Scene.SetSerializedProperties.", items = new { type = "object" } },
                    referenceBindings = new { type = "array", description = "Object-reference bindings for Unity.Scene.PreviewAssignObjectReferences / ApplyAssignObjectReferences.", items = new { type = "object" } },
                    runVerification = new { type = "boolean", description = "Run play-mode verification after durable authoring. Defaults to false." },
                    verification = new { type = "object", description = "Arguments passed to Unity.Workflow.RunPlayModeVerification when runVerification=true." }
                },
                required = new[] { "intent" }
            };
        }

        [McpSchema(AuthorPrefabToolName)]
        public static object GetAuthorPrefabSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Prefab authoring intent. Used for reuse discovery before authoring." },
                    context = new { type = "string", description = "Optional project constraints for reuse discovery." },
                    apply = new { type = "boolean", description = "When false, inspect/preview only. Defaults to false." },
                    prefabPath = new { type = "string", description = "Prefab asset path under Assets/." },
                    sourceSceneObject = new { description = "Scene GameObject target to save as a prefab asset." },
                    sourceSearchMethod = new { type = "string", description = "How to resolve sourceSceneObject. Defaults to by_id_or_name_or_path." },
                    connect = new { type = "boolean", description = "Connect source scene object to created prefab. Defaults to true." },
                    overwrite = new { type = "boolean", description = "Allow replacing an existing prefab asset. Defaults to false." },
                    inspectAfter = new { type = "boolean", description = "Inspect the prefab asset after apply. Defaults to true." },
                    instantiateInScene = new { type = "boolean", description = "Instantiate the prefab into the loaded scene after apply/inspection. Defaults to false." },
                    instanceName = new { type = "string", description = "Optional scene instance name." },
                    parent = new { description = "Optional scene parent for instantiation." },
                    position = new { description = "Optional local position for instantiation." },
                    rotation = new { description = "Optional local rotation for instantiation." },
                    scale = new { description = "Optional local scale for instantiation." }
                },
                required = new[] { "intent", "prefabPath" }
            };
        }

        [McpSchema(ConfigureExistingComponentToolName)]
        public static object GetConfigureExistingComponentSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    intent = new { type = "string", description = "Feature/configuration intent. Used for reuse discovery before mutation." },
                    context = new { type = "string", description = "Optional scene/project context for reuse discovery." },
                    apply = new { type = "boolean", description = "When false, preview only. Defaults to false." },
                    target = new { description = "Scene GameObject target, path, name, or id." },
                    searchMethod = new { type = "string", description = "How to resolve target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive objects while resolving target. Defaults to true." },
                    componentName = new { type = "string", description = "Component type to add/configure/remove." },
                    componentIndex = new { type = "integer", description = "0-based component index for set/remove. Defaults to 0." },
                    operation = new { type = "string", description = "Component mutation: add, setProperties, or remove. Defaults to setProperties." },
                    componentProperties = new { type = "object", description = "Component property payload for GameObject component mutation tools." },
                    serializedAssignments = new { type = "array", description = "Optional serialized property assignments for Unity.Scene.SetSerializedProperties.", items = new { type = "object" } },
                    referenceBindings = new { type = "array", description = "Optional object-reference bindings to preview/apply.", items = new { type = "object" } }
                },
                required = new[] { "intent", "target", "componentName" }
            };
        }

        [McpSchema(RunPlayModeVerificationToolName)]
        public static object GetRunPlayModeVerificationSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    enterPlayMode = new { type = "boolean", description = "Enter Play Mode before verification. Defaults to true." },
                    exitAfter = new { type = "boolean", description = "Exit Play Mode after verification. Defaults to false." },
                    scenePath = new { type = "string", description = "Optional scene path to load through Unity.PlayMode.EnterReady." },
                    waitMs = new { type = "integer", description = "Play-mode readiness timeout/wait budget." },
                    consoleCount = new { type = "integer", description = "Maximum grouped console rows to include before/after verification. Defaults to 20." },
                    componentSnapshots = new { type = "array", description = "Runtime component snapshots to collect.", items = new { type = "object" } },
                    pointerSmoke = new { type = "object", description = "Optional Unity.PlayMode.PointerInputSmoke arguments." },
                    captureGameView = new { type = "object", description = "Optional Unity.UI.CaptureGameView arguments." }
                }
            };
        }

        [McpTool(AuthorSceneObjectToolName, "Authors a durable scene object through discovery, preview, optional apply, dirty-state reporting, and optional runtime verification." + DescriptionSuffix, "Author Scene Object Workflow", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static async Task<object> AuthorSceneObject(JObject @params)
        {
            return await HandleAsync(AuthorSceneObjectToolName, "author_scene_object", @params, ExecuteAuthorSceneObjectAsync);
        }

        [McpTool(AuthorPrefabToolName, "Authors prefab assets through reuse discovery, explicit preview/apply workflow state, prefab inspection, and optional scene instantiation." + DescriptionSuffix, "Author Prefab Workflow", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static async Task<object> AuthorPrefab(JObject @params)
        {
            return await HandleAsync(AuthorPrefabToolName, "author_prefab", @params, ExecuteAuthorPrefabAsync);
        }

        [McpTool(ConfigureExistingComponentToolName, "Configures an existing scene component or adds a reusable component through discovery, schema inspection, preview/apply, and dirty-state reporting." + DescriptionSuffix, "Configure Existing Component Workflow", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static async Task<object> ConfigureExistingComponent(JObject @params)
        {
            return await HandleAsync(ConfigureExistingComponentToolName, "configure_existing_component", @params, ExecuteConfigureExistingComponentAsync);
        }

        [McpTool(RunPlayModeVerificationToolName, "Runs play-mode verification as a separate workflow phase with console deltas, component snapshots, optional pointer smoke, and optional Game view capture." + DescriptionSuffix, "Run Play Mode Verification Workflow", Groups = new[] { "project" }, EnabledByDefault = true)]
        public static async Task<object> RunPlayModeVerification(JObject @params)
        {
            return await HandleAsync(RunPlayModeVerificationToolName, "run_play_mode_verification", @params, ExecuteRunPlayModeVerificationAsync);
        }

        static async Task<object> HandleAsync(string toolName, string operation, JObject parameters, Func<JObject, Task<WorkflowResult>> execute)
        {
            parameters ??= new JObject();
            var timing = new ToolOperationTiming(toolName, operation, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            WorkflowResult result;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                }

                using (timing.Measure("service"))
                {
                    result = await execute(parameters);
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                errorKind = ex.GetType().Name;
                result = new WorkflowResult
                {
                    Success = false,
                    Message = $"Workflow failed: {ex.Message}",
                    ErrorKind = errorKind,
                    Data = new
                    {
                        status = "failed",
                        failurePoint = "workflow_exception",
                        errorKind,
                        error = ex.Message,
                        durableAuthoring = new { status = "failed" },
                        runtimeVerification = new { status = "not_started" }
                    }
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = result.Success
                    ? Response.Success(result.Message, ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        result.Data,
                        BuildCompactData(result.Data),
                        new { kind = "authoring_workflow_full_result" },
                        "authoring_workflow",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(result.Message ?? "Authoring workflow failed.", result.Data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(result.Success, result.Success ? null : result.ErrorKind ?? errorKind);
            return response;
        }

        static async Task<WorkflowResult> ExecuteAuthorSceneObjectAsync(JObject parameters)
        {
            bool apply = GetBool(parameters, false, "apply", "Apply");
            bool runVerification = GetBool(parameters, false, "runVerification", "RunVerification");
            string intent = GetString(parameters, "intent", "Intent") ?? GetString(parameters, "name", "Name") ?? "author scene object";
            var steps = new List<object>();
            string failurePoint = null;

            object dirtyStateBefore = CallTool("Unity.Scene.GetDirtyState", "durable_authoring", new JObject(), SceneTools.GetDirtyState, steps, false, ref failurePoint);
            object reusePlan = CallTool("Unity.Authoring.SuggestReusePlan", "discovery", BuildReusePlanParams(parameters, intent), ComponentDiscoveryTools.SuggestReusePlan, steps, false, ref failurePoint);

            JObject createParams = Pick(parameters, "name", "objectKind", "primitiveType", "prefabPath", "saveAsPrefab", "prefabFolder", "parent", "tag", "layer", "position", "rotation", "scale", "componentsToAdd");
            object previewCreate = CallTool("Unity.GameObject.PreviewCreate", "preview", createParams, GameObjectSplitTools.PreviewCreate, steps, true, ref failurePoint);
            if (failurePoint != null)
                return Failed("Scene object authoring preview failed.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

            object createResult = null;
            object inspectResult = null;
            if (apply)
            {
                createResult = CallTool("Unity.GameObject.Create", "apply", createParams, GameObjectSplitTools.Create, steps, true, ref failurePoint);
                if (failurePoint != null)
                    return Failed("Scene object creation failed after preview.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

                string target = ResolveCreatedTarget(parameters);
                if (!string.IsNullOrWhiteSpace(target))
                {
                    inspectResult = CallTool("Unity.GameObject.Inspect", "readback", new JObject
                    {
                        ["mode"] = "find",
                        ["target"] = target,
                        ["searchMethod"] = "by_id_or_name_or_path",
                        ["searchInactive"] = true
                    }, GameObjectSplitTools.Inspect, steps, false, ref failurePoint);

                    ApplyComponentEdits(parameters, target, steps, ref failurePoint);
                    if (failurePoint != null)
                        return Failed("Scene object component authoring failed after partial creation.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

                    ApplySerializedAssignments(parameters, target, steps, ref failurePoint);
                    if (failurePoint != null)
                        return Failed("Scene object serialized field authoring failed after partial creation.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

                    ApplyReferenceBindings(parameters, target, steps, ref failurePoint);
                    if (failurePoint != null)
                        return Failed("Scene object reference binding failed after partial creation.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);
                }
            }

            object dirtyStateAfter = CallTool("Unity.Scene.GetDirtyState", "dirty_state", new JObject(), SceneTools.GetDirtyState, steps, false, ref failurePoint);
            object runtimeVerification = new { status = "not_requested" };
            if (apply && runVerification)
                runtimeVerification = (await ExecuteRunPlayModeVerificationAsync(parameters["verification"] as JObject ?? new JObject())).Data;

            return Succeeded("Scene object workflow completed.", new
            {
                status = apply ? "applied" : "previewed",
                intent,
                durableAuthoring = new
                {
                    status = apply ? "applied" : "previewed",
                    applyRequested = apply,
                    reusePlan,
                    previewCreate,
                    createResult,
                    inspectResult,
                    dirtyStateBefore,
                    dirtyStateAfter,
                    saveState = new { requested = false, saved = false, reason = "Workflows do not save scenes; call Unity.Scene.Save explicitly." }
                },
                runtimeVerification,
                appliedEdits = ExtractSteps(steps, "apply", "apply_scene_instance"),
                objectEvidence = ExtractObjectEvidence(steps),
                partialResults = steps.ToArray(),
                failurePoint
            });
        }

        static async Task<WorkflowResult> ExecuteAuthorPrefabAsync(JObject parameters)
        {
            bool apply = GetBool(parameters, false, "apply", "Apply");
            bool inspectAfter = GetBool(parameters, true, "inspectAfter", "InspectAfter");
            bool instantiateInScene = GetBool(parameters, false, "instantiateInScene", "InstantiateInScene");
            string intent = GetString(parameters, "intent", "Intent") ?? "author prefab";
            string prefabPath = GetString(parameters, "prefabPath", "PrefabPath");
            var steps = new List<object>();
            string failurePoint = null;

            object reusePlan = CallTool("Unity.Authoring.SuggestReusePlan", "discovery", BuildReusePlanParams(parameters, intent), ComponentDiscoveryTools.SuggestReusePlan, steps, false, ref failurePoint);
            object dirtyStateBefore = CallTool("Unity.Scene.GetDirtyState", "dirty_state", new JObject(), SceneTools.GetDirtyState, steps, false, ref failurePoint);
            object prefabInspectBefore = string.IsNullOrWhiteSpace(prefabPath)
                ? null
                : CallTool("Unity.Prefab.Inspect", "prefab_read", new JObject { ["prefabPath"] = prefabPath, ["includeComponents"] = true }, PrefabAuthoringTools.Inspect, steps, false, ref failurePoint);

            object createFromSceneObject = null;
            JToken source = GetToken(parameters, "sourceSceneObject", "SourceSceneObject", "target", "Target");
            if (source != null)
            {
                JObject createParams = new()
                {
                    ["target"] = source.DeepClone(),
                    ["searchMethod"] = GetString(parameters, "sourceSearchMethod", "SourceSearchMethod", "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                    ["includeInactive"] = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                    ["prefabPath"] = prefabPath,
                    ["connect"] = GetBool(parameters, true, "connect", "Connect"),
                    ["overwrite"] = GetBool(parameters, false, "overwrite", "Overwrite")
                };

                if (apply)
                {
                    createFromSceneObject = CallTool("Unity.Prefab.CreateFromSceneObject", "apply", createParams, PrefabAuthoringTools.CreateFromSceneObject, steps, true, ref failurePoint);
                    if (failurePoint != null)
                        return Failed("Prefab creation failed after reuse discovery.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);
                }
                else
                {
                    steps.Add(BuildSyntheticStep("Unity.Prefab.CreateFromSceneObject", "preview", "preview_unavailable", createParams, new
                    {
                        reason = "Underlying Phase 3 prefab creation is an explicit asset save. This workflow is in preview mode and did not call it.",
                        applyRequired = true
                    }));
                }
            }

            object prefabInspectAfter = null;
            if (inspectAfter && !string.IsNullOrWhiteSpace(prefabPath))
                prefabInspectAfter = CallTool("Unity.Prefab.Inspect", "prefab_read", new JObject { ["prefabPath"] = prefabPath, ["includeComponents"] = true }, PrefabAuthoringTools.Inspect, steps, false, ref failurePoint);

            object instantiateResult = null;
            if (apply && instantiateInScene)
            {
                JObject instantiateParams = Pick(parameters, "prefabPath", "instanceName", "parent", "position", "rotation", "scale");
                instantiateResult = CallTool("Unity.Prefab.Instantiate", "apply_scene_instance", instantiateParams, PrefabAuthoringTools.Instantiate, steps, true, ref failurePoint);
                if (failurePoint != null)
                    return Failed("Prefab scene instantiation failed after partial prefab workflow.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);
            }

            object dirtyStateAfter = CallTool("Unity.Scene.GetDirtyState", "dirty_state", new JObject(), SceneTools.GetDirtyState, steps, false, ref failurePoint);
            return await Task.FromResult(Succeeded("Prefab workflow completed.", new
            {
                status = apply ? "applied" : "previewed",
                intent,
                durableAuthoring = new
                {
                    status = apply ? "applied" : "previewed",
                    applyRequested = apply,
                    reusePlan,
                    prefabPath,
                    prefabInspectBefore,
                    createFromSceneObject,
                    prefabInspectAfter,
                    instantiateResult,
                    dirtyStateBefore,
                    dirtyStateAfter,
                    saveState = new { requested = apply && source != null, savedByContract = apply && source != null, reason = source == null ? "No sourceSceneObject provided." : "Unity.Prefab.CreateFromSceneObject saves prefab assets by explicit tool contract." }
                },
                runtimeVerification = new { status = "not_requested" },
                appliedEdits = ExtractSteps(steps, "apply", "apply_scene_instance"),
                objectEvidence = ExtractObjectEvidence(steps),
                partialResults = steps.ToArray(),
                failurePoint
            }));
        }

        static async Task<WorkflowResult> ExecuteConfigureExistingComponentAsync(JObject parameters)
        {
            bool apply = GetBool(parameters, false, "apply", "Apply");
            string intent = GetString(parameters, "intent", "Intent") ?? "configure existing component";
            string target = GetToken(parameters, "target", "Target")?.ToString();
            string componentName = GetString(parameters, "componentName", "ComponentName", "component", "Component");
            var steps = new List<object>();
            string failurePoint = null;

            object dirtyStateBefore = CallTool("Unity.Scene.GetDirtyState", "dirty_state", new JObject(), SceneTools.GetDirtyState, steps, false, ref failurePoint);
            object reusePlan = CallTool("Unity.Authoring.SuggestReusePlan", "discovery", BuildReusePlanParams(parameters, intent), ComponentDiscoveryTools.SuggestReusePlan, steps, false, ref failurePoint);
            object schema = CallTool("Unity.Component.InspectSchema", "inspection", new JObject
            {
                ["componentName"] = componentName,
                ["target"] = target,
                ["searchMethod"] = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                ["includeInactive"] = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                ["componentIndex"] = Math.Max(0, GetInt(parameters, 0, "componentIndex", "ComponentIndex")),
                ["includeDefaults"] = true
            }, ComponentDiscoveryTools.InspectSchema, steps, false, ref failurePoint);

            JObject componentParams = new()
            {
                ["operation"] = GetString(parameters, "operation", "Operation") ?? "setProperties",
                ["target"] = GetToken(parameters, "target", "Target")?.DeepClone(),
                ["searchMethod"] = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                ["searchInactive"] = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                ["componentName"] = componentName,
                ["componentIndex"] = Math.Max(0, GetInt(parameters, 0, "componentIndex", "ComponentIndex"))
            };
            if (parameters.TryGetValue("componentProperties", StringComparison.OrdinalIgnoreCase, out JToken componentProperties))
                componentParams["componentProperties"] = componentProperties.DeepClone();

            object preview = CallTool("Unity.GameObject.PreviewComponentChanges", "preview", componentParams, GameObjectSplitTools.PreviewComponentChanges, steps, true, ref failurePoint);
            if (failurePoint != null)
                return Failed("Component configuration preview failed.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

            object applyResult = null;
            if (apply)
            {
                applyResult = CallTool("Unity.GameObject.ApplyComponentChanges", "apply", componentParams, GameObjectSplitTools.ApplyComponentChanges, steps, true, ref failurePoint);
                if (failurePoint != null)
                    return Failed("Component configuration apply failed.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

                ApplySerializedAssignments(parameters, target, steps, ref failurePoint);
                if (failurePoint != null)
                    return Failed("Component serialized field workflow failed after partial apply.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);

                ApplyReferenceBindings(parameters, target, steps, ref failurePoint);
                if (failurePoint != null)
                    return Failed("Component reference binding workflow failed after partial apply.", failurePoint, intent, apply, steps, dirtyStateBefore, reusePlan);
            }

            object dirtyStateAfter = CallTool("Unity.Scene.GetDirtyState", "dirty_state", new JObject(), SceneTools.GetDirtyState, steps, false, ref failurePoint);
            return await Task.FromResult(Succeeded("Component configuration workflow completed.", new
            {
                status = apply ? "applied" : "previewed",
                intent,
                durableAuthoring = new
                {
                    status = apply ? "applied" : "previewed",
                    applyRequested = apply,
                    reusePlan,
                    schema,
                    preview,
                    applyResult,
                    dirtyStateBefore,
                    dirtyStateAfter,
                    saveState = new { requested = false, saved = false, reason = "Scene changes remain unsaved until Unity.Scene.Save is called explicitly." }
                },
                runtimeVerification = new { status = "not_requested" },
                appliedEdits = ExtractSteps(steps, "apply"),
                objectEvidence = ExtractObjectEvidence(steps),
                partialResults = steps.ToArray(),
                failurePoint
            }));
        }

        static async Task<WorkflowResult> ExecuteRunPlayModeVerificationAsync(JObject parameters)
        {
            parameters ??= new JObject();
            bool enterPlayMode = GetBool(parameters, true, "enterPlayMode", "EnterPlayMode");
            bool exitAfter = GetBool(parameters, false, "exitAfter", "ExitAfter");
            int consoleCount = Math.Clamp(GetInt(parameters, 20, "consoleCount", "ConsoleCount"), 1, 100);
            var steps = new List<object>();
            string failurePoint = null;

            object consoleBefore = ReadConsoleSummary(consoleCount);
            steps.Add(BuildSyntheticStep("Unity.ReadConsole", "runtime_verification", "console_before", new { consoleCount }, consoleBefore));

            object enterResult = null;
            if (enterPlayMode)
            {
                JObject enterParams = new()
                {
                    ["mode"] = "enter",
                    ["waitMs"] = GetInt(parameters, 10000, "waitMs", "WaitMs")
                };
                string scenePath = GetString(parameters, "scenePath", "ScenePath");
                if (!string.IsNullOrWhiteSpace(scenePath))
                    enterParams["scenePath"] = scenePath;
                enterResult = await CallToolAsync("Unity.PlayMode.EnterReady", "runtime_enter", enterParams, PlayModeTools.EnterReady, steps, required: true, failurePointSetter: value => failurePoint = value);
                if (failurePoint != null)
                    return Failed("Play Mode verification failed while entering Play Mode.", failurePoint, "runtime verification", true, steps, null, null);
            }

            var componentSnapshots = new List<object>();
            foreach (JObject snapshotParams in (parameters["componentSnapshots"] as JArray)?.Children<JObject>() ?? Enumerable.Empty<JObject>())
            {
                object snapshot = CallTool("Unity.Runtime.GetComponentSnapshot", "runtime_snapshot", snapshotParams, RuntimeGetComponentSnapshotTools.GetComponentSnapshot, steps, false, ref failurePoint);
                componentSnapshots.Add(snapshot);
            }

            object pointerSmoke = null;
            if (parameters["pointerSmoke"] is JObject pointerParams)
                pointerSmoke = await CallToolAsync("Unity.PlayMode.PointerInputSmoke", "runtime_smoke", pointerParams, RuntimeDiagnosticsTools.PointerInputSmoke, steps, required: false, failurePointSetter: value => failurePoint = value);

            object capture = null;
            if (parameters["captureGameView"] is JObject captureParams)
            {
                CaptureGameViewParams captureGameViewParams = captureParams.ToObject<CaptureGameViewParams>() ?? new CaptureGameViewParams();
                capture = await CallToolAsync("Unity.UI.CaptureGameView", "runtime_capture", captureParams, _ => UiDiagnosticsTools.CaptureGameView(captureGameViewParams), steps, required: false, failurePointSetter: value => failurePoint = value);
            }

            object consoleAfter = ReadConsoleSummary(consoleCount);
            steps.Add(BuildSyntheticStep("Unity.ReadConsole", "runtime_verification", "console_after", new { consoleCount }, consoleAfter));

            object exitResult = null;
            if (exitAfter)
            {
                exitResult = await CallToolAsync("Unity.Editor.SetPlayMode", "runtime_exit", new JObject { ["mode"] = "exit", ["waitMs"] = GetInt(parameters, 10000, "waitMs", "WaitMs") }, PlayModeTools.SetPlayMode, steps, required: false, failurePointSetter: value => failurePoint = value);
            }

            return Succeeded("Play Mode verification workflow completed.", new
            {
                status = failurePoint == null ? "verified" : "completed_with_warnings",
                durableAuthoring = new { status = "not_run" },
                runtimeVerification = new
                {
                    status = failurePoint == null ? "verified" : "completed_with_warnings",
                    enterPlayMode,
                    enterResult,
                    componentSnapshots = componentSnapshots.ToArray(),
                    pointerSmoke,
                    capture,
                    capturePaths = ExtractCapturePaths(capture),
                    consoleDelta = new
                    {
                        before = consoleBefore,
                        after = consoleAfter
                    },
                    exitAfter,
                    exitResult
                },
                appliedEdits = Array.Empty<object>(),
                objectEvidence = ExtractObjectEvidence(steps),
                partialResults = steps.ToArray(),
                failurePoint
            });
        }

        static void ApplyComponentEdits(JObject parameters, string target, List<object> steps, ref string failurePoint)
        {
            foreach (JObject edit in (parameters["componentEdits"] as JArray)?.Children<JObject>() ?? Enumerable.Empty<JObject>())
            {
                JObject componentParams = (JObject)edit.DeepClone();
                if (componentParams["target"] == null)
                    componentParams["target"] = target;
                if (componentParams["searchMethod"] == null)
                    componentParams["searchMethod"] = "by_id_or_name_or_path";
                object preview = CallTool("Unity.GameObject.PreviewComponentChanges", "preview", componentParams, GameObjectSplitTools.PreviewComponentChanges, steps, true, ref failurePoint);
                if (failurePoint != null)
                    return;
                _ = preview;
                CallTool("Unity.GameObject.ApplyComponentChanges", "apply", componentParams, GameObjectSplitTools.ApplyComponentChanges, steps, true, ref failurePoint);
                if (failurePoint != null)
                    return;
            }
        }

        static void ApplySerializedAssignments(JObject parameters, string target, List<object> steps, ref string failurePoint)
        {
            if (string.IsNullOrWhiteSpace(target) || parameters["serializedAssignments"] is not JArray assignments || assignments.Count == 0)
                return;

            JObject serializedParams = new()
            {
                ["target"] = target,
                ["searchMethod"] = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                ["includeInactive"] = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                ["assignments"] = assignments.DeepClone()
            };

            JObject previewParams = (JObject)serializedParams.DeepClone();
            previewParams["previewOnly"] = true;
            CallSceneSerializedProperties("Unity.Scene.SetSerializedProperties", "preview", previewParams, steps, true, ref failurePoint);
            if (failurePoint != null)
                return;

            JObject applyParams = (JObject)serializedParams.DeepClone();
            applyParams["previewOnly"] = false;
            CallSceneSerializedProperties("Unity.Scene.SetSerializedProperties", "apply", applyParams, steps, true, ref failurePoint);
        }

        static void ApplyReferenceBindings(JObject parameters, string target, List<object> steps, ref string failurePoint)
        {
            if (string.IsNullOrWhiteSpace(target) || parameters["referenceBindings"] is not JArray bindings || bindings.Count == 0)
                return;

            JObject bindingParams = new()
            {
                ["target"] = target,
                ["searchMethod"] = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                ["includeInactive"] = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                ["bindings"] = bindings.DeepClone()
            };
            CallTool("Unity.Scene.PreviewAssignObjectReferences", "preview", bindingParams, SceneReferenceBindingTools.PreviewAssignObjectReferences, steps, true, ref failurePoint);
            if (failurePoint != null)
                return;
            CallTool("Unity.Scene.ApplyAssignObjectReferences", "apply", bindingParams, SceneReferenceBindingTools.ApplyAssignObjectReferences, steps, true, ref failurePoint);
        }

        static object CallSceneSerializedProperties(string toolName, string phase, JObject parameters, List<object> steps, bool required, ref string failurePoint)
        {
            SetSceneSerializedPropertiesParams request = parameters.ToObject<SetSceneSerializedPropertiesParams>();
            object result = SceneTools.SetSerializedProperties(request);
            steps.Add(BuildStep(toolName, phase, parameters, result));
            if (required && !IsSuccess(result))
                failurePoint ??= toolName;
            return result;
        }

        static object CallTool(string toolName, string phase, JObject parameters, Func<JObject, object> call, List<object> steps, bool required, ref string failurePoint)
        {
            object result;
            try
            {
                result = call(parameters ?? new JObject());
            }
            catch (Exception ex)
            {
                result = Response.Error($"{toolName} threw {ex.GetType().Name}", new { errorKind = ex.GetType().Name, error = ex.Message });
            }

            steps.Add(BuildStep(toolName, phase, parameters, result));
            if (required && !IsSuccess(result))
                failurePoint ??= toolName;
            return result;
        }

        static async Task<object> CallToolAsync(string toolName, string phase, JObject parameters, Func<JObject, Task<object>> call, List<object> steps, bool required, Action<string> failurePointSetter)
        {
            object result;
            try
            {
                result = await call(parameters ?? new JObject());
            }
            catch (Exception ex)
            {
                result = Response.Error($"{toolName} threw {ex.GetType().Name}", new { errorKind = ex.GetType().Name, error = ex.Message });
            }

            steps.Add(BuildStep(toolName, phase, parameters, result));
            if (required && !IsSuccess(result))
                failurePointSetter?.Invoke(toolName);
            return result;
        }

        static object ReadConsoleSummary(int count)
        {
            return ReadConsole.HandleCommand(new ReadConsoleParams
            {
                Action = ConsoleAction.Get,
                Types = new[] { ConsoleLogType.Error, ConsoleLogType.Warning, ConsoleLogType.Exception, ConsoleLogType.Assert },
                Count = count,
                Format = ConsoleOutputFormat.Summary,
                ExcludeMcpNoise = true,
                IncludeStacktrace = false
            });
        }

        static JObject BuildReusePlanParams(JObject parameters, string intent)
        {
            return new JObject
            {
                ["intent"] = intent,
                ["context"] = GetString(parameters, "context", "Context"),
                ["includeSceneSearch"] = true,
                ["includePrefabs"] = true,
                ["includePresets"] = true,
                ["includeMissingPackages"] = true,
                ["includePackageCapabilities"] = true,
                ["maxResults"] = Math.Max(4, GetInt(parameters, 12, "maxReuseResults", "MaxReuseResults"))
            };
        }

        static JObject Pick(JObject source, params string[] names)
        {
            var result = new JObject();
            foreach (string name in names)
            {
                if (source.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value) && value != null && value.Type != JTokenType.Null)
                    result[name] = value.DeepClone();
            }
            return result;
        }

        static object BuildStep(string tool, string phase, JObject parameters, object result)
        {
            JObject resultObject = SafeJObject(result);
            return new
            {
                phase,
                tool,
                success = IsSuccess(resultObject),
                message = resultObject.Value<string>("message") ?? resultObject.Value<string>("error"),
                parameters = parameters == null ? null : BuildParameterSummary(parameters),
                result = resultObject
            };
        }

        static object BuildSyntheticStep(string tool, string phase, string status, object parameters, object result)
        {
            return new
            {
                phase,
                tool,
                success = true,
                status,
                parameters,
                result
            };
        }

        static object BuildParameterSummary(JObject parameters)
        {
            var clone = (JObject)parameters.DeepClone();
            if (clone["componentProperties"] is JObject properties && properties.Count > 20)
                clone["componentProperties"] = new JObject { ["omittedPropertyCount"] = properties.Count };
            if (clone["assignments"] is JArray assignments && assignments.Count > 10)
                clone["assignments"] = new JArray(assignments.Take(10).Select(item => item.DeepClone()));
            if (clone["bindings"] is JArray bindings && bindings.Count > 10)
                clone["bindings"] = new JArray(bindings.Take(10).Select(item => item.DeepClone()));
            return clone;
        }

        static object[] ExtractSteps(IEnumerable<object> steps, params string[] phases)
        {
            var phaseSet = new HashSet<string>(phases ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return (steps ?? Array.Empty<object>())
                .Select(SafeJObject)
                .Where(step => phaseSet.Contains(step.Value<string>("phase") ?? string.Empty))
                .Select(step => new
                {
                    phase = step.Value<string>("phase"),
                    tool = step.Value<string>("tool"),
                    success = step.Value<bool?>("success") ?? false,
                    message = step.Value<string>("message")
                })
                .ToArray();
        }

        static object ExtractObjectEvidence(IEnumerable<object> steps)
        {
            var paths = new List<string>();
            var ids = new List<string>();
            JArray root = JArray.FromObject((steps ?? Array.Empty<object>()).ToArray());
            foreach (JProperty property in root.Descendants().OfType<JProperty>())
            {
                if (property.Value.Type != JTokenType.String && property.Value.Type != JTokenType.Integer)
                    continue;

                string name = property.Name ?? string.Empty;
                string value = property.Value.ToString();
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0)
                    paths.Add(value);
                if (name.IndexOf("stable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.EndsWith("id", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("objectId", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("instanceID", StringComparison.OrdinalIgnoreCase))
                    ids.Add(value);
            }

            return new
            {
                paths = paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray(),
                stableIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).Take(40).ToArray()
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            CompactArray(root, "partialResults", 12);
            if (root["runtimeVerification"] is JObject runtime)
                CompactArray(runtime, "componentSnapshots", 12);
            return root;
        }

        static void CompactArray(JObject root, string propertyName, int maxItems)
        {
            if (root == null || root[propertyName] is not JArray rows || rows.Count <= maxItems)
                return;

            int omitted = rows.Count - maxItems;
            root[propertyName] = new JArray(rows.Take(maxItems).Select(row => row.DeepClone()));
            root[$"compactOmitted{char.ToUpperInvariant(propertyName[0])}{propertyName.Substring(1)}Count"] = omitted;
        }

        static string ResolveCreatedTarget(JObject parameters)
        {
            return GetString(parameters, "target", "Target") ??
                   GetString(parameters, "name", "Name") ??
                   GetString(parameters, "instanceName", "InstanceName");
        }

        static string[] ExtractCapturePaths(object capture)
        {
            JObject root = SafeJObject(capture);
            var paths = new List<string>();
            foreach (JToken token in root.DescendantsAndSelf())
            {
                if (token is JProperty property &&
                    (property.Name.IndexOf("path", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     property.Name.IndexOf("output", StringComparison.OrdinalIgnoreCase) >= 0) &&
                    property.Value.Type == JTokenType.String)
                {
                    string value = property.Value.ToString();
                    if (value.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        value.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(value);
                    }
                }
            }

            return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static bool IsSuccess(object result) => IsSuccess(SafeJObject(result));

        static bool IsSuccess(JObject result)
        {
            if (result == null)
                return false;
            if (result.TryGetValue("success", StringComparison.OrdinalIgnoreCase, out JToken success))
                return success.Type == JTokenType.Boolean && success.Value<bool>();
            return string.Equals(result.Value<string>("status"), "ready", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(result.Value<string>("status"), "verified", StringComparison.OrdinalIgnoreCase);
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

        static bool GetBool(JObject parameters, bool defaultValue, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;
            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();
            return bool.TryParse(token.ToString(), out bool value) ? value : defaultValue;
        }

        static int GetInt(JObject parameters, int defaultValue, params string[] names)
        {
            JToken token = GetToken(parameters, names);
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;
            return int.TryParse(token.ToString(), out int value) ? value : defaultValue;
        }

        static WorkflowResult Succeeded(string message, object data)
        {
            return new WorkflowResult
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        static WorkflowResult Failed(string message, string failurePoint, string intent, bool apply, List<object> steps, object dirtyStateBefore, object reusePlan)
        {
            return new WorkflowResult
            {
                Success = false,
                Message = message,
                ErrorKind = "workflow_step_failed",
                Data = new
                {
                    status = "failed",
                    intent,
                    applyRequested = apply,
                    failurePoint,
                    durableAuthoring = new
                    {
                        status = "partial",
                        reusePlan,
                        dirtyStateBefore,
                        dirtyStateAfter = SceneTools.GetDirtyState(new JObject()),
                        saveState = new { requested = false, saved = false }
                    },
                    runtimeVerification = new { status = "not_started" },
                    partialResults = steps.ToArray()
                }
            };
        }

        sealed class WorkflowResult
        {
            public bool Success;
            public string Message;
            public string ErrorKind;
            public object Data;
        }
    }
}
