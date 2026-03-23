using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Unity.AI.MCP.Editor.Security
{
    /// <summary>
    /// Helper class to discover parent process information across Windows, macOS, and Linux.
    /// This is used to identify the MCP client that spawned the MCP server.
    /// </summary>
    static class ParentProcessHelper
    {
        /// <summary>
        /// Attempts to get parent process information for the given process ID.
        /// </summary>
        /// <param name="pid">Process ID to query</param>
        /// <param name="ppid">Parent process ID (output)</param>
        /// <param name="parentExePath">Parent executable path (output, "unknown" if unavailable)</param>
        /// <returns>True if parent info was successfully retrieved, false otherwise</returns>
        public static bool TryGetParentInfo(int pid, out int ppid, out string parentExePath)
        {
            ppid = -1;
            parentExePath = "unknown";

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Win_Try(pid, out ppid, out parentExePath);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return Mac_Try(pid, out ppid, out parentExePath);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return Linux_Try(pid, out ppid, out parentExePath);

            return false;
        }

        // -------------------- Windows --------------------
        static bool Win_Try(int pid, out int ppid, out string path)
        {
            ppid = -1;
            path = "unknown";
            IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snap == INVALID_HANDLE_VALUE) return false;

            try
            {
                var pe = new PROCESSENTRY32();
                pe.dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>();
                if (!Process32First(snap, ref pe)) return false;
                do
                {
                    if (pe.th32ProcessID == (uint)pid)
                    {
                        ppid = (int)pe.th32ParentProcessID;
                        break;
                    }
                } while (Process32Next(snap, ref pe));
            }
            finally
            {
                CloseHandle(snap);
            }

            if (ppid <= 0) return false;

            // Try to open parent and read its full image path
            const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)ppid);
            if (h != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int len = sb.Capacity;
                    if (QueryFullProcessImageName(h, 0, sb, ref len))
                        path = sb.ToString();
                }
                finally
                {
                    CloseHandle(h);
                }
            }
            return true;
        }

        const uint TH32CS_SNAPPROCESS = 0x00000002;
        static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct PROCESSENTRY32
        {
            public uint dwSize, cntUsage, th32ProcessID;
            public IntPtr th32DefaultHeapID;
            public uint th32ModuleID, cntThreads, th32ParentProcessID;
            public int pcPriClassBase;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeFile;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint th32ProcessID);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder exeName, ref int size);

        // -------------------- macOS --------------------
        static bool Mac_Try(int pid, out int ppid, out string path)
        {
            ppid = -1;
            path = "unknown";

            // Use ps command to get PPID
            try
            {
                var psi = new ProcessStartInfo("/bin/ps", $"-o ppid= -p {pid}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var ps = Process.Start(psi);
                string output = ps.StandardOutput.ReadToEnd();
                ps.WaitForExit(1000);
                if (!int.TryParse(output.Trim(), out ppid) || ppid <= 0) return false;
            }
            catch
            {
                return false;
            }

            // Get parent executable path using proc_pidpath
            try
            {
                var sb = new StringBuilder(4096);
                int ret = proc_pidpath(ppid, sb, (uint)sb.Capacity);
                if (ret > 0) path = sb.ToString();
            }
            catch
            {
                // Leave as "unknown"
            }

            return true;
        }

        [DllImport("/usr/lib/libproc.dylib", SetLastError = true)]
        static extern int proc_pidpath(int pid, StringBuilder buffer, uint buffersize);

        // -------------------- Linux --------------------
        static bool Linux_Try(int pid, out int ppid, out string path)
        {
            ppid = -1;
            path = "unknown";
            try
            {
                // Parse PPid from /proc/<pid>/status
                foreach (var line in File.ReadAllLines($"/proc/{pid}/status"))
                {
                    if (line.StartsWith("PPid:"))
                    {
                        var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 2 && int.TryParse(parts[1], out ppid)) break;
                    }
                }
            }
            catch
            {
                return false;
            }

            if (ppid <= 0) return false;

            try
            {
                // Resolve /proc/<ppid>/exe symlink
                byte[] buf = new byte[4096];
                IntPtr n = readlink($"/proc/{ppid}/exe", buf, (IntPtr)buf.Length);
                long len = n.ToInt64();
                if (len > 0 && len < buf.Length)
                    path = Encoding.UTF8.GetString(buf, 0, (int)len);
            }
            catch
            {
                // Leave as "unknown"
            }

            return true;
        }

        [DllImport("libc", SetLastError = true)]
        static extern IntPtr readlink(string pathname, byte[] buf, IntPtr bufsiz);
    }
}
