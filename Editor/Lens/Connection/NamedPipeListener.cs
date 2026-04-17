using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using UnityEngine;

#if UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Reflection;
#endif

namespace Becool.UnityMcpLens.Editor.Connection
{
    /// <summary>
    /// Named Pipe listener for Windows - accepts multiple client connections with ACL-based security
    /// Uses P/Invoke to create secure pipes via SDDL, avoiding managed PipeSecurity API
    /// which causes type conflicts with NuGet packages and .NET Standard compatibility issues
    /// </summary>
    class NamedPipeListener : IConnectionListener
    {
        #if UNITY_EDITOR_WIN
        // P/Invoke declarations for Windows named pipe creation with security
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern SafeFileHandle CreateNamedPipe(
            string lpName,
            uint dwOpenMode,
            uint dwPipeMode,
            uint nMaxInstances,
            uint nOutBufferSize,
            uint nInBufferSize,
            uint nDefaultTimeOut,
            IntPtr lpSecurityAttributes);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(
            string StringSecurityDescriptor,
            uint StringSDRevision,
            out IntPtr SecurityDescriptor,
            IntPtr SecurityDescriptorSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr LocalFree(IntPtr hMem);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool GetUserName(System.Text.StringBuilder lpBuffer, ref int nSize);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool LookupAccountName(
            string lpSystemName,
            string lpAccountName,
            IntPtr Sid,
            ref int cbSid,
            System.Text.StringBuilder ReferencedDomainName,
            ref int cchReferencedDomainName,
            out int peUse);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool ConvertSidToStringSid(IntPtr pSid, out IntPtr ptrStringSid);

        // Named pipe constants
        const uint PIPE_ACCESS_DUPLEX = 0x00000003;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        const uint PIPE_TYPE_BYTE = 0x00000000;
        const uint PIPE_READMODE_BYTE = 0x00000000;
        const uint PIPE_WAIT = 0x00000000;
        const uint PIPE_UNLIMITED_INSTANCES = 255;
        const uint SDDL_REVISION_1 = 1;

        [StructLayout(LayoutKind.Sequential)]
        struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }
        #endif

        string pipeName;
        bool isListening;
#pragma warning disable CS0414 // Field is assigned but its value is never used (only used on Windows)
        int clientCounter;
#pragma warning restore CS0414
        bool isDisposed;
        readonly object lockObj = new();

        public bool IsListening => isListening;
        public string ConnectionPath => pipeName != null ? $"\\\\.\\pipe\\{pipeName}" : null;

        public void Start(string connectionPath)
        {
            lock (lockObj)
            {
                if (isListening)
                {
                    McpLog.Log($"NamedPipeListener already listening on {ConnectionPath}");
                    return;
                }

                if (isDisposed)
                    throw new ObjectDisposedException(nameof(NamedPipeListener));

                // Extract pipe name from path (e.g., "\\.\pipe\unity-mcp-abc123" -> "unity-mcp-abc123")
                pipeName = ExtractPipeName(connectionPath);

                if (string.IsNullOrEmpty(pipeName))
                    throw new ArgumentException("Invalid pipe path", nameof(connectionPath));

                isListening = true;
                clientCounter = 0;
            }
        }

