using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry; // For McpTool attribute
using Unity.AI.MCP.Editor.ToolRegistry.Parameters;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Resource wrapper tools so clients that do not expose MCP resources primitives
    /// can still list and read files via normal tools. These call into the same
    /// safe path logic to ensure security.
    /// </summary>
    public static class ResourceTools
    {
        #region Tool Descriptions

        /// <summary>
        /// Description text for the Unity.ListResources MCP tool.
        /// Explains how to list project files and URIs under a specified folder.
        /// </summary>
        public const string ListResourcesTitle = "List project resources";

        public const string ListResourcesDescription = @"Lists project URIs (unity://path/...) under a folder (default: Assets). Returns all files by default; always appends unity://spec/script-edits.

Args:
    Pattern: Glob pattern for filtering files (default: *)
    Under: Folder under project root to search (default: Assets)
    Limit: Maximum number of results (default: 200)
    ProjectRoot: Override project root path

Returns:
    Dictionary with 'success', 'data' (containing 'uris' array and 'count'), and optional 'error'.";

        /// <summary>
        /// Description text for the Unity.ReadResource MCP tool.
        /// Explains how to read file contents with optional line-based or byte-based slicing.
        /// </summary>
        public const string ReadResourceTitle = "Read a project resource";

        public const string ReadResourceDescription = @"Reads a resource by unity://path/... URI with optional slicing.

Args:
    Uri: The resource URI to read under Assets/
    StartLine: Starting line number (1-based)
    LineCount: Number of lines to read
    HeadBytes: Number of bytes to read from start
    TailLines: Number of lines from end
    Full: When true, return the full file instead of the default preview window
    Request: Natural language request (e.g., 'last 120 lines', 'show 40 lines around MethodName')
    ProjectRoot: Override project root path

Returns:
    Dictionary with 'success', 'data' (containing preview text and metadata by default), and optional 'error'.";

        /// <summary>
        /// Description text for the Unity.FindInFile MCP tool.
        /// Explains how to search files using regex patterns for methods, variables, or any text.
        /// </summary>
        public const string FindInFileTitle = "Search within a file";

        public const string FindInFileDescription = @"Searches a file with a regex pattern and returns line numbers and excerpts.
            Ideal for: Finding methods, variables, classes, or any text patterns for renaming/editing.

Args:
    Uri: The resource URI to search under Assets/
    Pattern: Regex pattern to search for
    IgnoreCase: Case-insensitive search (default: true)
    MaxResults: Maximum results to return (default: 200)
    ProjectRoot: Override project root path

Returns:
    Dictionary with 'success', 'data' (containing 'matches' and 'count'), and optional 'error'.";

        #endregion

        #region Public Tool Methods

        /// <summary>
        /// Lists Unity project resources (URIs) under a specified folder.
        /// </summary>
        /// <param name="parameters">Parameters controlling which resources to list, including pattern filter, base folder, and result limit.</param>
        /// <returns>A response dictionary containing success status, list of resource URIs, count, and any error messages.</returns>
        [McpTool("Unity.ListResources", ListResourcesDescription, ListResourcesTitle, Groups = new[] {"core", "resources"})]
        public static object ListResources(ListResourcesParams parameters)
        {
            // Provide defaults when parameters is null
            parameters ??= new ListResourcesParams();

            try
            {
                var projectRoot = ResourceUriHelper.ResolveProjectRoot(parameters.ProjectRoot);
                var basePath = Path.Combine(projectRoot, parameters.Under ?? "Assets");

                // Validate base path is under project root
                if (!ResourceUriHelper.IsPathUnderProject(basePath, projectRoot))
                {
                    return Response.Error("Base path must be under project root");
                }

                // Enforce listing only under Assets
                var assetsPath = Path.Combine(projectRoot, "Assets");
                if (!ResourceUriHelper.IsPathUnderProject(basePath, assetsPath))
                {
                    return Response.Error("Listing is restricted to Assets/");
                }

                var matches = new List<string>();
                var limit = Math.Max(1, parameters.Limit);
                var pattern = parameters.Pattern ?? "*";

                if (Directory.Exists(basePath))
                {
                    var files = Directory.GetFiles(basePath, "*", SearchOption.AllDirectories);

                    foreach (var file in files)
                    {
                        try
                        {
                            var realPath = Path.GetFullPath(file);

                            // Ensure real path stays under project/Assets
                            if (!ResourceUriHelper.IsPathUnderProject(realPath, assetsPath))
                                continue;

                            // Apply pattern matching
                            if (!string.IsNullOrEmpty(pattern) && !ResourceUriHelper.MatchesGlobPattern(Path.GetFileName(file), pattern))
                                continue;

                            var relativePath = Path.GetRelativePath(projectRoot, realPath).Replace('\\', '/');
                            matches.Add($"unity://path/{relativePath}");

                            if (matches.Count >= limit)
                                break;
                        }
                        catch (Exception ex)
                        {
                            // Skip files that can't be processed (e.g., permission issues, path resolution failures)
                            Debug.LogWarning($"Skipping file '{file}' during resource listing: {ex.Message}");
                            continue;
                        }
                    }
                }

                // Always include the script-edits specification resource at the end
                if (!matches.Contains("unity://spec/script-edits"))
                {
                    matches.Add("unity://spec/script-edits");
                }

                var responseData = new ListResourcesResponse {Uris = matches, Count = matches.Count};

                return Response.Success("Resources listed successfully", responseData);
            }
            catch (Exception ex)
            {
                return Response.Error($"Error listing resources: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads a Unity project resource by URI with optional content slicing.
        /// </summary>
        /// <param name="parameters">Parameters specifying the URI to read and optional slicing options (line ranges, head bytes, tail lines, or natural language requests).</param>
        /// <returns>A response dictionary containing success status, file text content, metadata (SHA-256 and length), and any error messages.</returns>
        [McpTool("Unity.ReadResource", ReadResourceDescription, ReadResourceTitle, Groups = new[] {"core", "resources"})]
        public static object ReadResource(ReadResourceParams parameters)
        {
            if (parameters == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            try
            {
                // Handle special script-edits specification resource
                if (ResourceUriHelper.IsSpecialScriptEditsUri(parameters.Uri))
                {
                    var specJson = ResourceUriHelper.GetScriptEditsSpecification();
                    if (!string.IsNullOrEmpty(specJson))
                    {
                        var specSha = ResourceUriHelper.ComputeSha256(Encoding.UTF8.GetBytes(specJson));

                        var specResponse = new ReadResourceResponse {Text = specJson, Metadata = new ResourceMetadata {Sha256 = specSha, LengthBytes = Encoding.UTF8.GetByteCount(specJson)}};

                        return Response.Success("Dynamic script-edits specification retrieved", specResponse);
                    }
                }

                var projectRoot = ResourceUriHelper.ResolveProjectRoot(parameters.ProjectRoot);
                var filePath = ResourceUriHelper.ResolveSafePathFromUri(parameters.Uri, projectRoot);

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return Response.Error($"Resource not found: {parameters.Uri}");
                }

                // Ensure file is under Assets/
                var assetsPath = Path.Combine(projectRoot, "Assets");
                if (!ResourceUriHelper.IsPathUnderProject(filePath, assetsPath))
                {
                    return Response.Error("Read restricted to Assets/");
                }

                // Process natural language request and apply if no explicit values provided
                ResourceUriHelper.ProcessNaturalLanguageRequest(parameters.Request, filePath,
                    out var startLineFromRequest, out var lineCountFromRequest,
                    out var headBytesFromRequest, out var tailLinesFromRequest);

                var isDefaultSlicing = parameters.StartLine == 1 &&
                    parameters.LineCount == PayloadBudgetPolicy.MaxPreviewFileLines &&
                    parameters.HeadBytes == 0 &&
                    parameters.TailLines == 0 &&
                    !parameters.Full;

                var effectiveStartLine = isDefaultSlicing ? (startLineFromRequest ?? 1) : parameters.StartLine;
                var effectiveLineCount = parameters.Full
                    ? -1
                    : isDefaultSlicing
                        ? (lineCountFromRequest ?? PayloadBudgetPolicy.MaxPreviewFileLines)
                        : parameters.LineCount;
                var effectiveHeadBytes = parameters.HeadBytes > 0 ? parameters.HeadBytes : (headBytesFromRequest ?? 0);
                var effectiveTailLines = parameters.TailLines > 0 ? parameters.TailLines : (tailLinesFromRequest ?? 0);

                // Read file and compute metadata
                var fileBytes = File.ReadAllBytes(filePath);
                var fullSha = ResourceUriHelper.ComputeSha256(fileBytes);

                // Determine if selection is requested
                bool selectionRequested = parameters.Full ||
                    effectiveHeadBytes > 0 ||
                    effectiveTailLines > 0 ||
                    (effectiveStartLine != 1 || (effectiveLineCount != PayloadBudgetPolicy.MaxPreviewFileLines && effectiveLineCount != -1 && effectiveLineCount > 0)) ||
                    !string.IsNullOrEmpty(parameters.Request);

                var response = new ReadResourceResponse
                {
                    Metadata = new ResourceMetadata
                    {
                        Sha256 = fullSha,
                        LengthBytes = fileBytes.Length,
                        DetailAvailable = true
                    }
                };

                if (selectionRequested)
                {
                    // Apply windowing based on precedence: head_bytes > tail_lines > start_line+line_count
                    if (effectiveHeadBytes > 0)
                    {
                        var headBytes = Math.Min(effectiveHeadBytes, fileBytes.Length);
                        var headData = new byte[headBytes];
                        Array.Copy(fileBytes, headData, headBytes);
                        response.Text = Encoding.UTF8.GetString(headData);
                        response.Metadata.Truncated = headBytes < fileBytes.Length;
                    }
                    else
                    {
                        var fileText = Encoding.UTF8.GetString(fileBytes);
                        var lines = fileText.Split('\n');

                        if (effectiveTailLines > 0)
                        {
                            var tailCount = Math.Min(effectiveTailLines, lines.Length);
                            var tailLines = lines.Skip(lines.Length - tailCount).ToArray();
                            response.Text = string.Join("\n", tailLines);
                            response.Metadata.ReturnedStartLine = Math.Max(1, lines.Length - tailCount + 1);
                            response.Metadata.ReturnedLineCount = tailLines.Length;
                            response.Metadata.Truncated = tailCount < lines.Length;
                        }
                        else if (effectiveStartLine != 1 || (effectiveLineCount != -1 && effectiveLineCount > 0))
                        {
                            var startIdx = Math.Max(0, effectiveStartLine - 1);

                            if (effectiveLineCount == -1)
                            {
                                // Read from startLine to end of file
                                var selectedLines = lines.Skip(startIdx).ToArray();
                                response.Text = string.Join("\n", selectedLines);
                                response.Metadata.ReturnedStartLine = startIdx + 1;
                                response.Metadata.ReturnedLineCount = selectedLines.Length;
                                response.Metadata.Truncated = startIdx > 0;
                            }
                            else
                            {
                                // Read specific number of lines from startLine
                                var endIdx = Math.Min(lines.Length, startIdx + effectiveLineCount);
                                var selectedLines = lines.Skip(startIdx).Take(endIdx - startIdx).ToArray();
                                response.Text = string.Join("\n", selectedLines);
                                response.Metadata.ReturnedStartLine = startIdx + 1;
                                response.Metadata.ReturnedLineCount = selectedLines.Length;
                                response.Metadata.Truncated = startIdx > 0 || endIdx < lines.Length;
                            }
                        }
                        else
                        {
                            response.Text = PayloadBudgeting.CreateTextPreview(fileText, PayloadBudgetPolicy.MaxPreviewFileLines, PayloadBudgetPolicy.MaxPreviewFileBytes, out var truncated);
                            response.Metadata.ReturnedStartLine = 1;
                            response.Metadata.ReturnedLineCount = response.Text.Split('\n').Length;
                            response.Metadata.Truncated = truncated;
                        }
                    }
                }
                else
                {
                    response.Text = PayloadBudgeting.CreateTextPreview(Encoding.UTF8.GetString(fileBytes), PayloadBudgetPolicy.MaxPreviewFileLines, PayloadBudgetPolicy.MaxPreviewFileBytes, out var truncated);
                    response.Metadata.ReturnedStartLine = 1;
                    response.Metadata.ReturnedLineCount = response.Text.Split('\n').Length;
                    response.Metadata.Truncated = truncated;
                }

                PayloadStats.Record("tool_result", "Unity.ReadResource", fileBytes.Length, PayloadBudgeting.GetUtf8ByteCount(response.Text), PayloadBudgeting.EstimateTokensFromBytes(PayloadBudgeting.GetUtf8ByteCount(response.Text)), fullSha);

                return Response.Success("Resource read successfully", response);
            }
            catch (Exception ex)
            {
                return Response.Error($"Error reading resource: {ex.Message}");
            }
        }

        /// <summary>
        /// Searches for regex patterns within a Unity project file.
        /// </summary>
        /// <param name="parameters">Parameters specifying the URI to search, regex pattern, case sensitivity, and maximum results.</param>
        /// <returns>A response dictionary containing success status, list of matches with line/column positions, match count, file SHA-256, and any error messages.</returns>
        [McpTool("Unity.FindInFile", FindInFileDescription, FindInFileTitle, Groups = new[] {"core", "resources"})]
        public static object FindInFile(FindInFileParams parameters)
        {
            if (parameters == null)
            {
                return Response.Error("Parameters cannot be null.");
            }

            try
            {
                var projectRoot = ResourceUriHelper.ResolveProjectRoot(parameters.ProjectRoot);
                var filePath = ResourceUriHelper.ResolveSafePathFromUri(parameters.Uri, projectRoot);

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                {
                    return Response.Error($"Resource not found: {parameters.Uri}");
                }

                var fileBytes = File.ReadAllBytes(filePath);
                var fileText = Encoding.UTF8.GetString(fileBytes);
                var fileSha = ResourceUriHelper.ComputeSha256(fileBytes);
                var regexOptions = RegexOptions.Multiline;
                if (parameters.IgnoreCase)
                {
                    regexOptions |= RegexOptions.IgnoreCase;
                }

                var regex = new Regex(parameters.Pattern, regexOptions);
                var matches = new List<Dictionary<string, object>>();
                var maxResults = Math.Max(1, parameters.MaxResults);
                var lines = fileText.Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var match = regex.Match(line);

                    if (match.Success)
                    {
                        matches.Add(new Dictionary<string, object>
                        {
                            ["startLine"] = i + 1, // 1-based line numbers
                            ["startCol"] = match.Index + 1, // 1-based column
                            ["endLine"] = i + 1,
                            ["endCol"] = match.Index + match.Length + 1 // End exclusive, 1-based
                        });

                        if (matches.Count >= maxResults)
                            break;
                    }
                }

                var responseData = new FindInFileResponse {Matches = matches, Count = matches.Count, Sha256 = fileSha};

                return Response.Success("File search completed successfully", responseData);
            }
            catch (Exception ex)
            {
                return Response.Error($"Error searching in file: {ex.Message}");
            }
        }

        #endregion
    }
}
