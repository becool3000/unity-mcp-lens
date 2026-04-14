using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    public record TilemapSetupParams
    {
        [McpDescription("Optional scene path under Assets/. Uses the active scene when omitted.", Required = false)]
        public string ScenePath { get; set; }

        [McpDescription("Root Grid object name.", Required = false)]
        public string GridName { get; set; } = "LevelGrid";

        [McpDescription("Grid cell size X.", Required = false)]
        public float CellSizeX { get; set; } = 1f;

        [McpDescription("Grid cell size Y.", Required = false)]
        public float CellSizeY { get; set; } = 1f;

        [McpDescription("Grid cell size Z.", Required = false)]
        public float CellSizeZ { get; set; }

        [McpDescription("Ground tilemap child name.", Required = false)]
        public string GroundLayerName { get; set; } = "Ground";

        [McpDescription("Walls tilemap child name.", Required = false)]
        public string WallsLayerName { get; set; } = "Walls";

        [McpDescription("Overhead tilemap child name.", Required = false)]
        public string OverheadLayerName { get; set; } = "Overhead";

        [McpDescription("Sorting layer name to apply to all three tilemaps.", Required = false)]
        public string SortingLayerName { get; set; } = "Default";

        [McpDescription("Ground sorting order.", Required = false)]
        public int GroundOrder { get; set; }

        [McpDescription("Walls sorting order.", Required = false)]
        public int WallsOrder { get; set; } = 1;

        [McpDescription("Overhead sorting order.", Required = false)]
        public int OverheadOrder { get; set; } = 2;

        [McpDescription("When true, ensure a wall TilemapCollider2D exists and is enabled.", Required = false)]
        public bool AddWallCollider { get; set; } = true;

        [McpDescription("When true and wall colliders are enabled, configure a CompositeCollider2D workflow.", Required = false)]
        public bool UseCompositeCollider { get; set; } = true;

        [McpDescription("When true, save the scene after setup.", Required = false)]
        public bool SaveScene { get; set; } = true;

        [McpDescription("When true, include created object paths in the response.", Required = false)]
        public bool IncludeObjectPaths { get; set; }
    }
}