        public void Stop()
        {
            lock (lockObj)
            {
                if (!isListening)
                    return;

                isListening = false;
                pipeName = null;
            }
        }

#pragma warning disable CS1998 // Async method lacks 'await' operators (only awaits on Windows platform)
        public async Task<IConnectionTransport> AcceptClientAsync(CancellationToken cancellationToken)
        {
            if (!isListening)
                throw new InvalidOperationException("Listener is not running");

            if (isDisposed)
                throw new ObjectDisposedException(nameof(NamedPipeListener));

            #if UNITY_EDITOR_WIN
            try
            {
                NamedPipeServerStream pipeStream = null;

                // Create pipe with security ACL via P/Invoke (Windows API directly)
                // We use P/Invoke rather than the managed PipeSecurity API to avoid
                // CS0433 conflicts when users have NuGet packages (e.g. AWS GameLift SDK)
                // that bring in System.Security.Principal.Windows / System.Security.AccessControl,
                // and to avoid CS1729 errors in .NET Standard where the 8-arg
                // NamedPipeServerStream constructor doesn't exist.
                string fullPipePath = $"\\\\.\\pipe\\{pipeName}";
                SafeFileHandle fileHandle = CreatePipeViaPInvoke(fullPipePath);

                if (fileHandle != null && !fileHandle.IsInvalid)
                {
                    // P/Invoke succeeded - convert handle and use it
                    SafePipeHandle pipeHandle = ConvertFileHandleToPipeHandle(fileHandle);

                    if (pipeHandle != null && !pipeHandle.IsInvalid)
                    {
                        pipeStream = new NamedPipeServerStream(
                            PipeDirection.InOut,
                            true, // isAsync
                            false, // isConnected (will connect below)
                            pipeHandle);
                    }
                    else
                    {
                        fileHandle?.Dispose();
                    }
                }

                // If P/Invoke failed, fall back to pipe without ACL
                // This happens in restricted CI/test environments where security APIs are unavailable
                if (pipeStream == null)
                {
                    McpLog.Warning("Could not create secure pipe - falling back to unsecured pipe. " +
                                  "This is acceptable in test/CI environments.");

                    pipeStream = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous,
                        0, // inBufferSize (0 = default)
                        0); // outBufferSize (0 = default)
                }

                try
                {
                    // Wait for a client to connect
                    await pipeStream.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);

                    // Increment client counter for unique IDs
                    int clientId;
                    lock (lockObj)
                    {
                        clientId = ++clientCounter;
                    }

                    string connectionId = $"NamedPipe-{clientId}";
                    return new NamedPipeTransport(pipeStream, connectionId);
                }
                catch
                {
                    // Clean up pipe stream if connection failed
                    pipeStream?.Dispose();
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                McpLog.Error($"Error accepting client: {ex.Message}");
                throw;
            }
            #else
            // This should not be reached - Unix platforms use UnixSocketListener
            throw new PlatformNotSupportedException("NamedPipeListener is Windows-only. Use UnixSocketListener on Mac/Linux.");
            #endif
        }
#pragma warning restore CS1998

        static string ExtractPipeName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Handle Windows pipe paths: \\.\pipe\name -> name
            if (path.StartsWith("\\\\.\\pipe\\", StringComparison.OrdinalIgnoreCase))
                return path.Substring("\\\\.\\pipe\\".Length);

