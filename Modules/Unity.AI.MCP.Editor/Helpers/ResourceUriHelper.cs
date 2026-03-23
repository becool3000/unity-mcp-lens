using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Helpers
{
    /// <summary>
    /// Provides utilities for Unity resource URI handling, project path resolution, and file security validation.
    /// Essential for MCP tools that work with project files and assets.
    /// </summary>
    /// <remarks>
    /// This class provides the foundation for file-based MCP tools by handling:
    ///
    /// **URI Resolution:**
    /// - unity://path/Assets/... URIs
    /// - file:// URIs
    /// - Relative Assets/ paths
    /// - URL decoding and path normalization
    ///
    /// **Project Root Detection:**
    /// - Multiple fallback strategies (Unity API, environment variables, directory walking)
    /// - Cross-platform support
    ///
    /// **Security:**
    /// - Path validation to prevent directory traversal attacks
    /// - Ensures all file access stays within the project directory
    ///
    /// **Additional Features:**
    /// - Glob pattern matching
    /// - Natural language file windowing (e.g., "last 10 lines", "first 100 bytes")
    /// - SHA-256 hashing for file integrity
    /// - Dynamic script tool specification generation
    /// </remarks>
    static class ResourceUriHelper
    {
        /// <summary>
        /// Resolves the Unity project root directory using multiple fallback strategies.
        /// </summary>
        /// <remarks>
        /// Resolution order:
        /// 1. Explicit override parameter (if provided and valid)
        /// 2. Unity Editor API (Application.dataPath)
        /// 3. UNITY_PROJECT_ROOT environment variable
        /// 4. Walk up from current directory looking for Assets/ and ProjectSettings/
        /// 5. Search down from current directory (shallow)
        /// 6. Fallback to current directory
        ///
        /// A valid project root must contain an Assets/ folder.
        /// </remarks>
        /// <param name="overridePath">Optional explicit path to use as project root. If provided and valid, other strategies are skipped</param>
        /// <returns>Absolute path to the Unity project root directory</returns>
        public static string ResolveProjectRoot(string overridePath)
        {
            // 1) Explicit override
            if (!string.IsNullOrEmpty(overridePath))
            {
                var path = Path.GetFullPath(overridePath);
                if (Directory.Exists(Path.Combine(path, "Assets")))
                {
                    return path;
                }
            }

            // 2) Unity Editor API (when available)
            try
            {
                var dataPath = Application.dataPath;
                if (!string.IsNullOrEmpty(dataPath) && dataPath.EndsWith("Assets"))
                {
                    var projectPath = Path.GetDirectoryName(dataPath);
                    if (!string.IsNullOrEmpty(projectPath))
                    {
                        return Path.GetFullPath(projectPath);
                    }
                }
            }
            catch (Exception ex)
            {
                // Unity API not available - continue with fallbacks
                Debug.LogWarning($"Unity API unavailable for project root: {ex.Message}");
            }

            // 3) Environment variable
            var envPath = Environment.GetEnvironmentVariable("UNITY_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(envPath))
            {
                var path = Path.IsPathRooted(envPath) ? envPath : Path.GetFullPath(envPath);
                if (Directory.Exists(Path.Combine(path, "Assets")))
                {
                    return path;
                }
            }

            // 4) Walk up from current directory
            var current = Directory.GetCurrentDirectory();
            for (int i = 0; i < 6; i++)
            {
                if (Directory.Exists(Path.Combine(current, "Assets")) &&
                    Directory.Exists(Path.Combine(current, "ProjectSettings")))
                {
                    return Path.GetFullPath(current);
                }

                var parent = Path.GetDirectoryName(current);
                if (parent == current) break;
                current = parent;
            }

            // 5) Search downwards (shallow)
            try
            {
                var root = Directory.GetCurrentDirectory();
                var directories = Directory.GetDirectories(root, "*", SearchOption.TopDirectoryOnly);

                foreach (var dir in directories)
                {
                    if (Directory.Exists(Path.Combine(dir, "Assets")) &&
                        Directory.Exists(Path.Combine(dir, "ProjectSettings")))
                    {
                        return Path.GetFullPath(dir);
                    }
                }
            }
            catch (DirectoryNotFoundException ex)
            {
                // Root directory not found - expected in some contexts
                Debug.LogWarning($"Directory not found during project root search: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                // Access denied to directory - log but continue
                Debug.LogWarning($"Access denied during directory search: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Other directory search failures - log but continue with fallback
                Debug.LogWarning($"Directory search failed during project root detection: {ex.Message}");
            }

            // 6) Fallback to current directory
            return Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// Resolves and validates a Unity resource URI to an absolute file path within the project.
        /// </summary>
        /// <remarks>
        /// Supported URI formats:
        /// - unity://path/Assets/Scripts/MyScript.cs
        /// - file:///C:/Projects/MyProject/Assets/Scripts/MyScript.cs
        /// - Assets/Scripts/MyScript.cs
        ///
        /// Security features:
        /// - URL decodes the URI (handles %20 for spaces)
        /// - Normalizes path separators for the current platform
        /// - Validates the resolved path is under the project root
        /// - Returns null for any path outside the project (prevents directory traversal)
        /// </remarks>
        /// <param name="uri">The URI to resolve</param>
        /// <param name="projectRoot">The project root directory to resolve relative paths against</param>
        /// <returns>Absolute file path if valid and safe, or null if the URI is invalid or points outside the project</returns>
        public static string ResolveSafePathFromUri(string uri, string projectRoot)
        {
            if (string.IsNullOrEmpty(uri)) return null;

            string rawPath = null;

            if (uri.StartsWith("unity://path/"))
            {
                rawPath = uri.Substring("unity://path/".Length);
            }
            else if (uri.StartsWith("file://"))
            {
                var uriObj = new Uri(uri);
                rawPath = uriObj.LocalPath;

                // Handle Windows drive letters and UNC paths
                if (Path.DirectorySeparatorChar == '\\')
                {
                    if (rawPath.StartsWith("/") && rawPath.Length > 3 && rawPath[2] == ':')
                    {
                        rawPath = rawPath.Substring(1); // Remove leading slash for drive letters
                    }
                }
            }
            else if (uri.StartsWith("Assets/"))
            {
                rawPath = uri;
            }

            if (string.IsNullOrEmpty(rawPath)) return null;

            // URL decode to handle encoded characters (e.g., %20 for spaces)
            rawPath = WebUtility.UrlDecode(rawPath);

            // Normalize path separators
            rawPath = rawPath.Replace('/', Path.DirectorySeparatorChar);

            var fullPath = Path.GetFullPath(Path.Combine(projectRoot, rawPath));

            // Ensure path is under project root
            if (!IsPathUnderProject(fullPath, projectRoot))
            {
                return null;
            }

            return fullPath;
        }

        /// <summary>
        /// Validates that a file path is within the project directory (security check).
        /// </summary>
        /// <remarks>
        /// This is a critical security method that prevents directory traversal attacks.
        /// All file operations should validate paths using this method before accessing files.
        ///
        /// The check is performed on fully resolved absolute paths and uses case-insensitive
        /// comparison on Windows for compatibility.
        /// </remarks>
        /// <param name="path">The file path to validate (can be relative or absolute)</param>
        /// <param name="projectRoot">The project root directory that file access is restricted to</param>
        /// <returns>true if the path is within projectRoot, false if outside or if validation fails</returns>
        public static bool IsPathUnderProject(string path, string projectRoot)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var fullProjectRoot = Path.GetFullPath(projectRoot);

                return fullPath.StartsWith(fullProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fullPath, fullProjectRoot, StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException ex)
            {
                // Path contains invalid characters or format
                Debug.LogWarning($"Invalid path format for '{path}' or '{projectRoot}': {ex.Message}");
                return false;
            }
            catch (UnauthorizedAccessException ex)
            {
                // Access denied to path
                Debug.LogWarning($"Access denied when validating path '{path}': {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                // Other unexpected path validation failures
                Debug.LogWarning($"Path validation failed for '{path}' under '{projectRoot}': {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tests whether a filename matches a glob pattern with wildcard support.
        /// </summary>
        /// <remarks>
        /// Supported wildcards:
        /// - * matches any sequence of characters
        /// - ? matches any single character
        ///
        /// Matching is case-insensitive.
        /// Empty or null patterns match everything.
        /// </remarks>
        /// <param name="filename">The filename to test</param>
        /// <param name="pattern">The glob pattern (e.g., "*.cs", "Player?.cs", "My*Script.cs")</param>
        /// <returns>true if the filename matches the pattern, false otherwise</returns>
        public static bool MatchesGlobPattern(string filename, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return true;

            // Simple glob matching (*, ?)
            var regexPattern = "^" + Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
            return Regex.IsMatch(filename, regexPattern, RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Checks if URI refers to the special script-edits specification.
        /// </summary>
        /// <param name="uri">The URI to check</param>
        /// <returns>True if URI refers to script-edits spec</returns>
        public static bool IsSpecialScriptEditsUri(string uri)
        {
            return uri == "unity://spec/script-edits" ||
                   uri == "spec/script-edits" ||
                   uri == "script-edits";
        }

        /// <summary>
        /// Processes natural language requests to set windowing parameters.
        /// </summary>
        /// <param name="request">The natural language request</param>
        /// <param name="filePath">The file path for method searching</param>
        /// <param name="startLine">Output: starting line number</param>
        /// <param name="lineCount">Output: number of lines to read</param>
        /// <param name="headBytes">Output: number of bytes from start</param>
        /// <param name="tailLines">Output: number of lines from end</param>
        public static void ProcessNaturalLanguageRequest(string request, string filePath,
            out int? startLine, out int? lineCount, out int? headBytes, out int? tailLines)
        {
            startLine = null;
            lineCount = null;
            headBytes = null;
            tailLines = null;

            if (string.IsNullOrEmpty(request)) return;

            var req = request.Trim().ToLowerInvariant();

            // "last N lines"
            var match = Regex.Match(req, @"last\s+(\d+)\s+lines");
            if (match.Success)
            {
                tailLines = int.Parse(match.Groups[1].Value);
                return;
            }

            // "first N lines"
            match = Regex.Match(req, @"first\s+(\d+)\s+lines");
            if (match.Success)
            {
                startLine = 1;
                lineCount = int.Parse(match.Groups[1].Value);
                return;
            }

            // "first N bytes"
            match = Regex.Match(req, @"first\s+(\d+)\s*bytes");
            if (match.Success)
            {
                headBytes = int.Parse(match.Groups[1].Value);
                return;
            }

            // "show N lines around MethodName"
            match = Regex.Match(req, @"show\s+(\d+)\s+lines\s+around\s+([A-Za-z_][A-Za-z0-9_]*)");
            if (match.Success)
            {
                var window = int.Parse(match.Groups[1].Value);
                var methodName = match.Groups[2].Value;

                try
                {
                    var fileText = File.ReadAllText(filePath, Encoding.UTF8);
                    var lines = fileText.Split('\n');

                    var methodPattern = $@"^\s*(?:\[[^\]]+\]\s*)*(?:public|private|protected|internal|static|virtual|override|sealed|async|extern|unsafe|new|partial).*?\b{Regex.Escape(methodName)}\s*\(";
                    var regex = new Regex(methodPattern, RegexOptions.Multiline);

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (regex.IsMatch(lines[i]))
                        {
                            var half = Math.Max(1, window / 2);
                            startLine = Math.Max(1, i + 1 - half);
                            lineCount = window;
                            break;
                        }
                    }
                }
                catch (FileNotFoundException ex)
                {
                    // File not found for method search - expected if file doesn't exist
                    Debug.LogWarning($"File not found for method search '{methodName}': {ex.Message}");
                }
                catch (UnauthorizedAccessException ex)
                {
                    // Access denied to file during method search
                    Debug.LogWarning($"Access denied reading file for method search '{methodName}': {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Method search parsing failed - log and ignore the natural language request
                    Debug.LogWarning($"Failed to parse method search request '{request}' for method '{methodName}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Computes the SHA-256 hash of byte data for file integrity verification.
        /// </summary>
        /// <remarks>
        /// Used by file editing tools to implement optimistic concurrency control.
        /// Clients can specify a precondition_sha256 to ensure the file hasn't changed
        /// before applying edits.
        /// </remarks>
        /// <param name="data">The byte data to hash</param>
        /// <returns>Lowercase hexadecimal string representation of the SHA-256 hash (64 characters)</returns>
        public static string ComputeSha256(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// Returns a dynamically generated script tools specification JSON based on actually available tools.
        /// </summary>
        /// <returns>The current Unity MCP script tools specification JSON string</returns>
        public static string GetScriptEditsSpecification()
        {
            try
            {
                // Get available scripting tools dynamically
                var availableTools = ToolRegistry.McpToolRegistry.GetAvailableTools(ignoreEnabledState: true)
                    .Where(t => t.name != null && (
                        t.name.Contains("script") ||
                        t.name == "Unity.ApplyTextEdits" ||
                        t.name == "Unity.ValidateScript" ||
                        t.name == "Unity.CreateScript" ||
                        t.name == "Unity.DeleteScript" ||
                        t.name == "Unity.GetSha"
                    ))
                    .OrderBy(t => t.name)
                    .ToArray();

                var toolsJson = string.Join(",\n    ", availableTools.Select(tool => $@"{{
      ""name"": ""{tool.name}"",
      ""description"": ""{EscapeJsonString(tool.description ?? "")}"",
      ""title"": ""{EscapeJsonString(tool.title ?? tool.name)}""
    }}"));

                var specJson = $@"{{
  ""name"": ""Unity MCP - Available Script Tools"",
  ""version"": ""1.0"",
  ""generated"": ""{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}"",
  ""description"": ""Dynamically generated specification of available Unity script editing and management tools."",

  ""primary_tools"": {{
    ""script_reading"": ""Unity.ReadResource (via unity://path/Assets/... URIs)"",
    ""text_editing"": ""Unity.ApplyTextEdits (precise range-based edits with SHA validation)"",
    ""script_management"": ""Unity.ManageScript (legacy compatibility router)"",
    ""validation"": ""Unity.ValidateScript (C# syntax and structure validation)"",
    ""file_operations"": ""Unity.CreateScript, Unity.DeleteScript""
  }},

  ""recommended_workflows"": [
    {{
      ""name"": ""Safe Script Editing"",
      ""steps"": [
        ""1. Unity.ReadResource to get current content and SHA"",
        ""2. Unity.ValidateScript to check current state"",
        ""3. Unity.ApplyTextEdits with precondition_sha256"",
        ""4. Unity.ValidateScript to verify changes""
      ]
    }},
    {{
      ""name"": ""Create New Script"",
      ""steps"": [
        ""1. Unity.CreateScript with initial content"",
        ""2. Unity.ValidateScript to check syntax"",
        ""3. Unity.ApplyTextEdits for any corrections""
      ]
    }}
  ],

  ""available_tools"": [
    {toolsJson}
  ],

  ""integration_examples"": [
    {{
      ""title"": ""Read and edit a Unity script"",
      ""workflow"": [
        {{
          ""step"": 1,
          ""tool"": ""Unity.ReadResource"",
          ""args"": {{
            ""uri"": ""unity://path/Assets/Scripts/PlayerController.cs""
          }},
          ""purpose"": ""Get current file content and SHA256""
        }},
        {{
          ""step"": 2,
          ""tool"": ""Unity.ApplyTextEdits"",
          ""args"": {{
            ""uri"": ""unity://path/Assets/Scripts/PlayerController.cs"",
            ""edits"": [
              {{
                ""startLine"": 10,
                ""startCol"": 1,
                ""endLine"": 10,
                ""endCol"": 1,
                ""newText"": ""    // Added by Unity MCP\\n""
              }}
            ],
            ""precondition_sha256"": ""<sha-from-step1>""
          }},
          ""purpose"": ""Apply precise text changes with conflict prevention""
        }},
        {{
          ""step"": 3,
          ""tool"": ""Unity.ValidateScript"",
          ""args"": {{
            ""uri"": ""unity://path/Assets/Scripts/PlayerController.cs"",
            ""include_diagnostics"": true
          }},
          ""purpose"": ""Verify the edit didn't break syntax""
        }}
      ]
    }}
  ],

  ""notes"": [
    ""This specification is dynamically generated based on currently available tools."",
    ""Use Unity.ApplyTextEdits for precise range-based editing with 1-indexed line/column numbers."",
    ""Always use precondition_sha256 to prevent concurrent edit conflicts."",
    ""The Unity.ManageScript tool is a legacy compatibility router - prefer direct tool usage."",
    ""All script paths must be under Assets/ directory for security."",
    ""Use Unity.ValidateScript after edits to ensure C# syntax correctness.""
  ]
}}";

                return specJson;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error generating dynamic script specification: {ex.Message}");
                // Fallback to a minimal static specification
                return @"{
  ""name"": ""Unity MCP - Script Tools (Fallback)"",
  ""error"": ""Dynamic generation failed"",
  ""available_tools"": [
    ""Unity.ReadResource"",
    ""Unity.ApplyTextEdits"",
    ""Unity.ValidateScript"",
    ""Unity.ManageScript""
  ],
  ""note"": ""Use Unity.ReadResource for reading files, Unity.ApplyTextEdits for editing, Unity.ValidateScript for validation.""
}";
            }
        }

        /// <summary>
        /// Escapes a string for safe inclusion in JSON.
        /// </summary>
        /// <param name="input">The string to escape</param>
        /// <returns>JSON-safe escaped string</returns>
        static string EscapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            return input.Replace("\\", "\\\\")
                       .Replace("\"", "\\\"")
                       .Replace("\n", "\\n")
                       .Replace("\r", "\\r")
                       .Replace("\t", "\\t");
        }

        /// <summary>
        /// Converts a resolved file path to ManageScript-compatible name and directory parameters.
        /// This helper eliminates duplication between GetSHA and ApplyTextEdits tools.
        /// </summary>
        /// <param name="filePath">Full resolved file path from ResolveSafePathFromUri</param>
        /// <param name="projectRoot">Project root directory</param>
        /// <returns>Tuple containing (name: script name without .cs extension, directory: path relative to Assets/)</returns>
        public static (string name, string directory) ExtractManageScriptParams(string filePath, string projectRoot)
        {
            var fileName = Path.GetFileName(filePath);
            var name = Path.GetFileNameWithoutExtension(fileName);

            var assetsPath = Path.Combine(projectRoot, "Assets");
            var directoryPath = Path.GetDirectoryName(filePath);
            var relativeDirPath = Path.GetRelativePath(assetsPath, directoryPath).Replace('\\', '/');

            // Convert to ManageScript format: empty string or "." becomes ".", subfolders stay as-is
            var directory = string.IsNullOrEmpty(relativeDirPath) || relativeDirPath == "." ? "." : relativeDirPath;

            return (name, directory);
        }
    }
}
