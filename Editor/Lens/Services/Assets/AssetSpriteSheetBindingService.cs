#nullable disable
using System;
using Becool.UnityMcpLens.Editor.Adapters.Unity.Assets;
using Becool.UnityMcpLens.Editor.Models.Assets;
using Becool.UnityMcpLens.Editor.Services;

namespace Becool.UnityMcpLens.Editor.Services.Assets
{
    sealed class AssetSpriteSheetBindingService
    {
        readonly UnityAssetSpriteSheetBindingAdapter m_Adapter;

        public AssetSpriteSheetBindingService(UnityAssetSpriteSheetBindingAdapter adapter)
        {
            m_Adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public AssetSpritePipelineOperationResult PreviewImportSpriteSheetAndBind(AssetSpriteSheetAndBindRequest request, ToolOperationTiming timing)
        {
            return RunImportSpriteSheetAndBind(request, previewOnly: true, timing);
        }

        public AssetSpritePipelineOperationResult ApplyImportSpriteSheetAndBind(AssetSpriteSheetAndBindRequest request, ToolOperationTiming timing)
        {
            return RunImportSpriteSheetAndBind(request, previewOnly: false, timing);
        }

        public AssetSpritePipelineOperationResult VerifySpriteArrayBinding(AssetSpriteArrayBindingVerifyRequest request, ToolOperationTiming timing)
        {
            if (string.IsNullOrWhiteSpace(request?.TargetAssetPath))
                return AssetSpritePipelineOperationResult.Error("targetAssetPath is required.", "target_asset_path_required");

            if (string.IsNullOrWhiteSpace(request.TargetFieldName))
                return AssetSpritePipelineOperationResult.Error("targetFieldName is required.", "target_field_name_required");

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryVerifySpriteArrayBinding(request, out object data, out bool passed, out string error))
                {
                    return AssetSpritePipelineOperationResult.Error(
                        $"Failed to verify sprite-array binding: {error}",
                        "sprite_array_binding_verify_failed",
                        new { errorKind = "sprite_array_binding_verify_failed", error });
                }

                return AssetSpritePipelineOperationResult.Ok(
                    passed
                        ? $"Verified sprite-array binding on '{request.TargetAssetPath}'."
                        : $"Sprite-array binding verification failed on '{request.TargetAssetPath}'.",
                    data);
            }
        }

        AssetSpritePipelineOperationResult RunImportSpriteSheetAndBind(AssetSpriteSheetAndBindRequest request, bool previewOnly, ToolOperationTiming timing)
        {
            if (string.IsNullOrWhiteSpace(request?.AssetPath))
                return AssetSpritePipelineOperationResult.Error("assetPath is required.", "asset_path_required");

            if (request.FrameCount <= 0)
                return AssetSpritePipelineOperationResult.Error("frameCount must be greater than zero.", "invalid_frame_count");

            if (request.FrameWidth <= 0 || request.FrameHeight <= 0)
                return AssetSpritePipelineOperationResult.Error("frameWidth and frameHeight must be greater than zero.", "invalid_frame_size");

            if (string.IsNullOrWhiteSpace(request.TargetAssetPath))
                return AssetSpritePipelineOperationResult.Error("targetAssetPath is required.", "target_asset_path_required");

            if (string.IsNullOrWhiteSpace(request.TargetFieldName))
                return AssetSpritePipelineOperationResult.Error("targetFieldName is required.", "target_field_name_required");

            using (timing.Measure("adapter"))
            {
                if (!m_Adapter.TryImportSpriteSheetAndBind(request, previewOnly, out object data, out bool willModify, out string error))
                {
                    return AssetSpritePipelineOperationResult.Error(
                        $"Failed to {(previewOnly ? "preview" : "apply")} sprite-sheet import/bind: {error}",
                        "sprite_sheet_import_bind_failed",
                        new { errorKind = "sprite_sheet_import_bind_failed", error });
                }

                return AssetSpritePipelineOperationResult.Ok(
                    previewOnly
                        ? $"Previewed sprite-sheet import/bind for '{request.AssetPath}'."
                        : willModify
                            ? $"Applied sprite-sheet import/bind for '{request.AssetPath}'."
                            : $"No sprite-sheet import/bind changes were required for '{request.AssetPath}'.",
                    data);
            }
        }
    }
}
