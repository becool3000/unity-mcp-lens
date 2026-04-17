using System;
using System.Diagnostics;
using System.IO;
using Becool.UnityMcpLens.Editor.Models;

namespace Becool.UnityMcpLens.Editor.Security
{
    /// <summary>
    /// Validates MCP server processes using security checks.
    /// Focuses purely on validation logic - does NOT collect process information.
    /// </summary>
    static class ProcessValidator
    {
        /// <summary>
        /// Validate a server process using the provided process information.
        /// Returns a tuple of (isValid, reason).
        /// </summary>
        public static (bool isValid, string reason) ValidateServer(ProcessInfo serverInfo, ValidationConfig config)
        {
            if (serverInfo == null)
                return (false, "Server process information is null");

            if (serverInfo.Identity == null)
                return (false, "Server executable identity is null");

            // Skip validation if disabled
            if (!config.Enabled || config.Mode == ValidationMode.Disabled)
                return (true, "Validation disabled");

            // Step 1: Verify process still exists and hasn't been replaced (PID reuse check)
            var pidReuseCheck = VerifyProcessStillValid(serverInfo.ProcessId, serverInfo.StartTime);
            if (!pidReuseCheck.isValid)
                return pidReuseCheck;

            // Step 2: TOCTOU mitigation - verify executable hasn't been modified after process started
            var toctouCheck = VerifyExecutableNotModified(serverInfo.Identity, serverInfo.StartTime);
            if (!toctouCheck.isValid)
                return toctouCheck;

            // Step 3: Validate code signature (if configured)
            var signatureCheck = ValidateSignature(serverInfo.Identity, config);
            if (!signatureCheck.isValid)
                return signatureCheck;

            return (true, "Server validation successful");
        }

        /// <summary>
        /// Verify the process still exists and hasn't been replaced (PID reuse protection)
        /// </summary>
        static (bool isValid, string reason) VerifyProcessStillValid(int pid, DateTime expectedStartTime)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                using (process)
                {
                    DateTime actualStartTime = process.StartTime;

                    // Allow 1 second tolerance for DateTime precision differences
                    TimeSpan difference = (actualStartTime - expectedStartTime).Duration();
                    if (difference.TotalSeconds > 1.0)
                    {
                        return (false, $"PID reuse detected - process was replaced (expected: {expectedStartTime}, actual: {actualStartTime})");
                    }
                }
                return (true, "Process still valid");
            }
            catch (ArgumentException)
            {
                return (false, "Process no longer exists");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to verify process: {ex.Message}");
            }
        }

        /// <summary>
        /// Verify the executable hasn't been modified after the process started (TOCTOU protection)
        /// </summary>
        static (bool isValid, string reason) VerifyExecutableNotModified(ExecutableIdentity identity, DateTime processStartTime)
        {
            try
            {
                if (!File.Exists(identity.Path))
                {
                    return (false, $"Executable not found: {identity.Path}");
                }

                DateTime currentModTime = File.GetLastWriteTime(identity.Path);

                // The executable should not be modified after the process started
                // Allow small tolerance for filesystem timestamp precision
                if (currentModTime > processStartTime.AddSeconds(1))
                {
                    return (false, $"Executable modified after process start (file: {currentModTime}, process started: {processStartTime})");
                }

                return (true, "Executable not modified");
            }
            catch (Exception ex)
            {
                return (false, $"Failed to check executable timestamp: {ex.Message}");
            }
        }

        /// <summary>
        /// Validate code signature of the executable
        /// </summary>
        static (bool isValid, string reason) ValidateSignature(ExecutableIdentity identity, ValidationConfig config)
        {
            // If identity already has signature info collected, use it
            if (identity.IsSigned)
            {
                if (!identity.SignatureValid)
                {
                    return (false, $"Code signature invalid: {identity.Path}");
                }

                // Check if signature matches expected publisher
                #if UNITY_EDITOR_WIN
                if (!string.IsNullOrEmpty(config.WindowsPublisher))
                {
                    if (!identity.MatchesPublisher(config.WindowsPublisher))
                    {
                        return (false, $"Publisher mismatch. Expected: {config.WindowsPublisher}, Got: {identity.SignaturePublisher}");
                    }
                }
                #elif UNITY_EDITOR_OSX
                if (!string.IsNullOrEmpty(config.MacTeamId))
                {
                    if (!identity.MatchesPublisher(config.MacTeamId))
                    {
                        return (false, $"Team ID mismatch. Expected: {config.MacTeamId}, Got: {identity.SignaturePublisher}");
                    }
                }
                #endif

                return (true, $"Valid signature from {identity.SignaturePublisher}");
            }
            else
            {
                // Not signed
                return (false, $"Executable is not signed: {identity.Path}");
            }
        }
    }
}
