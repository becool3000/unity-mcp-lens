using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Becool.UnityMcpLens.Editor.Helpers;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Settings.UI
{
    static class CommandCenterLauncher
    {
        public static void Open()
        {
            if (!Application.platform.ToString().StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
            {
                EditorUtility.DisplayDialog(
                    "Unity MCP Lens",
                    "The external Lens Command Center is Windows-first in this release.",
                    "OK");
                return;
            }

            ServerInstaller.RefreshInstalledServers();

            string commandCenterPath = MCPConstants.LensInstalledCommandCenterMainFile;
            if (!File.Exists(commandCenterPath))
            {
                EditorUtility.DisplayDialog(
                    "Unity MCP Lens",
                    $"The Lens Command Center was not found at:\n{commandCenterPath}\n\nUse Install/Refresh after bundling or building the Command Center project.",
                    "OK");
                return;
            }

            try
            {
                using var process = Process.GetCurrentProcess();
                var startInfo = new ProcessStartInfo
                {
                    FileName = commandCenterPath,
                    Arguments = BuildArguments(
                        ("--project-root", GetProjectRoot()),
                        ("--package-root", Path.GetFullPath(MCPConstants.unityMcpLensAppPath)),
                        ("--status-dir", MCPConstants.StatusDirectory),
                        ("--unity-pid", process.Id.ToString())),
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(commandCenterPath) ?? MCPConstants.UnityMcpBaseDirectory
                };

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "Unity MCP Lens",
                    $"Could not open Lens Command Center:\n{ex.Message}",
                    "OK");
            }
        }

        static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        static string BuildArguments(params (string Name, string Value)[] arguments)
        {
            var builder = new StringBuilder();
            foreach (var (name, value) in arguments)
            {
                if (builder.Length > 0)
                    builder.Append(' ');
                builder.Append(Quote(name));
                builder.Append(' ');
                builder.Append(Quote(value ?? string.Empty));
            }

            return builder.ToString();
        }

        static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            string escaped = value.Replace("\"", "\\\"");
            if (escaped.EndsWith("\\", StringComparison.Ordinal))
                escaped += "\\";

            return "\"" + escaped + "\"";
        }
    }
}
