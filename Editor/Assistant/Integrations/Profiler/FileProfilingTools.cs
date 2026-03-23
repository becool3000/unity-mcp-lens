using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Tools.Editor;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class FileProfilingTools
    {
        [AgentTool("Search for content within files and return a list of matching file with the found content, including some context.",
            "Unity.Profiler.FindScriptFile",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask)]
        public static async Task<FileTools.SearchFileContentOutput> FindScriptFile(
            ToolExecutionContext context,
            [Parameter(
                "Regex pattern to search for in file contents.\n" +
                "Examples:\n" +
                "  \"TODO\": Match any line containing 'TODO'\n" +
                "  \"public\\s+class\\s+\\w+\": Match class declarations in C#\n" +
                "Leave empty to not filter by content (files will still be filtered by 'nameRegex').\n" +
                "In that case, will return a preview of the file content."
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
            string nameRegex = ""
        )
        {
            return await FileTools.FindFiles(context, searchPattern, nameRegex);
        }

        [AgentTool("Returns the number of lines of a C# script file",
            "Unity.Profiler.GetFileContentLineCount",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask)]
        public static async Task<int> GetFileContentLineCount(
            ToolExecutionContext context,
            [Parameter("The path to the file")]
            string filePath
            )
        {
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, filePath);
            var lines = File.ReadAllLines(filePath);
            return lines.Length;
        }

        [AgentTool("Returns the text content of a C# script file. Can return part of the file for easier processing",
            "Unity.Profiler.GetFileContent",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask)]
        public static async Task<string> GetFileContent(
            ToolExecutionContext context,
            [Parameter("The path to the file, as returned by FindScriptFile(...).")]
            string filePath,
            [Parameter("Start line of the text file. 0 is the start of the file")]
            int startLine = 0,
            [Parameter("Line count to return. -1 to return all lines")]
            int lineCount = -1
            )
        {
            await context.Permissions.CheckFileSystemAccess(IToolPermissions.ItemOperation.Read, filePath);
            if (!File.Exists(filePath))
                return $"File at path '{filePath}' not found";

            var lines = File.ReadAllLines(filePath);
            var start = Math.Min(startLine, lines.Length);
            if (lineCount == -1)
                lineCount = lines.Length;
            var count = Math.Min(lineCount, lines.Length - start);
            return string.Join(Environment.NewLine, lines, start, count);
        }

        [AgentTool("Returns the C# code of a specific profiling marker, if available.",
            "Unity.Profiler.GetMarkerCode",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask)]
        public static async Task<FileTools.SearchFileContentOutput> GetMarkerCode(
            ToolExecutionContext context,
            [Parameter("The name of the marker.")]
            string markerName
        )
        {
            var scriptTypeName = markerName;
            // strip the function brackets, these are also our best indicator that there is a an actual script file behind this marker
            var indexOfFunctionBrackets = scriptTypeName?.LastIndexOf("()") ?? -1;
            if (!string.IsNullOrEmpty(scriptTypeName) && indexOfFunctionBrackets > 0)
            {
                scriptTypeName = scriptTypeName.Substring(0, indexOfFunctionBrackets);
            }

            // Strip the namespace.
            // Also, if there was a namespace, the very first type part when splitting by "." is the main Type Name
            var lastIndexOfColon = scriptTypeName?.LastIndexOf(":") ?? -1;
            if (!string.IsNullOrEmpty(scriptTypeName) && lastIndexOfColon > 0)
            {
                scriptTypeName = scriptTypeName.Substring(lastIndexOfColon + 1);
            }

            // Make sure we only get the type name, ideally not a nested one as the file name would be unlikelz to use that as a name.
            var functionNameParts = scriptTypeName?.Split(".");
            if (!string.IsNullOrEmpty(scriptTypeName) && functionNameParts?.Length >= 2)
            {
                scriptTypeName = functionNameParts[lastIndexOfColon > 0 ? 0 : functionNameParts.Length - 2];
            }
            if (!string.IsNullOrEmpty(scriptTypeName))
                return await FindScriptFile(context, scriptTypeName, "\\.cs");

            return await Task.FromResult(new FileTools.SearchFileContentOutput());
        }
    }
}
