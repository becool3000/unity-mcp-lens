using System;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Actions that can be performed by the Unity.ManageShader tool.
    /// </summary>
    public enum ShaderAction
    {
        /// <summary>
        /// Create a new shader.
        /// </summary>
        Create,

        /// <summary>
        /// Read an existing shader.
        /// </summary>
        Read,

        /// <summary>
        /// Update an existing shader.
        /// </summary>
        Update,

        /// <summary>
        /// Delete a shader.
        /// </summary>
        Delete
    }

    /// <summary>
    /// Parameters for the Unity.ManageShader tool.
    /// </summary>
    public record ManageShaderParams
    {
        /// <summary>
        /// Gets or sets the operation to perform.
        /// </summary>
        [McpDescription("Operation to perform", Required = true)]
        public ShaderAction Action { get; set; }

        /// <summary>
        /// Gets or sets the shader name (without .shader extension).
        /// </summary>
        [McpDescription("Shader name (without .shader extension)", Required = true)]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the relative path under Assets/.
        /// </summary>
        [McpDescription("Relative path under Assets/", Required = false)]
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the shader contents (plain text).
        /// </summary>
        [McpDescription("Shader contents (plain text)", Required = false)]
        public string Contents { get; set; }

        /// <summary>
        /// Gets or sets whether contents are base64 encoded.
        /// </summary>
        [McpDescription("Whether contents are base64 encoded", Required = false)]
        public bool ContentsEncoded { get; set; }

        /// <summary>
        /// Gets or sets the base64 encoded shader contents.
        /// </summary>
        [McpDescription("Base64 encoded shader contents", Required = false)]
        public string EncodedContents { get; set; }
    }
}
