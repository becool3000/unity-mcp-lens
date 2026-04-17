using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record TilemapPaintParams
    {
        [McpDescription("Optional scene path under Assets/. Uses the active scene when omitted.", Required = false)]
        public string ScenePath { get; set; }

        [McpDescription("Root Grid object name.", Required = false)]
        public string GridName { get; set; } = "LevelGrid";

        [McpDescription("Target tilemap child name.", Required = true)]
        public string LayerName { get; set; }

        [McpDescription("Paint or clear commands to apply.", Required = true)]
        public List<TilemapPaintCommandParams> Commands { get; set; }

        [McpDescription("When true, save the scene after painting.", Required = false)]
        public bool SaveScene { get; set; } = true;

        [McpDescription("When true, include changed cell coordinates in the response.", Required = false)]
        public bool IncludeChangedCells { get; set; }
    }

    public record TilemapPaintCommandParams
    {
        [McpDescription("Command type: set_many, fill_rect, clear_many, or clear_rect.", Required = true)]
        public string Type { get; set; }

        [McpDescription("Direct TileBase asset path for set commands.", Required = false)]
        public string TileAssetPath { get; set; }

        [McpDescription("Unity 6 .tileset path for set commands resolved by TileName.", Required = false)]
        public string TileSetAssetPath { get; set; }

        [McpDescription("TileBase name within a .tileset asset.", Required = false)]
        public string TileName { get; set; }

        [McpDescription("Cell coordinates as [[x,y], ...] for set_many or clear_many.", Required = false)]
        public List<int[]> Cells { get; set; }

        [McpDescription("Rectangle origin X for fill_rect or clear_rect.", Required = false)]
        public int X { get; set; }

        [McpDescription("Rectangle origin Y for fill_rect or clear_rect.", Required = false)]
        public int Y { get; set; }

        [McpDescription("Rectangle width for fill_rect or clear_rect.", Required = false)]
        public int Width { get; set; }

        [McpDescription("Rectangle height for fill_rect or clear_rect.", Required = false)]
        public int Height { get; set; }
    }
}
