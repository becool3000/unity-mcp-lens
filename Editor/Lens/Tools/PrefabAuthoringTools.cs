#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PrefabAuthoringTools
    {
        const string InspectToolName = "Unity.Prefab.Inspect";
        const string InstantiateToolName = "Unity.Prefab.Instantiate";
        const string CreateFromSceneObjectToolName = "Unity.Prefab.CreateFromSceneObject";
        const string GetOverridesToolName = "Unity.Prefab.GetOverrides";
        const string ExplainOverridesToolName = "Unity.Prefab.ExplainOverrides";
        const string PreviewApplyOverridesToolName = "Unity.Prefab.PreviewApplyOverrides";
        const string ApplyOverridesToolName = "Unity.Prefab.ApplyOverrides";
        const string PreviewRevertOverridesToolName = "Unity.Prefab.PreviewRevertOverrides";
        const string RevertOverridesToolName = "Unity.Prefab.RevertOverrides";

        const string AssetsGroup = "assets";

        sealed class PrefabOverrideCandidate
        {
            public string Id;
            public string Classification;
            public string TargetPath;
            public string PropertyPath;
            public string SourceAssetPath;
            public bool IsNested;
            public PropertyModification Modification;
            public Object InstanceTarget;
            public object Row;
        }

        [McpSchema(InspectToolName)]
        public static object GetInspectSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    prefabPath = new { type = "string", description = "Prefab asset path under Assets/ to inspect." },
                    target = new { description = "Scene prefab instance GameObject target, path, or instance id to inspect." },
                    searchMethod = new { type = "string", description = "How to find target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving target. Defaults to true." },
                    includeComponents = new { type = "boolean", description = "Include component summaries. Defaults to true." },
                    includeOverrides = new { type = "boolean", description = "Include prefab instance override rows when target is a scene instance. Defaults to false." },
                    maxRows = new { type = "integer", description = "Maximum hierarchy/override rows to include inline. Defaults to 200." }
                }
            };
        }

        [McpSchema(InstantiateToolName)]
        public static object GetInstantiateSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    prefabPath = new { type = "string", description = "Prefab asset path under Assets/." },
                    instanceName = new { type = "string", description = "Optional scene instance name. Defaults to the prefab name." },
                    parent = new { description = "Optional scene parent GameObject target, path, or instance id." },
                    parentSearchMethod = new { type = "string", description = "How to find parent. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving parent. Defaults to true." },
                    position = new { description = "Local position as {x,y,z} or [x,y,z]." },
                    rotation = new { description = "Local Euler rotation as {x,y,z} or [x,y,z]." },
                    scale = new { description = "Local scale as {x,y,z} or [x,y,z]." }
                },
                required = new[] { "prefabPath" }
            };
        }

        [McpSchema(CreateFromSceneObjectToolName)]
        public static object GetCreateFromSceneObjectSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { description = "Scene GameObject target, path, or instance id to save as a prefab asset." },
                    searchMethod = new { type = "string", description = "How to find target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving target. Defaults to true." },
                    prefabPath = new { type = "string", description = "Destination prefab asset path under Assets/." },
                    connect = new { type = "boolean", description = "Connect the scene object to the saved prefab asset. Defaults to true." },
                    overwrite = new { type = "boolean", description = "Allow replacing an existing prefab asset. Defaults to false." }
                },
                required = new[] { "target", "prefabPath" }
            };
        }

        [McpSchema(GetOverridesToolName)]
        public static object GetOverridesSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { description = "Scene prefab instance GameObject target, path, or instance id." },
                    searchMethod = new { type = "string", description = "How to find target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving target. Defaults to true." },
                    includeInherited = new { type = "boolean", description = "Include inherited object/component summary rows. Defaults to true." },
                    includeNested = new { type = "boolean", description = "Include nested prefab override rows. Defaults to true." },
                    maxOverrides = new { type = "integer", description = "Maximum override rows to return inline. Defaults to 200." }
                },
                required = new[] { "target" }
            };
        }

        [McpSchema(ExplainOverridesToolName)]
        public static object GetExplainOverridesSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    target = new { description = "Scene prefab instance GameObject target, path, or instance id." },
                    searchMethod = new { type = "string", description = "How to find target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects when resolving target. Defaults to true." },
                    action = new { type = "string", @enum = new[] { "apply", "revert", "both" }, description = "Which override action to explain. Defaults to both." },
                    overrideIds = new { type = "array", items = new { type = "string" }, description = "Override ids returned by Unity.Prefab.GetOverrides or preview tools." },
                    propertyPaths = new { type = "array", items = new { type = "string" }, description = "Serialized property paths to select." },
                    targetPaths = new { type = "array", items = new { type = "string" }, description = "Override target paths to select." },
                    includeNested = new { type = "boolean", description = "Allow selected nested prefab overrides. Defaults to false, matching mutation tools." },
                    applyAll = new { type = "boolean", description = "Explicitly select every local override for apply explanation." },
                    revertAll = new { type = "boolean", description = "Explicitly select every local override for revert explanation." },
                    maxOverrides = new { type = "integer", description = "Maximum override rows to include inline. Clamped to 1..500, defaults to 200." }
                },
                required = new[] { "target" }
            };
        }

        [McpSchema(PreviewApplyOverridesToolName)]
        public static object GetPreviewApplyOverridesSchema() => GetOverrideMutationSchema("applyAll");

        [McpSchema(ApplyOverridesToolName)]
        public static object GetApplyOverridesSchema() => GetOverrideMutationSchema("applyAll");

        [McpSchema(PreviewRevertOverridesToolName)]
        public static object GetPreviewRevertOverridesSchema() => GetOverrideMutationSchema("revertAll");

        [McpSchema(RevertOverridesToolName)]
        public static object GetRevertOverridesSchema() => GetOverrideMutationSchema("revertAll");

        static object GetOverrideMutationSchema(string broadProperty)
        {
            return new
            {
                type = "object",
                properties = new Dictionary<string, object>
                {
                    ["target"] = new { description = "Scene prefab instance GameObject target, path, or instance id." },
                    ["searchMethod"] = new { type = "string", description = "How to find target. Defaults to by_id_or_name_or_path." },
                    ["includeInactive"] = new { type = "boolean", description = "Include inactive scene objects when resolving target. Defaults to true." },
                    ["overrideIds"] = new { type = "array", items = new { type = "string" }, description = "Override ids returned by Unity.Prefab.GetOverrides or preview tools." },
                    ["propertyPaths"] = new { type = "array", items = new { type = "string" }, description = "Serialized property paths to select." },
                    ["targetPaths"] = new { type = "array", items = new { type = "string" }, description = "Override target paths to select." },
                    ["includeNested"] = new { type = "boolean", description = "Allow selected nested prefab overrides. Defaults to false for mutation tools." },
                    [broadProperty] = new { type = "boolean", description = "Explicitly select every local override on the prefab instance." }
                },
                required = new[] { "target" }
            };
        }

        [McpTool(InspectToolName, "Inspects a prefab asset or scene prefab instance hierarchy, components, variants, and nested prefab boundaries.", "Inspect Prefab", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object Inspect(JObject @params)
        {
            return Handle(InspectToolName, "inspect", @params, ExecuteInspect);
        }

        [McpTool(InstantiateToolName, "Instantiates a prefab asset into a loaded scene and reports scene dirty state without saving the scene.", "Instantiate Prefab", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object Instantiate(JObject @params)
        {
            return Handle(InstantiateToolName, "instantiate", @params, ExecuteInstantiate);
        }

        [McpTool(CreateFromSceneObjectToolName, "Creates a prefab asset from a scene object. This is an explicit prefab asset save by tool contract.", "Create Prefab From Scene Object", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object CreateFromSceneObject(JObject @params)
        {
            return Handle(CreateFromSceneObjectToolName, "create_from_scene_object", @params, ExecuteCreateFromSceneObject);
        }

        [McpTool(GetOverridesToolName, "Lists inherited prefab content and local prefab instance overrides with risk classifications.", "Get Prefab Overrides", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object GetOverrides(JObject @params)
        {
            return Handle(GetOverridesToolName, "get_overrides", @params, ExecuteGetOverrides);
        }

        [McpTool(ExplainOverridesToolName, "Explains prefab instance connection state, override actionability, and why apply/revert is available or blocked without mutating scenes or assets.", "Explain Prefab Overrides", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object ExplainOverrides(JObject @params)
        {
            return Handle(ExplainOverridesToolName, "explain_overrides", @params, ExecuteExplainOverrides);
        }

        [McpTool(PreviewApplyOverridesToolName, "Previews applying selected prefab instance overrides to prefab assets and reports broad/nested risks before mutation.", "Preview Apply Prefab Overrides", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object PreviewApplyOverrides(JObject @params)
        {
            return Handle(PreviewApplyOverridesToolName, "preview_apply_overrides", @params, p => ExecuteOverrideMutation(p, previewOnly: true, applyToAsset: true));
        }

        [McpTool(ApplyOverridesToolName, "Applies selected prefab instance property overrides to prefab assets. This persists prefab asset changes by explicit tool contract.", "Apply Prefab Overrides", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object ApplyOverrides(JObject @params)
        {
            return Handle(ApplyOverridesToolName, "apply_overrides", @params, p => ExecuteOverrideMutation(p, previewOnly: false, applyToAsset: true));
        }

        [McpTool(PreviewRevertOverridesToolName, "Previews reverting selected prefab instance overrides and reports broad/nested risks before mutation.", "Preview Revert Prefab Overrides", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object PreviewRevertOverrides(JObject @params)
        {
            return Handle(PreviewRevertOverridesToolName, "preview_revert_overrides", @params, p => ExecuteOverrideMutation(p, previewOnly: true, applyToAsset: false));
        }

        [McpTool(RevertOverridesToolName, "Reverts selected prefab instance property overrides in the scene without saving the scene.", "Revert Prefab Overrides", Groups = new[] { AssetsGroup }, EnabledByDefault = true)]
        public static object RevertOverrides(JObject @params)
        {
            return Handle(RevertOverridesToolName, "revert_overrides", @params, p => ExecuteOverrideMutation(p, previewOnly: false, applyToAsset: false));
        }

        static object Handle(
            string toolName,
            string operation,
            JObject parameters,
            Func<JObject, (bool success, string message, object data, string errorKind)> execute)
        {
            parameters ??= new JObject();
            var timing = new ToolOperationTiming(toolName, operation, PayloadBudgeting.GetUtf8ByteCount(parameters.ToString(Formatting.None)));
            bool success = false;
            string message = null;
            string errorKind = null;
            object data = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                }

                using (timing.Measure("service"))
                {
                    (success, message, data, errorKind) = execute(parameters);
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                message = $"Prefab authoring operation failed: {ex.Message}";
                data = new
                {
                    status = "failed",
                    errorKind,
                    error = ex.Message,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                };
            }

            object response;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(message, ToolResultCompactor.ShapeStructuredPayload(
                        toolName,
                        data,
                        BuildCompactData(data),
                        new { kind = "prefab_authoring_full_result" },
                        "prefab_authoring",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(message ?? "Prefab authoring operation failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static (bool success, string message, object data, string errorKind) ExecuteInspect(JObject parameters)
        {
            string prefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath", "path", "Path"));
            JToken target = GetToken(parameters, "target", "Target");
            bool includeComponents = GetBool(parameters, true, "includeComponents", "IncludeComponents");
            bool includeOverrides = GetBool(parameters, false, "includeOverrides", "IncludeOverrides");
            int maxRows = GetInt(parameters, 200, "maxRows", "MaxRows");

            if (!string.IsNullOrWhiteSpace(prefabPath))
                return InspectPrefabAsset(prefabPath, includeComponents, maxRows);

            if (target == null)
                return Failure("TARGET_REQUIRED", "prefabPath or target is required.", new { status = "failed" });

            if (!TryResolveSceneTarget(parameters, out GameObject targetObject, out string error))
                return Failure("TARGET_NOT_FOUND", error, new { status = "failed", error });

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);
            if (instanceRoot == null)
            {
                return Failure("NOT_PREFAB_INSTANCE", $"Target '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}' is not part of a prefab instance.", new
                {
                    status = "failed",
                    target = DescribeGameObject(targetObject, ".")
                });
            }

            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            var warnings = new List<string>();
            object[] hierarchy = DescribeHierarchy(instanceRoot, includeComponents, maxRows, out int omittedHierarchyRows);
            object[] overrides = includeOverrides
                ? BuildOverrideRows(instanceRoot, includeInherited: false, includeNested: true, maxRows, warnings, out _)
                : Array.Empty<object>();

            object data = new
            {
                status = "inspected",
                mode = "prefab_instance",
                target = DescribeGameObject(targetObject, "."),
                instanceRoot = DescribeGameObject(instanceRoot, "."),
                sourcePrefabPath,
                prefabState = CapturePrefabState(sourcePrefabPath),
                assetState = CaptureAssetState(sourcePrefabPath),
                dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes(),
                hierarchy,
                hierarchyRowCount = hierarchy.Length,
                omittedHierarchyRows,
                overrides,
                overrideRowCount = overrides.Length,
                warnings = warnings.ToArray()
            };

            return (true, $"Inspected prefab instance '{UiDiagnosticsHelper.GetHierarchyPath(instanceRoot.transform)}'.", data, null);
        }

        static (bool success, string message, object data, string errorKind) InspectPrefabAsset(string prefabPath, bool includeComponents, int maxRows)
        {
            if (!IsPrefabAssetPath(prefabPath))
                return Failure("INVALID_PREFAB_PATH", $"prefabPath must point to a .prefab asset under Assets/. Received '{prefabPath}'.", new { status = "failed", prefabPath });

            GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (asset == null)
                return Failure("PREFAB_NOT_FOUND", $"Prefab asset '{prefabPath}' could not be loaded.", new { status = "failed", prefabPath });

            GameObject prefabRoot = null;
            try
            {
                prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
                object[] hierarchy = DescribeHierarchy(prefabRoot, includeComponents, maxRows, out int omittedHierarchyRows);
                object data = new
                {
                    status = "inspected",
                    mode = "prefab_asset",
                    prefabPath,
                    prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                    prefabAssetType = PrefabUtility.GetPrefabAssetType(asset).ToString(),
                    isVariant = PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant,
                    prefabState = CapturePrefabState(prefabPath),
                    assetState = CaptureAssetState(prefabPath),
                    hierarchy,
                    hierarchyRowCount = hierarchy.Length,
                    omittedHierarchyRows,
                    warnings = Array.Empty<string>()
                };

                return (true, $"Inspected prefab asset '{prefabPath}'.", data, null);
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static (bool success, string message, object data, string errorKind) ExecuteInstantiate(JObject parameters)
        {
            string prefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath"));
            if (!IsPrefabAssetPath(prefabPath))
                return Failure("INVALID_PREFAB_PATH", "prefabPath must point to a .prefab asset under Assets/.", new { status = "failed", prefabPath });

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                return Failure("PREFAB_NOT_FOUND", $"Prefab asset '{prefabPath}' could not be loaded.", new { status = "failed", prefabPath });

            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            object prefabStateBefore = CapturePrefabState(prefabPath);
            object assetStateBefore = CaptureAssetState(prefabPath);
            Transform parent = null;
            JToken parentToken = GetToken(parameters, "parent", "Parent");
            if (parentToken != null && parentToken.Type != JTokenType.Null)
            {
                string parentSearchMethod = GetString(parameters, "parentSearchMethod", "ParentSearchMethod") ?? "by_id_or_name_or_path";
                bool includeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive");
                JObject findParams = new()
                {
                    ["search_inactive"] = includeInactive
                };
                GameObject parentObject = ObjectsHelper.FindObject(parentToken, parentSearchMethod, findParams);
                if (parentObject == null)
                {
                    return Failure("PARENT_NOT_FOUND", "Parent target could not be resolved.", new
                    {
                        status = "failed",
                        prefabPath,
                        dirtyStateBefore,
                        dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                        saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                    });
                }

                parent = parentObject.transform;
            }

            Object createdObject = parent != null
                ? PrefabUtility.InstantiatePrefab(prefab, parent)
                : PrefabUtility.InstantiatePrefab(prefab);
            GameObject instanceRoot = createdObject as GameObject;
            if (instanceRoot == null)
                return Failure("INSTANTIATE_FAILED", $"Failed to instantiate prefab '{prefabPath}'.", new { status = "failed", prefabPath });

            string instanceName = GetString(parameters, "instanceName", "InstanceName", "name", "Name");
            if (!string.IsNullOrWhiteSpace(instanceName))
                instanceRoot.name = instanceName.Trim();

            var transformChanges = new List<object>();
            if (!TryApplyTransform(instanceRoot.transform, parameters, transformChanges, out string transformError))
            {
                Object.DestroyImmediate(instanceRoot);
                return Failure("INVALID_TRANSFORM", transformError, new { status = "failed", prefabPath });
            }

            EditorUtility.SetDirty(instanceRoot);
            SceneDirtyStateUtility.MarkSceneDirty(instanceRoot);

            object data = new
            {
                status = "instantiated",
                prefabPath,
                prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                changedObjects = new[] { DescribeGameObject(instanceRoot, ".") },
                addedObjects = new[] { DescribeGameObject(instanceRoot, ".") },
                transformChanges = transformChanges.ToArray(),
                parent = parent != null ? DescribeGameObject(parent.gameObject, ".") : null,
                warnings = Array.Empty<string>(),
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabStateBefore,
                prefabStateAfter = CapturePrefabState(prefabPath),
                assetStateBefore,
                assetStateAfter = CaptureAssetState(prefabPath),
                saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
            };

            return (true, $"Instantiated prefab '{prefabPath}' into the scene.", data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecuteCreateFromSceneObject(JObject parameters)
        {
            if (!TryResolveSceneTarget(parameters, out GameObject targetObject, out string error))
                return Failure("TARGET_NOT_FOUND", error, new { status = "failed", error });

            string prefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath"));
            if (!IsPrefabAssetPath(prefabPath))
                return Failure("INVALID_PREFAB_PATH", "prefabPath must point to a .prefab asset under Assets/.", new { status = "failed", prefabPath });

            bool overwrite = GetBool(parameters, false, "overwrite", "Overwrite");
            bool connect = GetBool(parameters, true, "connect", "Connect");
            Object existing = AssetDatabase.LoadMainAssetAtPath(prefabPath);
            if (existing != null && !overwrite)
            {
                return Failure("PREFAB_EXISTS", $"Prefab asset '{prefabPath}' already exists. Set overwrite=true to replace it.", new
                {
                    status = "failed",
                    prefabPath,
                    existing = DescribeObject(existing)
                });
            }

            EnsureAssetDirectory(prefabPath);
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            object prefabStateBefore = CapturePrefabState(prefabPath);
            object assetStateBefore = CaptureAssetState(prefabPath);

            GameObject savedPrefab = connect
                ? PrefabUtility.SaveAsPrefabAssetAndConnect(targetObject, prefabPath, InteractionMode.UserAction)
                : PrefabUtility.SaveAsPrefabAsset(targetObject, prefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (connect)
                SceneDirtyStateUtility.MarkSceneDirty(targetObject);

            object data = new
            {
                status = "created",
                prefabPath,
                prefabGuid = AssetDatabase.AssetPathToGUID(prefabPath),
                target = DescribeGameObject(targetObject, "."),
                changedObjects = new[] { DescribeGameObject(targetObject, ".") },
                createdPrefab = DescribeObject(savedPrefab ?? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath)),
                connectedSceneObject = connect,
                overwritten = existing != null,
                warnings = existing != null ? new[] { "Existing prefab asset was overwritten by explicit request." } : Array.Empty<string>(),
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabStateBefore,
                prefabStateAfter = CapturePrefabState(prefabPath),
                assetStateBefore,
                assetStateAfter = CaptureAssetState(prefabPath),
                saveState = BuildAssetSaveState(
                    requested: true,
                    attempted: true,
                    saved: true,
                    savedAssets: new object[] { CaptureAssetState(prefabPath) },
                    message: "prefab_asset_saved_by_tool_contract")
            };

            return (true, $"Created prefab asset '{prefabPath}' from scene object '{targetObject.name}'.", data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecuteGetOverrides(JObject parameters)
        {
            if (!TryResolvePrefabInstance(parameters, out GameObject targetObject, out GameObject instanceRoot, out string sourcePrefabPath, out string error))
                return Failure("PREFAB_INSTANCE_REQUIRED", error, new { status = "failed", error });

            bool includeInherited = GetBool(parameters, true, "includeInherited", "IncludeInherited");
            bool includeNested = GetBool(parameters, true, "includeNested", "IncludeNested");
            int maxOverrides = GetInt(parameters, 200, "maxOverrides", "MaxOverrides", "maxRows", "MaxRows");
            var warnings = new List<string>();
            object[] overrides = BuildOverrideRows(instanceRoot, includeInherited, includeNested, maxOverrides, warnings, out int totalLocalOverrideCount);
            object data = new
            {
                status = "inspected",
                target = DescribeGameObject(targetObject, "."),
                instanceRoot = DescribeGameObject(instanceRoot, "."),
                sourcePrefabPath,
                totalLocalOverrideCount,
                returnedOverrideCount = overrides.Length,
                classificationCounts = CountClassifications(overrides),
                overrides,
                warnings = warnings.ToArray(),
                dirtyState = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabState = CapturePrefabState(sourcePrefabPath),
                assetState = CaptureAssetState(sourcePrefabPath),
                saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
            };

            return (true, $"Listed prefab overrides for '{UiDiagnosticsHelper.GetHierarchyPath(instanceRoot.transform)}'.", data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecuteExplainOverrides(JObject parameters)
        {
            if (!TryResolveSceneTarget(parameters, out GameObject targetObject, out string targetError))
                return Failure("PREFAB_INSTANCE_REQUIRED", targetError, new { status = "failed", error = targetError });

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);
            if (instanceRoot == null)
            {
                string error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}' is not part of a prefab instance.";
                return Failure("PREFAB_INSTANCE_REQUIRED", error, new
                {
                    status = "failed",
                    error,
                    target = DescribeGameObject(targetObject, ".")
                });
            }

            int maxOverrides = Mathf.Clamp(GetInt(parameters, 200, "maxOverrides", "MaxOverrides", "maxRows", "MaxRows"), 1, 500);
            bool includeNested = GetBool(parameters, false, "includeNested", "IncludeNested");
            string requestedAction = NormalizeExplainAction(GetString(parameters, "action", "Action"));
            var warnings = new List<string>();
            List<PrefabOverrideCandidate> allCandidates = BuildOverrideCandidates(instanceRoot, includeNested: true, warnings);
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            GameObject sourceAsset = string.IsNullOrWhiteSpace(sourcePrefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(sourcePrefabPath);
            PrefabInstanceStatus instanceStatus = PrefabUtility.GetPrefabInstanceStatus(instanceRoot);
            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(instanceRoot);
            bool sourceAssetExists = sourceAsset != null;
            bool hasExplicitSelectors = HasAnySelector(parameters);

            var selectedById = new Dictionary<string, PrefabOverrideCandidate>(StringComparer.OrdinalIgnoreCase);
            var explanationRows = new List<object>();
            if (requestedAction == "apply" || requestedAction == "both")
            {
                explanationRows.Add(BuildOverrideActionExplanation(
                    parameters,
                    "apply",
                    includeNested,
                    hasExplicitSelectors,
                    allCandidates,
                    sourcePrefabPath,
                    sourceAssetExists,
                    instanceStatus,
                    maxOverrides,
                    selectedById));
            }

            if (requestedAction == "revert" || requestedAction == "both")
            {
                explanationRows.Add(BuildOverrideActionExplanation(
                    parameters,
                    "revert",
                    includeNested,
                    hasExplicitSelectors,
                    allCandidates,
                    sourcePrefabPath,
                    sourceAssetExists,
                    instanceStatus,
                    maxOverrides,
                    selectedById));
            }

            object[] candidateRows = allCandidates
                .Take(maxOverrides)
                .Select(candidate => candidate.Row)
                .ToArray();
            object[] selectedRows = selectedById.Values
                .Take(maxOverrides)
                .Select(candidate => candidate.Row)
                .ToArray();
            object[] allRows = allCandidates.Select(candidate => candidate.Row).ToArray();
            bool targetIsRoot = ReferenceEquals(targetObject, instanceRoot);
            string targetRole = targetIsRoot
                ? "instance_root"
                : IsNestedTransform(instanceRoot, targetObject) ? "nested_prefab_child" : "instance_child";

            object data = new
            {
                status = "explained",
                readOnly = true,
                target = DescribeGameObject(targetObject, GetRelativePath(instanceRoot.transform, targetObject.transform)),
                instanceRoot = DescribeGameObject(instanceRoot, "."),
                sourcePrefabPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                targetRole,
                connection = new
                {
                    prefabInstanceStatus = instanceStatus.ToString(),
                    prefabAssetType = assetType.ToString(),
                    sourcePrefabPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                    sourceAssetExists,
                    sourceAsset = DescribeObject(sourceAsset),
                    connected = instanceStatus == PrefabInstanceStatus.Connected && sourceAssetExists,
                    targetIsNearestPrefabInstanceRoot = PrefabUtility.IsAnyPrefabInstanceRoot(targetObject),
                    targetIsNestedPrefabBoundary = IsNestedTransform(instanceRoot, targetObject)
                },
                action = requestedAction,
                includeNested,
                hasExplicitSelectors,
                totalLocalOverrideCount = allCandidates.Count,
                returnedOverrideCount = candidateRows.Length,
                selectedOverrideCount = selectedById.Count,
                returnedSelectedOverrideCount = selectedRows.Length,
                nestedOverrideCount = allCandidates.Count(candidate => candidate.IsNested),
                missingReferenceOverrideCount = allCandidates.Count(candidate => string.Equals(candidate.Classification, "missing reference", StringComparison.OrdinalIgnoreCase)),
                localNullOverrideCount = allCandidates.Count(candidate => string.Equals(candidate.Classification, "local null override", StringComparison.OrdinalIgnoreCase)),
                classificationCounts = CountClassifications(allRows),
                truncated = allCandidates.Count > candidateRows.Length || selectedById.Count > selectedRows.Length,
                explanations = explanationRows.ToArray(),
                selectedOverrides = selectedRows,
                candidateOverrides = candidateRows,
                warnings = warnings.ToArray(),
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabStateBefore = CapturePrefabState(sourcePrefabPath),
                prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                assetStateBefore = CaptureAssetState(sourcePrefabPath),
                assetStateAfter = CaptureAssetState(sourcePrefabPath),
                saveState = BuildAssetSaveState(message: "not_requested_read_only_override_explain")
            };

            string message = $"Explained prefab overrides for '{UiDiagnosticsHelper.GetHierarchyPath(instanceRoot.transform)}': {allCandidates.Count} local override(s), {selectedById.Count} selected.";
            return (true, message, data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecuteOverrideMutation(JObject parameters, bool previewOnly, bool applyToAsset)
        {
            if (!TryResolvePrefabInstance(parameters, out GameObject targetObject, out GameObject instanceRoot, out string sourcePrefabPath, out string error))
                return Failure("PREFAB_INSTANCE_REQUIRED", error, new { status = "failed", error });

            bool includeNested = GetBool(parameters, false, "includeNested", "IncludeNested");
            string broadProperty = applyToAsset ? "applyAll" : "revertAll";
            bool broadSelectionRequested = GetBool(parameters, false, broadProperty, ToPascalCase(broadProperty), "all", "All");
            var warnings = new List<string>();
            List<PrefabOverrideCandidate> allCandidates = BuildOverrideCandidates(instanceRoot, includeNested: true, warnings);
            bool hasExplicitSelectors = HasAnySelector(parameters);
            if (!hasExplicitSelectors && !broadSelectionRequested && !previewOnly)
            {
                return Failure("OVERRIDE_SELECTION_REQUIRED", $"Select overrideIds/propertyPaths/targetPaths or set {broadProperty}=true before mutating prefab overrides.", new
                {
                    status = "failed",
                    target = DescribeGameObject(targetObject, "."),
                    instanceRoot = DescribeGameObject(instanceRoot, "."),
                    availableOverrideCount = allCandidates.Count,
                    warnings = new[] { "Broad apply/revert requires an explicit broad-selection flag." },
                    dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    prefabStateBefore = CapturePrefabState(sourcePrefabPath),
                    prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                    assetStateBefore = CaptureAssetState(sourcePrefabPath),
                    assetStateAfter = CaptureAssetState(sourcePrefabPath),
                    saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                });
            }

            List<PrefabOverrideCandidate> selected = SelectOverrideCandidates(parameters, allCandidates, broadSelectionRequested || (!hasExplicitSelectors && previewOnly), warnings, out string selectionError);
            if (!string.IsNullOrWhiteSpace(selectionError))
                return Failure("OVERRIDE_SELECTION_INVALID", selectionError, new { status = "failed", selectionError });

            bool selectedNested = selected.Any(candidate => candidate.IsNested);
            if (selectedNested && !includeNested)
            {
                warnings.Add("Selected overrides include nested prefab boundaries. Set includeNested=true only after reviewing the nested-prefab risk.");
                if (!previewOnly)
                {
                    return Failure("NESTED_PREFAB_OVERRIDE_RISK", "Nested prefab overrides were selected without includeNested=true.", new
                    {
                        status = "failed",
                        selectedOverrides = selected.Select(candidate => candidate.Row).ToArray(),
                        warnings = warnings.ToArray(),
                        dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes(),
                        dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                        prefabStateBefore = CapturePrefabState(sourcePrefabPath),
                        prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                        assetStateBefore = CaptureAssetState(sourcePrefabPath),
                        assetStateAfter = CaptureAssetState(sourcePrefabPath),
                        saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                    });
                }
            }

            if ((broadSelectionRequested || (!hasExplicitSelectors && previewOnly)) && selected.Count > 1)
                warnings.Add($"Broad {(applyToAsset ? "apply" : "revert")} selection includes {selected.Count} overrides. Review selectedOverrides before applying.");

            if (selected.Any(candidate => string.Equals(candidate.Classification, "missing reference", StringComparison.OrdinalIgnoreCase)))
                warnings.Add("Selected overrides include missing-reference rows. Applying can persist missing references to prefab assets; reverting clears the local override.");

            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            object prefabStateBefore = CapturePrefabState(sourcePrefabPath);
            object assetStateBefore = CaptureAssetState(sourcePrefabPath);
            bool savedAsset = false;
            var appliedRows = new List<object>();

            if (!previewOnly)
            {
                foreach (PrefabOverrideCandidate candidate in selected)
                {
                    if (!TryResolveOverrideProperty(candidate, out SerializedProperty property, out string propertyError))
                    {
                        warnings.Add($"Skipped override '{candidate.Id}': {propertyError}");
                        continue;
                    }

                    if (applyToAsset)
                    {
                        string assetPath = string.IsNullOrWhiteSpace(candidate.SourceAssetPath) ? sourcePrefabPath : candidate.SourceAssetPath;
                        PrefabUtility.ApplyPropertyOverride(property, assetPath, InteractionMode.UserAction);
                    }
                    else
                    {
                        PrefabUtility.RevertPropertyOverride(property, InteractionMode.UserAction);
                    }

                    appliedRows.Add(candidate.Row);
                }

                if (applyToAsset && appliedRows.Count > 0)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    savedAsset = true;
                }
                else if (!applyToAsset && appliedRows.Count > 0)
                {
                    SceneDirtyStateUtility.MarkSceneDirty(instanceRoot);
                }
            }

            object saveState = applyToAsset
                ? BuildAssetSaveState(
                    requested: !previewOnly,
                    attempted: !previewOnly,
                    saved: savedAsset,
                    savedAssets: savedAsset ? new object[] { CaptureAssetState(sourcePrefabPath) } : Array.Empty<object>(),
                    message: previewOnly ? "not_requested" : savedAsset ? "prefab_override_apply_saved_asset_by_tool_contract" : "no_overrides_applied")
                : SceneDirtyStateUtility.BuildSaveState(message: "not_requested");

            object data = new
            {
                status = previewOnly ? "preview" : applyToAsset ? "applied" : "reverted",
                mode = applyToAsset ? "apply_overrides" : "revert_overrides",
                previewOnly,
                target = DescribeGameObject(targetObject, "."),
                instanceRoot = DescribeGameObject(instanceRoot, "."),
                sourcePrefabPath,
                selectedOverrideCount = selected.Count,
                selectedOverrides = selected.Select(candidate => candidate.Row).ToArray(),
                changedObjects = new[] { DescribeGameObject(instanceRoot, ".") },
                appliedOverrides = appliedRows.ToArray(),
                warnings = warnings.ToArray(),
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabStateBefore,
                prefabStateAfter = CapturePrefabState(sourcePrefabPath),
                assetStateBefore,
                assetStateAfter = CaptureAssetState(sourcePrefabPath),
                saveState
            };

            string verb = applyToAsset ? "apply" : "revert";
            string message = previewOnly
                ? $"Previewed {verb} for {selected.Count} prefab override(s)."
                : $"{(applyToAsset ? "Applied" : "Reverted")} {appliedRows.Count} of {selected.Count} prefab override(s).";
            return (true, message, data, null);
        }

        static object BuildOverrideActionExplanation(
            JObject parameters,
            string action,
            bool includeNested,
            bool hasExplicitSelectors,
            List<PrefabOverrideCandidate> allCandidates,
            string sourcePrefabPath,
            bool sourceAssetExists,
            PrefabInstanceStatus instanceStatus,
            int maxOverrides,
            Dictionary<string, PrefabOverrideCandidate> selectedById)
        {
            bool applyToAsset = string.Equals(action, "apply", StringComparison.OrdinalIgnoreCase);
            string broadProperty = applyToAsset ? "applyAll" : "revertAll";
            bool broadSelectionRequested = GetBool(parameters, false, broadProperty, ToPascalCase(broadProperty), "all", "All");
            var warnings = new List<object>();
            var blockedReasons = new List<object>();
            var selectorWarnings = new List<string>();
            List<PrefabOverrideCandidate> selected = SelectOverrideCandidates(parameters, allCandidates, broadSelectionRequested, selectorWarnings, out string selectionError);

            foreach (PrefabOverrideCandidate candidate in selected)
            {
                if (!string.IsNullOrWhiteSpace(candidate?.Id))
                    selectedById[candidate.Id] = candidate;
            }

            foreach (string warning in selectorWarnings)
                AddReason(warnings, "selector_warning", warning);

            if (!string.IsNullOrWhiteSpace(selectionError))
                AddReason(blockedReasons, "selector_not_found", selectionError);

            AddMissingSelectorBlocks(parameters, allCandidates, blockedReasons);

            string instanceStatusText = instanceStatus.ToString();
            if (string.IsNullOrWhiteSpace(sourcePrefabPath) ||
                !sourceAssetExists ||
                !string.Equals(instanceStatusText, "Connected", StringComparison.OrdinalIgnoreCase))
            {
                AddReason(
                    blockedReasons,
                    "missing_or_disconnected_prefab_asset",
                    "The prefab instance is not connected to a loadable source prefab asset.",
                    new
                    {
                        sourcePrefabPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                        sourceAssetExists,
                        prefabInstanceStatus = instanceStatusText
                    });
            }

            if (allCandidates.Count == 0)
            {
                AddReason(blockedReasons, "no_overrides", "The prefab instance has no local property overrides to apply or revert.");
            }
            else if (!hasExplicitSelectors && !broadSelectionRequested)
            {
                AddReason(
                    blockedReasons,
                    "no_explicit_mutation_selection",
                    $"Select overrideIds/propertyPaths/targetPaths or set {broadProperty}=true before mutating prefab overrides.");
            }

            bool selectedNested = selected.Any(candidate => candidate.IsNested);
            if (selectedNested && !includeNested)
            {
                AddReason(
                    blockedReasons,
                    "nested_override_selected_without_include_nested",
                    "Selected overrides include nested prefab boundaries. Set includeNested=true only after reviewing the nested-prefab risk.");
            }

            PrefabOverrideCandidate[] missingReferenceSelections = selected
                .Where(candidate => string.Equals(candidate.Classification, "missing reference", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (missingReferenceSelections.Length > 0)
            {
                AddReason(
                    warnings,
                    "missing_reference_risk_warning",
                    applyToAsset
                        ? "Selected overrides include missing-reference rows. Applying can persist missing references to prefab assets."
                        : "Selected overrides include missing-reference rows. Reverting clears the local missing-reference override.",
                    new
                    {
                        overrideIds = missingReferenceSelections.Select(candidate => candidate.Id).ToArray()
                    });
            }

            foreach (PrefabOverrideCandidate candidate in selected)
            {
                if (TryResolveOverrideProperty(candidate, out _, out string propertyError))
                    continue;

                AddReason(
                    blockedReasons,
                    "unresolved_instance_property_target",
                    propertyError,
                    new
                    {
                        overrideId = candidate.Id,
                        targetPath = candidate.TargetPath,
                        propertyPath = candidate.PropertyPath
                    });
            }

            bool available = blockedReasons.Count == 0;
            return new
            {
                action,
                available,
                selectedOverrideCount = selected.Count,
                returnedSelectedOverrideCount = Math.Min(selected.Count, maxOverrides),
                broadSelectionRequested,
                explicitSelectionProvided = hasExplicitSelectors,
                includeNested,
                blockedReasons = blockedReasons.ToArray(),
                warnings = warnings.ToArray(),
                recommendedPreviewTool = applyToAsset ? PreviewApplyOverridesToolName : PreviewRevertOverridesToolName,
                recommendedApplyTool = applyToAsset ? ApplyOverridesToolName : RevertOverridesToolName,
                normalizedArguments = BuildNormalizedOverrideArgs(parameters, action, includeNested, broadSelectionRequested, hasExplicitSelectors, selected),
                selectedOverrides = selected
                    .Take(maxOverrides)
                    .Select(candidate => candidate.Row)
                    .ToArray()
            };
        }

        static string NormalizeExplainAction(string action)
        {
            if (string.Equals(action, "apply", StringComparison.OrdinalIgnoreCase))
                return "apply";

            if (string.Equals(action, "revert", StringComparison.OrdinalIgnoreCase))
                return "revert";

            return "both";
        }

        static JObject BuildNormalizedOverrideArgs(
            JObject parameters,
            string action,
            bool includeNested,
            bool broadSelectionRequested,
            bool hasExplicitSelectors,
            List<PrefabOverrideCandidate> selected)
        {
            bool applyToAsset = string.Equals(action, "apply", StringComparison.OrdinalIgnoreCase);
            string broadProperty = applyToAsset ? "applyAll" : "revertAll";
            var args = new JObject
            {
                ["searchMethod"] = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path",
                ["includeInactive"] = GetBool(parameters, true, "includeInactive", "IncludeInactive"),
                ["includeNested"] = includeNested
            };

            JToken target = GetToken(parameters, "target", "Target");
            if (target != null && target.Type != JTokenType.Null)
                args["target"] = target.DeepClone();

            if (broadSelectionRequested && !hasExplicitSelectors)
            {
                args[broadProperty] = true;
            }
            else if (selected.Count > 0)
            {
                args["overrideIds"] = new JArray(selected.Select(candidate => candidate.Id));
            }

            return args;
        }

        static void AddMissingSelectorBlocks(JObject parameters, List<PrefabOverrideCandidate> candidates, List<object> blockedReasons)
        {
            string[] missingOverrideIds = FindMissingSelectors(GetStringArray(parameters, "overrideIds", "OverrideIds"), candidates.Select(candidate => candidate.Id));
            string[] missingPropertyPaths = FindMissingSelectors(GetStringArray(parameters, "propertyPaths", "PropertyPaths"), candidates.Select(candidate => candidate.PropertyPath));
            string[] missingTargetPaths = FindMissingSelectors(GetStringArray(parameters, "targetPaths", "TargetPaths"), candidates.Select(candidate => candidate.TargetPath));
            if (missingOverrideIds.Length == 0 && missingPropertyPaths.Length == 0 && missingTargetPaths.Length == 0)
                return;

            AddReason(
                blockedReasons,
                "selector_not_found",
                "One or more selectors matched no local prefab overrides.",
                new
                {
                    overrideIds = missingOverrideIds,
                    propertyPaths = missingPropertyPaths,
                    targetPaths = missingTargetPaths
                });
        }

        static string[] FindMissingSelectors(string[] requested, IEnumerable<string> known)
        {
            if (requested == null || requested.Length == 0)
                return Array.Empty<string>();

            var knownSet = new HashSet<string>(
                known.Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase);
            return requested
                .Where(value => !string.IsNullOrWhiteSpace(value) && !knownSet.Contains(value))
                .ToArray();
        }

        static void AddReason(List<object> rows, string code, string message, object data = null)
        {
            rows.Add(new
            {
                code,
                message,
                data
            });
        }

        static object[] BuildOverrideRows(
            GameObject instanceRoot,
            bool includeInherited,
            bool includeNested,
            int maxRows,
            List<string> warnings,
            out int totalLocalOverrideCount)
        {
            List<PrefabOverrideCandidate> candidates = BuildOverrideCandidates(instanceRoot, includeNested, warnings);
            totalLocalOverrideCount = candidates.Count;
            var rows = new List<object>();

            rows.AddRange(candidates.Select(candidate => candidate.Row));
            if (includeInherited)
                rows.AddRange(BuildInheritedRows(instanceRoot, Math.Max(0, maxRows - rows.Count)));
            if (rows.Count > maxRows)
                warnings.Add($"Override output was truncated from {rows.Count} to {maxRows} rows. Use selectors or increase maxOverrides for more detail.");

            return rows.Take(Math.Max(1, maxRows)).ToArray();
        }

        static List<PrefabOverrideCandidate> BuildOverrideCandidates(GameObject instanceRoot, bool includeNested, List<string> warnings)
        {
            var candidates = new List<PrefabOverrideCandidate>();
            string rootSourcePath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            PropertyModification[] modifications = PrefabUtility.GetPropertyModifications(instanceRoot) ?? Array.Empty<PropertyModification>();
            int skippedNested = 0;

            for (int i = 0; i < modifications.Length; i++)
            {
                PropertyModification modification = modifications[i];
                if (modification == null || modification.target == null || string.IsNullOrWhiteSpace(modification.propertyPath))
                    continue;

                string sourceAssetPath = AssetDatabase.GetAssetPath(modification.target);
                bool nested = IsNestedOverride(rootSourcePath, sourceAssetPath);
                if (nested && !includeNested)
                {
                    skippedNested++;
                    continue;
                }

                string classification = ClassifyOverride(modification, nested);
                Object instanceTarget = ResolvePrefabInstanceTarget(instanceRoot, modification.target);
                Object identityTarget = instanceTarget != null ? instanceTarget : modification.target;
                string targetPath = GetObjectPath(identityTarget);
                string id = BuildOverrideId(i, identityTarget, modification.propertyPath);
                object row = new
                {
                    id,
                    classification,
                    kind = "property",
                    targetPath,
                    target = DescribeObject(identityTarget),
                    sourceTarget = DescribeObject(modification.target),
                    instanceTargetResolved = instanceTarget != null,
                    sourceAssetPath = string.IsNullOrWhiteSpace(sourceAssetPath) ? rootSourcePath : sourceAssetPath,
                    propertyPath = modification.propertyPath,
                    value = modification.value,
                    objectReference = DescribeObject(modification.objectReference),
                    isNestedPrefabOverride = nested,
                    risk = nested ? "nested_prefab_boundary" : classification == "missing reference" ? "missing_reference" : null
                };

                candidates.Add(new PrefabOverrideCandidate
                {
                    Id = id,
                    Classification = classification,
                    TargetPath = targetPath,
                    PropertyPath = modification.propertyPath,
                    SourceAssetPath = string.IsNullOrWhiteSpace(sourceAssetPath) ? rootSourcePath : sourceAssetPath,
                    IsNested = nested,
                    Modification = modification,
                    InstanceTarget = instanceTarget,
                    Row = row
                });
            }

            if (skippedNested > 0)
                warnings.Add($"Skipped {skippedNested} nested prefab override(s). Set includeNested=true to include them.");

            return candidates;
        }

        static bool TryResolveOverrideProperty(PrefabOverrideCandidate candidate, out SerializedProperty property, out string error)
        {
            property = null;
            error = null;

            PropertyModification modification = candidate?.Modification;
            if (modification == null)
            {
                error = "Override modification was null.";
                return false;
            }

            if (modification.target == null)
            {
                error = "Override target was null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(modification.propertyPath))
            {
                error = "Override property path was empty.";
                return false;
            }

            Object target = candidate?.InstanceTarget;
            if (target == null)
            {
                error = "Override instance target could not be resolved from the prefab source target.";
                return false;
            }

            var serializedObject = new SerializedObject(target);
            serializedObject.UpdateIfRequiredOrScript();
            property = serializedObject.FindProperty(modification.propertyPath);
            if (property != null)
                return true;

            error = $"Property '{modification.propertyPath}' could not be resolved on override instance target.";
            return false;
        }

        static Object ResolvePrefabInstanceTarget(GameObject instanceRoot, Object sourceOrInstanceTarget)
        {
            if (instanceRoot == null || sourceOrInstanceTarget == null)
                return null;

            if (IsObjectUnderRoot(instanceRoot, sourceOrInstanceTarget))
                return sourceOrInstanceTarget;

            foreach (Object candidate in EnumeratePrefabInstanceObjects(instanceRoot))
            {
                if (candidate == null)
                    continue;

                if (ReferenceEquals(candidate, sourceOrInstanceTarget) || candidate == sourceOrInstanceTarget)
                    return candidate;

                Object correspondingSource = PrefabUtility.GetCorrespondingObjectFromSource(candidate);
                if (correspondingSource != null &&
                    (ReferenceEquals(correspondingSource, sourceOrInstanceTarget) || correspondingSource == sourceOrInstanceTarget))
                {
                    return candidate;
                }

                Object originalSource = PrefabUtility.GetCorrespondingObjectFromOriginalSource(candidate);
                if (originalSource != null &&
                    (ReferenceEquals(originalSource, sourceOrInstanceTarget) || originalSource == sourceOrInstanceTarget))
                {
                    return candidate;
                }
            }

            return null;
        }

        static IEnumerable<Object> EnumeratePrefabInstanceObjects(GameObject instanceRoot)
        {
            if (instanceRoot == null)
                yield break;

            foreach (Transform transform in instanceRoot.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null)
                    continue;

                yield return transform.gameObject;
                Component[] components = transform.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null)
                        yield return components[i];
                }
            }
        }

        static bool IsObjectUnderRoot(GameObject root, Object obj)
        {
            GameObject owner = GetOwnerGameObject(obj);
            return root != null &&
                owner != null &&
                owner.scene.IsValid() &&
                (ReferenceEquals(owner, root) || owner.transform.IsChildOf(root.transform));
        }

        static IEnumerable<object> BuildInheritedRows(GameObject instanceRoot, int maxRows)
        {
            if (instanceRoot == null || maxRows <= 0)
                yield break;

            Transform[] transforms = instanceRoot.GetComponentsInChildren<Transform>(true);
            int emitted = 0;
            foreach (Transform transform in transforms)
            {
                if (transform == null || emitted >= maxRows)
                    yield break;

                GameObject gameObject = transform.gameObject;
                string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
                yield return new
                {
                    id = $"inherited:{emitted.ToString(CultureInfo.InvariantCulture)}",
                    classification = "inherited",
                    kind = "object",
                    targetPath = GetRelativePath(instanceRoot.transform, transform),
                    target = DescribeGameObject(gameObject, GetRelativePath(instanceRoot.transform, transform)),
                    sourceAssetPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                    propertyPath = (string)null,
                    isNestedPrefabOverride = IsNestedTransform(instanceRoot, gameObject),
                    risk = (string)null
                };
                emitted++;

                Component[] components = gameObject.GetComponents<Component>();
                for (int i = 0; i < components.Length && emitted < maxRows; i++)
                {
                    Component component = components[i];
                    yield return new
                    {
                        id = $"inherited:{emitted.ToString(CultureInfo.InvariantCulture)}",
                        classification = "inherited",
                        kind = "component",
                        targetPath = GetRelativePath(instanceRoot.transform, transform),
                        target = DescribeObject(component),
                        sourceAssetPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                        propertyPath = (string)null,
                        componentIndex = i,
                        isNestedPrefabOverride = IsNestedTransform(instanceRoot, gameObject),
                        risk = (string)null
                    };
                    emitted++;
                }
            }
        }

        static List<PrefabOverrideCandidate> SelectOverrideCandidates(
            JObject parameters,
            List<PrefabOverrideCandidate> candidates,
            bool selectAll,
            List<string> warnings,
            out string error)
        {
            error = null;
            if (selectAll)
                return candidates.ToList();

            string[] overrideIds = GetStringArray(parameters, "overrideIds", "OverrideIds");
            string[] propertyPaths = GetStringArray(parameters, "propertyPaths", "PropertyPaths");
            string[] targetPaths = GetStringArray(parameters, "targetPaths", "TargetPaths");
            var selected = candidates.Where(candidate =>
                MatchesAny(candidate.Id, overrideIds) ||
                MatchesAny(candidate.PropertyPath, propertyPaths) ||
                MatchesAny(candidate.TargetPath, targetPaths)).ToList();

            if (overrideIds.Length > 0)
            {
                var foundIds = selected.Select(candidate => candidate.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string[] missing = overrideIds.Where(id => !foundIds.Contains(id)).ToArray();
                if (missing.Length > 0)
                {
                    error = $"Override id(s) not found: {string.Join(", ", missing)}.";
                    return selected;
                }
            }

            if (selected.Count == 0 && (overrideIds.Length > 0 || propertyPaths.Length > 0 || targetPaths.Length > 0))
                warnings.Add("Selectors matched no local prefab overrides.");

            return selected;
        }

        static bool HasAnySelector(JObject parameters)
        {
            return GetStringArray(parameters, "overrideIds", "OverrideIds").Length > 0 ||
                GetStringArray(parameters, "propertyPaths", "PropertyPaths").Length > 0 ||
                GetStringArray(parameters, "targetPaths", "TargetPaths").Length > 0;
        }

        static bool TryResolvePrefabInstance(JObject parameters, out GameObject targetObject, out GameObject instanceRoot, out string sourcePrefabPath, out string error)
        {
            targetObject = null;
            instanceRoot = null;
            sourcePrefabPath = null;

            if (!TryResolveSceneTarget(parameters, out targetObject, out error))
                return false;

            instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(targetObject);
            if (instanceRoot == null)
            {
                error = $"Target '{UiDiagnosticsHelper.GetHierarchyPath(targetObject.transform)}' is not part of a prefab instance.";
                return false;
            }

            sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
            if (string.IsNullOrWhiteSpace(sourcePrefabPath))
            {
                error = $"Prefab source asset for '{UiDiagnosticsHelper.GetHierarchyPath(instanceRoot.transform)}' could not be resolved.";
                return false;
            }

            return true;
        }

        static bool TryResolveSceneTarget(JObject parameters, out GameObject targetObject, out string error)
        {
            targetObject = null;
            error = null;
            JToken target = GetToken(parameters, "target", "Target");
            if (target == null || target.Type == JTokenType.Null)
            {
                error = "target is required.";
                return false;
            }

            string searchMethod = GetString(parameters, "searchMethod", "SearchMethod") ?? "by_id_or_name_or_path";
            bool includeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive");
            JObject findParams = new()
            {
                ["search_inactive"] = includeInactive
            };
            targetObject = ObjectsHelper.FindObject(target, searchMethod, findParams);
            if (targetObject == null)
            {
                error = "Scene target could not be found.";
                return false;
            }

            if (!targetObject.scene.IsValid())
            {
                error = "Target does not belong to a valid loaded scene.";
                return false;
            }

            return true;
        }

        static object[] DescribeHierarchy(GameObject root, bool includeComponents, int maxRows, out int omittedRows)
        {
            var rows = new List<object>();
            if (root == null)
            {
                omittedRows = 0;
                return rows.ToArray();
            }

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform transform in transforms.Take(Math.Max(1, maxRows)))
            {
                GameObject gameObject = transform.gameObject;
                string relativePath = GetRelativePath(root.transform, transform);
                rows.Add(new
                {
                    path = relativePath,
                    name = gameObject.name,
                    objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                    stableId = GetStableObjectId(gameObject),
                    activeSelf = gameObject.activeSelf,
                    activeInHierarchy = gameObject.activeInHierarchy,
                    layer = gameObject.layer,
                    tag = gameObject.tag,
                    prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString(),
                    prefabAssetType = PrefabUtility.GetPrefabAssetType(gameObject).ToString(),
                    isNearestPrefabInstanceRoot = PrefabUtility.IsAnyPrefabInstanceRoot(gameObject),
                    isNestedPrefabBoundary = IsNestedTransform(root, gameObject),
                    nearestPrefabAssetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject),
                    components = includeComponents ? DescribeComponents(gameObject) : Array.Empty<object>()
                });
            }

            omittedRows = Math.Max(0, transforms.Length - rows.Count);
            return rows.ToArray();
        }

        static object[] DescribeComponents(GameObject gameObject)
        {
            Component[] components = gameObject.GetComponents<Component>();
            var rows = new List<object>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null)
                {
                    rows.Add(new
                    {
                        componentIndex = i,
                        type = "missing_script",
                        componentId = (object)0,
                        stableId = (string)null
                    });
                    continue;
                }

                rows.Add(new
                    {
                        componentIndex = i,
                        type = component.GetType().FullName,
                        typeName = component.GetType().Name,
                        componentId = UnityApiAdapter.GetObjectIdOrZero(component),
                        stableId = GetStableObjectId(component),
                        enabled = component is Behaviour behaviour ? behaviour.enabled : (bool?)null
                    });
            }

            return rows.ToArray();
        }

        static bool TryApplyTransform(Transform transform, JObject parameters, List<object> changes, out string error)
        {
            error = null;
            if (transform == null)
                return true;

            if (!TryApplyVector(parameters, transform, "position", vector => transform.localPosition = vector, transform.localPosition, changes, out error))
                return false;
            if (!TryApplyVector(parameters, transform, "rotation", vector => transform.localEulerAngles = vector, transform.localEulerAngles, changes, out error))
                return false;
            if (!TryApplyVector(parameters, transform, "scale", vector => transform.localScale = vector, transform.localScale, changes, out error))
                return false;

            return true;
        }

        static bool TryApplyVector(
            JObject parameters,
            Transform transform,
            string propertyName,
            Action<Vector3> apply,
            Vector3 before,
            List<object> changes,
            out string error)
        {
            error = null;
            JToken token = GetToken(parameters, propertyName, ToPascalCase(propertyName));
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (!TryParseVector3(token, out Vector3 value))
            {
                error = $"{propertyName} must be an object {{x,y,z}} or array [x,y,z].";
                return false;
            }

            apply(value);
            changes.Add(new
            {
                target = DescribeGameObject(transform.gameObject, "."),
                property = propertyName == "rotation" ? "localEulerAngles" : "local" + ToPascalCase(propertyName),
                previousValue = DescribeVector(before),
                newValue = DescribeVector(value)
            });
            return true;
        }

        static bool TryParseVector3(JToken token, out Vector3 vector)
        {
            vector = default;
            if (token is JArray array && array.Count >= 3)
            {
                vector = new Vector3(array[0].Value<float>(), array[1].Value<float>(), array[2].Value<float>());
                return true;
            }

            if (token is JObject obj)
            {
                vector = new Vector3(
                    obj.Value<float?>("x") ?? 0f,
                    obj.Value<float?>("y") ?? 0f,
                    obj.Value<float?>("z") ?? 0f);
                return true;
            }

            if (token.Type == JTokenType.String && TryParseVectorString(token.Value<string>(), out vector))
                return true;

            return false;
        }

        static bool TryParseVectorString(string value, out Vector3 vector)
        {
            vector = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string trimmed = value.Trim();
            if ((trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal)) ||
                (trimmed.StartsWith("{", StringComparison.Ordinal) && trimmed.EndsWith("}", StringComparison.Ordinal)))
            {
                try
                {
                    return TryParseVector3(JToken.Parse(trimmed), out vector);
                }
                catch
                {
                    return false;
                }
            }

            string[] parts = trimmed.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return false;

            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return false;
            }

            vector = new Vector3(x, y, z);
            return true;
        }

        static string ClassifyOverride(PropertyModification modification, bool nested)
        {
            if (nested)
                return "nested prefab override";

            if (IsLikelyMissingReference(modification))
                return "missing reference";

            if (IsLikelyNullReferenceOverride(modification))
                return "local null override";

            return "local override";
        }

        static bool IsLikelyMissingReference(PropertyModification modification)
        {
            return modification.objectReference == null &&
                !string.IsNullOrWhiteSpace(modification.value) &&
                IsLikelyReferenceProperty(modification.propertyPath) &&
                !string.Equals(modification.value, "0", StringComparison.Ordinal);
        }

        static bool IsLikelyNullReferenceOverride(PropertyModification modification)
        {
            return modification.objectReference == null &&
                string.IsNullOrWhiteSpace(modification.value) &&
                IsLikelyReferenceProperty(modification.propertyPath);
        }

        static bool IsLikelyReferenceProperty(string propertyPath)
        {
            if (string.IsNullOrWhiteSpace(propertyPath))
                return false;

            return propertyPath.IndexOf("reference", StringComparison.OrdinalIgnoreCase) >= 0 ||
                propertyPath.IndexOf("m_Object", StringComparison.OrdinalIgnoreCase) >= 0 ||
                propertyPath.IndexOf("m_Script", StringComparison.OrdinalIgnoreCase) >= 0 ||
                propertyPath.IndexOf("m_Material", StringComparison.OrdinalIgnoreCase) >= 0 ||
                propertyPath.IndexOf("m_Prefab", StringComparison.OrdinalIgnoreCase) >= 0 ||
                propertyPath.IndexOf("m_CorrespondingSourceObject", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool IsNestedOverride(string rootSourcePath, string sourceAssetPath)
        {
            return !string.IsNullOrWhiteSpace(rootSourcePath) &&
                !string.IsNullOrWhiteSpace(sourceAssetPath) &&
                !string.Equals(rootSourcePath, sourceAssetPath, StringComparison.OrdinalIgnoreCase);
        }

        static bool IsNestedTransform(GameObject root, GameObject gameObject)
        {
            if (root == null || gameObject == null || ReferenceEquals(root, gameObject))
                return false;

            GameObject nearestRoot = PrefabUtility.GetNearestPrefabInstanceRoot(gameObject);
            return nearestRoot != null && !ReferenceEquals(nearestRoot, root);
        }

        static object CountClassifications(object[] rows)
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (object row in rows ?? Array.Empty<object>())
            {
                string classification = JObject.FromObject(row)["classification"]?.Value<string>() ?? "unknown";
                counts[classification] = counts.TryGetValue(classification, out int count) ? count + 1 : 1;
            }

            return counts;
        }

        static string BuildOverrideId(int index, Object target, string propertyPath)
        {
            string stableId = GetStableObjectId(target) ?? UnityApiAdapter.GetObjectIdOrZero(target)?.ToString();
            string property = (propertyPath ?? string.Empty).Replace('/', '_').Replace('.', '_').Replace('[', '_').Replace(']', '_');
            return $"override:{index.ToString(CultureInfo.InvariantCulture)}:{stableId}:{property}";
        }

        static string GetObjectPath(Object obj)
        {
            GameObject gameObject = GetOwnerGameObject(obj);
            return gameObject == null ? obj?.name : UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform);
        }

        static GameObject GetOwnerGameObject(Object obj)
        {
            return obj switch
            {
                GameObject go => go,
                Component component => component.gameObject,
                _ => null
            };
        }

        static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return string.Empty;

            if (ReferenceEquals(root, target))
                return ".";

            var parts = new Stack<string>();
            Transform current = target;
            while (current != null && !ReferenceEquals(current, root))
            {
                parts.Push(current.name);
                current = current.parent;
            }

            return current == null ? UiDiagnosticsHelper.GetHierarchyPath(target) : string.Join("/", parts);
        }

        static object DescribeGameObject(GameObject gameObject, string relativePath)
        {
            if (gameObject == null)
                return null;

            string sourcePrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(gameObject);
            return new
            {
                name = gameObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                relativePath,
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject),
                stableId = GetStableObjectId(gameObject),
                sourcePrefabPath = string.IsNullOrWhiteSpace(sourcePrefabPath) ? null : sourcePrefabPath,
                prefabInstanceStatus = PrefabUtility.GetPrefabInstanceStatus(gameObject).ToString()
            };
        }

        static object DescribeObject(Object obj)
        {
            if (obj == null)
                return null;

            string assetPath = AssetDatabase.GetAssetPath(obj);
            GameObject owner = GetOwnerGameObject(obj);
            return new
            {
                name = obj.name,
                type = obj.GetType().FullName,
                objectId = UnityApiAdapter.GetObjectIdOrZero(obj),
                stableId = GetStableObjectId(obj),
                assetPath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath,
                assetGuid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath),
                hierarchyPath = owner != null ? UiDiagnosticsHelper.GetHierarchyPath(owner.transform) : null
            };
        }

        static string GetStableObjectId(Object obj)
        {
            if (obj == null)
                return null;

            try
            {
                if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId) &&
                    !string.IsNullOrWhiteSpace(guid))
                {
                    return $"{guid}:{localId.ToString(CultureInfo.InvariantCulture)}";
                }
            }
            catch
            {
            }

            object id = UnityApiAdapter.GetObjectIdOrZero(obj);
            return id == null ? null : id.ToString();
        }

        static object CapturePrefabState(string prefabPath)
        {
            prefabPath = NormalizeAssetPath(prefabPath);
            GameObject asset = string.IsNullOrWhiteSpace(prefabPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            return new
            {
                prefabPath,
                exists = asset != null,
                guid = string.IsNullOrWhiteSpace(prefabPath) ? null : AssetDatabase.AssetPathToGUID(prefabPath),
                name = asset != null ? asset.name : null,
                prefabAssetType = asset != null ? PrefabUtility.GetPrefabAssetType(asset).ToString() : null,
                isDirty = asset != null && EditorUtility.IsDirty(asset)
            };
        }

        static object CaptureAssetState(string assetPath)
        {
            assetPath = NormalizeAssetPath(assetPath);
            Object asset = string.IsNullOrWhiteSpace(assetPath)
                ? null
                : AssetDatabase.LoadMainAssetAtPath(assetPath);
            return new
            {
                assetPath,
                exists = asset != null,
                guid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath),
                name = asset != null ? asset.name : null,
                type = asset != null ? asset.GetType().FullName : null,
                isDirty = asset != null && EditorUtility.IsDirty(asset)
            };
        }

        static object BuildAssetSaveState(
            bool requested = false,
            bool attempted = false,
            bool saved = false,
            object savedAssets = null,
            string message = null,
            string error = null)
        {
            return new
            {
                requested,
                attempted,
                saved,
                savedAssets = savedAssets ?? Array.Empty<object>(),
                message = message ?? (requested ? "save_requested" : "not_requested"),
                error
            };
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            TruncateArray(root, "hierarchy", 20);
            TruncateArray(root, "overrides", 20);
            TruncateArray(root, "candidateOverrides", 20);
            TruncateArray(root, "selectedOverrides", 20);
            TruncateArray(root, "appliedOverrides", 20);
            TruncateArray(root, "changedObjects", 12);
            TruncateArray(root, "addedObjects", 12);
            return root;
        }

        static void TruncateArray(JObject root, string propertyName, int limit)
        {
            if (root[propertyName] is not JArray array || array.Count <= limit)
                return;

            root[propertyName] = new JArray(array.Take(limit));
            root[$"omitted{ToPascalCase(propertyName)}Count"] = array.Count - limit;
        }

        static (bool success, string message, object data, string errorKind) Failure(string errorKind, string message, object data)
        {
            return (false, message, data, errorKind);
        }

        static bool IsPrefabAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            return "Assets/" + normalized.TrimStart('/');
        }

        static void EnsureAssetDirectory(string assetPath)
        {
            string directory = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(directory) || AssetDatabase.IsValidFolder(directory))
                return;

            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), directory));
            AssetDatabase.Refresh();
        }

        static JToken GetToken(JObject obj, params string[] names)
        {
            if (obj == null)
                return null;

            foreach (string name in names)
            {
                if (obj.TryGetValue(name, StringComparison.OrdinalIgnoreCase, out JToken token))
                    return token;
            }

            return null;
        }

        static string GetString(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            return token?.Type == JTokenType.Null ? null : token?.ToString();
        }

        static bool GetBool(JObject obj, bool fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            return token.Type switch
            {
                JTokenType.Boolean => token.Value<bool>(),
                JTokenType.String when bool.TryParse(token.Value<string>(), out bool value) => value,
                _ => fallback
            };
        }

        static int GetInt(JObject obj, int fallback, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token == null || token.Type == JTokenType.Null)
                return fallback;

            int value = token.Type switch
            {
                JTokenType.Integer => token.Value<int>(),
                JTokenType.String when int.TryParse(token.Value<string>(), out int parsed) => parsed,
                _ => fallback
            };
            return Mathf.Max(1, value);
        }

        static string[] GetStringArray(JObject obj, params string[] names)
        {
            JToken token = GetToken(obj, names);
            if (token == null || token.Type == JTokenType.Null)
                return Array.Empty<string>();

            if (token is JArray array)
            {
                return array
                    .Select(item => item?.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray();
            }

            string single = token.ToString();
            return string.IsNullOrWhiteSpace(single) ? Array.Empty<string>() : new[] { single };
        }

        static bool MatchesAny(string value, string[] candidates)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                candidates != null &&
                candidates.Any(candidate => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase));
        }

        static object DescribeVector(Vector3 value)
        {
            return new { x = value.x, y = value.y, z = value.z };
        }

        static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
