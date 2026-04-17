using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Becool.UnityMcpLens.Editor.Models;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Security
{
    /// <summary>
    /// Collects process information including executable identity and parent process chain.
    /// </summary>
    static class ProcessInfoCollector
    {
        #if UNITY_EDITOR_OSX
        // Mac-specific: Use libproc to get executable path
        [DllImport("/usr/lib/libproc.dylib", SetLastError = true)]
        static extern int proc_pidpath(int pid, StringBuilder buffer, uint buffersize);
        #endif

        /// <summary>
        /// Collect process information for a given PID
        /// </summary>
        public static ProcessInfo CollectProcessInfo(int pid)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                using (process)
                {
                    DateTime startTime = process.StartTime;
                    string executablePath = GetExecutablePath(pid);

                    if (string.IsNullOrEmpty(executablePath))
                    {
                        return new ProcessInfo
                        {
                            ProcessId = pid,
                            ProcessName = process.ProcessName ?? "unknown",
                            StartTime = startTime,
                            Identity = null
                        };
                    }

                    return new ProcessInfo
                    {
                        ProcessId = pid,
                        ProcessName = Path.GetFileNameWithoutExtension(executablePath),
                        StartTime = startTime,
                        Identity = ExecutableIdentityCollector.CollectIdentity(executablePath)
                    };
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to collect process info for PID {pid}: {ex.Message}");
                return new ProcessInfo
                {
                    ProcessId = pid,
                    ProcessName = "unknown",
                    StartTime = DateTime.MinValue,
                    Identity = null
                };
            }
        }

        /// <summary>
        /// Collect complete connection information including server and client processes
        /// </summary>
        public static ConnectionInfo CollectConnectionInfo(int serverPid, ValidationConfig config)
        {
            var connectionInfo = new ConnectionInfo
            {
                ConnectionId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                Server = CollectProcessInfo(serverPid)
            };

            // Collect parent (client) information if enabled
            if (config.CollectParentInfo)
            {
                var (parentPid, parentPath, chainDepth) = FindMcpClient(serverPid, config.MaxParentChainDepth);
                if (parentPid.HasValue && parentPid.Value > 1)
                {
                    connectionInfo.Client = CollectProcessInfo(parentPid.Value);
                    connectionInfo.ClientChainDepth = chainDepth;
                }
            }

            return connectionInfo;
        }

        /// <summary>
        /// Get executable path for a process ID
        /// </summary>
        static string GetExecutablePath(int pid)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                using (process)
                {
                    #if UNITY_EDITOR_OSX
                    // On Mac, use proc_pidpath for more reliable path retrieval
                    var sb = new StringBuilder(4096);
                    int ret = proc_pidpath(pid, sb, (uint)sb.Capacity);
                    if (ret > 0)
                    {
                        return sb.ToString();
                    }
                    // Fallback to Process.MainModule
                    return process.MainModule?.FileName;
                    #else
                    return process.MainModule?.FileName;
                    #endif
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to get executable path for PID {pid}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Walk up the process tree to find the actual MCP client, skipping intermediate shells
        /// </summary>
        static (int? parentPid, string parentPath, int depth) FindMcpClient(int serverPid, int maxDepth)
        {
            int currentPid = serverPid;

            for (int depth = 0; depth < maxDepth; depth++)
            {
                if (!ParentProcessHelper.TryGetParentInfo(currentPid, out int ppid, out string ppath))
                {
                    // Could not get parent info
                    return (null, null, depth);
                }

                if (ppid <= 1)
                {
                    // Reached init/launchd (PID 1) - parent probably exited
                    return (null, null, depth);
                }

                // Check if this is a shell or launcher (keep walking up if so)
                string processName = Path.GetFileNameWithoutExtension(ppath ?? "").ToLowerInvariant();

                bool isShell = processName.Contains("sh") ||      // sh, bash, zsh, dash, etc.
                               processName.Contains("cmd") ||      // cmd.exe
                               processName.Contains("powershell") || // powershell.exe
                               processName.Contains("pwsh") ||     // pwsh (PowerShell Core)
                               processName.Contains("conhost");    // conhost.exe

                // Note: We intentionally do NOT include "terminal" in the shell check because
                // WindowsTerminal.exe, Terminal.app, etc. are the actual MCP clients, not shells to skip

                if (!isShell || ppath == "unknown")
                {
                    // Found a real MCP client (not a shell)
                    return (ppid, ppath, depth + 1);
                }

                // Keep walking up the chain
                currentPid = ppid;
            }

            // Reached max depth without finding a non-shell parent
            return (null, null, maxDepth);
        }
    }
}
