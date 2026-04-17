using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Becool.UnityMcpLens.Editor.ToolRegistry
{
    /// <summary>
    /// Defines standard categories for organizing and filtering MCP tools.
    /// Categories are flags that can be combined to assign tools to multiple categories.
    /// </summary>
    /// <remarks>
    /// Categories enable:
    /// - User-selectable filtering in project settings
    /// - Organization of tools by functionality
    /// - Batch enabling/disabling of related tools
    ///
    /// Apply categories to tools via the <see cref="McpToolAttribute.Groups"/> property.
    /// Core category tools are always enabled and cannot be disabled by users.
    ///
    /// Use <see cref="ToolCategoryExtensions"/> for conversion between enum and string identifiers.
    /// </remarks>
    [Flags]
    enum ToolCategory
    {
        /// <summary>No category assigned. Tools without categories are always shown.</summary>
        None = 0,

        /// <summary>Core Unity functionality - essential tools that are always enabled.</summary>
        Core = 1 << 0,

        /// <summary>Script and code-related operations (reading, writing, editing C# files).</summary>
        Scripting = 1 << 1,

        /// <summary>Asset management and manipulation (prefabs, materials, textures, models).</summary>
        Assets = 1 << 2,

        /// <summary>Scene and GameObject operations (hierarchy manipulation, component management).</summary>
        Scene = 1 << 3,

        /// <summary>Editor functionality and workflow (menu items, editor state, console access).</summary>
        Editor = 1 << 4,

        /// <summary>Web and network operations (HTTP requests, downloads).</summary>
        Web = 1 << 5,

        /// <summary>External tool integrations and imports (model importers, external converters).</summary>
        External = 1 << 8,

        /// <summary>Validation and analysis tools (script validation, static analysis).</summary>
        Validation = 1 << 9,

        /// <summary>Debug and diagnostic tools (logging, profiling, troubleshooting).</summary>
        Debug = 1 << 10,

        /// <summary>Legacy compatibility tools retained under the historical category identifier.</summary>
        Assistant = 1 << 11
    }

    /// <summary>
    /// Extension methods for converting between <see cref="ToolCategory"/> enum values and string identifiers.
    /// </summary>
    /// <remarks>
    /// String identifiers are used in:
    /// - <see cref="McpToolAttribute.Groups"/> property
    /// - Serialization to settings
    /// - API compatibility
    ///
    /// All conversions use lowercase snake_case identifiers (e.g., "scripting", "scene").
    /// </remarks>
    static class ToolCategoryExtensions
    {
        /// <summary>
        /// Converts a <see cref="ToolCategory"/> enum value to its lowercase string identifier.
        /// </summary>
        /// <param name="category">The category to convert</param>
        /// <returns>Lowercase string identifier (e.g., "scripting", "assets")</returns>
        public static string ToStringId(this ToolCategory category)
        {
            return category switch
            {
                ToolCategory.Core => "core",
                ToolCategory.Scripting => "scripting",
                ToolCategory.Assets => "assets",
                ToolCategory.Scene => "scene",
                ToolCategory.Editor => "editor",
                ToolCategory.Web => "web",
                ToolCategory.External => "external",
                ToolCategory.Validation => "validation",
                ToolCategory.Debug => "debug",
                ToolCategory.Assistant => "assistant",
                _ => category.ToString().ToLowerInvariant()
            };
        }

        /// <summary>
        /// Converts a lowercase string identifier to its corresponding <see cref="ToolCategory"/> enum value.
        /// </summary>
        /// <param name="stringId">The lowercase string identifier (e.g., "scripting", "assets"). Case-insensitive.</param>
        /// <returns>The matching <see cref="ToolCategory"/> enum value, or <see cref="ToolCategory.None"/> if no match is found.</returns>
        public static ToolCategory FromStringId(string stringId)
        {
            return stringId?.ToLowerInvariant() switch
            {
                "core" => ToolCategory.Core,
                "scripting" => ToolCategory.Scripting,
                "assets" => ToolCategory.Assets,
                "scene" => ToolCategory.Scene,
                "editor" => ToolCategory.Editor,
                "web" => ToolCategory.Web,
                "external" => ToolCategory.External,
                "validation" => ToolCategory.Validation,
                "debug" => ToolCategory.Debug,
                "assistant" => ToolCategory.Assistant,
                _ => ToolCategory.None
            };
        }

        /// <summary>
        /// Converts an array of string identifiers to a combined <see cref="ToolCategory"/> enum flags value.
        /// </summary>
        /// <param name="categories">Array of lowercase string identifiers (e.g., ["scripting", "assets"]).</param>
        /// <returns>Combined <see cref="ToolCategory"/> flags representing all provided categories, or <see cref="ToolCategory.None"/> if array is null or empty.</returns>
        public static ToolCategory FromStringArray(string[] categories)
        {
            if (categories == null || categories.Length == 0)
                return ToolCategory.None;

            ToolCategory result = ToolCategory.None;
            foreach (var category in categories)
            {
                result |= FromStringId(category);
            }
            return result;
        }

        /// <summary>
        /// Converts a <see cref="ToolCategory"/> enum flags value to an array of lowercase string identifiers.
        /// </summary>
        /// <param name="categories">The category flags to convert.</param>
        /// <returns>Array of lowercase string identifiers (e.g., ["scripting", "assets"]), or empty array if <see cref="ToolCategory.None"/>.</returns>
        public static string[] ToStringArray(this ToolCategory categories)
        {
            if (categories == ToolCategory.None)
                return Array.Empty<string>();

            var result = new List<string>();
            foreach (ToolCategory value in Enum.GetValues(typeof(ToolCategory)))
            {
                if (value != ToolCategory.None && categories.HasFlag(value))
                {
                    result.Add(value.ToStringId());
                }
            }
            return result.ToArray();
        }
    }

    /// <summary>
    /// Static utilities for working with tool categories.
    /// </summary>
    static class ToolCategories
    {
        /// <summary>
        /// Gets all available categories as enum flags.
        /// </summary>
        public static readonly ToolCategory AllCategories =
            ToolCategory.Core | ToolCategory.Scripting | ToolCategory.Assets | ToolCategory.Scene |
            ToolCategory.Editor | ToolCategory.Web | ToolCategory.External |
            ToolCategory.Validation | ToolCategory.Debug | ToolCategory.Assistant;

        /// <summary>
        /// Categories that are always enabled (cannot be disabled by user).
        /// </summary>
        public static readonly ToolCategory AlwaysEnabledCategories = ToolCategory.Core;

        /// <summary>
        /// Category metadata for UI display.
        /// </summary>
        public static readonly Dictionary<ToolCategory, CategoryInfo> CategoryMetadata = new()
        {
            { ToolCategory.Core, new CategoryInfo("Core", "Essential Unity operations (always enabled)", true, true) },
            { ToolCategory.Scripting, new CategoryInfo("Scripting", "Script management, editing, and code operations", true, false) },
            { ToolCategory.Assets, new CategoryInfo("Assets", "Asset creation, modification, and management", true, false) },
            { ToolCategory.Scene, new CategoryInfo("Scene & GameObjects", "Scene operations and GameObject manipulation", true, false) },
            { ToolCategory.Editor, new CategoryInfo("Editor", "Unity Editor functionality and workflow tools", true, false) },
            { ToolCategory.Web, new CategoryInfo("Web & Network", "Web requests, downloads, and network operations", false, false) },
            { ToolCategory.External, new CategoryInfo("External Tools", "External tool integrations and imports", false, false) },
            { ToolCategory.Validation, new CategoryInfo("Validation", "Code validation, analysis, and quality tools", true, false) },
            { ToolCategory.Debug, new CategoryInfo("Debug & Diagnostics", "Debugging, diagnostics, and troubleshooting tools", false, false) },
            { ToolCategory.Assistant, new CategoryInfo("Legacy Compatibility", "Legacy compatibility tools exposed through Lens", true, false) }
        };

        /// <summary>
        /// EditorPrefs key for storing enabled categories
        /// </summary>
        const string EnabledCategoriesKey = "UnityMCP.EnabledCategories";

        /// <summary>
        /// Gets the currently enabled categories from EditorPrefs.
        /// If no preferences are set, returns default categories based on <see cref="CategoryInfo.EnabledByDefault"/>.
        /// Always includes <see cref="AlwaysEnabledCategories"/>.
        /// </summary>
        /// <returns>Combined <see cref="ToolCategory"/> flags representing all currently enabled categories.</returns>
        public static ToolCategory GetEnabledCategories()
        {
            var enabledString = EditorPrefs.GetString(EnabledCategoriesKey, "");
            var enabled = AlwaysEnabledCategories; // Always include core

            if (!string.IsNullOrEmpty(enabledString))
            {
                var savedCategories = enabledString.Split(',');
                foreach (var category in savedCategories)
                {
                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        enabled |= ToolCategoryExtensions.FromStringId(category.Trim());
                    }
                }
            }
            else
            {
                // First time - enable default categories
                var defaultCategories = CategoryMetadata
                    .Where(kvp => kvp.Value.EnabledByDefault)
                    .Select(kvp => kvp.Key);

                foreach (var category in defaultCategories)
                {
                    enabled |= category;
                }
            }

            return enabled;
        }

        /// <summary>
        /// Saves the enabled categories to EditorPrefs.
        /// Automatically ensures <see cref="AlwaysEnabledCategories"/> are included.
        /// </summary>
        /// <param name="enabledCategories">The combined category flags to save as enabled.</param>
        public static void SaveEnabledCategories(ToolCategory enabledCategories)
        {
            // Always ensure core categories are included
            enabledCategories |= AlwaysEnabledCategories;

            var categoriesString = string.Join(",", enabledCategories.ToStringArray());
            EditorPrefs.SetString(EnabledCategoriesKey, categoriesString);
        }

        /// <summary>
        /// Checks if a specific category is currently enabled.
        /// <see cref="ToolCategory.None"/> and <see cref="AlwaysEnabledCategories"/> always return true.
        /// </summary>
        /// <param name="category">The category to check (single flag, not combined flags).</param>
        /// <returns>True if the category is enabled or always enabled, false otherwise.</returns>
        public static bool IsCategoryEnabled(ToolCategory category)
        {
            if (category == ToolCategory.None) return true; // Uncategorized tools are always enabled
            if (AlwaysEnabledCategories.HasFlag(category)) return true;

            var enabled = GetEnabledCategories();
            return enabled.HasFlag(category);
        }

        /// <summary>
        /// Checks if a tool should be included based on its categories.
        /// A tool is included if ANY of its categories are enabled (OR logic).
        /// Uncategorized tools (<see cref="ToolCategory.None"/>) are always included.
        /// </summary>
        /// <param name="toolCategories">The combined category flags assigned to the tool.</param>
        /// <returns>True if the tool should be included, false otherwise.</returns>
        public static bool ShouldIncludeTool(ToolCategory toolCategories)
        {
            if (toolCategories == ToolCategory.None) return true; // Uncategorized tools

            var enabledCategories = GetEnabledCategories();

            // Tool is enabled if ANY of its categories are enabled
            return (toolCategories & enabledCategories) != ToolCategory.None;
        }

        /// <summary>
        /// Gets display-friendly category information for UI presentation.
        /// Returns metadata including display name, description, and enabled settings.
        /// </summary>
        /// <param name="category">The category to get information for (single flag, not combined flags).</param>
        /// <returns>A <see cref="CategoryInfo"/> instance with display metadata, or a generated instance for unknown categories.</returns>
        public static CategoryInfo GetCategoryInfo(ToolCategory category)
        {
            return CategoryMetadata.TryGetValue(category, out var info)
                ? info
                : new CategoryInfo(category.ToString(), $"Custom category: {category}", false, false);
        }



        /// <summary>
        /// Helper method to create category arrays for tool attributes.
        /// Filters out null or whitespace entries from the input array.
        /// </summary>
        /// <param name="categories">Variable number of category string identifiers.</param>
        /// <returns>Filtered array containing only non-empty category strings, or empty array if all entries are invalid.</returns>
        public static string[] CreateCategories(params string[] categories)
        {
            return categories?.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray() ?? Array.Empty<string>();
        }
    }

    /// <summary>
    /// Metadata about a tool category for UI display.
    /// Provides user-friendly information about categories including display names, descriptions, and default settings.
    /// </summary>
    class CategoryInfo
    {
        /// <summary>
        /// Gets the user-friendly display name for the category (e.g., "Scripting", "Scene and GameObjects").
        /// </summary>
        public string DisplayName
        {
            get;
        }

        /// <summary>
        /// Gets the description text explaining what the category includes.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Gets a value indicating whether this category is enabled by default for new projects.
        /// </summary>
        public bool EnabledByDefault { get; }

        /// <summary>
        /// Gets a value indicating whether this category is always enabled and cannot be disabled by users.
        /// </summary>
        public bool AlwaysEnabled { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CategoryInfo"/> class.
        /// </summary>
        /// <param name="displayName">The user-friendly display name for the category.</param>
        /// <param name="description">The description text explaining what the category includes.</param>
        /// <param name="enabledByDefault">Whether this category is enabled by default for new projects.</param>
        /// <param name="alwaysEnabled">Whether this category is always enabled and cannot be disabled.</param>
        public CategoryInfo(string displayName, string description, bool enabledByDefault, bool alwaysEnabled)
        {
            DisplayName = displayName;
            Description = description;
            EnabledByDefault = enabledByDefault;
            AlwaysEnabled = alwaysEnabled;
        }
    }

    /// <summary>
    /// Predefined category combinations for common tool types.
    /// Provides convenient constants for tools that belong to multiple categories.
    /// </summary>
    static class CategoryCombinations
    {
        /// <summary>
        /// Combination of Core and Scripting categories.
        /// Use for essential script-related tools.
        /// </summary>
        public static readonly ToolCategory CoreScripting = ToolCategory.Core | ToolCategory.Scripting;

        /// <summary>
        /// Combination of Core and Assets categories.
        /// Use for essential asset management tools.
        /// </summary>
        public static readonly ToolCategory CoreAssets = ToolCategory.Core | ToolCategory.Assets;

        /// <summary>
        /// Combination of Core and Scene categories.
        /// Use for essential scene and GameObject tools.
        /// </summary>
        public static readonly ToolCategory CoreScene = ToolCategory.Core | ToolCategory.Scene;

        /// <summary>
        /// Combination of Web and External categories.
        /// Use for tools that involve web requests and external integrations.
        /// </summary>
        public static readonly ToolCategory WebExternal = ToolCategory.Web | ToolCategory.External;

        /// <summary>
        /// Combination of Validation and Debug categories.
        /// Use for tools that perform validation, analysis, and debugging.
        /// </summary>
        public static readonly ToolCategory ValidationDebug = ToolCategory.Validation | ToolCategory.Debug;

    }
}
