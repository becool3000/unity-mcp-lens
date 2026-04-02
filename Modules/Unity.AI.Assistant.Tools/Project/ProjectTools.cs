using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class ProjectTool
    {
        const string k_GetProjectDataID = "Unity.GetProjectData";
        const string k_GetProjectOverviewID = "Unity.GetProjectOverview";
        const string k_SaveFileID = "Unity.SaveFile";
        
        [Serializable]
        public class GetProjectDataOutput
        {
            public string ProjectTaxonomy = string.Empty;
        }

        // Tool ID "Unity.GetProjectData" is directly used by backend, do not change without backend update.
        [AgentTool("Returns basic project data to generate project overview markdown file.",
            k_GetProjectDataID,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: new string[] { FunctionCallingUtilities.k_StaticContextTag, FunctionCallingUtilities.k_ProjectOverviewTag },
            Mcp = McpAvailability.Available
        )]
        public static async Task<GetProjectDataOutput> GetProjectData(
            ToolExecutionContext context,
            [Parameter("Optional: Specify the depth of the folder hierarchy. Default is 2.")]
            int maxTaxonomyDepth = 2,
            [Parameter("Optional: Maximum number of assets/folders to process. Default is 300.")]
            int maxAssetItems = PayloadBudgetPolicy.MaxProjectDataItems,
            [Parameter("Optional: Maximum output size in characters. Default is 32,000.")]
            int maxOutputChars = PayloadBudgetPolicy.MaxProjectDataChars
        )
        {
            var output = new GetProjectDataOutput();
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, Application.dataPath);

            maxTaxonomyDepth = Math.Max(1, maxTaxonomyDepth);
            maxAssetItems = Math.Max(1, Math.Min(maxAssetItems, PayloadBudgetPolicy.MaxProjectDataItems));
            maxOutputChars = Math.Max(1024, Math.Min(maxOutputChars, PayloadBudgetPolicy.MaxProjectDataChars));

            output.ProjectTaxonomy = ProjectHierarchyExporter.GetAssetsHierarchyMarkdown(maxTaxonomyDepth, maxAssetItems, maxOutputChars);
            var taxonomyBytes = PayloadBudgeting.GetUtf8ByteCount(output.ProjectTaxonomy);
            PayloadStats.Record("tool_result", k_GetProjectDataID, taxonomyBytes, taxonomyBytes, PayloadBudgeting.EstimateTokensFromBytes(taxonomyBytes), PayloadBudgeting.ComputeSha256(output.ProjectTaxonomy));

            return output;
        }

        [AgentTool("Returns project overview markdown file content.",
            k_GetProjectOverviewID,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_ProjectOverviewTag
        )]
        public static async Task<string> GetProjectOverview(ToolExecutionContext context)
        {
            var fullPath = Path.Combine(Application.dataPath, "Project_Overview.md");
            var result = "";

            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, fullPath);

            if (File.Exists(fullPath))
            {
                result = await File.ReadAllTextAsync(fullPath);
            }

            return result;
        }

        [AgentTool(
            "Save text file (markdown, json etc.) with file content, if the file already exists, the content will be overwritten. File paths can be relative to Unity project root (e.g., \"Assets/Project_Overview.md\") or absolute.",
            k_SaveFileID,
            ToolCallEnvironment.EditMode,
            tags: FunctionCallingUtilities.k_ProjectOverviewTag)]
        public static async Task<string> SaveFile(
            ToolExecutionContext context,
            [Parameter("Path to the file to create. Can be relative to Unity project root (e.g., \"Assets/Project_Overview.md\") or absolute.")]
            string filePath,
            [Parameter("The entire content of the new file. Include proper whitespace, indentation, and ensure resulting code is correct.")]
            string fileContent,
            [Parameter("Text file type (i.e. markdown, json etc.) for syntax highlighting (defaults to 'markdown')")]
            string fileType = "markdown")
        {
            try
            {
                // Resolve file path (handle relative paths from Unity project root)
                var resolvedPath = ResolvePath(filePath);
                await context.Permissions.CheckFileSystemAccess(
                    File.Exists(resolvedPath) ?
                        IToolPermissions.ItemOperation.Modify :
                        IToolPermissions.ItemOperation.Create,
                    resolvedPath
                );

                // Ensure parent directory exists for new files
                var directory = Path.GetDirectoryName(resolvedPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    await context.Permissions.CheckFileSystemAccess(
                        IToolPermissions.ItemOperation.Create,
                        directory
                    );
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(resolvedPath, fileContent);

                AssetDatabase.Refresh();

                string message = $"Successfully created file {resolvedPath}";
                return message;
            }
            catch (Exception ex)
            {
                InternalLog.LogException(ex);
                throw;
            }
        }

        static string ResolvePath(string filePath)
        {
            if (Path.IsPathRooted(filePath))
                return filePath;

            // Handle Unity project relative paths
            var projectPath = Directory.GetCurrentDirectory();
            return Path.Combine(projectPath, filePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    static class ProjectHierarchyExporter
    {
        public static string GetAssetsHierarchyMarkdown(
            int maxDepth = int.MaxValue,
            int maxItems = PayloadBudgetPolicy.MaxProjectDataItems,
            int maxOutputChars = PayloadBudgetPolicy.MaxProjectDataChars)
        {
            // Collect all assets under Assets/
            var guids = AssetDatabase.FindAssets("", new[] { "Assets" });
            var totalAssets = guids.Length;
            
            // Ensure top-level items are kept when hitting maxItems limit
            var assetPaths = guids
                .Select(guid => AssetDatabase.GUIDToAssetPath(guid))
                .Where(path => !string.IsNullOrEmpty(path))
                .Where(path =>
                {
                    // Filter by maxDepth early
                    if (maxDepth == int.MaxValue) return true;
                    var depth = path.Count(c => c == '/');
                    return depth <= maxDepth;
                })
                .OrderBy(path => path.Count(c => c == '/'))  // Sort by depth (shallower first)
                .ThenBy(path => path)  // Secondary sort for deterministic ordering
                .ToList();

            // Prepare the hierarchy
            var hierarchy = new AssetTools.AssetHierarchy();
            var rootFolders = new Dictionary<string, AssetTools.AssetFolder>(StringComparer.OrdinalIgnoreCase);

            // Build folder tree + asset listings
            var processedPaths = new HashSet<string>();
            var itemCount = 0;

            foreach (var path in assetPaths)
            {
                // Hard limit to prevent processing too many items
                if (itemCount >= maxItems)
                    break;
                
                if (!processedPaths.Add(path))
                    continue;

                var parts = path.Split('/');
                if (parts.Length == 0)
                    continue;

                var rootName = parts[0];

                if (!rootFolders.TryGetValue(rootName, out var rootFolder))
                {
                    rootFolder = new AssetTools.AssetFolder { Name = rootName };
                    rootFolders[rootName] = rootFolder;
                }

                // Build folder structure
                var folder = FileUtils.GetOrCreateFolder(rootFolder, parts, 1, path);

                if (AssetDatabase.IsValidFolder(path))
                {
                    itemCount++;
                    continue;
                }

                // Use metadata API to get file information without loading the asset
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                if (type == null)
                    continue;

                var assetInfo = new AssetTools.AssetInfo
                {
                    MainAsset = new AssetTools.InstanceInfo
                    {
                        Name = Path.GetFileName(path),
                        Type = type
                    }
                };

                folder.Assets.Add(assetInfo);
                itemCount++;
            }

            hierarchy.Roots = rootFolders.Values.ToList();

            // Export using markdown exporter
            var output = AssetResultMarkdownExporter.ToMarkdownTree(hierarchy, includeID: false);
            
            // Check if we hit limits and add messages to indicate truncation
            var sb = new StringBuilder();
            
            // Show truncation message if we hit item limit or if maxDepth filtered items
            if (itemCount >= maxItems || (maxDepth != int.MaxValue && totalAssets > itemCount))
            {
                sb.AppendLine($"Project hierarchy truncated:");
                sb.AppendLine($"   Showing {itemCount} of {totalAssets} total assets.");
                if (maxDepth != int.MaxValue)
                    sb.AppendLine($"   Limited to depth {maxDepth}.");
                sb.AppendLine("   Use search tools to explore other areas of the project.");
                sb.AppendLine();
            }
            
            sb.Append(output);
            
            // Enforce maximum output size, truncating at last newline to avoid breaking formatting
            if (sb.Length > maxOutputChars)
            {
                // Find the last newline before the limit
                var truncateAt = sb.ToString(0, maxOutputChars).LastIndexOf('\n');
                if (truncateAt > 0)
                {
                    sb.Length = truncateAt;
                }
                else
                {
                    // Fallback if no newline found
                    sb.Length = maxOutputChars;
                }
                sb.AppendLine();
                sb.AppendLine("... [Output truncated]");
            }
            
            return sb.ToString();
        }
    }
}
