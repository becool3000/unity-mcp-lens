using System.Diagnostics;
using System.Text;
using System.Text.Json;

var options = BenchmarkOptions.Parse(args);
var benchmark = new PackSwitchBenchmark(options);
var report = await benchmark.RunAsync();

if (options.AsJson)
{
    Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions.Output));
    return;
}

Console.WriteLine();
Console.WriteLine("Lens pack switch benchmark");
Console.WriteLine($"Project: {report.ProjectPath}");
Console.WriteLine($"Stats:   {report.StatsPath}");
Console.WriteLine($"Server:  {report.ServerPath}");
Console.WriteLine();

foreach (var scenario in report.Scenarios)
{
    Console.WriteLine($"{scenario.Scenario} ({scenario.From} -> {scenario.To})");
    Console.WriteLine($"  Exported tools:     {scenario.ToolCountBefore} -> {scenario.ToolCountAfter}");
    Console.WriteLine($"  Active packs:       {string.Join(", ", scenario.ActiveToolPacks)}");
    Console.WriteLine($"  Bridge requests:    {scenario.BridgeRequestCount}");
    Console.WriteLine($"  Bridge responses:   {scenario.BridgeResponseCount}");
    Console.WriteLine($"  Bridge bytes:       {scenario.BridgeRequestBytes} request, {scenario.BridgeResponseBytes} response");
    Console.WriteLine($"  list_changed notif: {scenario.ListChangedNotifications}");
    Console.WriteLine($"  get_tool_schema:    {scenario.SchemaRequestCount} requests, {scenario.SchemaRequestBytes} request bytes, {scenario.SchemaResponseCount} responses, {scenario.SchemaResponseBytes} response bytes, {scenario.SchemaCount} schemas");
    Console.WriteLine();
}

static class JsonOptions
{
    public static readonly JsonSerializerOptions Output = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

sealed class BenchmarkOptions
{
    public string ProjectPath { get; init; } = string.Empty;
    public string ServerPath { get; init; } = string.Empty;
    public int RpcTimeoutSeconds { get; init; } = 30;
    public int StatsSettleMilliseconds { get; init; } = 800;
    public bool AsJson { get; init; }

    public static BenchmarkOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? serverPath = null;
        int rpcTimeoutSeconds = 30;
        int statsSettleMilliseconds = 800;
        bool asJson = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--project-path":
                    projectPath = args[++i];
                    break;
                case "--server-path":
                    serverPath = args[++i];
                    break;
                case "--rpc-timeout-seconds":
                    rpcTimeoutSeconds = int.Parse(args[++i]);
                    break;
                case "--stats-settle-ms":
                    statsSettleMilliseconds = int.Parse(args[++i]);
                    break;
                case "--json":
                    asJson = true;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown argument '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(projectPath))
            throw new InvalidOperationException("--project-path is required.");
        if (string.IsNullOrWhiteSpace(serverPath))
            throw new InvalidOperationException("--server-path is required.");

        return new BenchmarkOptions
        {
            ProjectPath = Path.GetFullPath(projectPath),
            ServerPath = Path.GetFullPath(serverPath),
            RpcTimeoutSeconds = Math.Max(1, rpcTimeoutSeconds),
            StatsSettleMilliseconds = Math.Max(250, statsSettleMilliseconds),
            AsJson = asJson
        };
    }
}

sealed class PackSwitchBenchmark(BenchmarkOptions options)
{
    static readonly ScenarioDefinition[] k_Scenarios =
    [
        new("foundation_to_scene", "foundation", [], "scene", ["scene"]),
        new("scene_to_console", "scene", ["scene"], "console", ["console"]),
        new("scene_to_foundation", "scene", ["scene"], "foundation", [])
    ];

    readonly string m_StatsPath = Path.Combine(options.ProjectPath, "Library", "AI.Gateway.PayloadStats.jsonl");

    public async Task<BenchmarkReport> RunAsync()
    {
        var scenarios = new List<ScenarioResult>();
        foreach (var scenario in k_Scenarios)
            scenarios.Add(await RunScenarioAsync(scenario));

        return new BenchmarkReport(
            options.ProjectPath,
            m_StatsPath,
            options.ServerPath,
            DateTimeOffset.UtcNow,
            scenarios);
    }

