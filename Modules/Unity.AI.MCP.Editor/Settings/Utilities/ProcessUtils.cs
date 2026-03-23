using System;
using System.Diagnostics;

namespace Unity.AI.MCP.Editor.Settings.Utilities
{
    static class ProcessUtils
    {
        internal struct ProcessResult
        {
            public bool Success;
            public int ExitCode;
            public string Output;
            public string Error;
        }

        internal static ProcessResult Execute(string fileName, string arguments, string workingDirectory = null, int timeoutMs = 10000)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    processInfo.WorkingDirectory = workingDirectory;
                }

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        return new ProcessResult { Success = false, ExitCode = -1, Error = "Failed to start process" };
                    }

                    bool finished = process.WaitForExit(timeoutMs);
                    if (!finished)
                    {
                        process.Kill();
                        return new ProcessResult { Success = false, ExitCode = -1, Error = "Process timed out" };
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    return new ProcessResult
                    {
                        Success = process.ExitCode == 0,
                        ExitCode = process.ExitCode,
                        Output = output,
                        Error = error
                    };
                }
            }
            catch (Exception ex)
            {
                return new ProcessResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = ex.Message
                };
            }
        }

        internal static ProcessResult ExecuteSimple(string fileName, string arguments, string workingDirectory = null)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (!string.IsNullOrEmpty(workingDirectory))
                {
                    processInfo.WorkingDirectory = workingDirectory;
                }

                using (var process = Process.Start(processInfo))
                {
                    process?.WaitForExit(10000);
                    return new ProcessResult
                    {
                        Success = process?.ExitCode == 0,
                        ExitCode = process?.ExitCode ?? -1
                    };
                }
            }
            catch (Exception ex)
            {
                return new ProcessResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = ex.Message
                };
            }
        }
    }
}