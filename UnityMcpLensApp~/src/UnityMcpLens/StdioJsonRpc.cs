using System.Text;
using System.Text.Json;

namespace UnityMcpLens;

static class StdioJsonRpc
{
    static readonly byte[] k_SingleByteBuffer = new byte[1];
    static bool s_UseJsonLineTransport;

    public static async Task<JsonDocument?> ReadMessageAsync(Stream input, CancellationToken cancellationToken)
    {
        var prefixBuffer = new List<byte>();
        while (true)
        {
            int read = await input.ReadAsync(k_SingleByteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return null;

            byte current = k_SingleByteBuffer[0];
            prefixBuffer.Add(current);
            if (char.IsWhiteSpace((char)current))
                continue;

            if (current == '{' || current == '[')
            {
                s_UseJsonLineTransport = true;
                return await ReadJsonLineMessageAsync(input, prefixBuffer, cancellationToken).ConfigureAwait(false);
            }

            break;
        }

        s_UseJsonLineTransport = false;
        return await ReadFramedMessageAsync(input, prefixBuffer, cancellationToken).ConfigureAwait(false);
    }

    static async Task<JsonDocument?> ReadFramedMessageAsync(Stream input, List<byte> headerBuffer, CancellationToken cancellationToken)
    {
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

    static async Task<JsonDocument?> ReadJsonLineMessageAsync(Stream input, List<byte> messageBuffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            int read = await input.ReadAsync(k_SingleByteBuffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                return messageBuffer.Count == 0 ? null : JsonDocument.Parse(messageBuffer.ToArray());

            messageBuffer.Add(k_SingleByteBuffer[0]);
            if (k_SingleByteBuffer[0] == '\n')
                return JsonDocument.Parse(messageBuffer.ToArray());

            if ((k_SingleByteBuffer[0] == '}' || k_SingleByteBuffer[0] == ']') &&
                TryParseJsonDocument(messageBuffer, out var document))
            {
                return document;
            }
        }
    }

    static bool TryParseJsonDocument(List<byte> messageBuffer, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(messageBuffer.ToArray());
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    public static async Task WriteMessageAsync(Stream output, object payload, JsonSerializerOptions serializerOptions, CancellationToken cancellationToken)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(payload, serializerOptions);
        if (s_UseJsonLineTransport)
        {
            await output.WriteAsync(body.AsMemory(0, body.Length), cancellationToken).ConfigureAwait(false);
            await output.WriteAsync("\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

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
