using System.Runtime.InteropServices;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Settings.Utilities
{
    /// <summary>
    /// Provides platform detection utilities for determining the current operating system
    /// and retrieving platform-specific configuration paths.
    /// </summary>
    static class PlatformUtils
    {
        /// <summary>
        /// Gets a value indicating whether the current platform is Windows.
        /// </summary>
        public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                                       Application.platform == RuntimePlatform.WindowsEditor;

        /// <summary>
        /// Gets a value indicating whether the current platform is macOS.
        /// </summary>
        public static bool IsMacOS => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ||
                                     Application.platform == RuntimePlatform.OSXEditor;

        /// <summary>
        /// Gets a value indicating whether the current platform is Linux.
        /// </summary>
        public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                                     Application.platform == RuntimePlatform.LinuxEditor;

        /// <summary>
        /// Gets the platform-specific configuration file path for the specified MCP client.
        /// </summary>
        /// <param name="client">The MCP client to get the configuration path for.</param>
        /// <returns>The platform-specific configuration path, or an empty string if the platform is unsupported.</returns>
        public static string GetConfigPathForClient(Models.McpClient client)
        {
            if (IsWindows)
            {
                return client.windowsConfigPath;
            }

            if (IsMacOS)
            {
                return string.IsNullOrEmpty(client.macConfigPath) ? client.linuxConfigPath : client.macConfigPath;
            }

            if (IsLinux)
            {
                return client.linuxConfigPath;
            }

            return string.Empty;
        }
    }
}
