using System;
using System.Runtime.InteropServices;

namespace Becool.UnityMcpLens.Editor.Connection
{
    /// <summary>
    /// Factory for creating platform-specific connection listeners
    /// </summary>
    static class ConnectionFactory
    {
        /// <summary>
        /// Create the appropriate connection listener for the current platform.
        /// Windows: Uses NamedPipeListener with ACL-based security.
        /// Mac/Linux: Uses UnixSocketListener with P/Invoke-based Unix domain sockets (UnixDomainSocketEndPoint not available in Unity).
        /// </summary>
        public static IConnectionListener CreateListener()
        {
            #if UNITY_EDITOR_WIN
            return new NamedPipeListener();
            #else
            return new UnixSocketListener();
            #endif
        }

        /// <summary>
        /// Check if running on Windows
        /// </summary>
        public static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        /// <summary>
        /// Check if running on Mac
        /// </summary>
        public static bool IsMacOS() => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>
        /// Check if running on Linux
        /// </summary>
        public static bool IsLinux() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        /// <summary>
        /// Get platform-specific connection type name
        /// </summary>
        public static string GetConnectionTypeName() => IsWindows() ? "Named Pipe" : "Unix Socket";
    }
}
