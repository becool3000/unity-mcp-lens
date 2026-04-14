using System.Collections.Generic;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    public record TileBuildSetParams
    {
        [McpDescription("Sprite sheet texture path under Assets/. Required for TileSet or Both output modes.", Required = false)]
        public string SourceTexturePath { get; set; }

        [McpDescription("Optional sprite asset paths for classic tile creation. Only used for TileAssets mode.", Required = false)]
        public List<string> SourceSpritePaths { get; set; }

        [McpDescription("Optional tile slice width in pixels. Provide with SliceCellHeight to apply deterministic grid slicing.", Required = false)]
        public int? SliceCellWidth { get; set; }

        [McpDescription("Optional tile slice height in pixels. Provide with SliceCellWidth to apply deterministic grid slicing.", Required = false)]
        public int? SliceCellHeight { get; set; }

        [McpDescription("Optional horizontal slice padding in pixels.", Required = false)]
        public int SlicePaddingX { get; set; }

        [McpDescription("Optional vertical slice padding in pixels.", Required = false)]
        public int SlicePaddingY { get; set; }

        [McpDescription("Optional horizontal slice offset in pixels.", Required = false)]
        public int SliceOffsetX { get; set; }

        [McpDescription("Optional vertical slice offset in pixels.", Required = false)]
        public int SliceOffsetY { get; set; }

        [McpDescription("Optional pixels-per-unit import override.", Required = false)]
        public float? PixelsPerUnit { get; set; }

        [McpDescription("Optional filter mode override: Point, Bilinear, or Trilinear.", Required = false)]
        public string FilterMode { get; set; }

        [McpDescription("Optional compression override: Uncompressed, Compressed, CompressedHQ, or CompressedLQ.", Required = false)]
        public string Compression { get; set; }

        [McpDescription("Optional alpha-is-transparency import override.", Required = false)]
        public bool? AlphaIsTransparency { get; set; }

        [McpDescription("Output mode: TileAssets, TileSet, or Both.", Required = false)]
        public string OutputMode { get; set; } = "TileAssets";

        [McpDescription("Folder for classic Tile assets. Defaults next to the source texture when omitted.", Required = false)]
        public string TileOutputFolder { get; set; }

        [McpDescription("Path for the Unity 6 .tileset asset. Defaults next to the source texture when omitted.", Required = false)]
        public string TileSetAssetPath { get; set; }

        [McpDescription("When true, create or update a palette prefab for classic Tile assets.", Required = false)]
        public bool CreatePalette { get; set; }

        [McpDescription("Folder for the classic palette prefab. Defaults to TileOutputFolder when omitted.", Required = false)]
        public string PaletteFolder { get; set; }

        [McpDescription("Classic palette prefab name without extension. Defaults from the source asset name.", Required = false)]
        public string PaletteName { get; set; }

        [McpDescription("Palette grid cell layout: Rectangle, HexagonalPointTop, HexagonalFlatTop, Isometric, or IsometricZAsY.", Required = false)]
        public string CellLayout { get; set; } = "Rectangle";

        [McpDescription("Palette cell sizing: Automatic or Manual.", Required = false)]
        public string CellSizing { get; set; } = "Automatic";

        [McpDescription("Palette cell size X.", Required = false)]
        public float CellSizeX { get; set; } = 1f;

        [McpDescription("Palette cell size Y.", Required = false)]
        public float CellSizeY { get; set; } = 1f;

        [McpDescription("Palette cell size Z.", Required = false)]
        public float CellSizeZ { get; set; }

        [McpDescription("Transparency sort mode: Default, Orthographic, or CustomAxis.", Required = false)]
        public string SortMode { get; set; } = "Default";

        [McpDescription("Transparency sort axis X.", Required = false)]
        public float SortAxisX { get; set; }

        [McpDescription("Transparency sort axis Y.", Required = false)]
        public float SortAxisY { get; set; }

        [McpDescription("Transparency sort axis Z.", Required = false)]
        public float SortAxisZ { get; set; } = 1f;

        [McpDescription("Classic Tile collider type: None, Sprite, or Grid.", Required = false)]
        public string TileColliderType { get; set; } = "None";

        [McpDescription("When true, include classic tile asset paths and generated tile names in the response.", Required = false)]
        public bool IncludeCreatedAssetPaths { get; set; }
    }
}
