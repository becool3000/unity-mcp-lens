using System.Text.Json;

namespace UnityMcpServer;

sealed class UnityMcpServerHost
{
    sealed class CachedToolSchema
    {
        public string? SchemaHash { get; init; }
        public JsonElement InputSchema { get; init; }
        public JsonElement OutputSchema { get; init; }
        public JsonElement Annotations { get; init; }
    }

    readonly JsonSerializerOptions m_JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    readonly SemaphoreSlim m_StdoutLock = new(1, 1);
    readonly Dictionary<string, BridgeToolDescriptor> m_ToolCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, CachedToolSchema> m_ToolSchemaCache = new(StringComparer.OrdinalIgnoreCase);

    UnityBridgeClient? m_BridgeClient;
    string? m_BridgeSessionId;
    long m_ManifestVersion;
    string[] m_ActiveToolPacks = ["foundation"];
    bool m_ClientInitialized;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.InputEncoding = System.Text.Encoding.UTF8;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        using Stream stdin = Console.OpenStandardInput();
        while (!cancellationToken.IsCancellationRequested)
        {
            using var requestDocument = await StdioJsonRpc.ReadMessageAsync(stdin, cancellationToken).ConfigureAwait(false);
            if (requestDocument == null)
                break;

            await HandleRequestAsync(requestDocument.RootElement, cancellationToken).ConfigureAwait(false);
        }
    }

    async Task HandleRequestAsync(JsonElement request, CancellationToken cancellationToken)
    {
        string? method = request.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
        JsonElement? idElement = request.TryGetProperty("id", out var id) ? id : null;
        JsonElement paramsElement = request.TryGetProperty("params", out var @params) ? @params : default;

        if (string.IsNullOrWhiteSpace(method))
        {
            if (idElement.HasValue)
            {
                await WriteRpcAsync(new
                {
                    jsonrpc = "2.0",
                    id = idElement.Value,
                    error = new
                    {
                        code = -32600,
                        message = "Missing JSON-RPC method."
                    }
                }, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        switch (method)
        {
            case "initialize":
                await WriteRpcAsync(new
                {
                    jsonrpc = "2.0",
                    id = idElement.GetValueOrDefault(),
                    result = new
                    {
                        protocolVersion = "2025-06-18",
                        capabilities = new
                        {
                            tools = new
                            {
                                listChanged = true
                            }
                        },
                        serverInfo = new
                        {
                            name = "unity-mcp-vnext",
                            version = "0.1.0-alpha.1"
                        }
                    }
                }, cancellationToken).ConfigureAwait(false);
                return;

            case "notifications/initialized":
                m_ClientInitialized = true;
                return;

            case "ping":
                await WriteRpcAsync(new
                {
                    jsonrpc = "2.0",
                    id = idElement.GetValueOrDefault(),
                    result = new { }
                }, cancellationToken).ConfigureAwait(false);
                return;

            case "tools/list":
                await HandleToolsListAsync(idElement, cancellationToken).ConfigureAwait(false);
                return;

            case "tools/call":
                await HandleToolsCallAsync(idElement, paramsElement, cancellationToken).ConfigureAwait(false);
                return;

            default:
                if (idElement.HasValue)
                {
                    await WriteRpcAsync(new
                    {
                        jsonrpc = "2.0",
                        id = idElement.Value,
                        error = new
                        {
                            code = -32601,
                            message = $"Unsupported MCP method '{method}'."
                        }
                    }, cancellationToken).ConfigureAwait(false);
                }
                return;
        }
    }

    async Task HandleToolsListAsync(JsonElement? idElement, CancellationToken cancellationToken)
    {
        try
        {
            await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[unity-mcp-vnext] tools/list bridge bootstrap failed: {ex.Message}");
        }

        var tools = m_ToolCache.Values
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => new
            {
                name = tool.Name,
                title = tool.Title,
                description = tool.Description,
                inputSchema = tool.InputSchema.ValueKind == JsonValueKind.Undefined
                    ? JsonSerializer.SerializeToElement(new { type = "object", properties = new { } }, m_JsonOptions)
                    : tool.InputSchema,
                annotations = tool.Annotations.ValueKind == JsonValueKind.Undefined ? (object?)null : tool.Annotations
            })
            .ToArray();

        await WriteRpcAsync(new
        {
            jsonrpc = "2.0",
            id = idElement.GetValueOrDefault(),
            result = new
            {
                tools
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    async Task HandleToolsCallAsync(JsonElement? idElement, JsonElement paramsElement, CancellationToken cancellationToken)
    {
        if (!paramsElement.TryGetProperty("name", out var toolNameElement))
        {
            await WriteRpcAsync(new
            {
                jsonrpc = "2.0",
                id = idElement.GetValueOrDefault(),
                error = new
                {
                    code = -32602,
                    message = "tools/call requires a tool name."
                }
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        string toolName = toolNameElement.GetString() ?? string.Empty;
        JsonElement argumentsElement = paramsElement.TryGetProperty("arguments", out var arguments) ? arguments : JsonSerializer.SerializeToElement(new { }, m_JsonOptions);

        try
        {
            await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);

            object result;
            if (MatchesToolName(toolName, "Unity.SetToolPacks"))
            {
                string[] requestedPacks = ExtractPacks(argumentsElement);
                var manifestEnvelope = await m_BridgeClient!.SetToolPacksAsync(requestedPacks, includeSchemas: false, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
                {
                    result = BuildToolCallResult(CreateErrorPayload(manifestEnvelope.Error ?? "Failed to update Unity tool packs."), isError: true);
                }
                else
                {
                    await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
                    if (m_ClientInitialized)
                        await SendToolsListChangedNotificationAsync(cancellationToken).ConfigureAwait(false);

                    result = BuildToolCallResult(JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        message = "Updated active Unity MCP tool packs.",
                        data = new
                        {
                            activeToolPacks = manifestEnvelope.Result.ActiveToolPacks,
                            manifestVersion = manifestEnvelope.Result.ManifestVersion,
                            bridgeSessionId = manifestEnvelope.Result.BridgeSessionId,
                            toolCount = m_ToolCache.Count
                        }
                    }, m_JsonOptions));
                }
            }
            else if (MatchesToolName(toolName, "Unity.ReadDetailRef"))
            {
                string refId = ExtractRefId(argumentsElement);
                var detailEnvelope = await m_BridgeClient!.ReadDetailRefAsync(refId, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(detailEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    result = BuildToolCallResult(CreateErrorPayload(detailEnvelope.Error ?? $"Detail ref '{refId}' was not found."), isError: true);
                }
                else
                {
                    result = BuildToolCallResult(detailEnvelope.Result);
                }
            }
            else
            {
                var toolEnvelope = await m_BridgeClient!.CallToolAsync(toolName, argumentsElement, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(toolEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase))
                {
                    result = BuildToolCallResult(CreateErrorPayload(toolEnvelope.Error ?? $"Tool '{toolName}' failed."), isError: true);
                }
                else
                {
                    bool isError = IsToolLevelError(toolEnvelope.Result);
                    result = BuildToolCallResult(toolEnvelope.Result, isError);
                }
            }

            await WriteRpcAsync(new
            {
                jsonrpc = "2.0",
                id = idElement.GetValueOrDefault(),
                result
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteRpcAsync(new
            {
                jsonrpc = "2.0",
                id = idElement.GetValueOrDefault(),
                result = BuildToolCallResult(CreateErrorPayload(ex.Message), isError: true)
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    async Task EnsureBridgeReadyAsync(CancellationToken cancellationToken)
    {
        if (m_BridgeClient is { IsConnected: true })
            return;

        string projectPathHint = Environment.GetEnvironmentVariable("UNITY_MCP_PROJECT_PATH") ?? Directory.GetCurrentDirectory();
        BridgeDiscoveryResult? discoveryResult = BridgeDiscovery.FindBestBridge(projectPathHint);
        if (discoveryResult == null)
            throw new InvalidOperationException("No active Unity MCP bridge status file was found.");

        m_BridgeClient = new UnityBridgeClient(m_JsonOptions);
        m_BridgeClient.ToolsChanged += HandleBridgeToolsChangedAsync;
        await m_BridgeClient.ConnectAsync(discoveryResult, cancellationToken).ConfigureAwait(false);

        var registerEnvelope = await m_BridgeClient.RegisterClientAsync("unity-mcp-vnext", "0.1.0-alpha.1", "Unity MCP VNext", cancellationToken).ConfigureAwait(false);
        if (!string.Equals(registerEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || registerEnvelope.Result == null)
            throw new InvalidOperationException(registerEnvelope.Error ?? "Unity bridge rejected VNext client registration.");

        m_BridgeSessionId = registerEnvelope.Result.BridgeSessionId;
        m_ManifestVersion = registerEnvelope.Result.ManifestVersion;
        m_ActiveToolPacks = registerEnvelope.Result.ActiveToolPacks;

        var manifestEnvelope = await m_BridgeClient.GetManifestAsync(null, null, includeSchemas: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            throw new InvalidOperationException(manifestEnvelope.Error ?? "Unity bridge did not return an initial manifest.");

        await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
    }

    async Task HandleBridgeToolsChangedAsync(BridgeToolsChangedNotification notification)
    {
        if (m_BridgeClient == null)
            return;

        try
        {
            var manifestEnvelope = await m_BridgeClient.GetManifestAsync(m_BridgeSessionId, m_ManifestVersion, includeSchemas: false, CancellationToken.None).ConfigureAwait(false);
            if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
                return;

            await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, CancellationToken.None).ConfigureAwait(false);
            if (m_ClientInitialized)
                await SendToolsListChangedNotificationAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[unity-mcp-vnext] tools_changed refresh failed: {ex.Message}");
        }
    }

    async Task ApplyManifestAsync(BridgeManifestResult manifest, bool shouldFetchSchemas, CancellationToken cancellationToken)
    {
        m_BridgeSessionId = manifest.BridgeSessionId;
        m_ManifestVersion = manifest.ManifestVersion;
        m_ActiveToolPacks = manifest.ActiveToolPacks;

        if (string.Equals(manifest.Kind, "unchanged", StringComparison.OrdinalIgnoreCase))
            return;

        HashSet<string> toolsNeedingSchemas = new(StringComparer.OrdinalIgnoreCase);

        if (string.Equals(manifest.Kind, "full", StringComparison.OrdinalIgnoreCase))
        {
            m_ToolCache.Clear();
            foreach (var tool in manifest.Tools ?? [])
            {
                m_ToolCache[tool.Name] = ResolveToolSchemas(tool, toolsNeedingSchemas);
            }
        }
        else if (string.Equals(manifest.Kind, "delta", StringComparison.OrdinalIgnoreCase) && manifest.Delta != null)
        {
            foreach (string removedTool in manifest.Delta.Removed ?? [])
                m_ToolCache.Remove(removedTool);

            foreach (var addedTool in manifest.Delta.Added ?? [])
            {
                m_ToolCache[addedTool.Name] = ResolveToolSchemas(addedTool, toolsNeedingSchemas);
            }

            foreach (var updatedTool in manifest.Delta.Updated ?? [])
            {
                m_ToolCache[updatedTool.Name] = ResolveToolSchemas(updatedTool, toolsNeedingSchemas);
            }
        }

        if (!shouldFetchSchemas || toolsNeedingSchemas.Count == 0)
            return;

        var schemasEnvelope = await m_BridgeClient!.GetToolSchemasAsync(toolsNeedingSchemas.OrderBy(name => name, StringComparer.Ordinal).ToArray(), cancellationToken).ConfigureAwait(false);
        if (!string.Equals(schemasEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || schemasEnvelope.Result == null)
            return;

        foreach (var tool in schemasEnvelope.Result.Tools ?? [])
        {
            if (!m_ToolCache.TryGetValue(tool.Name, out var cachedTool))
                continue;

            cachedTool.InputSchema = tool.InputSchema;
            cachedTool.OutputSchema = tool.OutputSchema;
            cachedTool.Annotations = tool.Annotations;
            m_ToolCache[tool.Name] = cachedTool;
            RememberToolSchemas(tool);
        }
    }

    BridgeToolDescriptor ResolveToolSchemas(BridgeToolDescriptor tool, ISet<string> toolsNeedingSchemas)
    {
        if (HasInlineSchemas(tool))
        {
            RememberToolSchemas(tool);
            return tool;
        }

        if (TryRestoreSchemasFromCache(tool, out var restoredTool))
            return restoredTool;

        toolsNeedingSchemas.Add(tool.Name);
        return tool;
    }

    bool TryRestoreSchemasFromCache(BridgeToolDescriptor tool, out BridgeToolDescriptor restoredTool)
    {
        restoredTool = tool;
        if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.SchemaHash))
            return false;

        if (!m_ToolSchemaCache.TryGetValue(tool.Name, out var cachedSchema))
            return false;

        if (!string.Equals(cachedSchema.SchemaHash, tool.SchemaHash, StringComparison.Ordinal))
            return false;

        tool.InputSchema = cachedSchema.InputSchema;
        tool.OutputSchema = cachedSchema.OutputSchema;
        tool.Annotations = cachedSchema.Annotations;
        restoredTool = tool;
        return true;
    }

    void RememberToolSchemas(BridgeToolDescriptor tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name) || string.IsNullOrWhiteSpace(tool.SchemaHash))
            return;

        if (!HasSchemaPayload(tool.InputSchema))
            return;

        m_ToolSchemaCache[tool.Name] = new CachedToolSchema
        {
            SchemaHash = tool.SchemaHash,
            InputSchema = tool.InputSchema,
            OutputSchema = tool.OutputSchema,
            Annotations = tool.Annotations
        };
    }

    static bool HasInlineSchemas(BridgeToolDescriptor tool)
    {
        return HasSchemaPayload(tool.InputSchema) || HasSchemaPayload(tool.OutputSchema) || HasSchemaPayload(tool.Annotations);
    }

    static bool HasSchemaPayload(JsonElement element)
    {
        return element.ValueKind != JsonValueKind.Undefined && element.ValueKind != JsonValueKind.Null;
    }

    async Task SendToolsListChangedNotificationAsync(CancellationToken cancellationToken)
    {
        await WriteRpcAsync(new
        {
            jsonrpc = "2.0",
            method = "notifications/tools/list_changed",
            @params = new { }
        }, cancellationToken).ConfigureAwait(false);
    }

    async Task WriteRpcAsync(object payload, CancellationToken cancellationToken)
    {
        using Stream stdout = Console.OpenStandardOutput();
        await m_StdoutLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StdioJsonRpc.WriteMessageAsync(stdout, payload, m_JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            m_StdoutLock.Release();
        }
    }

    static string[] ExtractPacks(JsonElement argumentsElement)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return [];

        if (argumentsElement.TryGetProperty("packs", out var packsElement) || argumentsElement.TryGetProperty("Packs", out packsElement))
        {
            return packsElement.ValueKind == JsonValueKind.Array
                ? packsElement.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
                : [];
        }

        return [];
    }

    static string ExtractRefId(JsonElement argumentsElement)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (argumentsElement.TryGetProperty("refId", out var refIdElement) || argumentsElement.TryGetProperty("RefId", out refIdElement))
            return refIdElement.GetString() ?? string.Empty;

        return string.Empty;
    }

    static bool MatchesToolName(string actualToolName, string expectedToolName)
    {
        static string Normalize(string toolName) => string.IsNullOrWhiteSpace(toolName)
            ? string.Empty
            : toolName.Replace('.', '_');

        return string.Equals(
            Normalize(actualToolName),
            Normalize(expectedToolName),
            StringComparison.OrdinalIgnoreCase);
    }

    object BuildToolCallResult(JsonElement structuredContent, bool isError = false)
    {
        string summaryText = TryGetSummaryText(structuredContent);
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = summaryText
                }
            },
            structuredContent,
            isError
        };
    }

    static JsonElement CreateErrorPayload(string message)
    {
        return JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = message,
            code = "UNITY_MCP_ERROR"
        });
    }

    static bool IsToolLevelError(JsonElement structuredContent)
    {
        if (structuredContent.ValueKind != JsonValueKind.Object)
            return false;

        if (structuredContent.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.False)
            return true;

        if (structuredContent.TryGetProperty("isError", out var isErrorElement) && isErrorElement.ValueKind == JsonValueKind.True)
            return true;

        return false;
    }

    static string TryGetSummaryText(JsonElement structuredContent)
    {
        if (structuredContent.ValueKind == JsonValueKind.Object)
        {
            if (structuredContent.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String)
                return messageElement.GetString() ?? "Unity MCP tool call completed.";

            if (structuredContent.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
                return errorElement.GetString() ?? "Unity MCP tool call failed.";
        }

        string raw = structuredContent.GetRawText();
        return raw.Length <= 400 ? raw : raw[..400] + "...";
    }
}

internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        var host = new UnityMcpServerHost();
        await host.RunAsync(cts.Token).ConfigureAwait(false);
    }
}
