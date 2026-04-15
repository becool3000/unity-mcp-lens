using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.ToolRegistry.Parameters
{
    /// <summary>
    /// Parameters for the Unity.ListResources tool.
    /// Lists project URIs (unity://path/...) under a folder (default: Assets).
    /// </summary>
    public record ListResourcesParams
    {
        /// <summary>
        /// Gets or sets the glob pattern for filtering files (e.g., "*.cs", "*.txt").
        /// Default is "*.cs" to list C# script files.
        /// </summary>
        [McpDescription("Glob pattern for filtering files", Required = false)]
        public string Pattern { get; set; } = "*.cs";

        /// <summary>
        /// Gets or sets the folder under project root to search in.
        /// Default is "Assets" to search only under the Assets folder.
        /// </summary>
        [McpDescription("Folder under project root to search in", Required = false)]
        public string Under { get; set; } = "Assets";

        /// <summary>
        /// Gets or sets the maximum number of results to return.
        /// Default is 200 to prevent overwhelming responses.
        /// </summary>
        [McpDescription("Maximum number of results to return", Required = false)]
        public int Limit { get; set; } = 200;

        /// <summary>
        /// Gets or sets an override for the project root path.
        /// When null or empty, uses the current Unity project root.
        /// </summary>
        [McpDescription("Override project root path", Required = false)]
        public string ProjectRoot { get; set; }
    }

    /// <summary>
    /// Parameters for the Unity.ReadResource tool.
    /// Reads a resource by unity://path/... URI with optional slicing.
    /// Uses explicit defaults for clearer LLM guidance.
    /// </summary>
    public record ReadResourceParams
    {
        /// <summary>
        /// Gets or sets the resource URI to read (e.g., "unity://path/Assets/Scripts/MyScript.cs").
        /// This is a required parameter.
        /// </summary>
        [McpDescription("The resource URI to read under Assets/", Required = true)]
        public string Uri { get; set; }

        /// <summary>
        /// Gets or sets the starting line number for reading (1-based).
        /// Default is 1 (start from the beginning of the file).
        /// </summary>
        [McpDescription("The starting line number (1-based, default: 1 = start from beginning)", Required = false)]
        public int StartLine { get; set; } = 1;  // Default: start from line 1 (beginning)

        /// <summary>
        /// Gets or sets the number of lines to read from the starting line.
        /// Default is -1 (read all lines from StartLine to end of file).
        /// </summary>
        [McpDescription("The number of lines to read (default: preview window)", Required = false)]
        public int LineCount { get; set; } = PayloadBudgetPolicy.MaxPreviewFileLines;

        /// <summary>
        /// Gets or sets the number of bytes to read from the start of the file.
        /// Default is 0 (no byte limit, takes precedence over line-based slicing when set).
        /// </summary>
        [McpDescription("The number of bytes to read from the start of the file (default: 0 = no byte limit)", Required = false)]
        public int HeadBytes { get; set; } = 0;  // Default: 0 = no byte limit

        /// <summary>
        /// Gets or sets the number of lines to read from the end of the file.
        /// Default is 0 (no tail limit, takes precedence over StartLine/LineCount when set).
        /// </summary>
        [McpDescription("The number of lines to read from the end of the file (default: 0 = no tail limit)", Required = false)]
        public int TailLines { get; set; } = 0;  // Default: 0 = no tail limit

        /// <summary>
        /// Gets or sets a natural language request for content slicing.
        /// Examples: "last 120 lines", "show 40 lines around MethodName".
        /// Processed when explicit slicing parameters are at their defaults.
        /// </summary>
        [McpDescription("Natural language request for content slicing (e.g., 'last 120 lines', 'show 40 lines around MethodName')", Required = false)]
        public string Request { get; set; }

        [McpDescription("When true, return the full file contents instead of the default preview slice.", Required = false)]
        public bool Full { get; set; }

        /// <summary>
        /// Gets or sets an override for the project root path.
        /// When null or empty, uses the current Unity project root.
        /// </summary>
        [McpDescription("Override project root path", Required = false)]
        public string ProjectRoot { get; set; }
    }

    /// <summary>
    /// Parameters for the Unity.FindInFile tool.
    /// Searches a file with a regex pattern and returns line numbers and excerpts.
    /// </summary>
    public record FindInFileParams
    {
        /// <summary>
        /// Gets or sets the resource URI to search (e.g., "unity://path/Assets/Scripts/MyScript.cs").
        /// This is a required parameter.
        /// </summary>
        [McpDescription("The resource URI to search under Assets/", Required = true)]
        public string Uri { get; set; }

        /// <summary>
        /// Gets or sets the regex pattern to search for in the file.
        /// This is a required parameter.
        /// </summary>
        [McpDescription("The regex pattern to search for", Required = true)]
        public string Pattern { get; set; }

        /// <summary>
        /// Gets or sets whether the search should be case-insensitive.
        /// Default is true for more flexible matching.
        /// </summary>
        [McpDescription("Case-insensitive search", Required = false)]
        public bool IgnoreCase { get; set; } = true;

        /// <summary>
        /// Gets or sets the maximum number of results to return.
        /// Default is 200 to prevent overwhelming responses.
        /// </summary>
        [McpDescription("Maximum number of results to return", Required = false)]
        public int MaxResults { get; set; } = 200;

        /// <summary>
        /// Gets or sets an override for the project root path.
        /// When null or empty, uses the current Unity project root.
        /// </summary>
        [McpDescription("Override project root path", Required = false)]
        public string ProjectRoot { get; set; }
    }

    /// <summary>
    /// Response data for Unity.ListResources tool.
    /// </summary>
    public class ListResourcesResponse
    {
        /// <summary>
        /// Gets or sets the list of resource URIs found (e.g., "unity://path/Assets/Scripts/MyScript.cs").
        /// </summary>
        public List<string> Uris { get; set; } = new();

        /// <summary>
        /// Gets or sets the total count of resources found.
        /// </summary>
        public int Count { get; set; }
    }

    /// <summary>
    /// Response data for Unity.ReadResource tool.
    /// </summary>
    public class ReadResourceResponse
    {
        /// <summary>
        /// Gets or sets the text content of the resource (may be partial if slicing was applied).
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Gets or sets the metadata about the resource (SHA-256 hash and byte length).
        /// </summary>
        public ResourceMetadata Metadata { get; set; } = new();
    }

    /// <summary>
    /// Metadata for Unity.ReadResource responses.
    /// </summary>
    public class ResourceMetadata
    {
        /// <summary>
        /// Gets or sets the SHA-256 hash of the complete file content (not just the slice).
        /// Useful for change detection and file integrity verification.
        /// </summary>
        public string Sha256 { get; set; }

        /// <summary>
        /// Gets or sets the total length of the file in bytes (not just the slice).
        /// </summary>
        public long LengthBytes { get; set; }

        public bool Truncated { get; set; }

        public bool DetailAvailable { get; set; }

        public object DetailRef { get; set; }

        public int ReturnedStartLine { get; set; }

        public int ReturnedLineCount { get; set; }
    }

    /// <summary>
    /// Match result for Unity.FindInFile tool.
    /// Represents a single pattern match with line and column positions.
    /// </summary>
    public class FindMatch
    {
        /// <summary>
        /// Gets or sets the starting line number (1-based) where the match begins.
        /// </summary>
        public int StartLine { get; set; }

        /// <summary>
        /// Gets or sets the starting column number (1-based) where the match begins.
        /// </summary>
        public int StartCol { get; set; }

        /// <summary>
        /// Gets or sets the ending line number (1-based) where the match ends.
        /// </summary>
        public int EndLine { get; set; }

        /// <summary>
        /// Gets or sets the ending column number (1-based, exclusive) where the match ends.
        /// </summary>
        public int EndCol { get; set; }
    }

    /// <summary>
    /// Response data for Unity.FindInFile tool.
    /// </summary>
    public class FindInFileResponse
    {
        /// <summary>
        /// Gets or sets the list of matches found, each containing startLine, startCol, endLine, and endCol properties.
        /// </summary>
        [JsonProperty("matches")]
        public List<Dictionary<string, object>> Matches { get; set; } = new();

        /// <summary>
        /// Gets or sets the total count of matches found.
        /// </summary>
        [JsonProperty("count")]
        public int Count { get; set; }

        /// <summary>
        /// Gets or sets the SHA-256 hash of the file that was searched.
        /// Useful for change detection and file integrity verification.
        /// </summary>
        [JsonProperty("sha256")]
        public string Sha256 { get; set; }
    }
}
