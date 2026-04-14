using System.Text;
using System.Text.Json;

namespace UnityMcpServer;

static class StdioJsonRpc
{
    static readonly byte[] k_SingleByteBuffer = new byte[1];

    public static async Task<JsonDocument?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var headerBuffer = new List<byte>();
        while (true)
        {
            int read = await input.ReadAsync(k_SingleByteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return null;

            headerBuffer.Add(k_SingleByteBuffer[0]);
            if (headerBuffer.Count >= 4 &&
                headerBuffer[^4] == '\r' &&
                headerBuffer[^3] == '\n' &&
                headerBuffer[^2] == '\r' &&
                headerBuffer[^1] == '\n')
            {
                break;
            }
        }

        string headers = Encoding.ASCII.GetString(headerBuffer.ToArray());
        int contentLength = ParseContentLength(headers);
        if (contentLength <= 0)
            return null;

        byte[] body = new byte[contentLength];
        int offset = 0;
        while (offset < contentLength)
        {
            int read = await input.ReadAsync(body.AsMemory(offset, contentLength - offset), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return null;
            offset += read;
        }

        return JsonDocument.Parse(body);
    }

    public static async Task WriteMessageAsync(Stream output, object payload, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, serializerOptions);
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await output.WriteAsync(header.AsMemory(0, header.Length), cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(body.AsMemory(0, body.Length), cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    static int ParseContentLength(string headers)
    {
        using var reader = new StringReader(headers);
        while (reader.ReadLine() is { } line)
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                continue;

            var lengthText = line.Substring("Content-Length:".Length).Trim();
            if (int.TryParse(lengthText, out int contentLength))
                return contentLength;
        }

        return 0;
    }
}
