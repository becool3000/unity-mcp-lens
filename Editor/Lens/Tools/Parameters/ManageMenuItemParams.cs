using System;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    /// <summary>
    /// Actions that can be performed by the Unity.ManageMenuItem tool.
    /// </summary>
    public enum MenuItemAction
    {
        /// <summary>
        /// Execute a specific menu item.
        /// </summary>
        Execute,

        /// <summary>
        /// List available menu items (with optional search).
        /// </summary>
        List,

        /// <summary>
        /// Check if a menu item exists.
        /// </summary>
        Exists,

        /// <summary>
        /// Force refresh of the menu cache.
        /// </summary>
        Refresh
    }

    /// <summary>
    /// Parameters for the Unity.ManageMenuItem tool.
    /// </summary>
    public record ManageMenuItemParams
    {
        /// <summary>
        /// Gets or sets the operation to perform.
        /// </summary>
        [McpDescription("Operation to perform", Required = true, Default = MenuItemAction.Execute)]
        public MenuItemAction Action { get; set; } = MenuItemAction.Execute;

        /// <summary>
        /// Gets or sets the menu path to execute/check (e.g., 'Assets/Create/C# Script', 'File/Save Project').
        /// </summary>
        [McpDescription("Menu path to execute/check (e.g., 'Assets/Create/C# Script', 'File/Save Project')")]
        public string MenuPath { get; set; }

        /// <summary>
        /// Gets or sets the filter string for list action (case-insensitive search).
        /// </summary>
        [McpDescription("Filter string for list action (case-insensitive search)")]
        public string Search { get; set; }

        /// <summary>
        /// Gets or sets whether to force refresh of menu cache before operation.
        /// </summary>
        [McpDescription("Force refresh of menu cache before operation")]
        public bool Refresh { get; set; } = false;
    }
}
