using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Tools.Editor
{
    class FileTools
    {
        const string k_FindFilesFunctionId = "Unity.FindFiles";
        const string k_GetFileContentFunctionId = "Unity.GetFileContent";

        internal const int k_FindFilesMaxResults = 50;
        const int k_MaxContentLength = 1000;

        static readonly string[] k_SearchFolders = { "Assets" };

        [Serializable]
        public struct FileMatch
        {
            [Description("The relative path to the file containing the match")]
            public string FilePath;

            [Description("The first line number of the MatchingContent block (only set when searchPattern is provided)")]
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? StartLineNumber;

            [Description("The last line number of the MatchingContent block (only set when searchPattern is provided)")]
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public int? EndLineNumber;

            [Description("The found content, including context lines (only set when searchPattern is provided)")]
            [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
            public string MatchingContent;
        }

        [Serializable]
        public struct SearchFileContentOutput
        {
            [Description("The matching results")]
            public List<FileMatch> Matches;

            public string Info;
        }

        [AgentTool(
            "Search for content within files and return a list of matching file with the found content, including some context.",
            k_FindFilesFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        public static async Task<SearchFileContentOutput> FindFiles(
            ToolExecutionContext context,
            [Parameter(
                "Regex pattern to search for in file contents.\n" +
                "Examples:\n" +
                "  \"TODO\": Match any line containing 'TODO'\n" +
                "  \"public\\s+class\\s+\\w+\": Match class declarations in C#\n" +
                "Leave empty to not filter by content (files will still be filtered by 'nameRegex')."
            )]
            string searchPattern,
	        [Parameter(
		        "Regex pattern applied to the relative file path (including filename + extension).\n" +
		        "Examples:\n" +
		        "  \".*Program\\.cs$\": Match a specific filename 'Program.cs'\n" +
		        "  \".*Controllers/.*\": Match all files under a 'Controllers' folder\n" +
		        "  \".*\\.txt$\": Match all files with the .txt extension\n" +
		        "  \".*Test.*\": Match any file path containing 'Test'\n" +
                "Leave empty to include all files BUT try to use this field as much as possible to limit the number of results."
	        )]
            string nameRegex = "",
	        [Parameter("Number of context lines to show around matches (Defaults to 2)")]
            int contextLines = 2,
	        [Parameter("Index of the first match to return (for pagination, defaults to 0 to get the first page)")]
            int startIndex = 0,
            [Parameter("Internal parameter: set to false to run synchronously (for testing). Defaults to true for async execution.")]
            bool runOnBackgroundThread = true
        )
        {
	        var projectPath = Directory.GetCurrentDirectory();

            // Only search in Assets folder (excludes Packages)
            var searchPaths = k_SearchFolders.Select(folder => Path.Combine(projectPath, folder)).ToList();
            foreach (var folder in searchPaths)
            {
                await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, Path.Combine(projectPath, folder));
            }

	        Regex searchRegex = null;
	        Regex fileRegex = null;
	        try
	        {
		        if (!string.IsNullOrEmpty(searchPattern))
			        searchRegex = new Regex(searchPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(3));

		        if (!string.IsNullOrWhiteSpace(nameRegex))
			        fileRegex = new Regex(nameRegex, RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromSeconds(3));
	        }
	        catch (ArgumentException ex)
	        {
		        throw new ArgumentException($"Invalid regex: {ex.Message}", ex);
	        }

	        // Run file search - on background thread in production, synchronously in tests to avoid deadlocks
	        if (runOnBackgroundThread)
	        {
	            return await Task.Run(() => FindFilesCore(
	                searchPaths, projectPath, searchRegex, fileRegex,
	                contextLines, startIndex
	            )).ConfigureAwait(false);
	        }
	        else
	        {
	            // Run synchronously for tests - avoids deadlock with Unity's test runner
	            return FindFilesCore(
	                searchPaths, projectPath, searchRegex, fileRegex,
	                contextLines, startIndex
	            );
	        }
        }

        /// <summary>
        /// Core synchronous implementation of file search.
        /// Extracted to allow both async (via Task.Run) and sync execution.
        /// </summary>
        [ToolPermissionIgnore]
        static SearchFileContentOutput FindFilesCore(
            List<string> searchPaths,
            string projectPath,
            Regex searchRegex,
            Regex fileRegex,
            int contextLines,
            int startIndex)
        {
            const int maxResults = k_FindFilesMaxResults;
            var results = new SearchFileContentOutput { Matches = new List<FileMatch>(), Info = "" };

            // Step 1: Collect all candidate files (fast filtering by path)
            // Always excludes: Library folder and .meta files
            var allFiles = searchPaths
                .Where(Directory.Exists)
                .SelectMany(searchPath =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(searchPath, "*.*", SearchOption.AllDirectories);
                    }
                    catch
                    {
                        return Enumerable.Empty<string>();
                    }
                })
                .Select(file => new { File = file, RelativePath = Path.GetRelativePath(projectPath, file) })
                .Where(x => !x.RelativePath.StartsWith("Library" + Path.DirectorySeparatorChar))
                .Where(x => !x.File.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) // Always exclude .meta files
                .Where(x => fileRegex == null || fileRegex.IsMatch(x.RelativePath))
                .ToList();

            // Step 2: If no content search, just return file paths (no content reading needed)
            if (searchRegex == null)
            {
                var fileMatches = allFiles
                    .Skip(startIndex)
                    .Take(maxResults)
                    .Select(x => new FileMatch { FilePath = x.RelativePath })
                    .ToList();

                results.Matches = fileMatches;
                if (allFiles.Count > startIndex + maxResults)
                    results.Info = $"Showing {maxResults} of {allFiles.Count} files (limit: {maxResults}). Use startIndex={startIndex + maxResults} to fetch next page.";
                return results;
            }

            // Step 3: Content search - sequential but with fast File.ReadAllText
            var matchList = new List<FileMatch>();

            foreach (var x in allFiles)
            {
                // Early exit if we have enough results
                if (matchList.Count >= maxResults + startIndex)
                    break;

                try
                {
                    // Check if file is binary by looking for null bytes in first 8KB
                    // Binary files can't meaningfully match text patterns
                    if (IsBinaryFile(x.File))
                        continue;

                    // Read entire file at once (faster than line-by-line)
                    var content = File.ReadAllText(x.File);
                    
                    // Quick check: skip file if no match at all
                    try
                    {
                        if (!searchRegex.IsMatch(content))
                            continue;
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        continue;
                    }

                    // Find matching lines with context
                    var lines = content.Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (matchList.Count >= maxResults + startIndex)
                            break;

                        try
                        {
                            if (!searchRegex.IsMatch(lines[i]))
                                continue;
                        }
                        catch (RegexMatchTimeoutException)
                        {
                            // Skip this line if regex times out
                            continue;
                        }

                        // Build context
                        int startLine = Math.Max(0, i - contextLines);
                        int endLine = Math.Min(lines.Length - 1, i + contextLines);
                        
                        var contextContent = string.Join("\n", 
                            lines.Skip(startLine).Take(endLine - startLine + 1));
                        
                        if (contextContent.Length > k_MaxContentLength)
                            contextContent = contextContent.Substring(0, k_MaxContentLength) + "\n... [truncated]";

                        matchList.Add(new FileMatch
                        {
                            FilePath = x.RelativePath,
                            StartLineNumber = startLine + 1,  // First line in the context block
                            EndLineNumber = endLine + 1,      // Last line in the context block
                            MatchingContent = contextContent
                        });
                        
                        // Skip ahead to avoid overlapping contexts
                        i += contextLines;
                    }
                }
                catch
                {
                    // Skip files that can't be read
                }
            }

            // Step 4: Apply pagination and return results
            results.Matches = matchList
                .Skip(startIndex)
                .Take(maxResults)
                .ToList();

            // Note: matchList.Count >= limit means we hit the limit and there may be more matches
            if (matchList.Count >= startIndex + maxResults)
                results.Info = $"Showing {results.Matches.Count} matches (limit: {maxResults}). There may be more results. Use startIndex={startIndex + results.Matches.Count} to fetch next page.";

            return results;
        }

        /// <summary>
        /// Checks if a file is binary by looking for null bytes in the first 8KB.
        /// This is the same heuristic used by grep and other text search tools.
        /// </summary>
        static bool IsBinaryFile(string filePath)
        {
            const int bytesToCheck = 8192; // 8KB sample
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var buffer = new byte[Math.Min(bytesToCheck, stream.Length)];
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                
                // Array.IndexOf is optimized and faster than manual loop
                return Array.IndexOf(buffer, (byte)0, 0, bytesRead) >= 0;
            }
            catch
            {
                return false; // If we can't read it, assume it's not binary
            }
        }

        [AgentTool(
            "Returns the text content of a file.",
            k_GetFileContentFunctionId,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_SmartContextTag)]
        public static async Task<string> GetFileContent(
            ToolExecutionContext context,
            [Parameter("The path to the file, as returned by FindFile(...).")]
            string filePath,
            [Parameter("The first line number to read (1-based, inclusive). Defaults to 1.")]
            int startLine = 1,
            [Parameter("The last line number to read (1-based, inclusive). If -1, reads until the end of the file. Defaults to -1.")]
            int endLine = -1
            )
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return "File path cannot be null or empty.";

                var projectPath = Directory.GetCurrentDirectory();
                var fullPath = Path.IsPathRooted(filePath) ? filePath : Path.Combine(projectPath, filePath);

                if (!File.Exists(fullPath))
                    return $"File at path '{filePath}' not found";

                await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, fullPath);

                if (startLine == 1 && endLine == -1)
                {
                    return File.ReadAllText(fullPath);
                }

                // Handle partial read by finding line boundaries in the full text
                // to strictly preserve original line endings and whitespace.
                var content = File.ReadAllText(fullPath);
                if (string.IsNullOrEmpty(content))
                    return content;

                if (startLine < 1) 
                    startLine = 1;

                var lineStarts = new List<int> { 0 };
                for (var i = 0; i < content.Length; i++)
                {
                    if (content[i] == '\n')
                    {
                        lineStarts.Add(i + 1);
                    }
                }

                if (startLine > lineStarts.Count)
                    return string.Empty;

                var startIdx = lineStarts[startLine - 1];
                int endIdx;

                if (endLine == -1 || endLine >= lineStarts.Count)
                {
                    endIdx = content.Length;
                }
                else
                {
                    // endIdx is the start of the line AFTER the requested endLine
                    endIdx = lineStarts[endLine];
                }

                if (startIdx >= endIdx)
                    return string.Empty;

                return content.Substring(startIdx, endIdx - startIdx);
            }
            catch (UnauthorizedAccessException)
            {
                return $"Access denied to file '{filePath}'.";
            }
            catch (IOException ex)
            {
                return $"IO error reading file '{filePath}': {ex.Message}";
            }
            catch (Exception ex)
            {
                return $"Error reading file '{filePath}': {ex.Message}";
            }
        }
    }
}
