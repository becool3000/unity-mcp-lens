using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Reflection;

namespace UnityMcpLens;

sealed class UnityMcpLensHost
{
    static readonly TimeSpan s_BridgeQuarantineTtl = TimeSpan.FromSeconds(30);
    static readonly string s_HostVersion = ResolveHostVersion();

    static readonly HashSet<string> s_ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unity_GameObject_Inspect",
        "Unity_GameObject_PreviewChanges",
        "Unity_GetLensHealth",
        "Unity_ListToolPacks",
        "Unity_ReadDetailRef",
        "Unity_ReadConsole",
        "Unity_ListResources",
        "Unity_ReadResource",
        "Unity_FindInFile",
        "Unity_GetSha",
        "Unity_ValidateScript",
        "Unity_UI_PreviewEnsureHierarchy",
        "Unity_UI_PreviewLayoutProperties",
        "Unity_UI_VerifyScreenLayout",
        "Unity_UI_VerifyScreenLayoutMatrix",
        "Unity_UI_PreviewCreateCanvasPrefab",
        "Unity_UI_VerifyRaycastAndLayout",
        "Unity_Scene_PreviewBindSerializedReferences",
        "Unity_Scene_PreviewInstantiatePrefabAndBind",
        "Unity_Scene_VerifySerializedReferences",
        "Unity_Asset_PreviewImportSpriteSheetAndBind",
        "Unity_Asset_VerifySpriteArrayBinding",
        "Unity_UI_Raycast",
        "Unity_Asset_Search",
        "Unity_Object_ValidateReferences",
        "Unity_Project_ScanMissingScripts",
        "Unity_Project_GetInfo",
        "Unity_Project_GetPackages",
        "Unity_Profiler_Query",
        "Unity_ManageScript_capabilities"
    };

    static readonly HashSet<string> s_MutatingTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unity_GameObject_ApplyChanges",
        "Unity_ManageGameObject",
        "Unity_ManageScene",
        "Unity_ManageAsset",
        "Unity_ManageEditor",
        "Unity_ManageMenuItem",
        "Unity_ManageScript",
        "Unity_ManageShader",
        "Unity_ImportExternalModel",
        "Unity_ApplyTextEdits",
        "Unity_ScriptApplyEdits",
        "Unity_CreateScript",
        "Unity_DeleteScript",
        "Unity_RunCommand",
        "Unity_Resource_Write",
        "Unity_Resource_Delete",
        "Unity_Project_ManagePackages",
        "Unity_Asset_ConfigureSpriteImport",
        "Unity_Asset_ImportSpriteSheetAndBind",
        "Unity_Asset_ApplyImportSpriteSheetAndBind",
        "Unity_Prefab_SetSerializedProperties",
        "Unity_Scene_SetSerializedProperties",
        "Unity_Scene_ApplyBindSerializedReferences",
        "Unity_Editor_ScriptUpdatingConsentModal",
        "Unity_Tile_BuildSet",
        "Unity_Tilemap_Setup",
        "Unity_Tilemap_Paint",
        "Unity_UI_ApplyEnsureHierarchy",
        "Unity_UI_ApplyLayoutProperties",
        "Unity_UI_ApplyCreateCanvasPrefab",
        "Unity_Scene_ApplyInstantiatePrefabAndBind",
        "Unity_PlayMode_PointerInputSmoke",
        "Unity_Editor_ExitPlayMode",
        "Unity_UI_Toolkit"
    };

    static readonly string[] s_ReadOnlyPrefixes =
    [
        "Unity_Read",
        "Unity_Get",
        "Unity_List",
        "Unity_Find",
        "Unity_Validate",
        "Unity_Query",
        "Unity_Project_Get",
        "Unity_Runtime_Get",
        "Unity_UI_Get"
    ];

    sealed class CachedToolSchema
    {
        public string? SchemaHash { get; init; }
        public JsonElement InputSchema { get; init; }
        public JsonElement OutputSchema { get; init; }
        public JsonElement Annotations { get; init; }
    }

    sealed class BridgeConnectionSnapshot
    {
        public required string StatusPath { get; init; }
        public required string ConnectionPath { get; init; }
        public required string ProjectRoot { get; init; }
        public required DateTime LastHeartbeatUtc { get; init; }
        public required TimeSpan HeartbeatAge { get; init; }
        public required bool IsFresh { get; init; }
        public required bool IsProjectMatch { get; init; }
        public required bool EditorPidAlive { get; init; }
        public required DateTime ConnectedUtc { get; init; }
        public int EditorPid { get; init; }
        public string? BridgeSessionId { get; set; }
        public long ManifestVersion { get; set; }

        public static BridgeConnectionSnapshot From(BridgeDiscoveryResult discoveryResult)
        {
            return new BridgeConnectionSnapshot
            {
                StatusPath = discoveryResult.StatusPath,
                ConnectionPath = discoveryResult.ConnectionPath,
                ProjectRoot = discoveryResult.ProjectRoot,
                LastHeartbeatUtc = discoveryResult.LastHeartbeatUtc,
                HeartbeatAge = discoveryResult.HeartbeatAge,
                IsFresh = discoveryResult.IsFresh,
                IsProjectMatch = discoveryResult.IsProjectMatch,
                EditorPidAlive = discoveryResult.EditorPidAlive,
                ConnectedUtc = DateTime.UtcNow,
                EditorPid = discoveryResult.EditorPid,
                BridgeSessionId = discoveryResult.StatusFile.BridgeSessionId,
                ManifestVersion = discoveryResult.StatusFile.ManifestVersion
            };
        }
    }

    sealed class BridgeRecoveryState
    {
        public bool RetrySafe { get; init; }
        public bool RetryAttempted { get; set; }
        public bool RetrySucceeded { get; set; }
        public bool MaybeApplied { get; set; }
        public string? RecoveryError { get; set; }
        public string? FailedConnectionPath { get; set; }
        public string? FailedStatusPath { get; set; }
    }

    readonly JsonSerializerOptions m_JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    readonly SemaphoreSlim m_StdoutLock = new(1, 1);
    readonly Dictionary<string, BridgeToolDescriptor> m_ToolCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, CachedToolSchema> m_ToolSchemaCache = new(StringComparer.OrdinalIgnoreCase);

    UnityBridgeClient? m_BridgeClient;
    BridgeConnectionSnapshot? m_BridgeConnection;
    BridgeRecoveryState? m_LastRecoveryState;
    string? m_BridgeSessionId;
    long m_ManifestVersion;
    string[] m_ActiveToolPacks = ["foundation"];
    bool m_ClientInitialized;
    readonly Dictionary<string, DateTime> m_BridgeQuarantine = new(StringComparer.OrdinalIgnoreCase);

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
                            name = "unity-mcp-lens",
                            version = s_HostVersion
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
            await EnsureBridgeReadyWithRecoveryAsync("tools/list", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[unity-mcp-lens] tools/list bridge bootstrap failed: {ex.Message}");
            EnsureBootstrapToolsAvailable();
        }

        EnsureBootstrapToolsAvailable();

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
                annotations = ResolveToolAnnotations(tool)
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

    void EnsureBootstrapToolsAvailable()
    {
        bool needsBridgeBootstrap = m_ToolCache.Count == 0;
        if (needsBridgeBootstrap)
        {
            foreach (var tool in BuildBootstrapTools())
                m_ToolCache[tool.Name] = tool;
        }

        m_ToolCache[ScriptUpdatingConsentModalTool.ToolName] =
            ScriptUpdatingConsentModalTool.BuildDescriptor(m_JsonOptions);
    }

    BridgeToolDescriptor[] BuildBootstrapTools()
    {
        JsonElement emptyInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { }
        }, m_JsonOptions);

        JsonElement setToolPacksInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                packs = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "The non-foundation tool packs to activate for this connection."
                }
            }
        }, m_JsonOptions);

        JsonElement readDetailRefInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                refId = new
                {
                    type = "string",
                    description = "The stored detail ref identifier to resolve."
                }
            },
            required = new[] { "refId" }
        }, m_JsonOptions);

        return
        [
            BuildBootstrapTool(
                "Unity_GetLensHealth",
                "Get Unity Lens Health",
                "Returns a compact Lens health summary for the current Unity bridge connection, including active packs, exported tool count, bridge status, editor stability, and the recommended next action.",
                emptyInputSchema,
                readOnlyHint: true),
            BuildBootstrapTool(
                "Unity_ListToolPacks",
                "List Unity Tool Packs",
                "Lists the available Unity MCP tool packs, the active packs for this connection, and recommended next expansions.",
                emptyInputSchema,
                readOnlyHint: true),
            BuildBootstrapTool(
                "Unity_SetToolPacks",
                "Set Unity Tool Packs",
                "Sets the active Unity MCP tool packs for this connection. Foundation stays active automatically and at most two additional packs may be selected.",
                setToolPacksInputSchema,
                readOnlyHint: false),
            BuildBootstrapTool(
                "Unity_ReadDetailRef",
                "Read Unity Detail Ref",
                "Reads a stored detail ref payload previously returned by a compact Unity MCP tool result.",
                readDetailRefInputSchema,
                readOnlyHint: true)
        ];
    }

    BridgeToolDescriptor BuildBootstrapTool(string name, string title, string description, JsonElement inputSchema, bool readOnlyHint)
    {
        return new BridgeToolDescriptor
        {
            Name = name,
            Title = title,
            Description = description,
            Groups = ["assistant", "core"],
            Packs = ["foundation"],
            ReadOnlyHint = readOnlyHint,
            InputSchema = inputSchema,
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint }, m_JsonOptions)
        };
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
        string canonicalToolName = CanonicalizeToolName(toolName);
        JsonElement argumentsElement = paramsElement.TryGetProperty("arguments", out var arguments) ? arguments : JsonSerializer.SerializeToElement(new { }, m_JsonOptions);

        if (ScriptUpdatingConsentModalTool.MatchesToolName(canonicalToolName))
        {
            var localPayload = ScriptUpdatingConsentModalTool.Execute(argumentsElement, m_JsonOptions);
            await WriteRpcAsync(new
            {
                jsonrpc = "2.0",
                id = idElement.GetValueOrDefault(),
                result = BuildToolCallResult(localPayload, IsToolLevelError(localPayload))
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        BridgeRecoveryState recoveryState = new()
        {
            RetrySafe = IsSafeBridgeRetryTool(canonicalToolName)
        };
        int maxAttempts = recoveryState.RetrySafe ? 2 : 1;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                object result = await InvokeToolCallAsync(toolName, canonicalToolName, argumentsElement, cancellationToken).ConfigureAwait(false);
                recoveryState.RetrySucceeded = recoveryState.RetryAttempted;
                await WriteRpcAsync(new
                {
                    jsonrpc = "2.0",
                    id = idElement.GetValueOrDefault(),
                    result
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && IsBridgeTransportFailure(ex))
            {
                recoveryState.RetryAttempted = true;
                recoveryState.MaybeApplied = !recoveryState.RetrySafe && BridgeRequestWasSent(ex);
                Console.Error.WriteLine($"[unity-mcp-lens] Bridge transport failed for '{canonicalToolName}', reconnecting and retrying once: {ex.Message}");
                try
                {
                    await RecoverBridgeAfterTransportFailureAsync(ex, canonicalToolName, recoveryState, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception recoveryEx)
                {
                    recoveryState.RecoveryError = recoveryEx.Message;
                    JsonElement payload = CreateTransportErrorPayload(recoveryEx, canonicalToolName, recoveryState);
                    await WriteRpcAsync(new
                    {
                        jsonrpc = "2.0",
                        id = idElement.GetValueOrDefault(),
                        result = BuildToolCallResult(payload, isError: true)
                    }, cancellationToken).ConfigureAwait(false);
                    return;
                }
            }
            catch (Exception ex)
            {
                JsonElement payload;
                if (IsBridgeTransportFailure(ex))
                {
                    BridgeRecoveryState finalRecoveryState = new()
                    {
                        RetrySafe = recoveryState.RetrySafe,
                        RetryAttempted = recoveryState.RetryAttempted,
                        RetrySucceeded = recoveryState.RetrySucceeded,
                        MaybeApplied = !recoveryState.RetrySafe,
                        RecoveryError = recoveryState.RecoveryError,
                        FailedConnectionPath = recoveryState.FailedConnectionPath ?? m_BridgeConnection?.ConnectionPath,
                        FailedStatusPath = recoveryState.FailedStatusPath ?? m_BridgeConnection?.StatusPath
                    };
                    QuarantineCurrentBridge();
                    await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);
                    payload = CreateTransportErrorPayload(ex, canonicalToolName, finalRecoveryState);
                }
                else
                {
                    payload = CreateErrorPayload(ex.Message);
                }

                await WriteRpcAsync(new
                {
                    jsonrpc = "2.0",
                    id = idElement.GetValueOrDefault(),
                    result = BuildToolCallResult(payload, isError: true)
                }, cancellationToken).ConfigureAwait(false);
                return;
            }
        }
    }

    async Task<object> InvokeToolCallAsync(string toolName, string canonicalToolName, JsonElement argumentsElement, CancellationToken cancellationToken)
    {
        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);

        if (ToolNamesMatch(canonicalToolName, "Unity.SetToolPacks"))
        {
            string[] requestedPacks = ExtractPacks(argumentsElement);
            var manifestEnvelope = await m_BridgeClient!.SetToolPacksAsync(requestedPacks, includeSchemas: false, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            {
                return BuildToolCallResult(CreateErrorPayload(manifestEnvelope.Error ?? "Failed to update Unity tool packs."), isError: true);
            }

            bool unchanged = string.Equals(manifestEnvelope.Result.Kind, "unchanged", StringComparison.OrdinalIgnoreCase);
            await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
            if (!unchanged && m_ClientInitialized)
                await SendToolsListChangedNotificationAsync(cancellationToken).ConfigureAwait(false);

            return BuildToolCallResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = unchanged ? "Active Unity MCP tool packs unchanged." : "Updated active Unity MCP tool packs.",
                data = new
                {
                    activeToolPacks = manifestEnvelope.Result.ActiveToolPacks,
                    manifestVersion = manifestEnvelope.Result.ManifestVersion,
                    bridgeSessionId = manifestEnvelope.Result.BridgeSessionId,
                    unchanged,
                    manifestKind = manifestEnvelope.Result.Kind,
                    toolCount = m_ToolCache.Count
                }
            }, m_JsonOptions));
        }

        if (ToolNamesMatch(canonicalToolName, "Unity.ReadDetailRef"))
        {
            string refId = ExtractRefId(argumentsElement);
            var detailEnvelope = await m_BridgeClient!.ReadDetailRefAsync(refId, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(detailEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase))
            {
                return BuildToolCallResult(CreateErrorPayload(detailEnvelope.Error ?? $"Detail ref '{refId}' was not found."), isError: true);
            }

            return BuildToolCallResult(detailEnvelope.Result);
        }

        var toolEnvelope = await m_BridgeClient!.CallToolAsync(canonicalToolName, argumentsElement, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(toolEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase))
        {
            return BuildToolCallResult(CreateErrorPayload(toolEnvelope.Error ?? $"Tool '{toolName}' failed."), isError: true);
        }

        bool isError = IsToolLevelError(toolEnvelope.Result);
        return BuildToolCallResult(toolEnvelope.Result, isError);
    }

    async Task EnsureBridgeReadyWithRecoveryAsync(string operationName, CancellationToken cancellationToken)
    {
        BridgeRecoveryState recoveryState = new()
        {
            RetrySafe = true
        };

        try
        {
            await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsBridgeTransportFailure(ex))
        {
            recoveryState.RetryAttempted = true;
            Console.Error.WriteLine($"[unity-mcp-lens] Bridge setup failed for '{operationName}', reconnecting once: {ex.Message}");
            await RecoverBridgeAfterTransportFailureAsync(ex, operationName, recoveryState, cancellationToken).ConfigureAwait(false);
            recoveryState.RetrySucceeded = true;
            m_LastRecoveryState = recoveryState;
        }
    }

    async Task EnsureBridgeReadyAsync(CancellationToken cancellationToken)
    {
        BridgeDiscoveryResult? currentDiscovery = FindCurrentBridge();
        if (m_BridgeClient is { IsConnected: true } &&
            m_BridgeConnection != null &&
            currentDiscovery != null &&
            IsSameBridgeGeneration(m_BridgeConnection, currentDiscovery))
        {
            return;
        }

        string[] desiredActivePacks = m_ActiveToolPacks.Length > 0 ? m_ActiveToolPacks : ["foundation"];
        if (m_BridgeClient != null)
            await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);

        BridgeDiscoveryResult? discoveryResult = currentDiscovery ?? FindCurrentBridge();
        if (discoveryResult == null)
            throw new InvalidOperationException("No fresh active Unity MCP bridge status file was found.");

        m_BridgeConnection = BridgeConnectionSnapshot.From(discoveryResult);
        m_BridgeClient = new UnityBridgeClient(m_JsonOptions);
        m_BridgeClient.ToolsChanged += HandleBridgeToolsChangedAsync;
        await m_BridgeClient.ConnectAsync(discoveryResult, cancellationToken).ConfigureAwait(false);

        var registerEnvelope = await m_BridgeClient.RegisterClientAsync("unity-mcp-lens", s_HostVersion, "Unity MCP Lens", cancellationToken).ConfigureAwait(false);
        if (!string.Equals(registerEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || registerEnvelope.Result == null)
            throw new InvalidOperationException(registerEnvelope.Error ?? "Unity bridge rejected Lens client registration.");

        m_BridgeSessionId = registerEnvelope.Result.BridgeSessionId;
        m_ManifestVersion = registerEnvelope.Result.ManifestVersion;
        m_ActiveToolPacks = registerEnvelope.Result.ActiveToolPacks;
        if (m_BridgeConnection != null)
        {
            m_BridgeConnection.BridgeSessionId = m_BridgeSessionId;
            m_BridgeConnection.ManifestVersion = m_ManifestVersion;
        }

        var manifestEnvelope = await m_BridgeClient.GetManifestAsync(null, null, includeSchemas: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            throw new InvalidOperationException(manifestEnvelope.Error ?? "Unity bridge did not return an initial manifest.");

        await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
        await RestoreActiveToolPacksAsync(desiredActivePacks, cancellationToken).ConfigureAwait(false);
    }

    async Task RecoverBridgeAfterTransportFailureAsync(
        Exception exception,
        string operationName,
        BridgeRecoveryState recoveryState,
        CancellationToken cancellationToken)
    {
        recoveryState.FailedConnectionPath = m_BridgeConnection?.ConnectionPath;
        recoveryState.FailedStatusPath = m_BridgeConnection?.StatusPath;
        QuarantineCurrentBridge();
        await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);
        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        m_LastRecoveryState = recoveryState;
    }

    async Task ResetBridgeClientAsync(bool preserveActivePacks = true, bool clearToolCache = false)
    {
        var bridgeClient = m_BridgeClient;
        m_BridgeClient = null;
        m_BridgeConnection = null;
        m_BridgeSessionId = null;
        m_ManifestVersion = 0;
        if (!preserveActivePacks)
            m_ActiveToolPacks = ["foundation"];
        if (clearToolCache)
            m_ToolCache.Clear();

        if (bridgeClient == null)
            return;

        bridgeClient.ToolsChanged -= HandleBridgeToolsChangedAsync;
        await bridgeClient.DisposeAsync().ConfigureAwait(false);
    }

    BridgeDiscoveryResult? FindCurrentBridge()
    {
        string projectPathHint = ResolveProjectPathHint(out bool requireProjectMatch);
        return BridgeDiscovery.FindBestBridge(projectPathHint, GetActiveQuarantineIds(), requireProjectMatch);
    }

    string ResolveProjectPathHint(out bool requireProjectMatch)
    {
        string? projectPath = Environment.GetEnvironmentVariable("UNITY_MCP_PROJECT_PATH");
        requireProjectMatch = !string.IsNullOrWhiteSpace(projectPath);
        return requireProjectMatch ? projectPath! : Directory.GetCurrentDirectory();
    }

    bool IsSameBridgeGeneration(BridgeConnectionSnapshot connection, BridgeDiscoveryResult discoveryResult)
    {
        return discoveryResult.IsFresh &&
            string.Equals(connection.StatusPath, discoveryResult.StatusPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.ConnectionPath, discoveryResult.ConnectionPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(connection.ProjectRoot, discoveryResult.ProjectRoot, StringComparison.OrdinalIgnoreCase);
    }

    async Task RestoreActiveToolPacksAsync(string[] desiredActivePacks, CancellationToken cancellationToken)
    {
        if (m_BridgeClient == null)
            return;

        string[] desiredAdditionalPacks = NormalizeAdditionalToolPacks(desiredActivePacks);
        string[] currentAdditionalPacks = NormalizeAdditionalToolPacks(m_ActiveToolPacks);
        if (desiredAdditionalPacks.SequenceEqual(currentAdditionalPacks, StringComparer.OrdinalIgnoreCase))
            return;

        var restoreEnvelope = await m_BridgeClient.SetToolPacksAsync(desiredAdditionalPacks, includeSchemas: false, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(restoreEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || restoreEnvelope.Result == null)
            throw new InvalidOperationException(restoreEnvelope.Error ?? "Unity bridge did not restore active tool packs after reconnect.");

        await ApplyManifestAsync(restoreEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
    }

    static string[] NormalizeAdditionalToolPacks(string[] packs)
    {
        return packs
            .Where(pack => !string.IsNullOrWhiteSpace(pack))
            .Select(pack => pack.Trim())
            .Where(pack => !string.Equals(pack, "foundation", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(pack => pack, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    void QuarantineCurrentBridge()
    {
        if (m_BridgeConnection == null)
            return;

        DateTime expiresUtc = DateTime.UtcNow.Add(s_BridgeQuarantineTtl);
        AddBridgeQuarantine(m_BridgeConnection.ConnectionPath, expiresUtc);
        AddBridgeQuarantine(m_BridgeConnection.StatusPath, expiresUtc);
    }

    void AddBridgeQuarantine(string? id, DateTime expiresUtc)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        m_BridgeQuarantine[NormalizeBridgeQuarantineId(id)] = expiresUtc;
    }

    string[] GetActiveQuarantineIds()
    {
        PruneBridgeQuarantine();
        return m_BridgeQuarantine.Keys.ToArray();
    }

    void PruneBridgeQuarantine()
    {
        DateTime nowUtc = DateTime.UtcNow;
        foreach (string expiredKey in m_BridgeQuarantine
            .Where(entry => entry.Value <= nowUtc)
            .Select(entry => entry.Key)
            .ToArray())
        {
            m_BridgeQuarantine.Remove(expiredKey);
        }
    }

    static string NormalizeBridgeQuarantineId(string id)
    {
        string trimmed = id.Trim();
        if (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            try
            {
                return Path.GetFullPath(trimmed)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return trimmed;
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
            Console.Error.WriteLine($"[unity-mcp-lens] tools_changed refresh failed: {ex.Message}");
            if (IsBridgeTransportFailure(ex))
            {
                try
                {
                    QuarantineCurrentBridge();
                    await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);
                }
                catch (Exception resetEx)
                {
                    Console.Error.WriteLine($"[unity-mcp-lens] tools_changed transport reset failed: {resetEx.Message}");
                }
            }
        }
    }

    async Task ApplyManifestAsync(BridgeManifestResult manifest, bool shouldFetchSchemas, CancellationToken cancellationToken)
    {
        m_BridgeSessionId = manifest.BridgeSessionId;
        m_ManifestVersion = manifest.ManifestVersion;
        m_ActiveToolPacks = manifest.ActiveToolPacks;
        if (m_BridgeConnection != null)
        {
            m_BridgeConnection.BridgeSessionId = manifest.BridgeSessionId;
            m_BridgeConnection.ManifestVersion = manifest.ManifestVersion;
        }

        if (string.Equals(manifest.Kind, "unchanged", StringComparison.OrdinalIgnoreCase))
        {
            EnsureBootstrapToolsAvailable();
            return;
        }

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

        EnsureBootstrapToolsAvailable();

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

    static object ResolveToolAnnotations(BridgeToolDescriptor tool)
    {
        bool readOnlyHint = DeriveReadOnlyHint(tool.Name, tool.ReadOnlyHint);
        if (tool.Annotations.ValueKind != JsonValueKind.Object)
            return new { readOnlyHint };

        if (tool.Annotations.TryGetProperty("readOnlyHint", out _))
            return tool.Annotations;

        var merged = new JsonObject();
        foreach (var property in tool.Annotations.EnumerateObject())
            merged[property.Name] = JsonNode.Parse(property.Value.GetRawText());

        merged["readOnlyHint"] = readOnlyHint;
        return merged;
    }

    static bool DeriveReadOnlyHint(string toolName, bool descriptorHint)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return descriptorHint;

        string normalizedToolName = toolName.Replace('.', '_');
        if (s_MutatingTools.Contains(normalizedToolName))
            return false;
        if (s_ReadOnlyTools.Contains(normalizedToolName))
            return true;

        foreach (var prefix in s_ReadOnlyPrefixes)
        {
            if (normalizedToolName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return descriptorHint;
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

    static string CanonicalizeToolName(string toolName)
    {
        return string.IsNullOrWhiteSpace(toolName)
            ? string.Empty
            : toolName.Replace('.', '_');
    }

    static bool ToolNamesMatch(string actualToolName, string expectedToolName)
    {
        return string.Equals(
            CanonicalizeToolName(actualToolName),
            CanonicalizeToolName(expectedToolName),
            StringComparison.OrdinalIgnoreCase);
    }

    object BuildToolCallResult(JsonElement structuredContent, bool isError = false)
    {
        JsonElement normalizedStructuredContent = RemoveNestedStructuredContentEcho(structuredContent);
        string summaryText = TryGetSummaryText(normalizedStructuredContent);
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
            structuredContent = normalizedStructuredContent,
            isError
        };
    }

    static JsonElement RemoveNestedStructuredContentEcho(JsonElement structuredContent)
    {
        if (structuredContent.ValueKind != JsonValueKind.Object ||
            !structuredContent.TryGetProperty("structuredContent", out _))
        {
            return structuredContent.Clone();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in structuredContent.EnumerateObject())
            {
                if (property.NameEquals("structuredContent"))
                    continue;

                property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    JsonElement CreateTransportErrorPayload(Exception exception, string toolName, BridgeRecoveryState recoveryState)
    {
        string message = recoveryState.RetryAttempted
            ? $"Unity MCP bridge transport failed for '{toolName}' after one reconnect retry: {exception.Message}"
            : $"Unity MCP bridge transport failed for '{toolName}': {exception.Message}";

        m_LastRecoveryState = recoveryState;
        PruneBridgeQuarantine();
        return CreateErrorPayload(
            message,
            "UNITY_MCP_TRANSPORT_ERROR",
            new
            {
                transportFailure = true,
                retryAttempted = recoveryState.RetryAttempted,
                retrySucceeded = recoveryState.RetrySucceeded,
                retrySafe = recoveryState.RetrySafe,
                maybeApplied = recoveryState.MaybeApplied,
                recoveryError = recoveryState.RecoveryError,
                recovery = ResolveRecoveryGuidance(recoveryState),
                host = CreateHostDiagnostics(),
                bridge = CreateBridgeDiagnostics(recoveryState),
                quarantine = new
                {
                    ttlSeconds = (int)s_BridgeQuarantineTtl.TotalSeconds,
                    count = m_BridgeQuarantine.Count
                }
            });
    }

    static string ResolveRecoveryGuidance(BridgeRecoveryState recoveryState)
    {
        if (recoveryState.RetrySucceeded)
            return "The Lens host recovered from a stale Unity bridge transport and retried this safe call successfully.";
        if (recoveryState.RetryAttempted)
            return "The Lens host already reconnected and retried this safe call once. Check Unity bridge health, then retry the tool call.";
        if (recoveryState.MaybeApplied)
            return "The Unity tool call may have reached the editor before transport closed. Verify Unity state before retrying this mutating call.";
        if (recoveryState.RetrySafe)
            return "For read-only Lens tools the host retries one stale-pipe failure automatically; retry the call after the bridge reports ready.";

        return "Reconnect or restart the Unity MCP bridge, verify Unity state, then retry the tool call if it is safe to do so.";
    }

    object CreateHostDiagnostics()
    {
        using var process = Process.GetCurrentProcess();
        return new
        {
            processId = process.Id,
            executablePath = Environment.ProcessPath,
            currentDirectory = Directory.GetCurrentDirectory(),
            assemblyVersion = typeof(UnityMcpLensHost).Assembly.GetName().Version?.ToString(),
            informationalVersion = s_HostVersion,
            fileVersion = ResolveFileVersion(Environment.ProcessPath)
        };
    }

    object CreateBridgeDiagnostics(BridgeRecoveryState recoveryState)
    {
        return new
        {
            selectedStatusPath = m_BridgeConnection?.StatusPath,
            selectedConnectionPath = m_BridgeConnection?.ConnectionPath,
            projectRoot = m_BridgeConnection?.ProjectRoot,
            editorPid = m_BridgeConnection?.EditorPid,
            editorPidAlive = m_BridgeConnection?.EditorPidAlive,
            heartbeatAgeSeconds = m_BridgeConnection == null || m_BridgeConnection.HeartbeatAge == TimeSpan.MaxValue
                ? (double?)null
                : Math.Round(m_BridgeConnection.HeartbeatAge.TotalSeconds, 3),
            bridgeSessionId = m_BridgeConnection?.BridgeSessionId,
            manifestVersion = m_BridgeConnection?.ManifestVersion,
            failedStatusPath = recoveryState.FailedStatusPath,
            failedConnectionPath = recoveryState.FailedConnectionPath
        };
    }

    static JsonElement CreateErrorPayload(string message, string code = "UNITY_MCP_ERROR", object? data = null)
    {
        if (data == null)
        {
            return JsonSerializer.SerializeToElement(new
            {
                success = false,
                error = message,
                code
            });
        }

        return JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = message,
            code,
            data
        });
    }

    static bool IsSafeBridgeRetryTool(string toolName)
    {
        return ToolNamesMatch(toolName, "Unity.SetToolPacks") ||
            ToolNamesMatch(toolName, "Unity.ReadDetailRef") ||
            DeriveReadOnlyHint(toolName, descriptorHint: false);
    }

    static bool BridgeRequestWasSent(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is BridgeTransportException bridgeTransportException)
                return bridgeTransportException.RequestSent;
        }

        return false;
    }

    static bool IsBridgeTransportFailure(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is BridgeTransportException or System.IO.IOException or ObjectDisposedException or TimeoutException)
                return true;

            string message = current.Message ?? string.Empty;
            if (message.Contains("pipe is broken", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("broken pipe", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("transport closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection disconnected", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Unity bridge connection closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Cannot access a disposed object", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("did not send a handshake", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static string ResolveHostVersion()
    {
        var assembly = typeof(UnityMcpLensHost).Assembly;
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion;

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    static string? ResolveFileVersion(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            return null;

        try
        {
            return FileVersionInfo.GetVersionInfo(executablePath).FileVersion;
        }
        catch
        {
            return null;
        }
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

        var host = new UnityMcpLensHost();
        await host.RunAsync(cts.Token).ConfigureAwait(false);
    }
}
