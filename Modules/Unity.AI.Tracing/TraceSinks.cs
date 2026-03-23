using System;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
using System.Runtime.InteropServices;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif
using Debug = UnityEngine.Debug;

namespace Unity.AI.Tracing
{
    /// <summary>
    /// Writes trace events as JSON lines to traces.jsonl.
    /// Uses FileShare.ReadWrite to allow concurrent access from Node.js processes.
    /// </summary>
    class FileSink : ITraceSink
    {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
        // P/Invoke for atomic append on Unix. .NET's FileMode.Append uses lseek+write
        // (TOCTOU race) instead of O_APPEND, causing interleaving with concurrent Node.js writers.
        [DllImport("libc", SetLastError = true)]
        static extern int open(string pathname, int flags, int mode);

        [DllImport("libc", SetLastError = true)]
        static extern nint write(int fd, byte[] buf, nint count);

        [DllImport("libc", SetLastError = true)]
        static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        static extern int chmod(string pathname, int mode);

#if UNITY_EDITOR_OSX
        const int O_WRONLY = 0x1;
        const int O_CREAT  = 0x200;
        const int O_APPEND = 0x8;
#else // Linux
        const int O_WRONLY = 0x1;
        const int O_CREAT  = 0x40;
        const int O_APPEND = 0x400;
#endif
#endif

        public string Name => "file";
        public TraceConfig Config { get; set; }

        readonly string m_FilePath;
        readonly object m_Lock = new object();
        bool m_DirEnsured;
        bool m_PermissionsSet;

        /// <summary>
        /// Ensure traces.jsonl is readable by all users (0666) regardless of umask.
        /// Called once after the file is first created. Without this, a restrictive
        /// umask (e.g. 0077 on CI) would produce 0600 permissions, preventing the
        /// test runner from copying logs after tests complete.
        /// </summary>
        void EnsurePermissions()
        {
            if (m_PermissionsSet)
                return;
            m_PermissionsSet = true;
#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
            chmod(m_FilePath, 0x1B6 /* 0666 */);
#endif
        }

        public FileSink(string logDir, TraceConfig config, bool truncateOnStart = false)
        {
            Config = config;
            m_FilePath = Path.Combine(logDir, "traces.jsonl");

            if (truncateOnStart)
            {
                try
                {
                    var dir = Path.GetDirectoryName(m_FilePath);
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir!);
                    File.WriteAllText(m_FilePath, "");
                    EnsurePermissions();
                    m_DirEnsured = true;

                    var artifactDir = Path.Combine(logDir, "artifacts");
                    if (Directory.Exists(artifactDir))
                        Directory.Delete(artifactDir, true);
                }
                catch
                {
                    // Non-fatal
                }
            }
        }

        public void Write(TraceEvent evt)
        {
            try
            {
                lock (m_Lock)
                {
                    if (!m_DirEnsured)
                    {
                        var dir = Path.GetDirectoryName(m_FilePath);
                        if (!Directory.Exists(dir))
                            Directory.CreateDirectory(dir!);
                        m_DirEnsured = true;
                    }

                    var json = JsonConvert.SerializeObject(evt, Formatting.None,
                        new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

                    var bytes = Encoding.UTF8.GetBytes(json + "\n");

#if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                    // Use native open()+write() with O_APPEND for atomic appends on Unix.
                    // .NET's FileMode.Append uses lseek+write which races with
                    // concurrent Node.js appendFileSync calls (which use O_APPEND).
                    int fd = open(m_FilePath, O_WRONLY | O_CREAT | O_APPEND, 0x1B6 /* 0666 */);
                    if (fd < 0)
                        return;
                    try
                    {
                        EnsurePermissions();
                        write(fd, bytes, bytes.Length);
                    }
                    finally
                    {
                        close(fd);
                    }
#else
                    // On Windows, FileMode.Append maps to FILE_APPEND_DATA which is atomic.
                    using var fs = new FileStream(m_FilePath, FileMode.Append, FileAccess.Write,
                        FileShare.ReadWrite, bufferSize: 1, FileOptions.None);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush();
#endif
                }
            }
            catch
            {
                // File write failure is non-fatal
            }
        }
    }

    /// <summary>
    /// Writes trace events as human-readable text to the Unity console via Debug.Log.
    /// Thread-safe: dispatches to the main thread when called from background threads,
    /// since Debug.Log* must run on the main thread.
    /// </summary>
    class ConsoleSink : ITraceSink
    {
        public string Name => "console";
        public TraceConfig Config { get; set; }

        static readonly int s_MainThreadId;

        static ConsoleSink()
        {
            // Static constructor runs on main thread during domain load
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public ConsoleSink(TraceConfig config)
        {
            Config = config;
        }

        public void Write(TraceEvent evt)
        {
            // Unity console already provides timestamp and level (via LogError/LogWarning/Log).
            // Component, kind, and name are stored structurally in the trace; just show the message.
            var message = evt.data?["message"]?.ToString() ?? evt.name;
            var extra = FormatExtraData(evt);
            if (extra.Length > 0)
                message = $"{message}\n{extra}";

            // Debug.Log* must be called on the main thread.
            if (Thread.CurrentThread.ManagedThreadId == s_MainThreadId)
            {
                LogToConsole(evt.level, message, evt.exception);
            }
            else
            {
#if UNITY_EDITOR
                var exception = evt.exception;
                // Dispatch to main thread via the editor's dispatcher
                UnityEditor.Search.Dispatcher.Enqueue(() =>
                {
                    try { LogToConsole(evt.level, message, exception); }
                    catch { /* Sink errors must never break the application */ }
                });
#else
                // At runtime, Debug.Log is generally safe from any thread
                LogToConsole(evt.level, message, evt.exception);
#endif
            }
        }

        static void LogToConsole(string level, string message, Exception exception)
        {
            if (exception != null)
            {
                Debug.LogException(exception);
                return;
            }

            switch (level)
            {
                case "error":
                    Debug.LogError(message);
                    break;
                case "warn":
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }

        static string FormatExtraData(TraceEvent evt)
        {
            if (evt.data == null || !evt.data.HasValues) return "";

            var parts = new System.Collections.Generic.List<string>();
            foreach (var prop in evt.data.Properties())
            {
                if (prop.Name == "message") continue;
                var val = prop.Value.ToString();
                if (val.Length > 100) val = val.Substring(0, 100) + "...";
                parts.Add($"{prop.Name}={val}");
            }
            return parts.Count > 0 ? string.Join(" ", parts) : "";
        }
    }
}
