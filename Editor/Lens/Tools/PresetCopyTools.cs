#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.Utils.Scene;
using UnityEditor;
using UnityEditor.Presets;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class PresetCopyTools
    {
        const string PresetSearchToolName = "Unity.Preset.Search";
        const string PresetInspectToolName = "Unity.Preset.Inspect";
        const string PresetPreviewCreateToolName = "Unity.Preset.PreviewCreate";
        const string PresetCreateToolName = "Unity.Preset.Create";
        const string PresetPreviewApplyToolName = "Unity.Preset.PreviewApplyToComponent";
        const string PresetApplyToolName = "Unity.Preset.ApplyToComponent";
        const string ScenePreviewCopyToolName = "Unity.Scene.PreviewCopyComponentSerializedValues";
        const string SceneApplyCopyToolName = "Unity.Scene.ApplyCopyComponentSerializedValues";
        const string PrefabPreviewCopyToolName = "Unity.Prefab.PreviewCopyComponentSerializedValues";
        const string PrefabApplyCopyToolName = "Unity.Prefab.ApplyCopyComponentSerializedValues";

        sealed class ComponentTarget
        {
            public GameObject Root;
            public GameObject GameObject;
            public Component Component;
            public string TargetPath;
            public int ComponentIndex;
            public string ComponentTypeName;
        }

        sealed class CopyContext
        {
            public GameObject SourceRoot;
            public GameObject TargetRoot;
            public string ReferencePolicy;
            public bool TargetIsPrefabAsset;
        }

        [McpSchema(PresetSearchToolName)]
        public static object GetPresetSearchSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Optional preset name/path/target-type search text." },
                    componentName = new { type = "string", description = "Optional component type name used to score compatibility." },
                    includePackages = new { type = "boolean", description = "Include package presets as well as project presets. Defaults to true." },
                    maxResults = new { type = "integer", description = "Maximum result count. Defaults to 50." }
                }
            };
        }

        [McpSchema(PresetInspectToolName)]
        public static object GetPresetInspectSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    presetPath = new { type = "string", description = "Preset asset path under Assets/ or Packages/." },
                    componentName = new { type = "string", description = "Optional component type name used to report compatibility." },
                    maxFields = new { type = "integer", description = "Maximum serialized preset fields to return inline. Defaults to 120." }
                },
                required = new[] { "presetPath" }
            };
        }

        [McpSchema(PresetPreviewCreateToolName)]
        public static object GetPresetPreviewCreateSchema() => GetPresetCreateSchema();

        [McpSchema(PresetCreateToolName)]
        public static object GetPresetCreateSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    presetPath = new { type = "string", description = "Destination Preset asset path under Assets/." },
                    target = new { description = "Scene target root when creating from a loaded scene component." },
                    searchMethod = new { type = "string", description = "How to find scene target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects. Defaults to true." },
                    prefabPath = new { type = "string", description = "Prefab asset path when creating from a prefab asset component." },
                    targetPath = new { type = "string", description = "Relative child path under target or prefab root. Defaults to '.'." },
                    componentType = new { type = "string", description = "Component type on the target GameObject." },
                    componentIndex = new { type = "integer", description = "0-based component index when multiple matching components exist. Defaults to 0." },
                    overwrite = new { type = "boolean", description = "Allow replacing an existing Preset asset at presetPath. Defaults to false." },
                    maxFields = new { type = "integer", description = "Maximum source component fields to include. Defaults to 120." }
                },
                required = new[] { "presetPath", "componentType" }
            };
        }

        [McpSchema(PresetPreviewApplyToolName)]
        public static object GetPresetPreviewApplySchema() => GetPresetApplySchema();

        [McpSchema(PresetApplyToolName)]
        public static object GetPresetApplySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    presetPath = new { type = "string", description = "Preset asset path under Assets/ or Packages/." },
                    target = new { description = "Scene target root when applying to a loaded scene component." },
                    searchMethod = new { type = "string", description = "How to find scene target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects. Defaults to true." },
                    prefabPath = new { type = "string", description = "Prefab asset path when applying to a prefab asset component." },
                    targetPath = new { type = "string", description = "Relative child path under target or prefab root. Defaults to '.'." },
                    componentType = new { type = "string", description = "Component type on the target GameObject." },
                    componentIndex = new { type = "integer", description = "0-based component index when multiple matching components exist. Defaults to 0." },
                    maxFields = new { type = "integer", description = "Maximum before/after field rows to include. Defaults to 120." }
                },
                required = new[] { "presetPath", "componentType" }
            };
        }

        [McpSchema(ScenePreviewCopyToolName)]
        public static object GetScenePreviewCopySchema() => GetSceneCopySchema();

        [McpSchema(SceneApplyCopyToolName)]
        public static object GetSceneApplyCopySchema() => GetSceneCopySchema();

        static object GetSceneCopySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    source = new { description = "Source scene root GameObject target, path, or instance id." },
                    target = new { description = "Target scene root GameObject target, path, or instance id." },
                    sourceSearchMethod = new { type = "string", description = "How to find source. Defaults to by_id_or_name_or_path." },
                    targetSearchMethod = new { type = "string", description = "How to find target. Defaults to by_id_or_name_or_path." },
                    includeInactive = new { type = "boolean", description = "Include inactive scene objects. Defaults to true." },
                    sourcePath = new { type = "string", description = "Relative source child path under source root. Defaults to '.'." },
                    targetPath = new { type = "string", description = "Relative target child path under target root. Defaults to sourcePath." },
                    componentType = new { type = "string", description = "Component type to copy." },
                    sourceComponentIndex = new { type = "integer", description = "0-based source component index. Defaults to 0." },
                    targetComponentIndex = new { type = "integer", description = "0-based target component index. Defaults to sourceComponentIndex." },
                    propertyPaths = new { type = "array", items = new { type = "string" }, description = "Optional allow-list of serialized property paths." },
                    excludePropertyPaths = new { type = "array", items = new { type = "string" }, description = "Optional property paths to skip." },
                    referencePolicy = new { type = "string", description = "Object-reference handling policy.", @enum = new[] { "preserve", "remapByPath", "skip", "failOnUnresolved" } },
                    maxFields = new { type = "integer", description = "Maximum field rows to include. Defaults to 200." }
                },
                required = new[] { "source", "target", "componentType" }
            };
        }

        [McpSchema(PrefabPreviewCopyToolName)]
        public static object GetPrefabPreviewCopySchema() => GetPrefabCopySchema();

        [McpSchema(PrefabApplyCopyToolName)]
        public static object GetPrefabApplyCopySchema() => GetPrefabCopySchema();

        static object GetPrefabCopySchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    sourcePrefabPath = new { type = "string", description = "Source prefab asset path under Assets/." },
                    targetPrefabPath = new { type = "string", description = "Target prefab asset path under Assets/." },
                    sourcePath = new { type = "string", description = "Relative source child path under source prefab root. Defaults to '.'." },
                    targetPath = new { type = "string", description = "Relative target child path under target prefab root. Defaults to sourcePath." },
                    componentType = new { type = "string", description = "Component type to copy." },
                    sourceComponentIndex = new { type = "integer", description = "0-based source component index. Defaults to 0." },
                    targetComponentIndex = new { type = "integer", description = "0-based target component index. Defaults to sourceComponentIndex." },
                    propertyPaths = new { type = "array", items = new { type = "string" }, description = "Optional allow-list of serialized property paths." },
                    excludePropertyPaths = new { type = "array", items = new { type = "string" }, description = "Optional property paths to skip." },
                    referencePolicy = new { type = "string", description = "Object-reference handling policy.", @enum = new[] { "preserve", "remapByPath", "skip", "failOnUnresolved" } },
                    maxFields = new { type = "integer", description = "Maximum field rows to include. Defaults to 200." }
                },
                required = new[] { "sourcePrefabPath", "targetPrefabPath", "componentType" }
            };
        }

        [McpTool(PresetSearchToolName, "Searches project and package Preset assets and reports target type plus component compatibility hints.", "Search Presets", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object SearchPresets(JObject @params)
        {
            return Handle(PresetSearchToolName, "search", @params, ExecutePresetSearch);
        }

        [McpTool(PresetInspectToolName, "Inspects a Preset asset target type and serialized preset fields without mutation.", "Inspect Preset", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object InspectPreset(JObject @params)
        {
            return Handle(PresetInspectToolName, "inspect", @params, ExecutePresetInspect);
        }

        [McpTool(PresetPreviewCreateToolName, "Previews creating a Preset asset from a scene or prefab component without saving assets.", "Preview Create Preset", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object PreviewCreatePreset(JObject @params)
        {
            return Handle(PresetPreviewCreateToolName, "preview_create", @params, p => ExecutePresetCreate(p, previewOnly: true));
        }

        [McpTool(PresetCreateToolName, "Creates a Preset asset from a scene or prefab component. This persists the Preset asset by explicit tool contract.", "Create Preset", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object CreatePreset(JObject @params)
        {
            return Handle(PresetCreateToolName, "create", @params, p => ExecutePresetCreate(p, previewOnly: false));
        }

        [McpTool(PresetPreviewApplyToolName, "Previews whether a Preset asset can be applied to a scene or prefab component without mutating or saving.", "Preview Apply Preset To Component", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object PreviewApplyPresetToComponent(JObject @params)
        {
            return Handle(PresetPreviewApplyToolName, "preview_apply_to_component", @params, p => ExecutePresetApply(p, previewOnly: true));
        }

        [McpTool(PresetApplyToolName, "Applies a Preset asset to a scene component or prefab asset component. Scene applies mark scenes dirty; prefab applies save the prefab by tool contract.", "Apply Preset To Component", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object ApplyPresetToComponent(JObject @params)
        {
            return Handle(PresetApplyToolName, "apply_to_component", @params, p => ExecutePresetApply(p, previewOnly: false));
        }

        [McpTool(ScenePreviewCopyToolName, "Previews copying serialized component values between loaded scene objects with explicit object-reference handling.", "Preview Copy Scene Component Serialized Values", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object PreviewCopySceneComponentSerializedValues(JObject @params)
        {
            return Handle(ScenePreviewCopyToolName, "preview_copy_component_values", @params, p => ExecuteSceneCopy(p, previewOnly: true));
        }

        [McpTool(SceneApplyCopyToolName, "Copies serialized component values between loaded scene objects with explicit object-reference handling, marking scenes dirty without saving.", "Apply Copy Scene Component Serialized Values", Groups = new[] { "scene" }, EnabledByDefault = true)]
        public static object ApplyCopySceneComponentSerializedValues(JObject @params)
        {
            return Handle(SceneApplyCopyToolName, "apply_copy_component_values", @params, p => ExecuteSceneCopy(p, previewOnly: false));
        }

        [McpTool(PrefabPreviewCopyToolName, "Previews copying serialized component values between prefab assets with explicit object-reference handling.", "Preview Copy Prefab Component Serialized Values", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object PreviewCopyPrefabComponentSerializedValues(JObject @params)
        {
            return Handle(PrefabPreviewCopyToolName, "preview_copy_component_values", @params, p => ExecutePrefabCopy(p, previewOnly: true));
        }

        [McpTool(PrefabApplyCopyToolName, "Copies serialized component values between prefab assets and saves the target prefab by tool contract.", "Apply Copy Prefab Component Serialized Values", Groups = new[] { "assets" }, EnabledByDefault = true)]
        public static object ApplyCopyPrefabComponentSerializedValues(JObject @params)
        {
            return Handle(PrefabApplyCopyToolName, "apply_copy_component_values", @params, p => ExecutePrefabCopy(p, previewOnly: false));
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
                message = $"Preset/copy operation failed: {ex.Message}";
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
                        new { kind = "preset_copy_full_result" },
                        "preset_copy",
                        detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes))
                    : Response.Error(message ?? "Preset/copy operation failed.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, success ? null : errorKind);
            return response;
        }

        static (bool success, string message, object data, string errorKind) ExecutePresetSearch(JObject parameters)
        {
            string query = GetString(parameters, "query", "Query");
            string componentName = GetString(parameters, "componentName", "ComponentName", "componentType", "ComponentType");
            bool includePackages = GetBool(parameters, true, "includePackages", "IncludePackages");
            int maxResults = GetInt(parameters, 50, "maxResults", "MaxResults");
            Type requestedType = ResolveComponentType(componentName);

            var roots = includePackages ? new[] { "Assets", "Packages" } : new[] { "Assets" };
            string filter = string.IsNullOrWhiteSpace(query) ? "t:Preset" : $"{query} t:Preset";
            var results = new List<object>();
            foreach (string guid in AssetDatabase.FindAssets(filter, roots).Take(Math.Max(1, maxResults * 3)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                Preset preset = AssetDatabase.LoadAssetAtPath<Preset>(path);
                if (preset == null)
                    continue;

                string targetTypeName = ReadPresetTargetTypeName(preset);
                bool compatible = IsPresetCompatibleWithTypeName(preset, requestedType, out string compatibility);
                if (!string.IsNullOrWhiteSpace(componentName) && !compatible && ScoreText(query, $"{path} {targetTypeName}") <= 0)
                    continue;

                results.Add(new
                {
                    name = Path.GetFileNameWithoutExtension(path),
                    presetPath = path,
                    guid,
                    provider = path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase) ? "installed package" : "preset",
                    targetTypeName,
                    confidence = Math.Max(compatible ? 0.88 : 0.35, ScoreText(query, $"{path} {targetTypeName}")),
                    compatibleWithRequestedComponent = string.IsNullOrWhiteSpace(componentName) ? null : (bool?)compatible,
                    compatibility,
                    setupRequirements = compatible
                        ? new[] { "Resolve the target component and preview the preset apply before mutation." }
                        : new[] { "Use Unity.Preset.Inspect or choose a preset whose target type matches the component." },
                    serializedSchemaAvailable = true
                });

                if (results.Count >= maxResults)
                    break;
            }

            object data = new
            {
                status = "searched",
                query,
                componentName,
                provider = "preset",
                resultCount = results.Count,
                results = results.ToArray()
            };
            return (true, $"Found {results.Count} preset candidate(s).", data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecutePresetInspect(JObject parameters)
        {
            string presetPath = NormalizeAssetPath(GetString(parameters, "presetPath", "PresetPath", "path", "Path"), allowPackages: true);
            Preset preset = LoadPreset(presetPath, out string error);
            if (preset == null)
                return Failure("PRESET_NOT_FOUND", error, new { status = "failed", presetPath });

            int maxFields = GetInt(parameters, 120, "maxFields", "MaxFields");
            string componentName = GetString(parameters, "componentName", "ComponentName", "componentType", "ComponentType");
            Type componentType = ResolveComponentType(componentName);
            bool compatible = IsPresetCompatibleWithTypeName(preset, componentType, out string compatibility);
            object[] fields = ReadSerializedFields(preset, maxFields, out int totalFieldCount, out int omittedFieldCount);
            object data = new
            {
                status = "inspected",
                preset = DescribePreset(preset, presetPath, componentType),
                targetTypeName = ReadPresetTargetTypeName(preset),
                compatibleWithRequestedComponent = string.IsNullOrWhiteSpace(componentName) ? null : (bool?)compatible,
                compatibility,
                totalFieldCount,
                returnedFieldCount = fields.Length,
                omittedFieldCount,
                fields,
                warnings = Array.Empty<string>()
            };

            return (true, $"Inspected preset '{presetPath}'.", data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecutePresetCreate(JObject parameters, bool previewOnly)
        {
            string presetPath = NormalizeAssetPath(GetString(parameters, "presetPath", "PresetPath", "path", "Path"), allowPackages: false);
            if (string.IsNullOrWhiteSpace(presetPath) ||
                !presetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                !presetPath.EndsWith(".preset", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("INVALID_PRESET_PATH", "presetPath must point to a .preset asset under Assets/.", new { status = "failed", presetPath });
            }

            bool overwrite = GetBool(parameters, false, "overwrite", "Overwrite");
            Object existing = AssetDatabase.LoadMainAssetAtPath(presetPath);
            if (existing != null && !overwrite)
            {
                return Failure("PRESET_EXISTS", $"Preset asset '{presetPath}' already exists. Set overwrite=true to replace it.", new
                {
                    status = "failed",
                    presetPath,
                    existing = DescribeObject(existing)
                });
            }

            if (existing != null && existing is not Preset)
            {
                return Failure("PRESET_PATH_OCCUPIED", $"Asset '{presetPath}' exists but is not a Preset asset.", new
                {
                    status = "failed",
                    presetPath,
                    existing = DescribeObject(existing)
                });
            }

            int maxFields = GetInt(parameters, 120, "maxFields", "MaxFields");
            string prefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath"), allowPackages: false);
            bool prefabMode = !string.IsNullOrWhiteSpace(prefabPath);
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            object assetStateBefore = CaptureAssetState(presetPath);
            object prefabStateBefore = prefabMode ? CaptureAssetState(prefabPath) : null;
            GameObject prefabRoot = null;

            try
            {
                ComponentTarget target = prefabMode
                    ? ResolvePrefabComponent(parameters, prefabPath, out prefabRoot)
                    : ResolveSceneComponent(parameters, targetPropertyName: "target", searchMethodProperty: "searchMethod");

                object[] fields = ReadComponentFields(target.Component, maxFields, null);
                bool created = false;
                Preset createdPreset = null;
                var warnings = new List<string>();
                if (existing != null && overwrite)
                    warnings.Add("Existing Preset asset will be replaced by explicit overwrite=true.");

                if (!previewOnly)
                {
                    EnsureAssetDirectory(presetPath);
                    if (existing != null && !AssetDatabase.DeleteAsset(presetPath))
                    {
                        return Failure("PRESET_DELETE_FAILED", $"Existing Preset asset '{presetPath}' could not be removed before overwrite.", new
                        {
                            status = "failed",
                            presetPath,
                            target = DescribeComponentTarget(target),
                            dirtyStateBefore,
                            dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                            assetStateBefore,
                            assetStateAfter = CaptureAssetState(presetPath),
                            saveState = BuildAssetSaveState(requested: true, attempted: true, saved: false, message: "preset_overwrite_delete_failed")
                        });
                    }

                    Preset preset = new(target.Component);
                    AssetDatabase.CreateAsset(preset, presetPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    createdPreset = AssetDatabase.LoadAssetAtPath<Preset>(presetPath);
                    created = createdPreset != null;
                    if (!created)
                        warnings.Add("Preset asset creation was attempted but the asset could not be loaded afterward.");
                }

                object data = new
                {
                    status = previewOnly ? "preview" : created ? "created" : "failed",
                    previewOnly,
                    presetPath,
                    preset = previewOnly
                        ? new { name = Path.GetFileNameWithoutExtension(presetPath), presetPath, targetTypeName = target.ComponentTypeName }
                        : DescribePreset(createdPreset, presetPath, target.Component?.GetType()),
                    target = DescribeComponentTarget(target),
                    sourceFields = fields,
                    changedObjects = Array.Empty<object>(),
                    createdAssets = !previewOnly && created ? new object[] { CaptureAssetState(presetPath) } : Array.Empty<object>(),
                    warnings = warnings.ToArray(),
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    prefabStateBefore,
                    prefabStateAfter = prefabMode ? CaptureAssetState(prefabPath) : null,
                    assetStateBefore,
                    assetStateAfter = CaptureAssetState(presetPath),
                    saveState = BuildAssetSaveState(
                        requested: !previewOnly,
                        attempted: !previewOnly,
                        saved: !previewOnly && created,
                        savedAssets: !previewOnly && created ? new object[] { CaptureAssetState(presetPath) } : Array.Empty<object>(),
                        message: previewOnly ? "not_requested" : created ? "preset_asset_saved_by_tool_contract" : "preset_asset_create_failed")
                };

                if (!previewOnly && !created)
                    return Failure("PRESET_CREATE_FAILED", $"Preset asset '{presetPath}' could not be created.", data);

                return (true, previewOnly
                    ? $"Previewed Preset creation at '{presetPath}'."
                    : $"Created Preset asset '{presetPath}'.", data, null);
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static (bool success, string message, object data, string errorKind) ExecutePresetApply(JObject parameters, bool previewOnly)
        {
            string presetPath = NormalizeAssetPath(GetString(parameters, "presetPath", "PresetPath", "path", "Path"), allowPackages: true);
            Preset preset = LoadPreset(presetPath, out string error);
            if (preset == null)
                return Failure("PRESET_NOT_FOUND", error, new { status = "failed", presetPath });

            int maxFields = GetInt(parameters, 120, "maxFields", "MaxFields");
            string prefabPath = NormalizeAssetPath(GetString(parameters, "prefabPath", "PrefabPath"), allowPackages: false);
            bool prefabMode = !string.IsNullOrWhiteSpace(prefabPath);
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            object prefabStateBefore = prefabMode ? CaptureAssetState(prefabPath) : null;
            GameObject prefabRoot = null;

            try
            {
                ComponentTarget target = prefabMode
                    ? ResolvePrefabComponent(parameters, prefabPath, out prefabRoot)
                    : ResolveSceneComponent(parameters, targetPropertyName: "target", searchMethodProperty: "searchMethod");

                if (!CanApplyPresetToObject(preset, target.Component, out string compatibility))
                {
                    return Failure("PRESET_INCOMPATIBLE", compatibility, new
                    {
                        status = "failed",
                        preset = DescribePreset(preset, presetPath, target.Component?.GetType()),
                        target = DescribeComponentTarget(target),
                        dirtyStateBefore,
                        dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                        prefabStateBefore,
                        prefabStateAfter = prefabMode ? CaptureAssetState(prefabPath) : null,
                        saveState = prefabMode
                            ? BuildAssetSaveState(message: "not_requested")
                            : SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                    });
                }

                object[] beforeFields = ReadComponentFields(target.Component, maxFields, null);
                bool applied = false;
                if (!previewOnly)
                {
                    applied = ApplyPresetToObject(preset, target.Component, out string applyError);
                    if (!applied)
                        return Failure("PRESET_APPLY_FAILED", applyError, new { status = "failed", presetPath, target = DescribeComponentTarget(target) });

                    EditorUtility.SetDirty(target.Component);
                    if (prefabMode)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                        AssetDatabase.SaveAssets();
                    }
                    else
                    {
                        if (PrefabUtility.IsPartOfPrefabInstance(target.Component))
                            PrefabUtility.RecordPrefabInstancePropertyModifications(target.Component);
                        SceneDirtyStateUtility.MarkSceneDirty(target.Root);
                    }
                }

                object[] afterFields = ReadComponentFields(target.Component, maxFields, null);
                object[] changedFields = DiffFieldRows(beforeFields, afterFields);
                object data = new
                {
                    status = previewOnly ? "preview" : "applied",
                    previewOnly,
                    preset = DescribePreset(preset, presetPath, target.Component?.GetType()),
                    target = DescribeComponentTarget(target),
                    compatible = true,
                    applied = !previewOnly && applied,
                    changedObjects = new[] { DescribeGameObject(target.GameObject, target.TargetPath) },
                    fields = previewOnly ? beforeFields : changedFields,
                    beforeFields,
                    afterFields = previewOnly ? Array.Empty<object>() : afterFields,
                    warnings = Array.Empty<string>(),
                    dirtyStateBefore,
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    prefabStateBefore,
                    prefabStateAfter = prefabMode ? CaptureAssetState(prefabPath) : null,
                    saveState = prefabMode
                        ? BuildAssetSaveState(
                            requested: !previewOnly,
                            attempted: !previewOnly,
                            saved: !previewOnly && applied,
                            savedAssets: !previewOnly && applied ? new object[] { CaptureAssetState(prefabPath) } : Array.Empty<object>(),
                            message: previewOnly ? "not_requested" : "prefab_asset_saved_by_tool_contract")
                        : SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
                };

                return (true, previewOnly
                    ? $"Previewed preset apply for '{presetPath}'."
                    : $"Applied preset '{presetPath}' to component.", data, null);
            }
            finally
            {
                if (prefabRoot != null)
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        static (bool success, string message, object data, string errorKind) ExecuteSceneCopy(JObject parameters, bool previewOnly)
        {
            ComponentTarget source = ResolveSceneComponent(parameters, targetPropertyName: "source", searchMethodProperty: "sourceSearchMethod", pathPropertyName: "sourcePath", indexPropertyName: "sourceComponentIndex");
            ComponentTarget target = ResolveSceneComponent(parameters, targetPropertyName: "target", searchMethodProperty: "targetSearchMethod", pathPropertyName: "targetPath", fallbackPath: source.TargetPath, indexPropertyName: "targetComponentIndex", fallbackIndex: source.ComponentIndex);
            if (source.Component.GetType() != target.Component.GetType())
            {
                return Failure("COMPONENT_TYPE_MISMATCH", "Source and target components must have the same resolved component type.", new
                {
                    status = "failed",
                    source = DescribeComponentTarget(source),
                    target = DescribeComponentTarget(target)
                });
            }

            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            string referencePolicy = NormalizeReferencePolicy(GetString(parameters, "referencePolicy", "ReferencePolicy"));
            var copyContext = new CopyContext
            {
                SourceRoot = source.Root,
                TargetRoot = target.Root,
                ReferencePolicy = referencePolicy,
                TargetIsPrefabAsset = false
            };
            CopyResult copyResult = CopySerializedValues(source.Component, target.Component, copyContext, parameters, previewOnly);
            if (!copyResult.Success)
                return Failure(copyResult.ErrorKind, copyResult.Error, BuildCopyFailureData(source, target, copyResult, dirtyStateBefore));

            if (!previewOnly && copyResult.ChangedCount > 0)
            {
                EditorUtility.SetDirty(target.Component);
                if (PrefabUtility.IsPartOfPrefabInstance(target.Component))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target.Component);
                SceneDirtyStateUtility.MarkSceneDirty(target.Root);
            }

            object data = new
            {
                status = previewOnly ? "preview" : "applied",
                previewOnly,
                scope = "scene",
                referencePolicy,
                source = DescribeComponentTarget(source),
                target = DescribeComponentTarget(target),
                changedObjects = copyResult.ChangedCount > 0 ? new[] { DescribeGameObject(target.GameObject, target.TargetPath) } : Array.Empty<object>(),
                changedFieldCount = copyResult.ChangedCount,
                skippedFieldCount = copyResult.SkippedCount,
                incompatibleFieldCount = copyResult.IncompatibleCount,
                fields = copyResult.Rows.ToArray(),
                warnings = copyResult.Warnings.ToArray(),
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                prefabStateBefore = (object)null,
                prefabStateAfter = (object)null,
                assetStateBefore = (object)null,
                assetStateAfter = (object)null,
                saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
            };

            return (true, previewOnly
                ? $"Previewed copy of {copyResult.ChangedCount} serialized component field(s)."
                : $"Copied {copyResult.ChangedCount} serialized component field(s).", data, null);
        }

        static (bool success, string message, object data, string errorKind) ExecutePrefabCopy(JObject parameters, bool previewOnly)
        {
            string sourcePrefabPath = NormalizeAssetPath(GetString(parameters, "sourcePrefabPath", "SourcePrefabPath"), allowPackages: false);
            string targetPrefabPath = NormalizeAssetPath(GetString(parameters, "targetPrefabPath", "TargetPrefabPath"), allowPackages: false);
            if (!IsPrefabPath(sourcePrefabPath) || !IsPrefabPath(targetPrefabPath))
                return Failure("INVALID_PREFAB_PATH", "sourcePrefabPath and targetPrefabPath must point to .prefab assets under Assets/.", new { status = "failed", sourcePrefabPath, targetPrefabPath });

            GameObject sourceRoot = null;
            GameObject targetRoot = null;
            object sourceStateBefore = CaptureAssetState(sourcePrefabPath);
            object targetStateBefore = CaptureAssetState(targetPrefabPath);
            try
            {
                sourceRoot = PrefabUtility.LoadPrefabContents(sourcePrefabPath);
                targetRoot = PrefabUtility.LoadPrefabContents(targetPrefabPath);
                if (sourceRoot == null || targetRoot == null)
                    return Failure("PREFAB_LOAD_FAILED", "One or both prefab assets could not be loaded.", new { status = "failed", sourcePrefabPath, targetPrefabPath });

                string sourcePath = GetString(parameters, "sourcePath", "SourcePath") ?? ".";
                string targetPath = GetString(parameters, "targetPath", "TargetPath") ?? sourcePath;
                int sourceIndex = GetInt(parameters, 0, "sourceComponentIndex", "SourceComponentIndex", "componentIndex", "ComponentIndex");
                int targetIndex = GetInt(parameters, sourceIndex, "targetComponentIndex", "TargetComponentIndex");
                string componentType = GetString(parameters, "componentType", "ComponentType", "componentName", "ComponentName");
                ComponentTarget source = ResolveComponentUnderRoot(sourceRoot, sourcePath, componentType, sourceIndex);
                ComponentTarget target = ResolveComponentUnderRoot(targetRoot, targetPath, componentType, targetIndex);
                if (source.Component.GetType() != target.Component.GetType())
                    return Failure("COMPONENT_TYPE_MISMATCH", "Source and target prefab components must have the same resolved component type.", new { status = "failed", source = DescribeComponentTarget(source), target = DescribeComponentTarget(target) });

                string referencePolicy = NormalizeReferencePolicy(GetString(parameters, "referencePolicy", "ReferencePolicy"));
                var copyContext = new CopyContext
                {
                    SourceRoot = sourceRoot,
                    TargetRoot = targetRoot,
                    ReferencePolicy = referencePolicy,
                    TargetIsPrefabAsset = true
                };
                CopyResult copyResult = CopySerializedValues(source.Component, target.Component, copyContext, parameters, previewOnly);
                if (!copyResult.Success)
                    return Failure(copyResult.ErrorKind, copyResult.Error, new { status = "failed", source = DescribeComponentTarget(source), target = DescribeComponentTarget(target), fields = copyResult.Rows.ToArray(), warnings = copyResult.Warnings.ToArray() });

                bool saved = false;
                if (!previewOnly && copyResult.ChangedCount > 0)
                {
                    EditorUtility.SetDirty(target.Component);
                    PrefabUtility.SaveAsPrefabAsset(targetRoot, targetPrefabPath);
                    AssetDatabase.SaveAssets();
                    saved = true;
                }

                object data = new
                {
                    status = previewOnly ? "preview" : "applied",
                    previewOnly,
                    scope = "prefab_asset",
                    referencePolicy,
                    sourcePrefabPath,
                    targetPrefabPath,
                    source = DescribeComponentTarget(source),
                    target = DescribeComponentTarget(target),
                    changedObjects = copyResult.ChangedCount > 0 ? new[] { DescribeGameObject(target.GameObject, target.TargetPath) } : Array.Empty<object>(),
                    changedFieldCount = copyResult.ChangedCount,
                    skippedFieldCount = copyResult.SkippedCount,
                    incompatibleFieldCount = copyResult.IncompatibleCount,
                    fields = copyResult.Rows.ToArray(),
                    warnings = copyResult.Warnings.ToArray(),
                    dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                    prefabStateBefore = targetStateBefore,
                    prefabStateAfter = CaptureAssetState(targetPrefabPath),
                    assetStateBefore = targetStateBefore,
                    assetStateAfter = CaptureAssetState(targetPrefabPath),
                    sourceAssetState = sourceStateBefore,
                    saveState = BuildAssetSaveState(
                        requested: !previewOnly,
                        attempted: !previewOnly && copyResult.ChangedCount > 0,
                        saved: saved,
                        savedAssets: saved ? new object[] { CaptureAssetState(targetPrefabPath) } : Array.Empty<object>(),
                        message: previewOnly ? "not_requested" : saved ? "prefab_asset_saved_by_tool_contract" : "no_changes")
                };

                return (true, previewOnly
                    ? $"Previewed prefab component copy with {copyResult.ChangedCount} field change(s)."
                    : $"Copied {copyResult.ChangedCount} prefab component field(s).", data, null);
            }
            finally
            {
                if (sourceRoot != null)
                    PrefabUtility.UnloadPrefabContents(sourceRoot);
                if (targetRoot != null)
                    PrefabUtility.UnloadPrefabContents(targetRoot);
            }
        }

        sealed class CopyResult
        {
            public bool Success = true;
            public string ErrorKind;
            public string Error;
            public int ChangedCount;
            public int SkippedCount;
            public int IncompatibleCount;
            public readonly List<object> Rows = new();
            public readonly List<string> Warnings = new();
        }

        static CopyResult CopySerializedValues(Component source, Component target, CopyContext context, JObject parameters, bool previewOnly)
        {
            var result = new CopyResult();
            string[] propertyPaths = GetStringArray(parameters, "propertyPaths", "PropertyPaths");
            string[] excludePropertyPaths = GetStringArray(parameters, "excludePropertyPaths", "ExcludePropertyPaths");
            int maxFields = GetInt(parameters, 200, "maxFields", "MaxFields");
            var sourceObject = new SerializedObject(source);
            var targetObject = new SerializedObject(target);
            sourceObject.Update();
            targetObject.Update();

            IEnumerable<SerializedProperty> sourceProperties = propertyPaths.Length > 0
                ? propertyPaths.Select(sourceObject.FindProperty).Where(property => property != null).Select(property => property.Copy())
                : EnumerateTopLevelProperties(sourceObject);

            foreach (SerializedProperty sourceProperty in sourceProperties)
            {
                if (result.Rows.Count >= maxFields)
                {
                    result.Warnings.Add($"Field output truncated at {maxFields} rows.");
                    break;
                }

                string propertyPath = sourceProperty.propertyPath;
                if (ShouldSkipProperty(propertyPath, excludePropertyPaths))
                {
                    result.SkippedCount++;
                    result.Rows.Add(BuildCopyRow(propertyPath, sourceProperty, null, "skipped", "excluded_property", false, null, null));
                    continue;
                }

                SerializedProperty targetProperty = targetObject.FindProperty(propertyPath);
                if (targetProperty == null)
                {
                    result.IncompatibleCount++;
                    result.Rows.Add(BuildCopyRow(propertyPath, sourceProperty, null, "skipped", "missing_on_target", false, null, null));
                    continue;
                }

                object before = ReadPropertyValue(targetProperty);
                object requested = ReadPropertyValue(sourceProperty);
                bool canCopy = TryCopyPropertyValue(sourceProperty, targetProperty, context, previewOnly, out string status, out string reason, out object resolvedValue, out string fatalError);
                if (!string.IsNullOrWhiteSpace(fatalError))
                {
                    result.Success = false;
                    result.ErrorKind = "OBJECT_REFERENCE_UNRESOLVED";
                    result.Error = fatalError;
                    return result;
                }

                if (!canCopy)
                {
                    result.SkippedCount++;
                    result.Rows.Add(BuildCopyRow(propertyPath, sourceProperty, targetProperty, status, reason, false, before, requested));
                    continue;
                }

                object after = previewOnly ? resolvedValue : ReadPropertyValue(targetProperty);
                bool changed = !JsonEqual(before, previewOnly ? resolvedValue : after);
                if (changed)
                    result.ChangedCount++;
                result.Rows.Add(BuildCopyRow(propertyPath, sourceProperty, targetProperty, "copied", null, changed, before, previewOnly ? resolvedValue : after));
            }

            if (!previewOnly && result.Success)
                targetObject.ApplyModifiedPropertiesWithoutUndo();

            return result;
        }

        static IEnumerable<SerializedProperty> EnumerateTopLevelProperties(SerializedObject serializedObject)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.depth == 0 && !string.Equals(iterator.propertyPath, "m_Script", StringComparison.Ordinal))
                    yield return iterator.Copy();
            }
        }

        static bool TryCopyPropertyValue(
            SerializedProperty source,
            SerializedProperty target,
            CopyContext context,
            bool previewOnly,
            out string status,
            out string reason,
            out object resolvedValue,
            out string fatalError)
        {
            status = "copied";
            reason = null;
            fatalError = null;
            resolvedValue = ReadPropertyValue(source);

            if (source.propertyType != target.propertyType)
            {
                status = "skipped";
                reason = $"property_type_mismatch:{source.propertyType}->{target.propertyType}";
                return false;
            }

            try
            {
                switch (source.propertyType)
                {
                    case SerializedPropertyType.Integer:
                    case SerializedPropertyType.LayerMask:
                    case SerializedPropertyType.Character:
                        if (!previewOnly) target.intValue = source.intValue;
                        resolvedValue = source.intValue;
                        return true;
                    case SerializedPropertyType.Boolean:
                        if (!previewOnly) target.boolValue = source.boolValue;
                        resolvedValue = source.boolValue;
                        return true;
                    case SerializedPropertyType.Float:
                        if (!previewOnly) target.floatValue = source.floatValue;
                        resolvedValue = source.floatValue;
                        return true;
                    case SerializedPropertyType.String:
                        if (!previewOnly) target.stringValue = source.stringValue;
                        resolvedValue = source.stringValue;
                        return true;
                    case SerializedPropertyType.Color:
                        if (!previewOnly) target.colorValue = source.colorValue;
                        resolvedValue = DescribeColor(source.colorValue);
                        return true;
                    case SerializedPropertyType.Enum:
                        if (!previewOnly) target.enumValueIndex = source.enumValueIndex;
                        resolvedValue = ReadPropertyValue(source);
                        return true;
                    case SerializedPropertyType.Vector2:
                        if (!previewOnly) target.vector2Value = source.vector2Value;
                        resolvedValue = DescribeVector2(source.vector2Value);
                        return true;
                    case SerializedPropertyType.Vector3:
                        if (!previewOnly) target.vector3Value = source.vector3Value;
                        resolvedValue = DescribeVector3(source.vector3Value);
                        return true;
                    case SerializedPropertyType.Vector4:
                        if (!previewOnly) target.vector4Value = source.vector4Value;
                        resolvedValue = DescribeVector4(source.vector4Value);
                        return true;
                    case SerializedPropertyType.Rect:
                        if (!previewOnly) target.rectValue = source.rectValue;
                        resolvedValue = DescribeRect(source.rectValue);
                        return true;
                    case SerializedPropertyType.Bounds:
                        if (!previewOnly) target.boundsValue = source.boundsValue;
                        resolvedValue = DescribeBounds(source.boundsValue);
                        return true;
                    case SerializedPropertyType.Quaternion:
                        if (!previewOnly) target.quaternionValue = source.quaternionValue;
                        resolvedValue = source.quaternionValue.eulerAngles.ToString("F3");
                        return true;
                    case SerializedPropertyType.ObjectReference:
                        return TryCopyObjectReference(source, target, context, previewOnly, out status, out reason, out resolvedValue, out fatalError);
                    case SerializedPropertyType.Generic when source.isArray && target.isArray:
                        return TryCopyArray(source, target, context, previewOnly, out status, out reason, out resolvedValue, out fatalError);
                    default:
                        status = "skipped";
                        reason = $"unsupported_property_type:{source.propertyType}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                status = "skipped";
                reason = ex.Message;
                return false;
            }
        }

        static bool TryCopyArray(
            SerializedProperty source,
            SerializedProperty target,
            CopyContext context,
            bool previewOnly,
            out string status,
            out string reason,
            out object resolvedValue,
            out string fatalError)
        {
            status = "copied";
            reason = null;
            fatalError = null;
            resolvedValue = new { arraySize = source.arraySize };

            var values = new List<object>();
            for (int i = 0; i < source.arraySize; i++)
            {
                SerializedProperty sourceElement = source.GetArrayElementAtIndex(i);
                if (sourceElement == null)
                    continue;

                if (sourceElement.propertyType is SerializedPropertyType.Generic or SerializedPropertyType.ManagedReference)
                {
                    status = "skipped";
                    reason = "unsupported_array_element_type";
                    return false;
                }
            }

            if (!previewOnly)
                target.arraySize = source.arraySize;

            for (int i = 0; i < source.arraySize; i++)
            {
                SerializedProperty sourceElement = source.GetArrayElementAtIndex(i);
                SerializedProperty targetElement = previewOnly ? null : target.GetArrayElementAtIndex(i);
                if (previewOnly)
                {
                    values.Add(ReadPropertyValue(sourceElement));
                    continue;
                }

                if (!TryCopyPropertyValue(sourceElement, targetElement, context, previewOnly: false, out status, out reason, out object resolved, out fatalError))
                    return false;

                values.Add(resolved);
            }

            resolvedValue = values.ToArray();
            return true;
        }

        static bool TryCopyObjectReference(
            SerializedProperty source,
            SerializedProperty target,
            CopyContext context,
            bool previewOnly,
            out string status,
            out string reason,
            out object resolvedValue,
            out string fatalError)
        {
            status = "copied";
            reason = null;
            fatalError = null;
            Object sourceReference = source.objectReferenceValue;
            if (sourceReference == null)
            {
                if (!previewOnly)
                    target.objectReferenceValue = null;
                resolvedValue = null;
                return true;
            }

            string policy = NormalizeReferencePolicy(context.ReferencePolicy);
            if (string.Equals(policy, "skip", StringComparison.OrdinalIgnoreCase))
            {
                status = "skipped";
                reason = "object_reference_policy_skip";
                resolvedValue = DescribeObject(sourceReference);
                return false;
            }

            if (TryResolveReferenceByPolicy(sourceReference, context, policy, out Object resolved, out string resolveReason))
            {
                if (!previewOnly)
                    target.objectReferenceValue = resolved;
                resolvedValue = DescribeObject(resolved);
                return true;
            }

            if (string.Equals(policy, "failOnUnresolved", StringComparison.OrdinalIgnoreCase))
            {
                fatalError = $"Object reference '{source.propertyPath}' could not be resolved with referencePolicy=failOnUnresolved: {resolveReason}";
                resolvedValue = DescribeObject(sourceReference);
                return false;
            }

            status = "skipped";
            reason = resolveReason;
            resolvedValue = DescribeObject(sourceReference);
            return false;
        }

        static bool TryResolveReferenceByPolicy(Object sourceReference, CopyContext context, string policy, out Object resolved, out string reason)
        {
            resolved = null;
            reason = null;
            string assetPath = AssetDatabase.GetAssetPath(sourceReference);
            if (!string.IsNullOrWhiteSpace(assetPath))
            {
                resolved = sourceReference;
                return true;
            }

            if (string.Equals(policy, "preserve", StringComparison.OrdinalIgnoreCase) && !context.TargetIsPrefabAsset)
            {
                resolved = sourceReference;
                return true;
            }

            if (string.Equals(policy, "preserve", StringComparison.OrdinalIgnoreCase) && context.TargetIsPrefabAsset)
            {
                reason = "prefab_asset_cannot_preserve_non_asset_reference";
                return false;
            }

            GameObject sourceOwner = GetOwnerGameObject(sourceReference);
            if (sourceOwner == null || context.SourceRoot == null || context.TargetRoot == null)
            {
                reason = "reference_owner_or_copy_roots_unavailable";
                return false;
            }

            string relativePath = GetRelativePath(context.SourceRoot.transform, sourceOwner.transform);
            Transform targetTransform = relativePath == "."
                ? context.TargetRoot.transform
                : context.TargetRoot.transform.Find(relativePath);
            if (targetTransform == null)
            {
                reason = $"remap_target_path_not_found:{relativePath}";
                return false;
            }

            if (sourceReference is GameObject)
            {
                resolved = targetTransform.gameObject;
                return true;
            }

            if (sourceReference is Component sourceComponent)
            {
                Component[] sourceComponents = sourceOwner.GetComponents(sourceComponent.GetType());
                int index = Math.Max(0, Array.IndexOf(sourceComponents, sourceComponent));
                Component[] targetComponents = targetTransform.GetComponents(sourceComponent.GetType());
                if (targetComponents.Length <= index)
                {
                    reason = $"remap_component_not_found:{sourceComponent.GetType().FullName}[{index}]";
                    return false;
                }

                resolved = targetComponents[index];
                return true;
            }

            reason = "unsupported_non_asset_reference";
            return false;
        }

        static bool ShouldSkipProperty(string propertyPath, string[] excludePropertyPaths)
        {
            if (string.Equals(propertyPath, "m_Script", StringComparison.Ordinal))
                return true;

            return excludePropertyPaths.Any(excluded =>
                string.Equals(propertyPath, excluded, StringComparison.Ordinal) ||
                propertyPath.StartsWith(excluded + ".", StringComparison.Ordinal));
        }

        static object BuildCopyRow(
            string propertyPath,
            SerializedProperty source,
            SerializedProperty target,
            string status,
            string reason,
            bool changed,
            object before,
            object requested)
        {
            return new
            {
                propertyPath,
                propertyType = source?.propertyType.ToString(),
                targetPropertyType = target?.propertyType.ToString(),
                status,
                reason,
                changed,
                sourceValue = source != null ? ReadPropertyValue(source) : null,
                previousValue = before,
                requestedValue = requested
            };
        }

        static object BuildCopyFailureData(ComponentTarget source, ComponentTarget target, CopyResult copyResult, object dirtyStateBefore)
        {
            return new
            {
                status = "failed",
                source = DescribeComponentTarget(source),
                target = DescribeComponentTarget(target),
                fields = copyResult.Rows.ToArray(),
                warnings = copyResult.Warnings.ToArray(),
                dirtyStateBefore,
                dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                saveState = SceneDirtyStateUtility.BuildSaveState(message: "not_requested")
            };
        }

        static ComponentTarget ResolveSceneComponent(
            JObject parameters,
            string targetPropertyName,
            string searchMethodProperty,
            string pathPropertyName = "targetPath",
            string fallbackPath = ".",
            string indexPropertyName = "componentIndex",
            int fallbackIndex = 0)
        {
            JToken targetToken = GetToken(parameters, targetPropertyName, ToPascalCase(targetPropertyName));
            if (targetToken == null || targetToken.Type == JTokenType.Null)
                throw new InvalidOperationException($"{targetPropertyName} is required.");

            string searchMethod = GetString(parameters, searchMethodProperty, ToPascalCase(searchMethodProperty)) ?? "by_id_or_name_or_path";
            bool includeInactive = GetBool(parameters, true, "includeInactive", "IncludeInactive");
            JObject findParams = new()
            {
                ["search_inactive"] = includeInactive
            };
            GameObject root = ObjectsHelper.FindObject(targetToken, searchMethod, findParams);
            if (root == null)
                throw new InvalidOperationException($"{targetPropertyName} scene object could not be found.");

            if (!root.scene.IsValid())
                throw new InvalidOperationException($"{targetPropertyName} does not belong to a valid loaded scene.");

            string targetPath = GetString(parameters, pathPropertyName, ToPascalCase(pathPropertyName)) ?? fallbackPath ?? ".";
            string componentType = GetString(parameters, "componentType", "ComponentType", "componentName", "ComponentName");
            int componentIndex = GetInt(parameters, fallbackIndex, indexPropertyName, ToPascalCase(indexPropertyName), "componentIndex", "ComponentIndex");
            return ResolveComponentUnderRoot(root, targetPath, componentType, componentIndex);
        }

        static ComponentTarget ResolvePrefabComponent(JObject parameters, string prefabPath, out GameObject prefabRoot)
        {
            if (!IsPrefabPath(prefabPath))
                throw new InvalidOperationException("prefabPath must point to a .prefab asset under Assets/.");

            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            if (prefabRoot == null)
                throw new InvalidOperationException($"Prefab '{prefabPath}' could not be loaded.");

            string targetPath = GetString(parameters, "targetPath", "TargetPath") ?? ".";
            string componentType = GetString(parameters, "componentType", "ComponentType", "componentName", "ComponentName");
            int componentIndex = GetInt(parameters, 0, "componentIndex", "ComponentIndex");
            return ResolveComponentUnderRoot(prefabRoot, targetPath, componentType, componentIndex);
        }

        static ComponentTarget ResolveComponentUnderRoot(GameObject root, string targetPath, string componentTypeName, int componentIndex)
        {
            if (root == null)
                throw new InvalidOperationException("Component root is required.");
            if (string.IsNullOrWhiteSpace(componentTypeName))
                throw new InvalidOperationException("componentType is required.");

            targetPath = string.IsNullOrWhiteSpace(targetPath) ? "." : targetPath.Trim();
            Transform transform = targetPath == "." ? root.transform : root.transform.Find(targetPath);
            if (transform == null)
                throw new InvalidOperationException($"Target path '{targetPath}' was not found under '{root.name}'.");

            Type componentType = ResolveComponentType(componentTypeName);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                throw new InvalidOperationException($"Component type '{componentTypeName}' could not be resolved.");

            Component[] components = transform.GetComponents(componentType);
            int index = Math.Max(0, componentIndex);
            if (components.Length <= index || components[index] == null)
                throw new InvalidOperationException($"Component '{componentTypeName}' with index {index} was not found on '{UiDiagnosticsHelper.GetHierarchyPath(transform)}'.");

            return new ComponentTarget
            {
                Root = root,
                GameObject = transform.gameObject,
                Component = components[index],
                TargetPath = targetPath,
                ComponentIndex = index,
                ComponentTypeName = components[index].GetType().FullName
            };
        }

        static Preset LoadPreset(string presetPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(presetPath) || !presetPath.EndsWith(".preset", StringComparison.OrdinalIgnoreCase))
            {
                error = $"PresetPath must point to a .preset asset. Received '{presetPath}'.";
                return null;
            }

            Preset preset = AssetDatabase.LoadAssetAtPath<Preset>(presetPath);
            if (preset == null)
                error = $"Preset asset '{presetPath}' could not be loaded.";

            return preset;
        }

        static bool CanApplyPresetToObject(Preset preset, Object target, out string compatibility)
        {
            compatibility = null;
            if (preset == null || target == null)
            {
                compatibility = "Preset and target component are required.";
                return false;
            }

            try
            {
                MethodInfo method = typeof(Preset).GetMethod("CanBeAppliedTo", new[] { typeof(Object) });
                if (method != null && method.Invoke(preset, new[] { target }) is bool canApply)
                {
                    compatibility = canApply ? "compatible" : "Preset target type does not match the component.";
                    return canApply;
                }
            }
            catch (Exception ex)
            {
                compatibility = ex.Message;
                return false;
            }

            compatibility = "Preset compatibility API was unavailable; apply is not attempted.";
            return false;
        }

        static bool ApplyPresetToObject(Preset preset, Object target, out string error)
        {
            error = null;
            try
            {
                MethodInfo method = typeof(Preset).GetMethod("ApplyTo", new[] { typeof(Object) });
                object result = method?.Invoke(preset, new[] { target });
                return result is not bool value || value;
            }
            catch (Exception ex)
            {
                error = ex.InnerException?.Message ?? ex.Message;
                return false;
            }
        }

        static bool IsPresetCompatibleWithTypeName(Preset preset, Type requestedType, out string compatibility)
        {
            compatibility = null;
            if (requestedType == null)
            {
                compatibility = "no_component_filter";
                return false;
            }

            string targetTypeName = ReadPresetTargetTypeName(preset);
            bool compatible = string.Equals(targetTypeName, requestedType.FullName, StringComparison.Ordinal) ||
                string.Equals(targetTypeName, requestedType.Name, StringComparison.Ordinal) ||
                (!string.IsNullOrWhiteSpace(targetTypeName) && targetTypeName.EndsWith("." + requestedType.Name, StringComparison.Ordinal));
            compatibility = compatible
                ? "target_type_matches"
                : $"preset_target_type_is_{targetTypeName ?? "unknown"}";
            return compatible;
        }

        static string ReadPresetTargetTypeName(Object preset)
        {
            if (preset == null)
                return null;

            foreach (string methodName in new[] { "GetTargetFullTypeName", "GetTargetTypeName" })
            {
                try
                {
                    MethodInfo method = preset.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
                    object value = method?.Invoke(preset, null);
                    if (value != null && !string.IsNullOrWhiteSpace(value.ToString()))
                        return value.ToString();
                }
                catch
                {
                }
            }

            return null;
        }

        static object[] ReadSerializedFields(Object target, int maxFields, out int totalFieldCount, out int omittedFieldCount)
        {
            var rows = new List<object>();
            totalFieldCount = 0;
            if (target == null)
            {
                omittedFieldCount = 0;
                return rows.ToArray();
            }

            SerializedObject serializedObject = new(target);
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (string.Equals(iterator.propertyPath, "m_Script", StringComparison.Ordinal))
                    continue;

                totalFieldCount++;
                if (rows.Count < maxFields)
                {
                    rows.Add(new
                    {
                        propertyPath = iterator.propertyPath,
                        displayName = iterator.displayName,
                        propertyType = iterator.propertyType.ToString(),
                        editable = iterator.editable,
                        value = ReadPropertyValue(iterator)
                    });
                }
            }

            omittedFieldCount = Math.Max(0, totalFieldCount - rows.Count);
            return rows.ToArray();
        }

        static object[] ReadComponentFields(Component component, int maxFields, string[] propertyPaths)
        {
            if (component == null)
                return Array.Empty<object>();

            SerializedObject serializedObject = new(component);
            var rows = new List<object>();
            if (propertyPaths is { Length: > 0 })
            {
                foreach (string path in propertyPaths)
                {
                    SerializedProperty property = serializedObject.FindProperty(path);
                    if (property == null)
                        continue;

                    rows.Add(BuildFieldRow(property));
                }

                return rows.Take(maxFields).ToArray();
            }

            foreach (SerializedProperty property in EnumerateTopLevelProperties(serializedObject))
            {
                if (rows.Count >= maxFields)
                    break;

                rows.Add(BuildFieldRow(property));
            }

            return rows.ToArray();
        }

        static object BuildFieldRow(SerializedProperty property)
        {
            return new
            {
                propertyPath = property.propertyPath,
                displayName = property.displayName,
                propertyType = property.propertyType.ToString(),
                value = ReadPropertyValue(property)
            };
        }

        static object ReadPropertyValue(SerializedProperty property)
        {
            if (property == null)
                return null;

            return property.propertyType switch
            {
                SerializedPropertyType.Integer or SerializedPropertyType.LayerMask or SerializedPropertyType.Character => property.intValue,
                SerializedPropertyType.Boolean => property.boolValue,
                SerializedPropertyType.Float => property.floatValue,
                SerializedPropertyType.String => property.stringValue,
                SerializedPropertyType.Color => DescribeColor(property.colorValue),
                SerializedPropertyType.Enum => new
                {
                    enumValueIndex = property.enumValueIndex,
                    enumValue = property.enumDisplayNames != null &&
                        property.enumValueIndex >= 0 &&
                        property.enumValueIndex < property.enumDisplayNames.Length
                            ? property.enumDisplayNames[property.enumValueIndex]
                            : property.enumValueIndex.ToString(CultureInfo.InvariantCulture)
                },
                SerializedPropertyType.Vector2 => DescribeVector2(property.vector2Value),
                SerializedPropertyType.Vector3 => DescribeVector3(property.vector3Value),
                SerializedPropertyType.Vector4 => DescribeVector4(property.vector4Value),
                SerializedPropertyType.Rect => DescribeRect(property.rectValue),
                SerializedPropertyType.Bounds => DescribeBounds(property.boundsValue),
                SerializedPropertyType.Quaternion => property.quaternionValue.eulerAngles.ToString("F3"),
                SerializedPropertyType.ObjectReference => DescribeObject(property.objectReferenceValue),
                SerializedPropertyType.Generic when property.isArray => new { arraySize = property.arraySize },
                _ => new { unsupported = true, propertyType = property.propertyType.ToString() }
            };
        }

        static object[] DiffFieldRows(object[] beforeFields, object[] afterFields)
        {
            var beforeByPath = beforeFields
                .Select(row => JObject.FromObject(row))
                .Where(row => row["propertyPath"] != null)
                .ToDictionary(row => row["propertyPath"].Value<string>(), row => row, StringComparer.Ordinal);
            var changes = new List<object>();
            foreach (object afterRowObject in afterFields)
            {
                JObject afterRow = JObject.FromObject(afterRowObject);
                string path = afterRow["propertyPath"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                beforeByPath.TryGetValue(path, out JObject beforeRow);
                JToken beforeValue = beforeRow?["value"];
                JToken afterValue = afterRow["value"];
                if (!JToken.DeepEquals(beforeValue, afterValue))
                {
                    changes.Add(new
                    {
                        propertyPath = path,
                        propertyType = afterRow["propertyType"]?.Value<string>(),
                        previousValue = beforeValue,
                        newValue = afterValue,
                        changed = true
                    });
                }
            }

            return changes.ToArray();
        }

        static object BuildCompactData(object data)
        {
            JObject root = JObject.FromObject(data ?? new { });
            TruncateArray(root, "results", 20);
            TruncateArray(root, "fields", 24);
            TruncateArray(root, "sourceFields", 24);
            TruncateArray(root, "beforeFields", 12);
            TruncateArray(root, "afterFields", 12);
            TruncateArray(root, "changedObjects", 8);
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

        static Type ResolveComponentType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type direct = Type.GetType(typeName, false);
            if (direct != null)
                return direct;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type match = assembly.GetType(typeName, false) ??
                        assembly.GetTypes().FirstOrDefault(type =>
                            string.Equals(type.FullName, typeName, StringComparison.Ordinal) ||
                            string.Equals(type.Name, typeName, StringComparison.Ordinal));
                    if (match != null)
                        return match;
                }
                catch
                {
                }
            }

            return null;
        }

        static string NormalizeReferencePolicy(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "skip";

            return value.Trim() switch
            {
                "preserve" => "preserve",
                "remapByPath" => "remapByPath",
                "skip" => "skip",
                "failOnUnresolved" => "failOnUnresolved",
                _ => "skip"
            };
        }

        static bool IsPrefabPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) &&
                path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        }

        static string NormalizeAssetPath(string path, bool allowPackages)
        {
            if (string.IsNullOrWhiteSpace(path))
                return path;

            string normalized = path.Trim().Replace('\\', '/');
            if (normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return normalized;
            if (allowPackages && normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                return normalized;

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

        static object CaptureAssetState(string assetPath)
        {
            assetPath = NormalizeAssetPath(assetPath, allowPackages: true);
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

        static object DescribePreset(Preset preset, string presetPath, Type requestedType)
        {
            bool compatible = IsPresetCompatibleWithTypeName(preset, requestedType, out string compatibility);
            return new
            {
                name = preset != null ? preset.name : null,
                presetPath,
                guid = string.IsNullOrWhiteSpace(presetPath) ? null : AssetDatabase.AssetPathToGUID(presetPath),
                targetTypeName = ReadPresetTargetTypeName(preset),
                compatibleWithRequestedComponent = requestedType == null ? null : (bool?)compatible,
                compatibility
            };
        }

        static object DescribeComponentTarget(ComponentTarget target)
        {
            if (target == null)
                return null;

            return new
            {
                targetPath = target.TargetPath,
                hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(target.GameObject.transform),
                gameObjectId = UnityApiAdapter.GetObjectIdOrZero(target.GameObject),
                componentType = target.ComponentTypeName,
                componentIndex = target.ComponentIndex,
                componentId = UnityApiAdapter.GetObjectIdOrZero(target.Component)
            };
        }

        static object DescribeGameObject(GameObject gameObject, string relativePath)
        {
            if (gameObject == null)
                return null;

            return new
            {
                name = gameObject.name,
                path = UiDiagnosticsHelper.GetHierarchyPath(gameObject.transform),
                relativePath,
                objectId = UnityApiAdapter.GetObjectIdOrZero(gameObject)
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
                assetPath = string.IsNullOrWhiteSpace(assetPath) ? null : assetPath,
                assetGuid = string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath),
                hierarchyPath = owner != null ? UiDiagnosticsHelper.GetHierarchyPath(owner.transform) : null
            };
        }

        static GameObject GetOwnerGameObject(Object obj)
        {
            return obj switch
            {
                GameObject gameObject => gameObject,
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

        static bool JsonEqual(object left, object right)
        {
            return JToken.DeepEquals(JToken.FromObject(left ?? new { nullValue = true }), JToken.FromObject(right ?? new { nullValue = true }));
        }

        static double ScoreText(string query, string text)
        {
            if (string.IsNullOrWhiteSpace(query))
                return 0.5;
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            string[] terms = query.Split(new[] { ' ', '\t', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (terms.Length == 0)
                return 0.5;

            int hits = terms.Count(term => text.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            return hits <= 0 ? 0 : Math.Min(0.95, 0.35 + (hits / (double)terms.Length) * 0.55);
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
                JTokenType.String when bool.TryParse(token.Value<string>(), out bool parsed) => parsed,
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
            return Math.Max(0, value);
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

            string value = token.ToString();
            return string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
        }

        static object DescribeColor(Color value) => new { r = value.r, g = value.g, b = value.b, a = value.a };

        static object DescribeVector2(Vector2 value) => new { x = value.x, y = value.y };

        static object DescribeVector3(Vector3 value) => new { x = value.x, y = value.y, z = value.z };

        static object DescribeVector4(Vector4 value) => new { x = value.x, y = value.y, z = value.z, w = value.w };

        static object DescribeRect(Rect value) => new { x = value.x, y = value.y, width = value.width, height = value.height };

        static object DescribeBounds(Bounds value) => new { center = DescribeVector3(value.center), size = DescribeVector3(value.size) };

        static string ToPascalCase(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return value;

            return char.ToUpperInvariant(value[0]) + value.Substring(1);
        }
    }
}
