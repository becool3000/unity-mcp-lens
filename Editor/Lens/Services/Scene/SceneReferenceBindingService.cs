#nullable disable
using System;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Scene;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Models.Scene;
using Becool.UnityMcpLens.Editor.Services;
using UnityEditor.SceneManagement;

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

        SceneReferenceBindingOperationResult Run(SceneReferenceBindingRequest request, bool previewOnly, ToolOperationTiming timing)
        {
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
                    EditorSceneManager.MarkSceneDirty(targetRoot.scene);
                    EditorSceneManager.SaveOpenScenes();
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
                        bindings = bindings.ToArray()
                    });
            }
        }
    }
}