    async Task<ScenarioResult> RunScenarioAsync(ScenarioDefinition definition)
    {
        await using var session = await LensSession.StartAsync(options.ServerPath, options.ProjectPath, TimeSpan.FromSeconds(options.RpcTimeoutSeconds));
        await session.InitializeAsync();

        var initialToolCount = await session.GetToolCountAsync();
        await WaitForStatsToSettleAsync();

        var toolCountBefore = initialToolCount;
        if (definition.PrePacks.Length > 0)
        {
            _ = await session.SetToolPacksAsync(definition.PrePacks);
            toolCountBefore = await session.GetToolCountAsync();
            await WaitForStatsToSettleAsync();
        }

        var lineMarker = CountStatsLines();
        var setResult = await session.SetToolPacksAsync(definition.TargetPacks);
        var toolCountAfter = await session.GetToolCountAsync();
        await WaitForStatsToSettleAsync();

        var sliceLines = ReadStatsSlice(lineMarker);
        var sliceSummary = StatsSliceSummary.FromLines(sliceLines);

        return new ScenarioResult(
            definition.Name,
            definition.FromLabel,
            definition.ToLabel,
            toolCountBefore,
            toolCountAfter,
            setResult.ActiveToolPacks,
            setResult.ListChangedNotifications,
            sliceSummary.BridgeRequestCount,
            sliceSummary.BridgeResponseCount,
            sliceSummary.BridgeRequestBytes,
            sliceSummary.BridgeResponseBytes,
            sliceSummary.SchemaRequestCount,
            sliceSummary.SchemaRequestBytes,
            sliceSummary.SchemaResponseCount,
            sliceSummary.SchemaResponseBytes,
            sliceSummary.SchemaCount);
    }

    async Task WaitForStatsToSettleAsync()
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(options.StatsSettleMilliseconds);
        var lastCount = -1;
        var stableChecks = 0;

        while (DateTime.UtcNow < deadline)
        {
            var currentCount = CountStatsLines();
            if (currentCount == lastCount)
            {
                stableChecks++;
                if (stableChecks >= 2)
                    return;
            }
            else
            {
                lastCount = currentCount;
                stableChecks = 0;
            }

            await Task.Delay(200);
        }
    }

    int CountStatsLines()
    {
        if (!File.Exists(m_StatsPath))
            return 0;

        var count = 0;
        using var reader = new StreamReader(m_StatsPath, Encoding.UTF8, true);
        while (reader.ReadLine() is not null)
            count++;
        return count;
    }

    string[] ReadStatsSlice(int startLine)
    {
        if (!File.Exists(m_StatsPath))
            return [];

        var lines = File.ReadAllLines(m_StatsPath, Encoding.UTF8);
        if (startLine >= lines.Length)
            return [];

        return lines[startLine..];
    }
}

sealed record ScenarioDefinition(string Name, string FromLabel, string[] PrePacks, string ToLabel, string[] TargetPacks);

sealed record BenchmarkReport(
    string ProjectPath,
    string StatsPath,
    string ServerPath,
    DateTimeOffset RanAtUtc,
    IReadOnlyList<ScenarioResult> Scenarios);

sealed record ScenarioResult(
    string Scenario,
    string From,
    string To,
    int ToolCountBefore,
    int ToolCountAfter,
    string[] ActiveToolPacks,
    int ListChangedNotifications,
    int BridgeRequestCount,
    int BridgeResponseCount,
    long BridgeRequestBytes,
    long BridgeResponseBytes,
    int SchemaRequestCount,
    long SchemaRequestBytes,
    int SchemaResponseCount,
    long SchemaResponseBytes,
    int SchemaCount);

sealed class StatsSliceSummary
{
    public int BridgeRequestCount { get; private init; }
    public int BridgeResponseCount { get; private init; }
    public long BridgeRequestBytes { get; private init; }
    public long BridgeResponseBytes { get; private init; }
    public int SchemaRequestCount { get; private init; }
    public long SchemaRequestBytes { get; private init; }
    public int SchemaResponseCount { get; private init; }
    public long SchemaResponseBytes { get; private init; }
    public int SchemaCount { get; private init; }

