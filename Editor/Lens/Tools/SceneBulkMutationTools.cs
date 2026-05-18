#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Becool.UnityMcpLens.Editor.Adapters.Unity;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class SceneBulkMutationTools
    {
        const string PreviewToolName = "Unity.Scene.PreviewBulkMutation";
        const string ApplyToolName = "Unity.Scene.ApplyBulkMutation";

        sealed class BulkMutationRequest
        {
            public bool PreviewOnly = true;
            public string Scene;
            public string NamePrefix;
            public string NameExact;
            public string[] ComponentTypes = Array.Empty<string>();
            public string ComponentMatch = "all";
            public string Root;
            public string RootSearchMethod = "by_id_or_name_or_path";
            public bool IncludeInactive;
            public string GridFieldName;
            public string GridFieldComponentType;
            public int GridFieldComponentIndex;
            public FieldVariableSpec[] FieldVariables = Array.Empty<FieldVariableSpec>();
            public JObject[] Mutations = Array.Empty<JObject>();
            public int MaxObjects = 200;
            public int MaxRows = 50;
            public bool AllowPartial;
            public bool SaveScene;
        }

        sealed class FieldVariableSpec
        {
            public string Name;
            public string ComponentType;
            public int ComponentIndex;
            public string PropertyPath;
        }

        sealed class TargetRow
        {
            public GameObject GameObject;
            public Dictionary<string, double> NumericVariables = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, object> RawVariables = new(StringComparer.OrdinalIgnoreCase);
        }

        sealed class MutationOutcome
        {
            public readonly List<object> Changes = new();
            public readonly List<object> Errors = new();
            public int WouldChangeCount;
            public int AppliedCount;
        }

        [McpSchema(PreviewToolName)]
        public static object GetPreviewSchema() => BuildSchema(includeSaveScene: false);

        [McpSchema(ApplyToolName)]
        public static object GetApplySchema() => BuildSchema(includeSaveScene: true);

        [McpTool(PreviewToolName,
            "Previews bounded bulk scene mutations for GameObjects selected by component query, including transform, SpriteRenderer, and serialized-property changes.",
            "Preview Bulk Scene Mutation",
            Groups = new[] { "scene", "diagnostics" },
            EnabledByDefault = true)]
        public static object PreviewBulkMutation(JObject @params)
        {
            return Run(@params, previewOnly: true);
        }

        [McpTool(ApplyToolName,
            "Applies bounded bulk scene mutations for GameObjects selected by component query, with preview-style reporting and optional explicit scene save.",
            "Apply Bulk Scene Mutation",
            Groups = new[] { "scene", "editor" },
            EnabledByDefault = true)]
        public static object ApplyBulkMutation(JObject @params)
        {
            return Run(@params, previewOnly: false);
        }

        static object Run(JObject @params, bool previewOnly)
        {
            string toolName = previewOnly ? PreviewToolName : ApplyToolName;
            @params ??= new JObject();
            var timing = new ToolOperationTiming(toolName, "scene_bulk_mutation", PayloadBudgeting.GetUtf8ByteCount(@params.ToString(Formatting.None)));
            bool success = true;
            string errorKind = null;
            object data;

            try
            {
                BulkMutationRequest request;
                using (timing.Measure("normalization"))
                {
                    request = Normalize(@params, previewOnly);
                }

                using (timing.Measure("service"))
                {
                    data = BuildResult(request);
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
                    error = ex.Message
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(previewOnly ? "Bulk scene mutation preview completed." : "Bulk scene mutation applied.", ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "scene_bulk_mutation_full_result" },
                        "scene_bulk_mutation",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(previewOnly ? "SCENE_BULK_MUTATION_PREVIEW_FAILED" : "SCENE_BULK_MUTATION_APPLY_FAILED", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static object BuildSchema(bool includeSaveScene)
        {
            var properties = new Dictionary<string, object>
            {
                ["scene"] = new { type = "string", description = "Optional loaded scene name or Assets-relative .unity path filter." },
                ["scenePath"] = new { type = "string", description = "Alias for scene." },
                ["namePrefix"] = new { type = "string", description = "Optional GameObject name prefix filter." },
                ["nameExact"] = new { type = "string", description = "Optional exact GameObject name filter." },
                ["componentTypes"] = new { type = "array", description = "Component type names used to find target GameObjects. Short or fully-qualified names are accepted.", items = new { type = "string" } },
                ["componentType"] = new { type = "string", description = "Single component type alias appended to componentTypes." },
                ["componentMatch"] = new { type = "string", description = "How componentTypes are matched: all or any. Defaults to all." },
                ["root"] = new { type = "string", description = "Optional root GameObject name, hierarchy path, or id filter." },
                ["rootSearchMethod"] = new { type = "string", description = "Root search method: by_name, by_path, by_id, or by_id_or_name_or_path. Defaults to by_id_or_name_or_path." },
                ["includeInactive"] = new { type = "boolean", description = "Include inactive scene objects. Defaults to false." },
                ["gridFieldName"] = new { type = "string", description = "Optional shorthand serialized field/property path exposed as grid.x/grid.y/grid.z and x/y/z expression variables." },
                ["gridFieldComponentType"] = new { type = "string", description = "Optional component type for gridFieldName. Defaults to the first componentTypes entry." },
                ["gridFieldComponentIndex"] = new { type = "integer", description = "0-based component index for gridFieldName reads. Defaults to 0." },
                ["fieldVariables"] = new { type = "array", description = "Additional variables as {name, componentType, componentIndex, propertyPath}; vector values expose name.x/name.y/name.z.", items = new { } },
                ["mutations"] = new { type = "array", description = "Mutation specs. Supported kinds: transform, serializedProperty, spriteRenderer.", items = new { } },
                ["maxObjects"] = new { type = "integer", description = "Maximum selected objects to mutate. Defaults to 200 and is capped at 1000." },
                ["maxRows"] = new { type = "integer", description = "Maximum result rows returned inline. Defaults to 50 and is capped at 500." },
                ["allowPartial"] = new { type = "boolean", description = "Allow apply mode to mutate rows without errors when other rows fail validation. Defaults to false." }
            };

            if (includeSaveScene)
                properties["saveScene"] = new { type = "boolean", description = "Save touched loaded scenes after applying mutations. Defaults to false." };

            return new
            {
                type = "object",
                properties,
                required = new[] { "componentTypes", "mutations" }
            };
        }

        static BulkMutationRequest Normalize(JObject @params, bool previewOnly)
        {
            var componentTypes = GetStringArray(@params, "componentTypes", "ComponentTypes")
                .Concat(GetString(@params, "componentType", "ComponentType") is { } single ? new[] { single } : Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            if (componentTypes.Length == 0)
                throw new InvalidOperationException("componentTypes is required for safe bulk scene mutation.");

            var mutations = GetObjectArray(@params, "mutations", "Mutations");
            if (mutations.Length == 0)
                throw new InvalidOperationException("At least one mutation entry is required.");

            return new BulkMutationRequest
            {
                PreviewOnly = previewOnly,
                Scene = GetString(@params, "scene", "Scene", "scenePath", "ScenePath"),
                NamePrefix = GetString(@params, "namePrefix", "NamePrefix"),
                NameExact = GetString(@params, "nameExact", "NameExact"),
                ComponentTypes = componentTypes,
                ComponentMatch = (GetString(@params, "componentMatch", "ComponentMatch") ?? "all").Trim().ToLowerInvariant(),
                Root = GetString(@params, "root", "Root"),
                RootSearchMethod = GetString(@params, "rootSearchMethod", "RootSearchMethod") ?? "by_id_or_name_or_path",
                IncludeInactive = GetBool(@params, false, "includeInactive", "IncludeInactive"),
                GridFieldName = GetString(@params, "gridFieldName", "GridFieldName"),
                GridFieldComponentType = GetString(@params, "gridFieldComponentType", "GridFieldComponentType"),
                GridFieldComponentIndex = Math.Max(0, GetInt(@params, 0, "gridFieldComponentIndex", "GridFieldComponentIndex")),
                FieldVariables = ParseFieldVariables(GetToken(@params, "fieldVariables", "FieldVariables")),
                Mutations = mutations,
                MaxObjects = Math.Clamp(GetInt(@params, 200, "maxObjects", "MaxObjects"), 1, 1000),
                MaxRows = Math.Clamp(GetInt(@params, 50, "maxRows", "MaxRows"), 1, 500),
                AllowPartial = GetBool(@params, false, "allowPartial", "AllowPartial"),
                SaveScene = !previewOnly && GetBool(@params, false, "saveScene", "SaveScene")
            };
        }

        static object BuildResult(BulkMutationRequest request)
        {
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            var resolvedTypes = ResolveComponentTypes(request.ComponentTypes, out var missingTypes);
            if (missingTypes.Length > 0)
            {
                return new
                {
                    status = "failed",
                    errorKind = "component_type_not_found",
                    missingTypes,
                    previewOnly = request.PreviewOnly,
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    saveState = SceneDirtyStateUtility.BuildSaveState()
                };
            }

            var targets = SelectTargets(request, resolvedTypes);
            int selectedCount = targets.Length;
            var processedTargets = targets.Take(request.MaxObjects).Select(go => BuildTargetRow(go, request)).ToArray();
            var omittedObjectCount = Math.Max(0, selectedCount - processedTargets.Length);
            var validationRows = ProcessRows(processedTargets, request, previewOnly: true);
            var validationRowTokens = validationRows.Select(row => JObject.FromObject(row)).ToArray();
            int validationErrorCount = validationRowTokens.Sum(row => row["errorCount"]?.Value<int>() ?? 0);
            bool canApply = !request.PreviewOnly && (validationErrorCount == 0 || request.AllowPartial);

            object[] rows = validationRows;
            object saveState = SceneDirtyStateUtility.BuildSaveState(requested: request.SaveScene);
            if (canApply)
            {
                rows = ProcessRows(processedTargets, request, previewOnly: false);
                var touchedScenes = processedTargets
                    .Where(row => row.GameObject != null && row.GameObject.scene.IsValid())
                    .Select(row => row.GameObject.scene)
                    .GroupBy(scene => scene.handle)
                    .Select(group => group.First())
                    .ToArray();
                saveState = SaveTouchedScenes(touchedScenes, request.SaveScene);
            }

            var rowTokens = rows.Select(row => JObject.FromObject(row)).ToArray();
            int appliedChangeCount = rowTokens.Sum(row => row["appliedChangeCount"]?.Value<int>() ?? 0);
            int wouldChangeCount = rowTokens.Sum(row => row["wouldChangeCount"]?.Value<int>() ?? 0);
            int errorCount = rowTokens.Sum(row => row["errorCount"]?.Value<int>() ?? 0);
            bool blockedByErrors = !request.PreviewOnly && !request.AllowPartial && validationErrorCount > 0;

            return new
            {
                status = blockedByErrors ? "blocked_by_validation_errors" : "ready",
                previewOnly = request.PreviewOnly,
                applied = !request.PreviewOnly && !blockedByErrors,
                allowPartial = request.AllowPartial,
                scene = string.IsNullOrWhiteSpace(request.Scene) ? null : request.Scene,
                componentTypes = request.ComponentTypes,
                componentMatch = IsAnyMatch(request.ComponentMatch) ? "any" : "all",
                selectedCount,
                processedCount = processedTargets.Length,
                omittedObjectCount,
                mutationCount = request.Mutations.Length,
                wouldChangeCount,
                appliedChangeCount,
                errorCount,
                validationErrorCount,
                blockedByErrors,
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                saveState,
                rows = rowTokens.Take(request.MaxRows).ToArray(),
                compactOmittedRowCount = Math.Max(0, rowTokens.Length - request.MaxRows)
            };
        }

        static GameObject[] SelectTargets(BulkMutationRequest request, Dictionary<string, Type> resolvedTypes)
        {
            var inactiveMode = request.IncludeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude;
            var allObjects = UnityApiAdapter.FindObjectsByType<GameObject>(inactiveMode);
            GameObject rootObject = ResolveRoot(request, inactiveMode);
            Type[] typeValues = resolvedTypes.Values.ToArray();
            bool matchAny = IsAnyMatch(request.ComponentMatch);

            return allObjects
                .Where(go => MatchesScene(go, request.Scene))
                .Where(go => rootObject == null || go.transform == rootObject.transform || go.transform.IsChildOf(rootObject.transform))
                .Where(go => MatchesName(go, request.NamePrefix, request.NameExact))
                .Where(go => MatchesComponents(go, typeValues, matchAny))
                .OrderBy(go => UiDiagnosticsHelper.GetHierarchyPath(go.transform), StringComparer.Ordinal)
                .ToArray();
        }

        static TargetRow BuildTargetRow(GameObject gameObject, BulkMutationRequest request)
        {
            var row = new TargetRow { GameObject = gameObject };
            row.NumericVariables["index"] = 0;
            row.RawVariables["name"] = gameObject.name;
            AddVectorVariables(row, "position", gameObject.transform.position);
            AddVectorVariables(row, "localPosition", gameObject.transform.localPosition);
            AddVectorVariables(row, "localScale", gameObject.transform.localScale);

            if (!string.IsNullOrWhiteSpace(request.GridFieldName))
            {
                string componentType = string.IsNullOrWhiteSpace(request.GridFieldComponentType)
                    ? request.ComponentTypes.FirstOrDefault()
                    : request.GridFieldComponentType;
                if (TryReadMember(gameObject, componentType, request.GridFieldComponentIndex, request.GridFieldName, out object gridValue, out _))
                {
                    AddVariable(row, "grid", gridValue);
                    AddGridAliases(row, gridValue);
                }
            }

            foreach (FieldVariableSpec variable in request.FieldVariables)
            {
                if (variable == null || string.IsNullOrWhiteSpace(variable.Name))
                    continue;

                if (TryReadMember(gameObject, variable.ComponentType, variable.ComponentIndex, variable.PropertyPath, out object value, out _))
                    AddVariable(row, variable.Name, value);
            }

            return row;
        }

        static object[] ProcessRows(TargetRow[] targets, BulkMutationRequest request, bool previewOnly)
        {
            var rows = new List<object>();
            int count = targets?.Length ?? 0;
            for (int index = 0; index < count; index++)
            {
                TargetRow row = targets[index];
                row.NumericVariables["index"] = index;
                row.NumericVariables["count"] = count;
                var outcome = ProcessMutations(row, request, previewOnly);
                rows.Add(new
                {
                    path = UiDiagnosticsHelper.GetHierarchyPath(row.GameObject.transform),
                    name = row.GameObject.name,
                    objectId = UnityApiAdapter.GetObjectIdOrZero(row.GameObject),
                    scene = row.GameObject.scene.IsValid() ? row.GameObject.scene.path : null,
                    activeSelf = row.GameObject.activeSelf,
                    activeInHierarchy = row.GameObject.activeInHierarchy,
                    variables = BuildVariableSummary(row),
                    changes = outcome.Changes,
                    errors = outcome.Errors,
                    wouldChangeCount = outcome.WouldChangeCount,
                    appliedChangeCount = outcome.AppliedCount,
                    errorCount = outcome.Errors.Count
                });
            }

            return rows.ToArray();
        }

        static MutationOutcome ProcessMutations(TargetRow row, BulkMutationRequest request, bool previewOnly)
        {
            var outcome = new MutationOutcome();
            foreach (JObject mutation in request.Mutations)
            {
                try
                {
                    string kind = (GetString(mutation, "kind", "Kind", "operation", "Operation") ?? "serializedProperty").Trim();
                    switch (NormalizeKind(kind))
                    {
                        case "transform":
                            ProcessTransformMutation(row, mutation, previewOnly, outcome);
                            break;
                        case "spriterenderer":
                            ProcessSpriteRendererMutation(row, mutation, previewOnly, outcome);
                            break;
                        case "serializedproperty":
                            ProcessSerializedPropertyMutation(row, mutation, previewOnly, outcome);
                            break;
                        default:
                            outcome.Errors.Add(new { kind, error = $"Unsupported mutation kind '{kind}'." });
                            break;
                    }
                }
                catch (Exception ex)
                {
                    outcome.Errors.Add(new
                    {
                        kind = GetString(mutation, "kind", "Kind", "operation", "Operation"),
                        errorKind = ex.GetType().Name,
                        error = ex.Message
                    });
                }
            }

            if (!previewOnly && outcome.AppliedCount > 0)
                SceneDirtyStateUtility.MarkSceneDirty(row.GameObject);

            return outcome;
        }

        static void ProcessTransformMutation(TargetRow row, JObject mutation, bool previewOnly, MutationOutcome outcome)
        {
            string target = (GetString(mutation, "target", "Target", "property", "Property") ?? "localPosition").Trim();
            Vector3 before = GetTransformVector(row.GameObject.transform, target);
            Vector3 after = ResolveVector(mutation, before, row);
            bool wouldChange = !Approximately(before, after);
            if (!previewOnly && wouldChange)
            {
                Undo.RecordObject(row.GameObject.transform, "Apply bulk transform mutation");
                SetTransformVector(row.GameObject.transform, target, after);
                EditorUtility.SetDirty(row.GameObject.transform);
                outcome.AppliedCount++;
            }

            if (wouldChange)
                outcome.WouldChangeCount++;

            outcome.Changes.Add(new
            {
                kind = "transform",
                target,
                previousValue = ToVectorObject(before),
                newValue = ToVectorObject(after),
                wouldChange,
                applied = !previewOnly && wouldChange
            });
        }

        static void ProcessSpriteRendererMutation(TargetRow row, JObject mutation, bool previewOnly, MutationOutcome outcome)
        {
            int componentIndex = Math.Max(0, GetInt(mutation, 0, "componentIndex", "ComponentIndex"));
            SpriteRenderer[] renderers = row.GameObject.GetComponents<SpriteRenderer>();
            if (renderers == null || renderers.Length <= componentIndex || renderers[componentIndex] == null)
            {
                outcome.Errors.Add(new { kind = "spriteRenderer", error = $"SpriteRenderer with index {componentIndex} was not found." });
                return;
            }

            SpriteRenderer renderer = renderers[componentIndex];
            string property = (GetString(mutation, "property", "Property", "target", "Target") ?? "sortingOrder").Trim();
            object before;
            object after;
            bool wouldChange;
            switch (property.ToLowerInvariant())
            {
                case "sortingorder":
                case "sorting_order":
                    before = renderer.sortingOrder;
                    after = Convert.ToInt32(Math.Round(ResolveNumber(mutation, row), MidpointRounding.AwayFromZero));
                    wouldChange = renderer.sortingOrder != (int)after;
                    if (!previewOnly && wouldChange)
                    {
                        Undo.RecordObject(renderer, "Apply bulk sprite renderer mutation");
                        renderer.sortingOrder = (int)after;
                    }
                    break;
                case "sortinglayername":
                case "sorting_layer_name":
                    before = renderer.sortingLayerName;
                    after = ResolveString(mutation, row);
                    wouldChange = !string.Equals(renderer.sortingLayerName, (string)after, StringComparison.Ordinal);
                    if (!previewOnly && wouldChange)
                    {
                        Undo.RecordObject(renderer, "Apply bulk sprite renderer mutation");
                        renderer.sortingLayerName = (string)after;
                    }
                    break;
                case "enabled":
                    before = renderer.enabled;
                    after = ResolveBool(mutation, row);
                    wouldChange = renderer.enabled != (bool)after;
                    if (!previewOnly && wouldChange)
                    {
                        Undo.RecordObject(renderer, "Apply bulk sprite renderer mutation");
                        renderer.enabled = (bool)after;
                    }
                    break;
                default:
                    outcome.Errors.Add(new { kind = "spriteRenderer", property, error = $"SpriteRenderer property '{property}' is not supported." });
                    return;
            }

            if (!previewOnly && wouldChange)
            {
                EditorUtility.SetDirty(renderer);
                outcome.AppliedCount++;
            }

            if (wouldChange)
                outcome.WouldChangeCount++;

            outcome.Changes.Add(new
            {
                kind = "spriteRenderer",
                property,
                componentIndex,
                previousValue = before,
                newValue = after,
                wouldChange,
                applied = !previewOnly && wouldChange
            });
        }

        static void ProcessSerializedPropertyMutation(TargetRow row, JObject mutation, bool previewOnly, MutationOutcome outcome)
        {
            string componentTypeName = GetString(mutation, "componentType", "ComponentType");
            string propertyPath = GetString(mutation, "propertyPath", "PropertyPath");
            int componentIndex = Math.Max(0, GetInt(mutation, 0, "componentIndex", "ComponentIndex"));
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                outcome.Errors.Add(new { kind = "serializedProperty", error = "componentType is required." });
                return;
            }

            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                outcome.Errors.Add(new { kind = "serializedProperty", componentType = componentTypeName, error = "propertyPath is required." });
                return;
            }

            if (!TryGetComponent(row.GameObject, componentTypeName, componentIndex, out Component component, out string componentError))
            {
                outcome.Errors.Add(new { kind = "serializedProperty", componentType = componentTypeName, componentIndex, error = componentError });
                return;
            }

            var serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                outcome.Errors.Add(new { kind = "serializedProperty", componentType = component.GetType().FullName, componentIndex, propertyPath, error = "Serialized property was not found." });
                return;
            }

            string beforeValue = DescribeProperty(property);
            JToken value = ResolveValueToken(mutation, row);
            string targetValue = DescribeRequestedValue(value);
            bool applied = false;
            if (!previewOnly)
            {
                Undo.RecordObject(component, "Apply bulk serialized property mutation");
                if (!TryAssignSerializedProperty(property, value, out string assignError))
                {
                    outcome.Errors.Add(new { kind = "serializedProperty", componentType = component.GetType().FullName, componentIndex, propertyPath, error = assignError });
                    return;
                }

                serializedObject.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(component);
                serializedObject.UpdateIfRequiredOrScript();
                targetValue = DescribeProperty(property);
                applied = beforeValue != targetValue;
                if (applied)
                    outcome.AppliedCount++;
            }

            bool wouldChange = beforeValue != targetValue;
            if (wouldChange)
                outcome.WouldChangeCount++;

            outcome.Changes.Add(new
            {
                kind = "serializedProperty",
                componentType = component.GetType().FullName,
                componentIndex,
                propertyPath,
                propertyType = property.propertyType.ToString(),
                previousValue = beforeValue,
                newValue = targetValue,
                requestedValue = value,
                wouldChange,
                applied
            });
        }

        static object SaveTouchedScenes(Scene[] touchedScenes, bool requested)
        {
            if (!requested)
                return SceneDirtyStateUtility.BuildSaveState();

            var savedScenes = new List<object>();
            var errors = new List<string>();
            bool attempted = false;
            foreach (Scene scene in touchedScenes ?? Array.Empty<Scene>())
            {
                if (!scene.IsValid() || !scene.isLoaded || !scene.isDirty)
                    continue;

                attempted = true;
                try
                {
                    if (EditorSceneManager.SaveScene(scene))
                        savedScenes.Add(SceneDirtyStateUtility.ToSceneState(scene));
                    else
                        errors.Add($"SaveScene returned false for '{scene.path}'.");
                }
                catch (Exception ex)
                {
                    errors.Add($"{scene.path}: {ex.Message}");
                }
            }

            if (savedScenes.Count > 0)
                AssetDatabase.Refresh();

            return SceneDirtyStateUtility.BuildSaveState(
                requested: true,
                attempted: attempted,
                saved: errors.Count == 0 && savedScenes.Count > 0,
                savedScenes: savedScenes.ToArray(),
                message: !attempted ? "no_dirty_touched_scenes" : errors.Count == 0 ? "saved" : "save_failed",
                error: errors.Count == 0 ? null : string.Join("; ", errors));
        }

        static Dictionary<string, Type> ResolveComponentTypes(string[] componentTypeNames, out string[] missingTypes)
        {
            var resolved = new Dictionary<string, Type>(StringComparer.Ordinal);
            var missing = new List<string>();
            foreach (string componentTypeName in componentTypeNames ?? Array.Empty<string>())
            {
                if (UnityComponentResolver.TryResolve(componentTypeName, out Type type, out _) && typeof(Component).IsAssignableFrom(type))
                    resolved[componentTypeName] = type;
                else
                    missing.Add(componentTypeName);
            }

            missingTypes = missing.ToArray();
            return resolved;
        }

        static GameObject ResolveRoot(BulkMutationRequest request, FindObjectsInactive inactiveMode)
        {
            if (string.IsNullOrWhiteSpace(request.Root))
                return null;

            JObject findParams = new()
            {
                ["search_inactive"] = inactiveMode == FindObjectsInactive.Include
            };
            return ObjectsHelper.FindObject(request.Root, request.RootSearchMethod, findParams);
        }

        static bool MatchesScene(GameObject gameObject, string scene)
        {
            return string.IsNullOrWhiteSpace(scene) ||
                string.Equals(gameObject.scene.name, scene, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gameObject.scene.path, SceneDirtyStateUtility.NormalizeScenePath(scene), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(gameObject.scene.path, scene, StringComparison.OrdinalIgnoreCase);
        }

        static bool MatchesName(GameObject gameObject, string namePrefix, string nameExact)
        {
            return (string.IsNullOrWhiteSpace(namePrefix) || gameObject.name.StartsWith(namePrefix, StringComparison.Ordinal)) &&
                (string.IsNullOrWhiteSpace(nameExact) || string.Equals(gameObject.name, nameExact, StringComparison.Ordinal));
        }

        static bool MatchesComponents(GameObject gameObject, Type[] componentTypes, bool matchAny)
        {
            if (componentTypes == null || componentTypes.Length == 0)
                return true;

            if (matchAny)
                return componentTypes.Any(type => gameObject.GetComponent(type) != null);

            return componentTypes.All(type => gameObject.GetComponent(type) != null);
        }

        static bool TryGetComponent(GameObject gameObject, string componentTypeName, int componentIndex, out Component component, out string error)
        {
            component = null;
            error = null;
            Type componentType = UnityComponentResolver.FindType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Component type '{componentTypeName}' could not be resolved.";
                return false;
            }

            Component[] matches = gameObject.GetComponents(componentType);
            if (matches == null || matches.Length <= componentIndex || matches[componentIndex] == null)
            {
                error = $"Component '{componentTypeName}' with index {componentIndex} was not found.";
                return false;
            }

            component = matches[componentIndex];
            return true;
        }

        static bool TryReadMember(GameObject gameObject, string componentTypeName, int componentIndex, string propertyPath, out object value, out string error)
        {
            value = null;
            error = null;
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                error = "componentType is required for field reads.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                error = "propertyPath is required for field reads.";
                return false;
            }

            if (!TryGetComponent(gameObject, componentTypeName, Math.Max(0, componentIndex), out Component component, out error))
                return false;

            var serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty(propertyPath);
            if (property != null)
            {
                value = ReadSerializedValue(property);
                return true;
            }

            if (TryReadReflectionPath(component, propertyPath, out value))
                return true;

            error = $"Field or property '{propertyPath}' was not found on '{component.GetType().FullName}'.";
            return false;
        }

        static object ReadSerializedValue(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Integer => property.intValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Vector2 => property.vector2Value,
                SerializedPropertyType.Vector3 => property.vector3Value,
                SerializedPropertyType.Vector2Int => property.vector2IntValue,
                SerializedPropertyType.Vector3Int => property.vector3IntValue,
                SerializedPropertyType.Enum => property.enumNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                    ? property.enumNames[property.enumValueIndex]
                    : property.enumValueIndex,
                SerializedPropertyType.ObjectReference => property.objectReferenceValue == null ? null : property.objectReferenceValue.name,
                _ => property.displayName
            };
        }

        static bool TryReadReflectionPath(object owner, string path, out object value)
        {
            value = owner;
            foreach (string segment in path.Split('.'))
            {
                if (value == null)
                    return false;

                Type type = value.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                FieldInfo field = type.GetField(segment, flags);
                if (field != null)
                {
                    value = field.GetValue(value);
                    continue;
                }

                PropertyInfo property = type.GetProperty(segment, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    value = property.GetValue(value);
                    continue;
                }

                return false;
            }

            return true;
        }

        static void AddVariable(TargetRow row, string name, object value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            row.RawVariables[name] = DescribeRawValue(value);
            if (TryConvertNumber(value, out double number))
            {
                row.NumericVariables[name] = number;
                return;
            }

            if (TryConvertVector3(value, out Vector3 vector))
                AddVectorVariables(row, name, vector);
        }

        static void AddGridAliases(TargetRow row, object value)
        {
            if (!TryConvertVector3(value, out Vector3 vector))
                return;

            row.NumericVariables["x"] = vector.x;
            row.NumericVariables["y"] = vector.y;
            row.NumericVariables["z"] = vector.z;
        }

        static void AddVectorVariables(TargetRow row, string name, Vector3 vector)
        {
            row.RawVariables[name] = ToVectorObject(vector);
            row.NumericVariables[$"{name}.x"] = vector.x;
            row.NumericVariables[$"{name}.y"] = vector.y;
            row.NumericVariables[$"{name}.z"] = vector.z;
        }

        static object BuildVariableSummary(TargetRow row)
        {
            return new
            {
                numeric = row.NumericVariables
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(pair => pair.Key, pair => pair.Value),
                raw = row.RawVariables
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(pair => pair.Key, pair => pair.Value)
            };
        }

        static Vector3 ResolveVector(JObject mutation, Vector3 current, TargetRow row)
        {
            JToken value = GetToken(mutation, "value", "Value", "vector", "Vector");
            if (value is JArray array)
            {
                return new Vector3(
                    array.Count > 0 ? ResolveCoordinate(array[0], current.x, row) : current.x,
                    array.Count > 1 ? ResolveCoordinate(array[1], current.y, row) : current.y,
                    array.Count > 2 ? ResolveCoordinate(array[2], current.z, row) : current.z);
            }

            if (value is JObject obj)
            {
                return new Vector3(
                    ResolveCoordinate(GetToken(obj, "x", "X"), current.x, row),
                    ResolveCoordinate(GetToken(obj, "y", "Y"), current.y, row),
                    ResolveCoordinate(GetToken(obj, "z", "Z"), current.z, row));
            }

            return new Vector3(
                ResolveCoordinate(GetToken(mutation, "x", "X"), current.x, row),
                ResolveCoordinate(GetToken(mutation, "y", "Y"), current.y, row),
                ResolveCoordinate(GetToken(mutation, "z", "Z"), current.z, row));
        }

        static double ResolveCoordinate(JToken token, double fallback, TargetRow row)
        {
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            if (token.Type == JTokenType.String)
                return EvaluateExpression(token.ToString(), row.NumericVariables);

            return token.Value<double>();
        }

        static double ResolveNumber(JObject mutation, TargetRow row)
        {
            string expression = GetString(mutation, "expression", "Expression", "valueExpression", "ValueExpression", "formula", "Formula");
            if (!string.IsNullOrWhiteSpace(expression))
                return EvaluateExpression(expression, row.NumericVariables);

            JToken value = GetToken(mutation, "value", "Value");
            if (value == null || value.Type == JTokenType.Null)
                return 0d;

            if (value.Type == JTokenType.String)
                return EvaluateExpression(value.ToString(), row.NumericVariables);

            return value.Value<double>();
        }

        static string ResolveString(JObject mutation, TargetRow row)
        {
            JToken value = ResolveValueToken(mutation, row);
            return value == null || value.Type == JTokenType.Null ? null : value.ToString();
        }

        static bool ResolveBool(JObject mutation, TargetRow row)
        {
            JToken value = ResolveValueToken(mutation, row);
            if (value == null || value.Type == JTokenType.Null)
                return false;

            if (value.Type == JTokenType.String && double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                return Math.Abs(number) > double.Epsilon;

            return value.Value<bool>();
        }

        static JToken ResolveValueToken(JObject mutation, TargetRow row)
        {
            string expression = GetString(mutation, "expression", "Expression", "valueExpression", "ValueExpression", "formula", "Formula");
            if (!string.IsNullOrWhiteSpace(expression))
                return new JValue(EvaluateExpression(expression, row.NumericVariables));

            string fromVariable = GetString(mutation, "fromVariable", "FromVariable");
            if (!string.IsNullOrWhiteSpace(fromVariable))
            {
                if (row.RawVariables.TryGetValue(fromVariable, out object raw))
                    return raw == null ? JValue.CreateNull() : JToken.FromObject(raw);
                if (row.NumericVariables.TryGetValue(fromVariable, out double number))
                    return new JValue(number);
                return JValue.CreateNull();
            }

            JToken fromField = GetToken(mutation, "fromField", "FromField");
            if (fromField is JObject field)
            {
                var spec = ParseFieldVariable(field);
                if (spec != null && TryReadMember(row.GameObject, spec.ComponentType, spec.ComponentIndex, spec.PropertyPath, out object value, out _))
                    return value == null ? JValue.CreateNull() : JToken.FromObject(DescribeRawValue(value));
                return JValue.CreateNull();
            }

            JToken valueToken = GetToken(mutation, "value", "Value");
            if (valueToken != null && valueToken.Type == JTokenType.String)
            {
                string text = valueToken.ToString();
                if (text.StartsWith("=", StringComparison.Ordinal))
                    return new JValue(EvaluateExpression(text.Substring(1), row.NumericVariables));
            }

            return valueToken ?? JValue.CreateNull();
        }

        static JToken EvaluateValueForProperty(SerializedProperty property, JToken value, TargetRow row)
        {
            if (value != null && value.Type == JTokenType.String && property.propertyType != SerializedPropertyType.String)
            {
                string text = value.ToString();
                if (text.StartsWith("=", StringComparison.Ordinal))
                    return new JValue(EvaluateExpression(text.Substring(1), row.NumericVariables));
            }

            return value;
        }

        static bool TryAssignSerializedProperty(SerializedProperty property, JToken value, out string error)
        {
            error = null;
            try
            {
                switch (property.propertyType)
                {
                    case SerializedPropertyType.Boolean:
                        property.boolValue = value != null && value.Type != JTokenType.Null && value.Value<bool>();
                        return true;
                    case SerializedPropertyType.Integer:
                        property.intValue = value == null || value.Type == JTokenType.Null ? 0 : Convert.ToInt32(Math.Round(value.Value<double>(), MidpointRounding.AwayFromZero));
                        return true;
                    case SerializedPropertyType.Float:
                        property.floatValue = value == null || value.Type == JTokenType.Null ? 0f : value.Value<float>();
                        return true;
                    case SerializedPropertyType.String:
                        property.stringValue = value == null || value.Type == JTokenType.Null ? null : value.ToString();
                        return true;
                    case SerializedPropertyType.Color:
                        if (TryParseColor(value, out Color color))
                        {
                            property.colorValue = color;
                            return true;
                        }

                        error = "Expected a color object with r/g/b/a or an array [r,g,b,a].";
                        return false;
                    case SerializedPropertyType.ObjectReference:
                        if (SceneTools.TryResolveObjectReference(value, out UnityEngine.Object resolved, out error))
                        {
                            property.objectReferenceValue = resolved;
                            return true;
                        }

                        return false;
                    case SerializedPropertyType.Enum:
                        if (TryParseEnum(property, value, out int enumIndex, out error))
                        {
                            property.enumValueIndex = enumIndex;
                            return true;
                        }

                        return false;
                    case SerializedPropertyType.Vector2:
                        if (TryParseVector2(value, out Vector2 vector2))
                        {
                            property.vector2Value = vector2;
                            return true;
                        }

                        error = "Expected a Vector2 object {x,y} or array [x,y].";
                        return false;
                    case SerializedPropertyType.Vector3:
                        if (TryParseVector3(value, out Vector3 vector3))
                        {
                            property.vector3Value = vector3;
                            return true;
                        }

                        error = "Expected a Vector3 object {x,y,z} or array [x,y,z].";
                        return false;
                    case SerializedPropertyType.Vector2Int:
                        if (TryParseVector2(value, out Vector2 vector2Int))
                        {
                            property.vector2IntValue = new Vector2Int(Convert.ToInt32(Math.Round(vector2Int.x)), Convert.ToInt32(Math.Round(vector2Int.y)));
                            return true;
                        }

                        error = "Expected a Vector2Int object {x,y} or array [x,y].";
                        return false;
                    case SerializedPropertyType.Vector3Int:
                        if (TryParseVector3(value, out Vector3 vector3Int))
                        {
                            property.vector3IntValue = new Vector3Int(Convert.ToInt32(Math.Round(vector3Int.x)), Convert.ToInt32(Math.Round(vector3Int.y)), Convert.ToInt32(Math.Round(vector3Int.z)));
                            return true;
                        }

                        error = "Expected a Vector3Int object {x,y,z} or array [x,y,z].";
                        return false;
                    default:
                        error = $"Serialized property type '{property.propertyType}' is not supported by Unity.Scene.ApplyBulkMutation.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static Vector3 GetTransformVector(Transform transform, string target)
        {
            return target.ToLowerInvariant() switch
            {
                "position" => transform.position,
                "localposition" or "local_position" => transform.localPosition,
                "rotationeuler" or "rotation" or "eulerangles" => transform.eulerAngles,
                "localrotationeuler" or "local_rotation" or "localeulerangles" => transform.localEulerAngles,
                "localscale" or "local_scale" or "scale" => transform.localScale,
                _ => transform.localPosition
            };
        }

        static void SetTransformVector(Transform transform, string target, Vector3 value)
        {
            switch (target.ToLowerInvariant())
            {
                case "position":
                    transform.position = value;
                    break;
                case "rotationeuler":
                case "rotation":
                case "eulerangles":
                    transform.eulerAngles = value;
                    break;
                case "localrotationeuler":
                case "local_rotation":
                case "localeulerangles":
                    transform.localEulerAngles = value;
                    break;
                case "localscale":
                case "local_scale":
                case "scale":
                    transform.localScale = value;
                    break;
                default:
                    transform.localPosition = value;
                    break;
            }
        }

        static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                Mathf.Approximately(left.y, right.y) &&
                Mathf.Approximately(left.z, right.z);
        }

        static string DescribeProperty(SerializedProperty property)
        {
            return property.propertyType switch
            {
                SerializedPropertyType.Boolean => property.boolValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Integer => property.intValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Float => property.floatValue.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Color => $"rgba({property.colorValue.r:0.###},{property.colorValue.g:0.###},{property.colorValue.b:0.###},{property.colorValue.a:0.###})",
                SerializedPropertyType.ObjectReference => property.objectReferenceValue == null ? "null" : property.objectReferenceValue.name,
                SerializedPropertyType.Enum => property.enumNames != null && property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                    ? property.enumNames[property.enumValueIndex]
                    : property.enumValueIndex.ToString(CultureInfo.InvariantCulture),
                SerializedPropertyType.Vector2 => property.vector2Value.ToString("F3"),
                SerializedPropertyType.Vector3 => property.vector3Value.ToString("F3"),
                SerializedPropertyType.Vector2Int => property.vector2IntValue.ToString(),
                SerializedPropertyType.Vector3Int => property.vector3IntValue.ToString(),
                _ => property.displayName
            };
        }

        static string DescribeRequestedValue(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
                return "null";
            return value.Type == JTokenType.String ? value.ToString() : value.ToString(Formatting.None);
        }

        static object DescribeRawValue(object value)
        {
            if (value == null)
                return null;
            if (TryConvertVector3(value, out Vector3 vector))
                return ToVectorObject(vector);
            return value;
        }

        static object ToVectorObject(Vector3 vector)
        {
            return new { x = vector.x, y = vector.y, z = vector.z };
        }

        static string NormalizeKind(string kind)
        {
            string normalized = (kind ?? string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).Trim().ToLowerInvariant();
            return normalized switch
            {
                "serialized" or "property" or "component" => "serializedproperty",
                "sprite" or "renderer" => "spriterenderer",
                _ => normalized
            };
        }

        static bool IsAnyMatch(string componentMatch)
        {
            return string.Equals(componentMatch, "any", StringComparison.OrdinalIgnoreCase);
        }

        static double EvaluateExpression(string expression, Dictionary<string, double> variables)
        {
            return new NumericExpressionParser(expression, variables).Parse();
        }

        static FieldVariableSpec[] ParseFieldVariables(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return Array.Empty<FieldVariableSpec>();

            if (token is not JArray array)
                return Array.Empty<FieldVariableSpec>();

            return array
                .OfType<JObject>()
                .Select(ParseFieldVariable)
                .Where(spec => spec != null)
                .ToArray();
        }

        static FieldVariableSpec ParseFieldVariable(JObject obj)
        {
            if (obj == null)
                return null;

            return new FieldVariableSpec
            {
                Name = GetString(obj, "name", "Name", "key", "Key"),
                ComponentType = GetString(obj, "componentType", "ComponentType"),
                ComponentIndex = Math.Max(0, GetInt(obj, 0, "componentIndex", "ComponentIndex")),
                PropertyPath = GetString(obj, "propertyPath", "PropertyPath", "field", "Field", "fieldName", "FieldName")
            };
        }

        static JObject[] GetObjectArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token is not JArray array)
                return Array.Empty<JObject>();

            return array.OfType<JObject>().ToArray();
        }

        static string[] GetStringArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token is JArray array)
            {
                return array
                    .Select(item => item?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            if (token != null && token.Type != JTokenType.Null)
                return new[] { token.ToString() };

            return Array.Empty<string>();
        }

        static JToken GetToken(JObject obj, params string[] names)
        {
            if (obj == null)
                return null;

            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken value))
                    return value;
            }

            return null;
        }

        static string GetString(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? null : token.ToString();
        }

        static bool GetBool(JObject obj, bool fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<bool>();
        }

        static int GetInt(JObject obj, int fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token == null || token.Type == JTokenType.Null ? fallback : token.Value<int>();
        }

        static bool TryConvertNumber(object value, out double number)
        {
            try
            {
                switch (value)
                {
                    case byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal:
                        number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        return true;
                    case bool boolValue:
                        number = boolValue ? 1d : 0d;
                        return true;
                    case string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                        number = parsed;
                        return true;
                    default:
                        number = 0d;
                        return false;
                }
            }
            catch
            {
                number = 0d;
                return false;
            }
        }

        static bool TryConvertVector3(object value, out Vector3 vector)
        {
            switch (value)
            {
                case Vector3 vector3:
                    vector = vector3;
                    return true;
                case Vector2 vector2:
                    vector = new Vector3(vector2.x, vector2.y, 0f);
                    return true;
                case Vector3Int vector3Int:
                    vector = new Vector3(vector3Int.x, vector3Int.y, vector3Int.z);
                    return true;
                case Vector2Int vector2Int:
                    vector = new Vector3(vector2Int.x, vector2Int.y, 0f);
                    return true;
                case JObject obj when TryParseVector3(obj, out Vector3 parsed):
                    vector = parsed;
                    return true;
                default:
                    vector = default;
                    return false;
            }
        }

        static bool TryParseVector2(JToken value, out Vector2 vector)
        {
            vector = default;
            if (value == null || value.Type == JTokenType.Null)
                return false;

            if (value is JArray array && array.Count >= 2)
            {
                vector = new Vector2(array[0].Value<float>(), array[1].Value<float>());
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out JToken x) &&
                obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out JToken y))
            {
                vector = new Vector2(x.Value<float>(), y.Value<float>());
                return true;
            }

            return false;
        }

        static bool TryParseVector3(JToken value, out Vector3 vector)
        {
            vector = default;
            if (value == null || value.Type == JTokenType.Null)
                return false;

            if (value is JArray array && array.Count >= 2)
            {
                vector = new Vector3(
                    array[0].Value<float>(),
                    array[1].Value<float>(),
                    array.Count > 2 ? array[2].Value<float>() : 0f);
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("x", StringComparison.OrdinalIgnoreCase, out JToken x) &&
                obj.TryGetValue("y", StringComparison.OrdinalIgnoreCase, out JToken y))
            {
                obj.TryGetValue("z", StringComparison.OrdinalIgnoreCase, out JToken z);
                vector = new Vector3(x.Value<float>(), y.Value<float>(), z?.Value<float>() ?? 0f);
                return true;
            }

            return false;
        }

        static bool TryParseColor(JToken value, out Color color)
        {
            color = default;
            if (value == null || value.Type == JTokenType.Null)
                return false;

            if (value is JArray array && array.Count >= 3)
            {
                color = new Color(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>(), array.Count > 3 ? array[3].Value<float>() : 1f);
                return true;
            }

            if (value is JObject obj &&
                obj.TryGetValue("r", StringComparison.OrdinalIgnoreCase, out JToken r) &&
                obj.TryGetValue("g", StringComparison.OrdinalIgnoreCase, out JToken g) &&
                obj.TryGetValue("b", StringComparison.OrdinalIgnoreCase, out JToken b))
            {
                obj.TryGetValue("a", StringComparison.OrdinalIgnoreCase, out JToken a);
                color = new Color(r.Value<float>(), g.Value<float>(), b.Value<float>(), a?.Value<float>() ?? 1f);
                return true;
            }

            return false;
        }

        static bool TryParseEnum(SerializedProperty property, JToken value, out int enumIndex, out string error)
        {
            enumIndex = 0;
            error = null;
            if (value == null || value.Type == JTokenType.Null)
                return true;

            if (value.Type == JTokenType.Integer)
            {
                enumIndex = Mathf.Clamp(value.Value<int>(), 0, Math.Max(0, property.enumNames.Length - 1));
                return true;
            }

            string name = value.ToString();
            int index = Array.FindIndex(property.enumNames, enumName => string.Equals(enumName, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                enumIndex = index;
                return true;
            }

            error = $"Enum value '{name}' was not found. Valid values: {string.Join(", ", property.enumNames)}.";
            return false;
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            JArray rows = root["rows"] as JArray ?? new JArray();
            return new
            {
                status = root["status"],
                previewOnly = root["previewOnly"],
                applied = root["applied"],
                allowPartial = root["allowPartial"],
                scene = root["scene"],
                componentTypes = root["componentTypes"],
                componentMatch = root["componentMatch"],
                selectedCount = root["selectedCount"],
                processedCount = root["processedCount"],
                omittedObjectCount = root["omittedObjectCount"],
                mutationCount = root["mutationCount"],
                wouldChangeCount = root["wouldChangeCount"],
                appliedChangeCount = root["appliedChangeCount"],
                errorCount = root["errorCount"],
                validationErrorCount = root["validationErrorCount"],
                blockedByErrors = root["blockedByErrors"],
                saveState = root["saveState"],
                rows = rows.Take(25).ToArray(),
                compactOmittedRowCount = Math.Max(0, rows.Count - 25)
            };
        }

        sealed class NumericExpressionParser
        {
            readonly string m_Text;
            readonly Dictionary<string, double> m_Variables;
            int m_Index;

            public NumericExpressionParser(string text, Dictionary<string, double> variables)
            {
                m_Text = text ?? string.Empty;
                m_Variables = variables ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            }

            public double Parse()
            {
                double value = ParseExpression();
                SkipWhitespace();
                if (m_Index < m_Text.Length)
                    throw new FormatException($"Unexpected token '{m_Text[m_Index]}' in expression '{m_Text}'.");
                return value;
            }

            double ParseExpression()
            {
                double value = ParseTerm();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('+'))
                        value += ParseTerm();
                    else if (Match('-'))
                        value -= ParseTerm();
                    else
                        return value;
                }
            }

            double ParseTerm()
            {
                double value = ParseUnary();
                while (true)
                {
                    SkipWhitespace();
                    if (Match('*'))
                        value *= ParseUnary();
                    else if (Match('/'))
                        value /= ParseUnary();
                    else
                        return value;
                }
            }

            double ParseUnary()
            {
                SkipWhitespace();
                if (Match('+'))
                    return ParseUnary();
                if (Match('-'))
                    return -ParseUnary();
                return ParsePrimary();
            }

            double ParsePrimary()
            {
                SkipWhitespace();
                if (Match('('))
                {
                    double value = ParseExpression();
                    Expect(')');
                    return value;
                }

                if (m_Index < m_Text.Length && (char.IsDigit(m_Text[m_Index]) || m_Text[m_Index] == '.'))
                    return ParseNumber();

                string identifier = ParseIdentifier();
                if (string.IsNullOrWhiteSpace(identifier))
                    throw new FormatException($"Expected number or variable in expression '{m_Text}'.");

                SkipWhitespace();
                if (Match('('))
                {
                    var args = new List<double>();
                    SkipWhitespace();
                    if (!Peek(')'))
                    {
                        do
                        {
                            args.Add(ParseExpression());
                            SkipWhitespace();
                        } while (Match(','));
                    }

                    Expect(')');
                    return EvaluateFunction(identifier, args);
                }

                if (m_Variables.TryGetValue(identifier, out double value))
                    return value;

                throw new FormatException($"Unknown expression variable '{identifier}'.");
            }

            double ParseNumber()
            {
                int start = m_Index;
                while (m_Index < m_Text.Length && (char.IsDigit(m_Text[m_Index]) || m_Text[m_Index] == '.' || m_Text[m_Index] == 'e' || m_Text[m_Index] == 'E' || m_Text[m_Index] == '+' || m_Text[m_Index] == '-'))
                {
                    if ((m_Text[m_Index] == '+' || m_Text[m_Index] == '-') && m_Index > start && m_Text[m_Index - 1] != 'e' && m_Text[m_Index - 1] != 'E')
                        break;
                    m_Index++;
                }

                return double.Parse(m_Text.Substring(start, m_Index - start), CultureInfo.InvariantCulture);
            }

            string ParseIdentifier()
            {
                int start = m_Index;
                while (m_Index < m_Text.Length && (char.IsLetterOrDigit(m_Text[m_Index]) || m_Text[m_Index] == '_' || m_Text[m_Index] == '.'))
                    m_Index++;
                return m_Text.Substring(start, m_Index - start);
            }

            double EvaluateFunction(string name, List<double> args)
            {
                return name.ToLowerInvariant() switch
                {
                    "abs" when args.Count == 1 => Math.Abs(args[0]),
                    "floor" when args.Count == 1 => Math.Floor(args[0]),
                    "ceil" when args.Count == 1 => Math.Ceiling(args[0]),
                    "round" when args.Count == 1 => Math.Round(args[0], MidpointRounding.AwayFromZero),
                    "min" when args.Count >= 1 => args.Min(),
                    "max" when args.Count >= 1 => args.Max(),
                    "clamp" when args.Count == 3 => Math.Min(Math.Max(args[0], args[1]), args[2]),
                    _ => throw new FormatException($"Unsupported expression function '{name}'.")
                };
            }

            bool Match(char expected)
            {
                if (m_Index < m_Text.Length && m_Text[m_Index] == expected)
                {
                    m_Index++;
                    return true;
                }

                return false;
            }

            bool Peek(char expected)
            {
                return m_Index < m_Text.Length && m_Text[m_Index] == expected;
            }

            void Expect(char expected)
            {
                if (!Match(expected))
                    throw new FormatException($"Expected '{expected}' in expression '{m_Text}'.");
            }

            void SkipWhitespace()
            {
                while (m_Index < m_Text.Length && char.IsWhiteSpace(m_Text[m_Index]))
                    m_Index++;
            }
        }
    }
}
