using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Connection
{
    /// <summary>
    /// Named Pipe transport for Windows - handles read/write operations on Windows named pipes
    /// </summary>
    class NamedPipeTransport : IConnectionTransport
    {
        #if UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(IntPtr Pipe, out uint ClientProcessId);
        #endif

        readonly NamedPipeServerStream pipeStream;
        readonly string connectionId;
        volatile bool isDisposed;
        readonly object lockObj = new();
        readonly List<byte> readBuffer = new();
        int? cachedClientPid;

        public bool IsConnected => pipeStream?.IsConnected ?? false;
        public string ConnectionId => connectionId;
        public event Action OnDisconnected;

        public NamedPipeTransport(NamedPipeServerStream pipeStream, string connectionId)
        {
            this.pipeStream = pipeStream ?? throw new ArgumentNullException(nameof(pipeStream));
            this.connectionId = connectionId ?? throw new ArgumentNullException(nameof(connectionId));
        }

        public async Task WriteAsync(byte[] data, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            try
            {
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                await pipeStream.WriteAsync(data.AsMemory(0, data.Length), cancellationToken).ConfigureAwait(false);
#else
                await pipeStream.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
#endif
                await pipeStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                HandleDisconnection();
                throw new InvalidOperationException("Connection closed during write", ex);
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

            // Continue reading from stream
            byte[] buffer = new byte[8192];
            while (true)
            {
                int remainingTimeout = timeoutMs <= 0
                    ? Timeout.Infinite
                    : timeoutMs - (int)stopwatch.ElapsedMilliseconds;

                if (remainingTimeout != Timeout.Infinite && remainingTimeout <= 0)
                    throw new TimeoutException("Read timed out");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                if (remainingTimeout != Timeout.Infinite)
                    cts.CancelAfter(remainingTimeout);

                try
                {
                    int read;
#if NETSTANDARD2_1 || NET6_0_OR_GREATER
                    read = await pipeStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cts.Token).ConfigureAwait(false);
#else
                    read = await pipeStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false);
#endif

                    if (read == 0)
                    {
                        HandleDisconnection();
                        throw new IOException("Connection closed before finding delimiter");
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
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Read timed out");
                }
                catch (IOException ex)
                {
                    HandleDisconnection();
                    throw new InvalidOperationException("Connection closed during read", ex);
                }
            }
        }

        public void Close()
        {
            if (!isDisposed && pipeStream != null)
            {
                try
                {
                    if (pipeStream.IsConnected)
                        pipeStream.Disconnect();
                }
                catch { }
            }
        }

        void HandleDisconnection()
        {
            if (!isDisposed)
            {
                OnDisconnected?.Invoke();
            }
        }

        void ThrowIfDisposed()
        {
            if (isDisposed)
                throw new ObjectDisposedException(nameof(NamedPipeTransport));
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
            #if UNITY_EDITOR_WIN
            try
            {
                if (pipeStream == null || !pipeStream.IsConnected)
                    return null;

                IntPtr handle = pipeStream.SafePipeHandle.DangerousGetHandle();
                if (GetNamedPipeClientProcessId(handle, out uint pid))
                {
                    return (int)pid;
                }

                Debug.LogWarning($"GetNamedPipeClientProcessId failed with error: {Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to get client PID (Windows): {ex.Message}");
            }

            return null;
            #else
            Debug.LogWarning("NamedPipeTransport is Windows-only. Use UnixSocketTransport on Mac/Linux.");
            return null;
            #endif
        }

        public void Dispose()
        {
            if (!isDisposed)
            {
                isDisposed = true;
                try { pipeStream?.Dispose(); } catch { }
                readBuffer.Clear();
            }
        }
    }
}
