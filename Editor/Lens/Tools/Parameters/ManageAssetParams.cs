using System;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.ToolRegistry.Parameters
{
    /// <summary>
    /// Asset actions that can be performed by the Unity.ManageAsset tool.
    /// </summary>
    public enum AssetAction
    {
        /// <summary>
        /// Import or re-import an asset.
        /// </summary>
        Import,

        /// <summary>
        /// Create a new asset.
        /// </summary>
        Create,

        /// <summary>
        /// Modify an existing asset's properties.
        /// </summary>
        Modify,

        /// <summary>
        /// Delete an asset.
        /// </summary>
        Delete,

        /// <summary>
        /// Duplicate an existing asset.
        /// </summary>
        Duplicate,

        /// <summary>
        /// Move an asset to a new location.
        /// </summary>
        Move,

        /// <summary>
        /// Rename an asset.
        /// </summary>
        Rename,

        /// <summary>
        /// Search for assets matching criteria.
        /// </summary>
        Search,

        /// <summary>
        /// Get information about an asset.
        /// </summary>
        GetInfo,

        /// <summary>
        /// Create a new folder.
        /// </summary>
        CreateFolder,

        /// <summary>
        /// Get components from a prefab or GameObject asset.
        /// </summary>
        GetComponents
    }

    /// <summary>
    /// Demonstrates typed parameter record for Unity.ManageAsset tool.
    /// This shows how to use record types with enums for clean, type-safe parameters.
    /// </summary>
    public record ManageAssetParams
    {
        /// <summary>
        /// Gets or sets the operation to perform (e.g., 'Import', 'Create', 'Modify', 'Delete', etc.).
        /// </summary>
        [McpDescription("Operation to perform (e.g., 'Import', 'Create', 'Modify', 'Delete', 'Duplicate', 'Move', 'Rename', 'Search', 'GetInfo', 'CreateFolder', 'GetComponents').", Required = true)]
        public AssetAction Action { get; set; } = AssetAction.GetInfo;

        /// <summary>
        /// Gets or sets the asset path relative to project root.
        /// </summary>
        [McpDescription("Asset path relative to project root", Required = true)]
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the type of asset to create or work with.
        /// </summary>
        [McpDescription("Type of asset to create or work with")]
        public string AssetType { get; set; }

        /// <summary>
        /// Gets or sets the properties to set on the asset.
        /// </summary>
        [McpDescription("Properties to set on the asset")]
        public JObject Properties { get; set; }

        /// <summary>
        /// Gets or sets the destination path for move/copy operations.
        /// </summary>
        [McpDescription("Destination path for move/copy operations")]
        public string Destination { get; set; }

        /// <summary>
        /// Gets or sets whether to generate a preview when getting asset info.
        /// </summary>
        [McpDescription("Generate preview when getting asset info")]
        public bool GeneratePreview { get; set; } = false;

        // Search-specific parameters
        /// <summary>
        /// Gets or sets the search pattern for finding assets.
        /// </summary>
        [McpDescription("Search pattern for finding assets")]
        public string SearchPattern { get; set; }

        /// <summary>
        /// Gets or sets the filter by asset type.
        /// </summary>
        [McpDescription("Filter by asset type")]
        public string FilterType { get; set; }

        /// <summary>
        /// Gets or sets the filter for assets modified after this date.
        /// </summary>
        [McpDescription("Filter assets modified after this date")]
        public string FilterDate { get; set; }

        /// <summary>
        /// Gets or sets the number of results per page for search operations.
        /// </summary>
        [McpDescription("Number of results per page")]
        public int PageSize { get; set; } = 50;

        /// <summary>
        /// Gets or sets the page number (1-based) for search operations.
        /// </summary>
        [McpDescription("Page number (1-based)")]
        public int PageNumber { get; set; } = 1;
    }
}