    public static StatsSliceSummary FromLines(IEnumerable<string> lines)
    {
        var bridgeRequestCount = 0;
        var bridgeResponseCount = 0;
        long bridgeRequestBytes = 0;
        long bridgeResponseBytes = 0;
        var schemaRequestCount = 0;
        long schemaRequestBytes = 0;
        var schemaResponseCount = 0;
        long schemaResponseBytes = 0;
        var schemaCount = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            var stage = GetString(root, "stage");
            var commandType = GetString(root, "commandType");
            if (stage == "coverage_bridge_command_request")
            {
                bridgeRequestCount++;
                bridgeRequestBytes += GetInt64(root, "requestBytes");
                if (commandType == "get_tool_schema")
                {
                    schemaRequestCount++;
                    schemaRequestBytes += GetInt64(root, "requestBytes");
                }
            }
            else if (stage == "coverage_bridge_command_response")
            {
                bridgeResponseCount++;
                bridgeResponseBytes += GetInt64(root, "responseBytes");
                if (commandType == "get_tool_schema")
                {
                    schemaResponseCount++;
                    schemaResponseBytes += GetInt64(root, "responseBytes");
                    schemaCount += (int)GetInt64(root, "schemaCount");
                }
            }
        }

        return new StatsSliceSummary
        {
            BridgeRequestCount = bridgeRequestCount,
            BridgeResponseCount = bridgeResponseCount,
            BridgeRequestBytes = bridgeRequestBytes,
            BridgeResponseBytes = bridgeResponseBytes,
            SchemaRequestCount = schemaRequestCount,
            SchemaRequestBytes = schemaRequestBytes,
            SchemaResponseCount = schemaResponseCount,
            SchemaResponseBytes = schemaResponseBytes,
            SchemaCount = schemaCount
        };
    }

    static long GetInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt64(out var value) ? value : (long)property.GetDouble(),
            JsonValueKind.String when long.TryParse(property.GetString(), out var value) => value,
            _ => 0
        };
    }

    static string GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
            return string.Empty;

        return property.GetString() ?? string.Empty;
    }
}

sealed class LensSession : IAsyncDisposable
{
    readonly Process m_Process;
    readonly Stream m_Input;
    readonly Stream m_Output;
    readonly TimeSpan m_Timeout;
    int m_NextId = 1;

    LensSession(Process process, TimeSpan timeout)
    {
        m_Process = process;
        m_Input = process.StandardInput.BaseStream;
        m_Output = process.StandardOutput.BaseStream;
        m_Timeout = timeout;
    }

    public static async Task<LensSession> StartAsync(string serverPath, string projectPath, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = serverPath,
            WorkingDirectory = projectPath,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["UNITY_MCP_PROJECT_PATH"] = projectPath;

        var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start unity-mcp-lens.");

        return new LensSession(process, timeout);
    }

    public async Task InitializeAsync()
    {
        var initializeId = NextId();
        var initializeResponse = await SendRequestAsync(initializeId, "initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new
            {
                roots = new
                {
                    listChanged = false
                }
            },
            clientInfo = new
            {
                name = "unity-mcp-pack-switch-benchmark",
                version = "0.1.0"
            }
        });

        if (initializeResponse.Response.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"initialize failed: {error}");

