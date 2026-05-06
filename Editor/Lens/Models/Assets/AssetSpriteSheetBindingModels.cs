#nullable disable
namespace Becool.UnityMcpLens.Editor.Models.Assets
{
    sealed class AssetSpriteSheetAndBindRequest
    {
        public string AssetPath { get; set; }
        public int FrameCount { get; set; }
        public int FrameWidth { get; set; }
        public int FrameHeight { get; set; }
        public int PaddingX { get; set; }
        public int PaddingY { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public string SpriteNamePrefix { get; set; }
        public float? PixelsPerUnit { get; set; }
        public bool? MipmapEnabled { get; set; }
        public bool? AlphaIsTransparency { get; set; }
        public string Compression { get; set; }
        public string FilterMode { get; set; }
        public string WrapMode { get; set; }
        public string TargetAssetPath { get; set; }
        public string TargetFieldName { get; set; }
    }

    sealed class AssetSpriteArrayBindingVerifyRequest
    {
        public string TargetAssetPath { get; set; }
        public string TargetFieldName { get; set; }
        public int? ExpectedCount { get; set; }
        public string ExpectedTextureName { get; set; }
        public string ExpectedTextureGuid { get; set; }
        public string[] ExpectedSpriteNames { get; set; } = new string[0];
    }

    sealed class AssetSpritePipelineOperationResult
    {
        public bool success { get; set; }
        public string message { get; set; }
        public object data { get; set; }
        public string errorKind { get; set; }
        public object errorData { get; set; }

        public static AssetSpritePipelineOperationResult Ok(string message, object data = null)
        {
            return new AssetSpritePipelineOperationResult
            {
                success = true,
                message = message,
                data = data
            };
        }

        public static AssetSpritePipelineOperationResult Error(string message, string errorKind, object errorData = null)
        {
            return new AssetSpritePipelineOperationResult
            {
                success = false,
                message = message,
                errorKind = errorKind,
                errorData = errorData ?? new { errorKind }
            };
        }
    }
}
