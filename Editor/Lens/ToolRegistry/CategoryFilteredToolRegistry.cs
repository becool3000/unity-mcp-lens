using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.ToolRegistry
{
    /// <summary>
    /// Extension to McpToolRegistry that provides category-based filtering.
    /// Works alongside the existing registry without modifying core functionality.
    /// </summary>
    static class CategoryFilteredToolRegistry
    {
        /// <summary>
        /// Event fired when category filters change
        /// </summary>
        public static event Action CategoryFiltersChanged;

        /// <summary>
        /// Gets available tools filtered by currently enabled categories
        /// </summary>
        /// <returns>Array of filtered tool information objects</returns>
        public static object[] GetFilteredAvailableTools()
        {
            // Use the consolidated logic from GetToolsByCategory and flatten the result
            var toolsByCategory = GetToolsByCategory(includeDisabledTools: false);
            var allTools = new List<object>();

            foreach (var categoryTools in toolsByCategory.Values)
            {
                allTools.AddRange(categoryTools);
            }

            return allTools.OrderBy(t => ((dynamic)t).name).ToArray();
        }

        /// <summary>
        /// Gets tools grouped by category for UI display
        /// </summary>
        /// <param name="includeDisabledTools">If true, includes all tools regardless of individual tool and category filter settings</param>
        /// <returns>Dictionary with category as key and tools as value</returns>
        public static Dictionary<string, object[]> GetToolsByCategory(bool includeDisabledTools = false)
        {
            var allTools = McpToolRegistry.GetAvailableTools(ignoreEnabledState: includeDisabledTools);
            var toolsByCategory = new Dictionary<string, List<object>>();

            foreach (var toolObj in allTools)
            {
                var toolDict = toolObj as dynamic;
                if (toolDict == null) continue;

                string toolName = toolDict.name;
                var toolHandler = McpToolRegistry.GetTool(toolName);
                if (toolHandler == null) continue;

                // When includeDisabledTools is false, skip tools that don't pass category filters
                if (!includeDisabledTools && !ShouldIncludeTool(toolHandler))
                    continue;

                // Get categories for this tool
                var categories = GetToolCategories(toolHandler);
                var categoryStrings = categories.ToStringArray();

                // If tool has no categories, put it in "Uncategorized"
                if (categoryStrings.Length == 0)
                {
                    categoryStrings = new[] { "uncategorized" };
                }

                // Create enhanced tool with category info
                var enhancedTool = new
                {
                    name = toolDict.name,
                    description = toolDict.description,
                    inputSchema = toolDict.inputSchema,
                    categories = categoryStrings,
                    categoryInfo = GetCategoryDisplayInfo(categories)
                };

                foreach (var category in categoryStrings)
                {
                    if (!toolsByCategory.ContainsKey(category))
                    {
                        toolsByCategory[category] = new List<object>();
                    }
                    toolsByCategory[category].Add(enhancedTool);
                }
            }

            // Convert lists to arrays and sort
            return toolsByCategory.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.OrderBy(t => ((dynamic)t).name).ToArray()
            );
        }

        /// <summary>
        /// Gets category statistics for UI display
        /// </summary>
        /// <returns>Dictionary with category statistics</returns>
        public static Dictionary<string, CategoryStats> GetCategoryStatistics()
        {
            var stats = new Dictionary<string, CategoryStats>();
            var enabledCategories = ToolCategories.GetEnabledCategories();

            // Initialize stats for all known categories
            foreach (ToolCategory category in Enum.GetValues(typeof(ToolCategory)))
            {
                if (category == ToolCategory.None) continue;

                var info = ToolCategories.GetCategoryInfo(category);
                var categoryId = category.ToStringId();
                stats[categoryId] = new CategoryStats
                {
                    Category = categoryId,
                    DisplayName = info.DisplayName,
                    Description = info.Description,
                    IsEnabled = enabledCategories.HasFlag(category),
                    CanDisable = !ToolCategories.AlwaysEnabledCategories.HasFlag(category),
                    ToolCount = 0
                };
            }

            // Count tools per category
            var allTools = McpToolRegistry.GetAvailableTools();
            foreach (var toolObj in allTools)
            {
                var toolDict = toolObj as dynamic;
                if (toolDict == null) continue;

                string toolName = toolDict.name;
                var toolHandler = McpToolRegistry.GetTool(toolName);
                var categories = GetToolCategories(toolHandler);

                if (categories == ToolCategory.None)
                {
                    // Count uncategorized tools
                    if (!stats.ContainsKey("uncategorized"))
                    {
                        stats["uncategorized"] = new CategoryStats
                        {
                            Category = "uncategorized",
                            DisplayName = "Uncategorized",
                            Description = "Tools without specific categories",
                            IsEnabled = true,
                            CanDisable = false,
                            ToolCount = 0
                        };
                    }
                    stats["uncategorized"].ToolCount++;
                }
                else
                {
                    foreach (ToolCategory category in Enum.GetValues(typeof(ToolCategory)))
                    {
                        if (category != ToolCategory.None && categories.HasFlag(category))
                        {
                            var categoryId = category.ToStringId();
                            if (stats.ContainsKey(categoryId))
                            {
                                stats[categoryId].ToolCount++;
                            }
                        }
                    }
                }
            }

            return stats;
        }

        /// <summary>
        /// Updates the enabled categories and triggers refresh
        /// </summary>
        /// <param name="enabledCategories">Categories to enable</param>
        public static void UpdateEnabledCategories(ToolCategory enabledCategories)
        {
            ToolCategories.SaveEnabledCategories(enabledCategories);
            CategoryFiltersChanged?.Invoke();

            Debug.Log($"[CategoryFilteredToolRegistry] Updated enabled categories: {string.Join(", ", enabledCategories.ToStringArray())}");
        }


        /// <summary>
        /// Toggles a category on/off
        /// </summary>
        /// <param name="category">Category to toggle</param>
        /// <returns>New enabled state</returns>
        public static bool ToggleCategory(ToolCategory category)
        {
            var enabled = ToolCategories.GetEnabledCategories();

            if (enabled.HasFlag(category))
            {
                // Don't allow disabling always-enabled categories
                if (!ToolCategories.AlwaysEnabledCategories.HasFlag(category))
                {
                    enabled &= ~category; // Remove the flag
                }
            }
            else
            {
                enabled |= category; // Add the flag
            }

            UpdateEnabledCategories(enabled);
            return enabled.HasFlag(category);
        }


        /// <summary>
        /// Checks if a tool should be included based on category filters
        /// </summary>
        static bool ShouldIncludeTool(IToolHandler toolHandler)
        {
            var categories = GetToolCategories(toolHandler);
            return ToolCategories.ShouldIncludeTool(categories);
        }

        /// <summary>
        /// Extracts categories from a tool handler
        /// </summary>
        static ToolCategory GetToolCategories(IToolHandler toolHandler)
        {
            var stringCategories = toolHandler?.Attribute?.Groups ?? Array.Empty<string>();
            return ToolCategoryExtensions.FromStringArray(stringCategories);
        }

        /// <summary>
        /// Gets display information for categories
        /// </summary>
        static object GetCategoryDisplayInfo(ToolCategory categories)
        {
            var result = new List<object>();
            foreach (ToolCategory category in Enum.GetValues(typeof(ToolCategory)))
            {
                if (category != ToolCategory.None && categories.HasFlag(category))
                {
                    var info = ToolCategories.GetCategoryInfo(category);
                    result.Add(new
                    {
                        name = category.ToStringId(),
                        displayName = info.DisplayName,
                        description = info.Description
                    });
                }
            }
            return result.ToArray();
        }
    }

    /// <summary>
    /// Statistics about a tool category
    /// </summary>
    class CategoryStats
    {
        /// <summary>
        /// The category identifier (e.g., "core", "scripting", "assets")
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Human-readable display name for the category
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Description of what the category contains
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether this category is currently enabled
        /// </summary>
        public bool IsEnabled { get; set; }

        /// <summary>
        /// Whether this category can be disabled by the user
        /// </summary>
        public bool CanDisable { get; set; }

        /// <summary>
        /// Number of tools in this category
        /// </summary>
        public int ToolCount { get; set; }
    }
}