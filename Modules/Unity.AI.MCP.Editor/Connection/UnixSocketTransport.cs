using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.MCP.Editor.Connection
{
    /// <summary>
    /// Unix socket transport for Mac/Linux - handles read/write operations using raw file descriptors and P/Invoke
    /// </summary>
    class UnixSocketTransport : IConnectionTransport
    {
        // P/Invoke for socket operations
        [DllImport("libc", SetLastError = true)]
        static extern int send(int sockfd, byte[] buf, int len, int flags);

        [DllImport("libc", SetLastError = true)]
        static extern int recv(int sockfd, byte[] buf, int len, int flags);

        [DllImport("libc", SetLastError = true)]
        static extern int close(int fd);

        [DllImport("libc", SetLastError = true)]
        static extern int poll(ref PollFd fds, uint nfds, int timeout);

        [DllImport("libc", EntryPoint = "getsockopt", SetLastError = true)]
        static extern int GetSocketOption(int socket, int level, int optname, IntPtr optval, ref int optlen);

        [StructLayout(LayoutKind.Sequential)]
        struct PollFd
        {
            public int fd;
            public short events;
            public short revents;
        }

        const short POLLIN = 0x0001;  // Data available to read
        const short POLLHUP = 0x0010; // Hang up
        const short POLLERR = 0x0008; // Error condition

        // Error codes
        const int EINTR = 4;          // Interrupted system call - retry

        // Platform-specific socket option constants
        #if UNITY_EDITOR_OSX
        const int SOL_LOCAL = 0;
        const int LOCAL_PEERPID = 0x002;
        #elif UNITY_EDITOR_LINUX
        [StructLayout(LayoutKind.Sequential)]
        struct PeerCredentials
        {
            public int pid;
            public int uid;
            public int gid;
        }
        const int SOL_SOCKET = 1;
        const int SO_PEERCRED = 17;
        #endif

        readonly int socket;
        readonly string connectionId;
        volatile bool isDisposed;
        volatile bool isConnected = true;
        readonly object lockObj = new();
        readonly List<byte> readBuffer = new();
        int? cachedClientPid;

        public bool IsConnected => isConnected && socket >= 0;
        public string ConnectionId => connectionId;
        public event Action OnDisconnected;

        public UnixSocketTransport(int socketFd, string connectionId)
        {
            if (socketFd < 0)
                throw new ArgumentException("Invalid socket file descriptor", nameof(socketFd));

            socket = socketFd;
            this.connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        }

        public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            try
            {
                int sent = 0;
                while (sent < data.Length && !cancellationToken.IsCancellationRequested)
                {
                    int result = await Task.Run(() =>
                    {
                        // Send from the current position
                        byte[] chunk = new byte[data.Length - sent];
                        Array.Copy(data, sent, chunk, 0, chunk.Length);
                        return send(socket, chunk, chunk.Length, 0);
                    }, cancellationToken);

                    if (result < 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        HandleDisconnection();
                        throw new InvalidOperationException($"send() failed: errno={error}");
                    }
                    if (result == 0)
                    {
                        HandleDisconnection();
                        throw new InvalidOperationException("Connection closed during write");
                    }
                    sent += result;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                HandleDisconnection();
                throw new InvalidOperationException($"Write error: {ex.Message}", ex);
            }
        }

        public async Task<byte[]> ReadUntilDelimiterAsync(
            byte delimiter,
            int maxBytes,
            int timeoutMs,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            var result = new List<byte>();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            // Check buffered data first
            lock (lockObj)
            {
                for (int i = 0; i < readBuffer.Count; i++)
                {
                    byte b = readBuffer[i];
                    result.Add(b);

                    if (b == delimiter)
                    {
                        readBuffer.RemoveRange(0, i + 1);
                        return result.ToArray();
                    }

                    if (result.Count >= maxBytes)
                        throw new InvalidOperationException($"Maximum message size ({maxBytes} bytes) exceeded");
                }

                // Move all buffered data to result
                result.AddRange(readBuffer);
                readBuffer.Clear();
            }

            // Continue reading from socket
            byte[] buffer = new byte[8192];
            while (true)
            {
                int remainingTimeout = timeoutMs <= 0
                    ? -1  // Infinite timeout
                    : timeoutMs - (int)stopwatch.ElapsedMilliseconds;

                if (remainingTimeout != -1 && remainingTimeout <= 0)
                    throw new TimeoutException("Read timed out");

                try
                {
                    // Use poll() to wait for data with timeout
                    bool dataAvailable = await Task.Run(() =>
                    {
                        while (true)
                        {
                            var pollFd = new PollFd
                            {
                                fd = socket,
                                events = POLLIN,
                                revents = 0
                            };

                            int pollResult = poll(ref pollFd, 1, remainingTimeout);

                            if (pollResult < 0)
                            {
                                int error = Marshal.GetLastWin32Error();

                                // EINTR means the system call was interrupted by a signal - retry
                                if (error == EINTR)
                                {
                                    continue;
                                }

                                throw new InvalidOperationException($"poll() failed: errno={error}");
                            }

                            if (pollResult == 0)
                                return false; // Timeout

                            // Check for errors or hangup
                            if ((pollFd.revents & (POLLHUP | POLLERR)) != 0)
                            {
                                return false; // Connection closed
                            }

                            return (pollFd.revents & POLLIN) != 0;
                        }
                    }, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                        throw new OperationCanceledException();

                    if (!dataAvailable)
                    {
                        if (remainingTimeout == -1)
                        {
                            // Should not happen with infinite timeout unless connection closed
                            HandleDisconnection();
                            throw new InvalidOperationException("Connection closed");
                        }
                        throw new TimeoutException("Read timed out");
                    }

                    // Read available data
                    int read = await Task.Run(() => recv(socket, buffer, buffer.Length, 0), cancellationToken);

                    if (read < 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        HandleDisconnection();
                        throw new InvalidOperationException($"recv() failed: errno={error}");
                    }

                    if (read == 0)
                    {
                        HandleDisconnection();
                        throw new InvalidOperationException("Connection closed before finding delimiter");
                    }

                    for (int i = 0; i < read; i++)
                    {
                        byte b = buffer[i];
                        result.Add(b);

                        if (b == delimiter)
                        {
                            // Buffer any excess data
                            int excess = read - i - 1;
                            if (excess > 0)
                            {
                                lock (lockObj)
                                {
                                    for (int j = i + 1; j < read; j++)
                                        readBuffer.Add(buffer[j]);
                                }
                            }
                            return result.ToArray();
                        }

                        if (result.Count >= maxBytes)
                            throw new InvalidOperationException($"Maximum message size ({maxBytes} bytes) exceeded");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (!(ex is TimeoutException))
                {
                    HandleDisconnection();
                    throw new InvalidOperationException($"Read error: {ex.Message}", ex);
                }
            }
        }

        public void Close()
        {
            if (!isDisposed && socket >= 0)
            {
                try
                {
                    close(socket);
                }
                catch { }

                isConnected = false;
            }
        }

        void HandleDisconnection()
        {
            if (!isDisposed)
            {
                isConnected = false;
                OnDisconnected?.Invoke();
            }
        }

        void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(UnixSocketTransport));
        }

        public void CacheClientProcessId()
        {
            cachedClientPid = QueryClientProcessId();
        }

        public int? GetClientProcessId()
        {
            return cachedClientPid ?? QueryClientProcessId();
        }

        int? QueryClientProcessId()
        {
            #if UNITY_EDITOR_OSX
            return GetClientProcessIdMac();
            #elif UNITY_EDITOR_LINUX
            return GetClientProcessIdLinux();
            #else
            return null;
            #endif
        }

        #if UNITY_EDITOR_OSX
        int? GetClientProcessIdMac()
        {
            try
            {
                // Mac: Use LOCAL_PEERPID to get the peer process ID
                int len = sizeof(int);
                IntPtr pidPtr = Marshal.AllocHGlobal(len);
                try
                {
                    Marshal.WriteInt32(pidPtr, 0);
                    if (GetSocketOption(socket, SOL_LOCAL, LOCAL_PEERPID, pidPtr, ref len) == 0)
                    {
                        int pid = Marshal.ReadInt32(pidPtr);
                        if (pid > 0)
                            return pid;
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        UnityEngine.Debug.LogWarning($"getsockopt LOCAL_PEERPID failed with error code: {error}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pidPtr);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to get client PID (Mac): {ex.Message}");
            }

            return null;
        }
        #endif

        #if UNITY_EDITOR_LINUX
        int? GetClientProcessIdLinux()
        {
            try
            {
                // Linux: Use SO_PEERCRED to get peer credentials
                int len = Marshal.SizeOf<PeerCredentials>();
                IntPtr credsPtr = Marshal.AllocHGlobal(len);
                try
                {
                    if (GetSocketOption(socket, SOL_SOCKET, SO_PEERCRED, credsPtr, ref len) == 0)
                    {
                        var creds = Marshal.PtrToStructure<PeerCredentials>(credsPtr);
                        if (creds.pid > 0)
                            return creds.pid;
                    }
                    else
                    {
                        int error = Marshal.GetLastWin32Error();
                        UnityEngine.Debug.LogWarning($"getsockopt SO_PEERCRED failed with error code: {error}");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(credsPtr);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"Failed to get client PID (Linux): {ex.Message}");
            }

            return null;
        }
        #endif

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;
                Close();
                readBuffer.Clear();
            }
        }
    }
}
