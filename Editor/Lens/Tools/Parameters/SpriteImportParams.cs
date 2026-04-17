using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record ConfigureSpriteImportParams
    {
        [McpDescription("Texture asset path under Assets/ to configure as a sprite.", Required = true)]
        public string AssetPath { get; set; }

        [McpDescription("Optional sprite mode: Single, Sheet, or Multiple. Omit to preserve the current mode.", Required = false)]
        public string SpriteMode { get; set; }

        [McpDescription("Optional alpha/transparency import flag. Omit to preserve the current value.", Required = false)]
        public bool? AlphaIsTransparency { get; set; }

        [McpDescription("Optional filter mode: Point, Bilinear, or Trilinear. Omit to preserve the current value.", Required = false)]
        public string FilterMode { get; set; }

        [McpDescription("Optional texture compression: Uncompressed, Compressed, CompressedHQ, or CompressedLQ. Omit to preserve the current value.", Required = false)]
        public string Compression { get; set; }

        [McpDescription("Optional pixels-per-unit value. Omit to preserve the current value.", Required = false)]
        public float? PixelsPerUnit { get; set; }

        [McpDescription("When true, preserves existing multiple-sprite slicing metadata unless an explicit change would invalidate it.", Required = false)]
        public bool PreserveExistingSlicing { get; set; } = true;
    }
}
