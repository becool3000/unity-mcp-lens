using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;

namespace Unity.AI.Assistant.Agent.Dynamic.Extension.Editor
{
    class AssemblyCSProjectPostprocessor : AssetPostprocessor
    {
        public static string OnGeneratedCSProject(string path, string content)
        {
            if (!path.EndsWith(AssemblyCSProject.FileName))
                return content;

            if (!AssemblyCSProject.TemporaryFiles.Any())
                return content;

            // Add temporary files inside this Assembly .csproj
            var match = Regex.Match(content, @"(<ItemGroup>\s*(?:<Compile Include=.*?\.cs"" />[\s\S]*?)</ItemGroup>)", RegexOptions.Singleline);

            if (!match.Success) return content;

            string itemGroup = match.Groups[1].Value;
            string newCompileItems = "";

            foreach (string tempFilePath in AssemblyCSProject.TemporaryFiles)
            {
                string fullPath = Path.GetFullPath(tempFilePath);
                string escapedPath = fullPath.Replace(@"\", @"\\");

                // Check if the file is already included
                if (!content.Contains(escapedPath))
                {
                    newCompileItems += $"\n    <Compile Include=\"{escapedPath}\" />";
                }
            }

            if (!string.IsNullOrEmpty(newCompileItems))
            {
                string updatedItemGroup = itemGroup.Insert(itemGroup.LastIndexOf("</ItemGroup>", StringComparison.Ordinal), newCompileItems);
                content = content.Replace(itemGroup, updatedItemGroup);
            }

            return content;
        }

    }
}
