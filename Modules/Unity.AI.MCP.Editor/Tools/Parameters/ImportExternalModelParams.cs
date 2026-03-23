using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.ImportExternalModel tool.
    /// </summary>
    public record ImportExternalModelParams
    {
        /// <summary>
        /// Gets or sets the simple name of the asset (must be a single word or id string, no spaces).
        /// </summary>
        [McpDescription("Simple name of the asset, needs to be a single word or id string, no spaces", Required = true)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the URL to the FBX file to import and instantiate (can be a local file or a URL).
        /// </summary>
        [McpDescription("The url to the fbx file to import and instantiate, can be a local file or a url", Required = true)]
        public string FbxUrl { get; set; }

        /// <summary>
        /// Gets or sets the desired height of the asset in the scene.
        /// </summary>
        [McpDescription("Float value that represents the desired height of the asset in the scene", Required = true)]
        public float Height { get; set; }

        /// <summary>
        /// Gets or sets the URL to the albedo texture file to import and instantiate (can be a local file or a URL).
        /// </summary>
        [McpDescription("The url to the albedo texture file to import and instantiate, can be a local file or a url", Required = false)]
        public string AlbedoTextureUrl { get; set; }
    }
}
