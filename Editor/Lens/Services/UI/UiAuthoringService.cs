#nullable disable
using System;
using Becool.UnityMcpLens.Editor.Adapters.Unity.UI;
using Becool.UnityMcpLens.Editor.Models.UI;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.Helpers;
using UnityEditor.SceneManagement;

namespace Becool.UnityMcpLens.Editor.Services.UI
{
    sealed class UiAuthoringService
    {
        readonly UnityUiAuthoringAdapter m_Adapter;

        public UiAuthoringService(UnityUiAuthoringAdapter adapter)
        {
            m_Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public UiOperationResult PreviewEnsureHierarchy(UiEnsureHierarchyRequest request, ToolOperationTiming timing)
        {
            if (!TryRunEnsureHierarchy(request, previewOnly: true, timing, out var result))
                return result;

            return result;
        }

        public UiOperationResult ApplyEnsureHierarchy(UiEnsureHierarchyRequest request, ToolOperationTiming timing)
        {
            if (!TryRunEnsureHierarchy(request, previewOnly: false, timing, out var result))
                return result;

            return result;
        }

        public UiOperationResult PreviewLayoutProperties(UiLayoutPropertiesRequest request, ToolOperationTiming timing)
        {
            return RunLayoutProperties(request, previewOnly: true, timing);
        }

        public UiOperationResult ApplyLayoutProperties(UiLayoutPropertiesRequest request, ToolOperationTiming timing)
        {
            return RunLayoutProperties(request, previewOnly: false, timing);
        }

        public UiOperationResult VerifyScreenLayout(UiVerifyScreenLayoutRequest request, ToolOperationTiming timing)
        {
            object data;
            string error;
            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryVerifyScreenLayout(request, out data, out error))
                {
                    return UiOperationResult.Error(
                        $"Failed to verify screen layout: {error}",
                        "verify_failed",
                        new { errorKind = "verify_failed", error });
                }
            }

            return UiOperationResult.Ok("Verified UI screen layout.", data);
        }

        bool TryRunEnsureHierarchy(UiEnsureHierarchyRequest request, bool previewOnly, ToolOperationTiming timing, out UiOperationResult result)
        {
            result = null;
            if (request?.Target == null)
            {
                result = UiOperationResult.Error("target is required.", "target_required");
                return false;
            }

            if (request.Nodes == null || request.Nodes.Length == 0)
            {
                result = UiOperationResult.Error("nodes is required.", "nodes_required");
                return false;
            }

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryEnsureHierarchy(request, previewOnly, out var targetRoot, out var nodes, out var applied, out var error))
                {
                    result = UiOperationResult.Error(
                        $"Failed to {(previewOnly ? "preview" : "apply")} UI hierarchy changes: {error}",
                        "ensure_hierarchy_failed",
                        new { errorKind = "ensure_hierarchy_failed", error });
                    return false;
                }

                if (!previewOnly && applied)
                {
                    EditorSceneManager.MarkSceneDirty(targetRoot.scene);
                    EditorSceneManager.SaveOpenScenes();
                }

                result = UiOperationResult.Ok(
                    previewOnly
                        ? $"Previewed UI hierarchy under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                        : applied
                            ? $"Applied UI hierarchy changes under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'."
                            : $"No UI hierarchy changes were required under '{UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform)}'.",
                    new
                    {
                        target = UiDiagnosticsHelper.GetHierarchyPath(targetRoot.transform),
                        applied = !previewOnly && applied,
                        willModify = applied,
                        nodes = nodes.ToArray()
                    });
                return true;
            }
        }

        UiOperationResult RunLayoutProperties(UiLayoutPropertiesRequest request, bool previewOnly, ToolOperationTiming timing)
        {
            if (string.IsNullOrWhiteSpace(request?.Target))
            {
                return UiOperationResult.Error("target is required.", "target_required");
            }

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryApplyLayoutProperties(request, previewOnly, out var targetRoot, out var targetTransform, out var changes, out var applied, out var error))
                {
                    return UiOperationResult.Error(
                        $"Failed to {(previewOnly ? "preview" : "apply")} UI layout properties: {error}",
                        "layout_properties_failed",
                        new { errorKind = "layout_properties_failed", error });
                }

                if (!previewOnly && applied)
                {
                    EditorSceneManager.MarkSceneDirty(targetRoot.scene);
                    EditorSceneManager.SaveOpenScenes();
                }

                string hierarchyPath = UiDiagnosticsHelper.GetHierarchyPath(targetTransform);
                return UiOperationResult.Ok(
                    previewOnly
                        ? $"Previewed UI layout properties on '{hierarchyPath}'."
                        : applied
                            ? $"Applied UI layout properties on '{hierarchyPath}'."
                            : $"No UI layout changes were required on '{hierarchyPath}'.",
                    new
                    {
                        target = hierarchyPath,
                        applied = !previewOnly && applied,
                        willModify = applied,
                        changes = changes.ToArray()
                    });
            }
        }
    }
}
