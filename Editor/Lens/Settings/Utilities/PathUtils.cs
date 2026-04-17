using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Settings.Utilities
{
    static class PathUtils
    {
        public static bool ValidatePath(string path, string requiredFileName)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(requiredFileName))
            {
                return false;
            }

            return Directory.Exists(path) && File.Exists(Path.Combine(path, requiredFileName));
        }


        public static string GetServerPath()
        {
            try
            {
                string relayPath = MCPConstants.RelayBaseDirectory;
                if (Directory.Exists(relayPath))
                    return relayPath;

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string GetLensServerPath()
        {
            try
            {
                string serverPath = MCPConstants.UnityMcpBaseDirectory;
                if (Directory.Exists(serverPath))
                    return serverPath;

                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }


        public static void OpenServerMainFile() => EditorUtility.RevealInFinder(GetServerMainFile());
        public static void OpenLensServerMainFile() => EditorUtility.RevealInFinder(GetLensServerMainFile());

        public static string GetServerMainFile() => MCPConstants.InstalledServerMainFile;
        public static string GetLensServerMainFile() => MCPConstants.LensInstalledServerMainFile;

        public static string GetServerMainFile(string serverPath)
        {
            // Legacy method for compatibility - now returns centralized location
            return MCPConstants.InstalledServerMainFile;
        }

        public static bool IsServerInstalled()
        {
            // Check if the relay binary exists
            return File.Exists(MCPConstants.InstalledServerMainFile);
        }

        public static bool IsLensServerInstalled()
        {
            return File.Exists(MCPConstants.LensInstalledServerMainFile);
        }

        public static string GetProjectDirectory()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }
    }
}