        await SendNotificationAsync("notifications/initialized", new { });
    }

    public async Task<int> GetToolCountAsync()
    {
        var response = await SendRequestAsync(NextId(), "tools/list", new { });
        var tools = response.Response.GetProperty("result").GetProperty("tools");
        return tools.GetArrayLength();
    }

    public async Task<SetToolPacksResult> SetToolPacksAsync(string[] packs)
    {
        var response = await SendRequestAsync(NextId(), "tools/call", new
        {
            name = "Unity_SetToolPacks",
            arguments = new
            {
                packs
            }
        });

        if (response.Response.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"Unity_SetToolPacks failed: {error}");

        var structuredContent = response.Response
            .GetProperty("result")
            .GetProperty("structuredContent");
        if (structuredContent.ValueKind != JsonValueKind.Object ||
            !structuredContent.TryGetProperty("data", out var dataElement) ||
            !dataElement.TryGetProperty("activeToolPacks", out var activeToolPacksElement))
        {
            throw new InvalidOperationException($"Unity_SetToolPacks returned an unexpected payload: {response.Response}");
        }
        var activeToolPacks = activeToolPacksElement
            .EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();

        var listChangedNotifications = response.Notifications.Count(notification => notification.Method == "notifications/tools/list_changed");
        return new SetToolPacksResult(activeToolPacks, listChangedNotifications);
    }

    async Task SendNotificationAsync(string method, object payload)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            method,
            @params = payload
        });
        await WriteMessageAsync(message);
    }

    async Task<RpcResponseEnvelope> SendRequestAsync(int id, string method, object payload)
    {
        var message = JsonSerializer.SerializeToUtf8Bytes(new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = payload
        });
        await WriteMessageAsync(message);

        var notifications = new List<RpcNotification>();
        while (true)
        {
            using var response = await ReadMessageAsync();
            if (response.RootElement.TryGetProperty("id", out var responseId))
            {
                if (responseId.GetInt32() == id)
                    return new RpcResponseEnvelope(response.RootElement.Clone(), notifications);
            }
            else if (response.RootElement.TryGetProperty("method", out var notificationMethod))
            {
                notifications.Add(new RpcNotification(notificationMethod.GetString() ?? string.Empty, response.RootElement.Clone()));
            }
        }
    }

    async Task WriteMessageAsync(byte[] body)
    {
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await m_Input.WriteAsync(header);
        await m_Input.WriteAsync(body);
        await m_Input.FlushAsync();
    }

    async Task<JsonDocument> ReadMessageAsync()
    {
        using var timeoutCts = new CancellationTokenSource(m_Timeout);
        var headerBuffer = new List<byte>();
        var singleByte = new byte[1];

        while (true)
        {
            var read = await m_Output.ReadAsync(singleByte.AsMemory(0, 1), timeoutCts.Token);
            if (read <= 0)
            {
                var stderr = await m_Process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"unity-mcp-lens closed stdout before the next JSON-RPC message. stderr: {stderr}");
            }

            headerBuffer.Add(singleByte[0]);
            var count = headerBuffer.Count;
            if (count >= 4 &&
                headerBuffer[count - 4] == '\r' &&
                headerBuffer[count - 3] == '\n' &&
                headerBuffer[count - 2] == '\r' &&
                headerBuffer[count - 1] == '\n')
                break;
        }

        var headerText = Encoding.ASCII.GetString(headerBuffer.ToArray());
        var contentLength = ParseContentLength(headerText);
        if (contentLength <= 0)
            throw new InvalidOperationException("unity-mcp-lens returned an invalid Content-Length header.");

        var body = new byte[contentLength];
        var offset = 0;
        while (offset < contentLength)
        {
            var read = await m_Output.ReadAsync(body.AsMemory(offset, contentLength - offset), timeoutCts.Token);
            if (read <= 0)
                throw new InvalidOperationException("unity-mcp-lens closed stdout before the JSON-RPC body was fully read.");
            offset += read;
        }

        return JsonDocument.Parse(body);
    }

    static int ParseContentLength(string headers)
    {
        using var reader = new StringReader(headers);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (!line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (int.TryParse(line["Content-Length:".Length..].Trim(), out var contentLength))
                return contentLength;
        }

        return 0;
    }

    int NextId() => m_NextId++;

    public async ValueTask DisposeAsync()
    {
        try
        {
            m_Process.StandardInput.Close();
        }
        catch
        {
        }

        if (!m_Process.HasExited)
            m_Process.Kill(entireProcessTree: true);

        await m_Process.WaitForExitAsync();
        m_Process.Dispose();
    }
}

sealed record RpcNotification(string Method, JsonElement Payload);
sealed record RpcResponseEnvelope(JsonElement Response, IReadOnlyList<RpcNotification> Notifications);
sealed record SetToolPacksResult(string[] ActiveToolPacks, int ListChangedNotifications);
