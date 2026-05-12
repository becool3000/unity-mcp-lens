#nullable disable
using System;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Scene;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.Scene;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Utils.Scene;

namespace Becool.UnityMcpLens.Editor.Services.Scene
{
    sealed class SceneReferenceBindingService
    {
        readonly UnitySceneReferenceBindingAdapter m_Adapter;

        public SceneReferenceBindingService(UnitySceneReferenceBindingAdapter adapter)
        {
            m_Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public SceneReferenceBindingOperationResult Preview(SceneReferenceBindingRequest request, ToolOperationTiming timing)
        {
            return Run(request, previewOnly: true, timing);
        }

        public SceneReferenceBindingOperationResult Apply(SceneReferenceBindingRequest request, ToolOperationTiming timing)
        {
            return Run(request, previewOnly: false, timing);
        }

        public SceneReferenceBindingOperationResult PreviewInstantiatePrefabAndBind(ScenePrefabInstantiateAndBindRequest request, ToolOperationTiming timing)
        {
            return RunInstantiatePrefabAndBind(request, previewOnly: true, timing);
        }

        public SceneReferenceBindingOperationResult ApplyInstantiatePrefabAndBind(ScenePrefabInstantiateAndBindRequest request, ToolOperationTiming timing)
        {
            return RunInstantiatePrefabAndBind(request, previewOnly: false, timing);
        }

        public SceneReferenceBindingOperationResult VerifySerializedReferences(SceneSerializedReferenceVerifyRequest request, ToolOperationTiming timing)
        {
            if (request?.Target == null)
            {
                return SceneReferenceBindingOperationResult.Error("target is required.", "target_required");
            }

            if (request.Checks == null || request.Checks.Length == 0)
            {
                return SceneReferenceBindingOperationResult.Error("checks is required.", "checks_required");
            }

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryVerifySerializedReferences(request, out var targetRoot, out var checks, out var passed, out var error))
                {
                    return SceneReferenceBindingOperationResult.Error(
                        $"Failed to verify serialized references: {error}",
                        "serialized_reference_verify_failed",
                        new { errorKind = "serialized_reference_verify_failed", error });
                }

                return SceneReferenceBindingOperationResult.Ok(
                    passed
                        ? $"Verified serialized references on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                        : $"Serialized reference verification failed on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.",
                    new
                    {
                        target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                        passed,
                        checkCount = checks.Count,
                        checks = checks.ToArray()
                    });
            }
        }

        SceneReferenceBindingOperationResult Run(SceneReferenceBindingRequest request, bool previewOnly, ToolOperationTiming timing)
        {
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            if (request?.Target == null)
            {
                return SceneReferenceBindingOperationResult.Error("target is required.", "target_required");
            }

            if (request.Bindings == null || request.Bindings.Length == 0)
            {
                return SceneReferenceBindingOperationResult.Error("bindings is required.", "bindings_required");
            }

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryBindReferences(request, previewOnly, out var targetRoot, out var bindings, out var applied, out var error))
                {
                    return SceneReferenceBindingOperationResult.Error(
                        $"Failed to {(previewOnly ? "preview" : "apply")} serialized reference bindings: {error}",
                        "serialized_reference_binding_failed",
                        new { errorKind = "serialized_reference_binding_failed", error });
                }

                if (!previewOnly && applied)
                {
                    SceneDirtyStateUtility.MarkSceneDirty(targetRoot);
                }

                return SceneReferenceBindingOperationResult.Ok(
                    previewOnly
                        ? $"Previewed serialized reference bindings on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                        : applied
                            ? $"Applied serialized reference bindings on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                            : $"No serialized reference bindings changed on '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.",
                    new
                    {
                        target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                        applied = !previewOnly && applied,
                        willModify = applied,
                        bindings = bindings.ToArray(),
                        dirtyStateBefore,
                        dirtyStateAfter = SceneDirtyStateUtility.CaptureLoadedScenes(),
                        saveState = SceneDirtyStateUtility.BuildSaveState()
                    });
            }
        }

        SceneReferenceBindingOperationResult RunInstantiatePrefabAndBind(ScenePrefabInstantiateAndBindRequest request, bool previewOnly, ToolOperationTiming timing)
        {
            object dirtyStateBefore = SceneDirtyStateUtility.CaptureLoadedScenes();
            if (string.IsNullOrWhiteSpace(request?.PrefabPath))
            {
                return SceneReferenceBindingOperationResult.Error("prefabPath is required.", "prefab_path_required");
            }

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryInstantiatePrefabAndBind(request, previewOnly, out var instanceRoot, out var data, out var applied, out var error))
                {
                    return SceneReferenceBindingOperationResult.Error(
                        $"Failed to {(previewOnly ? "preview" : "apply")} prefab instantiate/bind: {error}",
                        "prefab_instantiate_bind_failed",
                        new { errorKind = "prefab_instantiate_bind_failed", error });
                }

                if (!previewOnly && applied && instanceRoot != null)
                {
                    SceneDirtyStateUtility.MarkSceneDirty(instanceRoot);
                }

                data = AddDirtyAndSaveState(data, dirtyStateBefore);
                return SceneReferenceBindingOperationResult.Ok(
                    previewOnly
                        ? $"Previewed prefab instantiate/bind for '{request.PrefabPath}'."
                        : applied
                            ? $"Applied prefab instantiate/bind for '{request.PrefabPath}'."
                            : $"No prefab instantiate/bind changes were required for '{request.PrefabPath}'.",
                    data);
            }
        }

        static object AddDirtyAndSaveState(object data, object dirtyStateBefore)
        {
            var root = Newtonsoft.Json.Linq.JObject.FromObject(data ?? new { });
            root["dirtyStateBefore"] = Newtonsoft.Json.Linq.JToken.FromObject(dirtyStateBefore);
            root["dirtyStateAfter"] = Newtonsoft.Json.Linq.JToken.FromObject(SceneDirtyStateUtility.CaptureLoadedScenes());
            root["saveState"] = Newtonsoft.Json.Linq.JToken.FromObject(SceneDirtyStateUtility.BuildSaveState());
            return root;
        }
    }
}
