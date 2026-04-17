using System;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Connection
{
    /// <summary>
    /// Unix domain socket listener for Mac/Linux - accepts multiple client connections with file permission-based security (0600).
    /// Uses P/Invoke for raw socket operations since UnixDomainSocketEndPoint is not available in Unity's .NET Standard 2.1 runtime.
    /// </summary>
    class UnixSocketListener : IConnectionListener
    {
        // P/Invoke declarations for Unix socket operations
        [DllImport("libc", SetLastError = true)]
        static extern int socket(int domain, int type, int protocol);

        [DllImport("libc", SetLastError = true)]
        static extern int bind(int sockfd, ref SockAddrUn addr, int addrlen);

        [DllImport("libc", SetLastError = true)]
        static extern int listen(int sockfd, int backlog);

        [DllImport("libc", SetLastError = true)]
        static extern int accept(int sockfd, IntPtr addr, IntPtr addrlen);

        [DllImport("libc", SetLastError = true)]
        static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        static extern int chmod(string pathname, uint mode);

        [DllImport("libc", SetLastError = true)]
        static extern uint umask(uint mask);

        // Unix domain socket address structure
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        struct SockAddrUn
        {
            public ushort sun_family; // AF_UNIX
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 108)]
            public string sun_path;   // Socket path (max 108 bytes on most systems)
        }

        // Constants
        const int AF_UNIX = 1;        // Unix domain sockets
        const int SOCK_STREAM = 1;    // Stream socket (like TCP)
        const int BACKLOG = 5;        // Listen backlog
        const uint S_IRUSR = 0x100;   // Owner read
        const uint S_IWUSR = 0x080;   // Owner write
        const uint OWNER_ONLY_PERMS = S_IRUSR | S_IWUSR; // 0600
        const uint UMASK_OWNER_ONLY = 0x077; // Mask out group and other (umask 077)

        // Error codes for accept() retry logic
        const int EINTR = 4;          // Interrupted system call - retry
        const int EBADF = 9;          // Bad file descriptor - socket was closed
        #if UNITY_EDITOR_OSX
        const int ECONNABORTED = 53;  // Connection aborted (macOS) - retry
        #elif UNITY_EDITOR_LINUX
        const int ECONNABORTED = 103; // Connection aborted (Linux) - retry
        #endif

        string socketPath;
        string socketDir;
        int serverSocket = -1;
        bool isListening;
        int clientCounter;
        bool isDisposed;
        readonly object lockObj = new();

        public bool IsListening => isListening;
        public string ConnectionPath => socketPath;

        public void Start(string connectionPath)
        {
            lock (lockObj)
            {
                if (isListening)
                {
                    McpLog.Log($"UnixSocketListener already listening on {ConnectionPath}");
                    return;
                }

                if (isDisposed)
                    throw new ObjectDisposedException(nameof(UnixSocketListener));

                socketPath = connectionPath;

                // Create directory for socket if it doesn't exist
                socketDir = Path.GetDirectoryName(socketPath);
                if (!string.IsNullOrEmpty(socketDir) && !Directory.Exists(socketDir))
                {
                    Directory.CreateDirectory(socketDir);
                    McpLog.Log($"Created directory: {socketDir}");
                }

                // Set directory permissions to 0700 (owner only) if directory was specified
                if (!string.IsNullOrEmpty(socketDir))
                {
                    try
                    {
                        if (chmod(socketDir, 0x1C0) == 0) // 0x1C0 = 0700 octal
                        {
                            McpLog.Log($"Set directory permissions to 0700: {socketDir}");
                        }
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warning($"Could not set directory permissions: {ex.Message}");
                    }
                }

                // Remove existing socket file if it exists
                // Retry a few times in case the file is still being released by a previous listener
                if (File.Exists(socketPath))
                {
                    bool deleted = false;
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            File.Delete(socketPath);
                            McpLog.Log($"Deleted existing socket file: {socketPath}");
                            deleted = true;
                            break;
                        }
                        catch (Exception ex)
                        {
                            if (attempt < 4)
                            {
                                McpLog.Log($"Socket file still in use (attempt {attempt + 1}/5), retrying...");
                                System.Threading.Thread.Sleep(50); // Wait 50ms before retry
                            }
                            else
                            {
                                throw new InvalidOperationException($"Cannot delete existing socket file after 5 attempts: {socketPath}", ex);
                            }
                        }
                    }

                    // Wait a bit after deletion to ensure filesystem has released the file
                    if (deleted)
                    {
                        System.Threading.Thread.Sleep(50);
                    }
                }

                // Set umask to ensure socket is created with restricted permissions
                uint oldUmask = umask(UMASK_OWNER_ONLY);
                McpLog.Log($"Set umask to 077 for secure socket creation");

                try
                {
                    #if UNITY_EDITOR_LINUX
                    var socketStartTime = DateTime.Now;
                    #endif

                    // Create Unix domain socket
                    serverSocket = socket(AF_UNIX, SOCK_STREAM, 0);
                    if (serverSocket < 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        throw new InvalidOperationException($"Failed to create Unix socket: errno={error}");
                    }

                    McpLog.Log($"Created Unix socket with fd={serverSocket}");

                    // Bind to socket path
                    var addr = new SockAddrUn
                    {
                        sun_family = AF_UNIX,
                        sun_path = socketPath
                    };

                    int addrLen = Marshal.SizeOf(addr);
                    if (bind(serverSocket, ref addr, addrLen) < 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        close(serverSocket);
                        serverSocket = -1;
                        throw new InvalidOperationException($"Failed to bind Unix socket to {socketPath}: errno={error}");
                    }

                    McpLog.Log($"Bound Unix socket to {socketPath}");

                    // Set socket file permissions to 0600 (owner read/write only)
                    if (chmod(socketPath, OWNER_ONLY_PERMS) == 0)
                    {
                        McpLog.Log($"Set socket permissions to 0600: {socketPath}");
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        McpLog.Warning($"Could not set socket permissions: errno={error}");
                    }

                    // Start listening
                    if (listen(serverSocket, BACKLOG) < 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        close(serverSocket);
                        serverSocket = -1;
                        throw new InvalidOperationException($"Failed to listen on Unix socket: errno={error}");
                    }

                    McpLog.Log($"Listening on Unix socket: {socketPath}");

                    #if UNITY_EDITOR_LINUX
                    var socketSetupDuration = (DateTime.Now - socketStartTime).TotalMilliseconds;
                    McpLog.Log($"[Ubuntu CI] Socket setup completed in {socketSetupDuration:F0}ms");
                    #endif

                    isListening = true;
                    clientCounter = 0;
                }
                finally
                {
                    // Restore original umask
                    umask(oldUmask);
                    McpLog.Log($"Restored umask to {oldUmask:X3}");
                }
            }
        }

        public void Stop()
        {
            lock (lockObj)
            {
                if (!isListening)
                    return;

                isListening = false;

                // Close server socket
                if (serverSocket >= 0)
                {
                    close(serverSocket);
                    serverSocket = -1;
                }

                // Delete socket file
                if (!string.IsNullOrEmpty(socketPath) && File.Exists(socketPath))
                {
                    try
                    {
                        File.Delete(socketPath);
                        McpLog.Log($"Deleted socket file: {socketPath}");
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warning($"Could not delete socket file: {ex.Message}");
                    }
                }

                socketPath = null;
            }
        }

        public async Task<IConnectionTransport> AcceptClientAsync(CancellationToken cancellationToken)
        {
            if (!isListening)
                throw new InvalidOperationException("Listener is not running");

            if (isDisposed)
                throw new ObjectDisposedException(nameof(UnixSocketListener));

            #if UNITY_EDITOR_LINUX
            var acceptStartTime = DateTime.Now;
            McpLog.Log($"[Ubuntu CI] Starting accept() on socket fd={serverSocket}, path={socketPath}");
            #endif

            try
            {
                // Accept connection on background thread (accept() is blocking)
                int clientSocket = await Task.Run(() =>
                {
                    while (!cancellationToken.IsCancellationRequested && isListening)
                    {
                        // Check if socket is still valid before calling accept
                        if (serverSocket < 0)
                        {
                            throw new OperationCanceledException("Server socket was closed");
                        }

                        // Accept with timeout by checking cancellation periodically
                        // Note: This is not ideal but accept() doesn't have a timeout parameter
                        // A better approach would use poll() or select(), but this is simpler
                        int fd = accept(serverSocket, IntPtr.Zero, IntPtr.Zero);
                        if (fd >= 0)
                        {
                            return fd;
                        }

                        // If accept failed, check if it's a temporary error that should be retried
                        int error = Marshal.GetLastWin32Error();

                        // EBADF means the socket was closed (during shutdown) - exit gracefully
                        if (error == EBADF)
                        {
                            throw new OperationCanceledException("Server socket was closed during accept");
                        }

                        // Retry on temporary errors:
                        // EINTR (4) - system call interrupted by signal
                        // ECONNABORTED (53 on macOS, 103 on Linux) - connection aborted before accept completed
                        bool shouldRetry = error == EINTR;
                        #if UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX
                        shouldRetry = shouldRetry || error == ECONNABORTED;
                        #endif

                        if (shouldRetry)
                        {
                            McpLog.Log($"accept() returned temporary error (errno={error}), retrying...");
                            continue;
                        }

                        throw new InvalidOperationException($"accept() failed: errno={error}");
                    }

                    throw new OperationCanceledException();
                }, cancellationToken);

                #if UNITY_EDITOR_LINUX
                var acceptDuration = (DateTime.Now - acceptStartTime).TotalMilliseconds;
                McpLog.Log($"[Ubuntu CI] accept() completed in {acceptDuration:F0}ms, client fd={clientSocket}");
                #endif

                McpLog.Log($"Accepted Unix socket connection: fd={clientSocket}");

                // Increment client counter for unique IDs
                int clientId;
                lock (lockObj)
                {
                    clientId = ++clientCounter;
                }

                string connectionId = $"UnixSocket-{clientId}";
                // Pass the raw file descriptor directly to the transport
                return new UnixSocketTransport(clientSocket, connectionId);
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
        }

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