            // If already just a name, use it directly
            return path;
        }

        #if UNITY_EDITOR_WIN
        /// <summary>
        /// Convert SafeFileHandle to SafePipeHandle for Unity's .NET runtime.
        /// Some Unity runtimes require explicit SafePipeHandle type for NamedPipeServerStream constructor.
        /// </summary>
        static SafePipeHandle ConvertFileHandleToPipeHandle(SafeFileHandle fileHandle)
        {
            try
            {
                // Get the raw handle value from SafeFileHandle
                IntPtr rawHandle = fileHandle.DangerousGetHandle();

                // Create SafePipeHandle using reflection (constructor is often internal/private)
                // SafePipeHandle(IntPtr handle, bool ownsHandle)
                var constructor = typeof(SafePipeHandle).GetConstructor(
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new Type[] { typeof(IntPtr), typeof(bool) },
                    null);

                if (constructor != null)
                {
                    // Create pipe handle, transferring ownership from fileHandle
                    var pipeHandle = (SafePipeHandle)constructor.Invoke(new object[] { rawHandle, true });

                    // Prevent fileHandle from closing the handle since pipeHandle now owns it
                    fileHandle.SetHandleAsInvalid();

                    return pipeHandle;
                }
                else
                {
                    McpLog.Error("Failed to find SafePipeHandle constructor via reflection");
                    return null;
                }
            }
            catch (Exception ex)
            {
                McpLog.Error($"Failed to convert SafeFileHandle to SafePipeHandle: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get the current user's SID string using P/Invoke.
        /// Returns null if unable to get the SID.
        /// </summary>
        static string GetCurrentUserSid()
        {
            try
            {
                // Get current username
                var username = new System.Text.StringBuilder(256);
                int usernameSize = username.Capacity;
                if (!GetUserName(username, ref usernameSize))
                    return null;

                string currentUser = username.ToString();

                // First call to get required buffer sizes
                int sidSize = 0;
                int domainSize = 0;
                int peUse;
                LookupAccountName(null, currentUser, IntPtr.Zero, ref sidSize, null, ref domainSize, out peUse);

                if (sidSize == 0)
                    return null;

                // Allocate buffers and make real call
                IntPtr sidPtr = Marshal.AllocHGlobal(sidSize);
                var domain = new System.Text.StringBuilder(domainSize);

                try
                {
                    if (!LookupAccountName(null, currentUser, sidPtr, ref sidSize, domain, ref domainSize, out peUse))
                        return null;

                    // Convert SID to string
                    IntPtr stringSidPtr;
                    if (!ConvertSidToStringSid(sidPtr, out stringSidPtr))
                        return null;

                    try
                    {
                        // ConvertSidToStringSid returns an ANSI string, not Unicode
                        return Marshal.PtrToStringAnsi(stringSidPtr);
                    }
                    finally
                    {
                        LocalFree(stringSidPtr);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(sidPtr);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Create a secure named pipe using P/Invoke (Windows API directly).
        /// Creates a secure named pipe using P/Invoke (Windows API directly) with owner-only ACL via SDDL.
        /// </summary>
        static SafeFileHandle CreatePipeViaPInvoke(string fullPipePath)
        {
            IntPtr securityDescriptor = IntPtr.Zero;
            try
            {
                // Try to get current user SID for more reliable security
                string userSid = GetCurrentUserSid();

                string sddl;
                if (!string.IsNullOrEmpty(userSid))
                {
                    // SDDL string with specific user SID - most secure and reliable
                    // D: = DACL (Discretionary Access Control List)
                    // (A;;GA;;;{SID}) = Allow Generic All to specific user SID
                    // (A;;GA;;;SY) = Allow Generic All to System
                    sddl = $"D:(A;;GA;;;{userSid})(A;;GA;;;SY)";
                }
                else
                {
                    // Fallback to owner-based SDDL if we can't get SID
                    // (A;;GA;;;OW) = Allow Generic All to Owner
                    // (A;;GA;;;SY) = Allow Generic All to System
                    sddl = "D:(A;;GA;;;OW)(A;;GA;;;SY)";
                    McpLog.Log("Using owner-based SDDL (could not retrieve user SID)");
                }

                // Convert SDDL to security descriptor
                if (!ConvertStringSecurityDescriptorToSecurityDescriptor(
                    sddl,
                    SDDL_REVISION_1,
                    out securityDescriptor,
                    IntPtr.Zero))
                {
                    return null;
                }

                // Create SECURITY_ATTRIBUTES structure
                var sa = new SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                    lpSecurityDescriptor = securityDescriptor,
                    bInheritHandle = 0
                };

                // Pin the structure and get pointer
                IntPtr pSa = Marshal.AllocHGlobal(sa.nLength);
                try
                {
                    Marshal.StructureToPtr(sa, pSa, false);

                    // Create the named pipe with security attributes
                    SafeFileHandle handle = CreateNamedPipe(
                        fullPipePath,
                        PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED,
                        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT,
                        PIPE_UNLIMITED_INSTANCES,
                        0, // default buffer size
                        0, // default buffer size
                        0, // default timeout
                        pSa);

                    if (handle == null || handle.IsInvalid)
                    {
                        return null;
                    }

                    McpLog.Log("Created secure pipe via P/Invoke");
                    return handle;
                }
                finally
                {
                    Marshal.FreeHGlobal(pSa);
                }
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (securityDescriptor != IntPtr.Zero)
                {
                    LocalFree(securityDescriptor);
                }
            }
        }
        #endif

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;
                Stop();
            }
        }
    }
}
