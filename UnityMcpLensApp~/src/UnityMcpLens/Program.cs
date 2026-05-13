using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using System.Reflection;

namespace UnityMcpLens;

sealed class UnityMcpLensHost
{
    static readonly TimeSpan s_BridgeQuarantineTtl = TimeSpan.FromSeconds(30);
    static readonly TimeSpan s_BridgeDiscoveryReloadRetryWindow = TimeSpan.FromSeconds(4);
    static readonly TimeSpan s_BridgeDiscoveryReloadRetryPollInterval = TimeSpan.FromMilliseconds(250);
    static readonly string s_HostVersion = ResolveHostVersion();
    const string ToolSurfaceModeEnvVar = "UNITY_MCP_LENS_TOOL_SURFACE_MODE";
    const string DynamicPacksToolSurfaceMode = "dynamic_packs";
    const string StaticAllToolSurfaceMode = "static_all";
    static readonly string s_ToolSurfaceMode = ResolveToolSurfaceMode();

    static readonly HashSet<string> s_ReadOnlyTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unity_GameObject_Inspect",
        "Unity_GameObject_ListComponents",
        "Unity_GameObject_GetComponent",
        "Unity_GameObject_PreviewChanges",
        "Unity_GameObject_PreviewComponentChanges",
        "Unity_GameObject_PreviewCreate",
        "Unity_GameObject_PreviewDelete",
        "Unity_Component_Search",
        "Unity_Component_ResolveCapability",
        "Unity_Component_InspectSchema",
        "Unity_Authoring_SuggestReusePlan",
        "Unity_Package_ResolveCapability",
        "Unity_Package_PreviewInstallForCapability",
        "Unity_Preset_Search",
        "Unity_Preset_Inspect",
        "Unity_Preset_PreviewApplyToComponent",
        "Unity_Prefab_Inspect",
        "Unity_Prefab_GetOverrides",
        "Unity_Prefab_PreviewApplyOverrides",
        "Unity_Prefab_PreviewRevertOverrides",
        "Unity_Prefab_PreviewCopyComponentSerializedValues",
        "Unity_GetLensHealth",
        "Unity_ListToolPacks",
        "Unity_Bridge_ListConnections",
        "Unity_ReadDetailRef",
        "Unity_Tools_Menu",
        "Unity_Tools_Describe",
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
        "Unity_Scene_PreviewAssignObjectReferences",
        "Unity_Scene_PreviewInstantiatePrefabAndBind",
        "Unity_Scene_PreviewCopyComponentSerializedValues",
        "Unity_Scene_VerifySerializedReferences",
        "Unity_Scene_FindComponents",
        "Unity_Scene_GetDirtyState",
        "Unity_Asset_PreviewImportSpriteSheetAndBind",
        "Unity_Asset_VerifySpriteArrayBinding",
        "Unity_Runtime_QueryObjects",
        "Unity_UI_Raycast",
        "Unity_Object_ResolveStablePath",
        "Unity_Asset_Search",
        "Unity_Object_ValidateReferences",
        "Unity_Project_ScanMissingScripts",
        "Unity_Project_GetInfo",
        "Unity_Project_GetPackages",
        "Unity_Profiler_Query",
        "Unity_Runtime_GetComponentSnapshot",
        "Unity_ManageScript_capabilities"
    };

    static readonly HashSet<string> s_MutatingTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unity_GameObject_ApplyChanges",
        "Unity_GameObject_ApplyComponentChanges",
        "Unity_GameObject_Create",
        "Unity_GameObject_Delete",
        "Unity_Workflow_AuthorSceneObject",
        "Unity_Workflow_AuthorPrefab",
        "Unity_Workflow_ConfigureExistingComponent",
        "Unity_Workflow_RunPlayModeVerification",
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
        "Unity_Tools_ActivateAndVerify",
        "Unity_Asset_ConfigureSpriteImport",
        "Unity_Asset_SetSerializedProperties",
        "Unity_Asset_ImportSpriteSheetAndBind",
        "Unity_Asset_ApplyImportSpriteSheetAndBind",
        "Unity_Preset_ApplyToComponent",
        "Unity_Prefab_Instantiate",
        "Unity_Prefab_CreateFromSceneObject",
        "Unity_Prefab_ApplyOverrides",
        "Unity_Prefab_RevertOverrides",
        "Unity_Prefab_ApplyCopyComponentSerializedValues",
        "Unity_Prefab_SetSerializedProperties",
        "Unity_Scene_SetSerializedProperties",
        "Unity_Scene_ApplyBindSerializedReferences",
        "Unity_Scene_ApplyAssignObjectReferences",
        "Unity_Scene_Save",
        "Unity_Scene_ApplyCopyComponentSerializedValues",
        "Unity_Editor_ScriptUpdatingConsentModal",
        "Unity_Editor_SyncScripts",
        "Unity_Editor_SetPlayMode",
        "Unity_PlayMode_EnterReady",
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

    sealed class BridgeDiscoveryException : InvalidOperationException
    {
        public BridgeDiscoveryException(string message, BridgeDiscoverySnapshot snapshot)
            : base(message)
        {
            Snapshot = snapshot;
        }

        public BridgeDiscoverySnapshot Snapshot { get; }
    }

    sealed class HostPlayReadyResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public bool EditorIdle { get; init; }
        public bool IsPlaying { get; init; }
        public bool RuntimeAdvanced { get; init; }
        public bool RuntimeProbeAvailable { get; init; }
        public int UpdateCount { get; init; }
        public int FixedUpdateCount { get; init; }
        public double UnscaledTime { get; init; }
        public string ActiveScene { get; init; } = string.Empty;
        public List<object> Attempts { get; init; } = [];
        public object? LastState { get; init; }
        public string? LastError { get; init; }
    }

    sealed class HostSyncReadyResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public bool EditorIdle { get; init; }
        public bool TimedOut { get; init; }
        public bool ConsoleCheckSucceeded { get; init; } = true;
        public int FinalConsoleErrorCount { get; init; }
        public int NewConsoleErrorCount { get; init; }
        public object? FinalConsole { get; init; }
        public List<object> Attempts { get; init; } = [];
        public object? LastState { get; init; }
        public string? LastError { get; init; }
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
    BridgeDiscoverySnapshot? m_LastBridgeDiscoverySnapshot;
    BridgeRecoveryState? m_LastRecoveryState;
    string? m_BridgeSessionId;
    string? m_SelectedProjectPathHint;
    bool m_SelectedProjectRequireFreshBridge = true;
    long m_ManifestVersion;
    string[] m_ActiveToolPacks = GetDefaultActivePacksForSurfaceMode();
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
        foreach (var tool in BuildBootstrapTools())
        {
            if (!m_ToolCache.ContainsKey(tool.Name))
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

        JsonElement toolsDescribeInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                toolName = new
                {
                    type = "string",
                    description = "Optional tool name to describe. Dot and underscore forms are equivalent."
                },
                includeSchemas = new
                {
                    type = "boolean",
                    description = "Include input/output schemas and annotations."
                },
                includePackRequirements = new
                {
                    type = "boolean",
                    description = "Include non-foundation pack requirements."
                },
                includeExamples = new
                {
                    type = "boolean",
                    description = "Include compact example call metadata."
                },
                maxTools = new
                {
                    type = "integer",
                    description = "Maximum matching tools to return."
                }
            }
        }, m_JsonOptions);

        JsonElement toolsMenuInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                maxToolsPerPack = new
                {
                    type = "integer",
                    description = "Maximum number of tool names to return per pack."
                }
            }
        }, m_JsonOptions);

        JsonElement bridgeListConnectionsInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                projectPath = new
                {
                    type = "string",
                    description = "Optional Unity project root filter. Defaults to UNITY_MCP_PROJECT_PATH or the current Unity project root if discoverable."
                },
                includeStale = new
                {
                    type = "boolean",
                    description = "Include stale/dead/mismatched candidates. Defaults to true."
                },
                maxEntries = new
                {
                    type = "integer",
                    description = "Maximum candidate rows to return. Defaults to 12."
                }
            }
        }, m_JsonOptions);

        JsonElement selectProjectInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                projectPath = new
                {
                    type = "string",
                    description = "Unity project root to bind this MCP host session to. An Assets folder path is normalized to its project root."
                },
                requireFreshBridge = new
                {
                    type = "boolean",
                    description = "Require the selected bridge heartbeat to be fresh and editor PID alive. Defaults to true."
                },
                connect = new
                {
                    type = "boolean",
                    description = "Connect or reconnect to the selected bridge immediately. Defaults to true."
                },
                maxCandidates = new
                {
                    type = "integer",
                    description = "Maximum bridge candidates to include in diagnostics. Defaults to 12."
                }
            },
            required = new[] { "projectPath" }
        }, m_JsonOptions);

        JsonElement activateAndVerifyInputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                packs = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "The non-foundation tool packs to activate for this connection."
                },
                expectedTools = new
                {
                    type = "array",
                    items = new { type = "string" },
                    description = "Expected tools that should be present after activation. Dot and underscore forms are equivalent."
                }
            }
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
                "Unity_Bridge_ListConnections",
                "List Unity Bridge Connections",
                "Lists Unity MCP bridge connection candidates, project-root match state, heartbeat age, editor PID liveness, quarantine state, and exclusion reasons without touching the bridge.",
                bridgeListConnectionsInputSchema,
                readOnlyHint: true),
            BuildBootstrapTool(
                "Unity_Session_SelectProject",
                "Select Unity Project",
                "Binds this MCP host session to an explicit Unity project root so subsequent direct Lens tool calls use the matching editor bridge instead of the host current working directory.",
                selectProjectInputSchema,
                readOnlyHint: false),
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
                readOnlyHint: true),
            BuildBootstrapTool(
                "Unity_Tools_Describe",
                "Describe Unity Tools",
                "Describes live Unity MCP Lens tools, including current active packs, manifest version, required packs, and schemas when requested.",
                toolsDescribeInputSchema,
                readOnlyHint: true),
            BuildBootstrapTool(
                "Unity_Tools_Menu",
                "Unity Tools Menu",
                "Returns a compact pack-grouped menu of Unity MCP Lens tools and workflow recommendations.",
                toolsMenuInputSchema,
                readOnlyHint: true),
            BuildBootstrapTool(
                "Unity_Tools_ActivateAndVerify",
                "Activate And Verify Unity Tools",
                "Activates Unity MCP Lens tool packs and verifies expected tools against the MCP host-visible tool surface.",
                activateAndVerifyInputSchema,
                readOnlyHint: false)
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
                    payload = ex is BridgeDiscoveryException discoveryException
                        ? CreateBridgeDiscoveryErrorPayload(discoveryException)
                        : CreateErrorPayload(ex.Message);
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
        if (ToolNamesMatch(canonicalToolName, "Unity.SetToolPacks") && IsStaticAllToolSurface)
            return BuildToolCallResult(CreateStaticAllSetToolPacksNoopPayload(argumentsElement));

        if (ToolNamesMatch(canonicalToolName, "Unity.Bridge.ListConnections"))
            return BuildToolCallResult(CreateBridgeListConnectionsPayload(argumentsElement));

        if (ToolNamesMatch(canonicalToolName, "Unity.Session.SelectProject"))
        {
            JsonElement payload = await CreateSelectProjectPayloadAsync(argumentsElement, cancellationToken).ConfigureAwait(false);
            return BuildToolCallResult(payload, IsToolLevelError(payload));
        }

        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);

        if (ToolNamesMatch(canonicalToolName, "Unity.PlayMode.EnterReady"))
        {
            JsonElement payload = await CreatePlayModeEnterReadyPayloadAsync(argumentsElement, cancellationToken).ConfigureAwait(false);
            return BuildToolCallResult(payload, IsToolLevelError(payload));
        }

        if (ToolNamesMatch(canonicalToolName, "Unity.Editor.SyncScripts"))
        {
            JsonElement payload = await CreateSyncScriptsReadyPayloadAsync(argumentsElement, cancellationToken).ConfigureAwait(false);
            return BuildToolCallResult(payload, IsToolLevelError(payload));
        }

        if (ToolNamesMatch(canonicalToolName, "Unity.SetToolPacks"))
        {
            string[] requestedPacks = ExtractPacks(argumentsElement);
            if (IsStaticAllToolSurface)
            {
                return BuildToolCallResult(CreateStaticAllSetToolPacksNoopPayload(argumentsElement));
            }

            var manifestEnvelope = await m_BridgeClient!.SetToolPacksAsync(
                requestedPacks,
                includeSchemas: false,
                cancellationToken,
                reason: "dynamic_pack_update",
                toolSurfaceMode: s_ToolSurfaceMode).ConfigureAwait(false);
            if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            {
                return BuildToolCallResult(CreateErrorPayload(manifestEnvelope.Error ?? "Failed to update Unity tool packs."), isError: true);
            }

            bool unchanged = string.Equals(manifestEnvelope.Result.Kind, "unchanged", StringComparison.OrdinalIgnoreCase);
            await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
            bool toolsListChangedNotificationSent = false;
            if (!unchanged && m_ClientInitialized)
            {
                await SendToolsListChangedNotificationAsync(cancellationToken).ConfigureAwait(false);
                toolsListChangedNotificationSent = true;
            }

            return BuildToolCallResult(JsonSerializer.SerializeToElement(new
            {
                success = true,
                message = unchanged ? "Active Unity MCP tool packs unchanged." : "Updated active Unity MCP tool packs.",
                data = new
                {
                    activeToolPacks = manifestEnvelope.Result.ActiveToolPacks,
                    toolSurfaceMode = s_ToolSurfaceMode,
                    manifestVersion = manifestEnvelope.Result.ManifestVersion,
                    bridgeSessionId = manifestEnvelope.Result.BridgeSessionId,
                    unchanged,
                    manifestKind = manifestEnvelope.Result.Kind,
                    toolCount = m_ToolCache.Count,
                    toolsListChangedNotificationSent,
                    clientSurface = new
                    {
                        expectedRefresh = toolsListChangedNotificationSent,
                        note = toolsListChangedNotificationSent
                            ? "Lens emitted notifications/tools/list_changed after applying the new pack surface. If the MCP client still cannot call described tools, use Unity.Tools.Describe or helper scripts and refresh the client session."
                            : "No tools/list_changed notification was emitted because the active tool packs were unchanged or the client has not completed initialize yet."
                    }
                }
            }, m_JsonOptions));
        }

        if (ToolNamesMatch(canonicalToolName, "Unity.Tools.ActivateAndVerify"))
        {
            string[] requestedPacks = ExtractPacks(argumentsElement);
            string[] expectedTools = NormalizeToolNames(ExtractExpectedTools(argumentsElement));
            if (IsStaticAllToolSurface)
            {
                string[] staticHostToolNames = m_ToolCache.Keys
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
                string[] staticMatchedExpectedTools = expectedTools
                    .Where(expected => staticHostToolNames.Any(actual => ToolNamesMatch(actual, expected)))
                    .ToArray();
                string[] staticMissingExpectedTools = expectedTools
                    .Where(expected => !staticHostToolNames.Any(actual => ToolNamesMatch(actual, expected)))
                    .ToArray();
                bool staticVerificationSucceeded = staticMissingExpectedTools.Length == 0;

                return BuildToolCallResult(JsonSerializer.SerializeToElement(new
                {
                    success = staticVerificationSucceeded,
                    message = staticVerificationSucceeded
                        ? "Verified expected host-visible tools in static_all tool surface mode."
                        : "Expected tools were missing from the static_all host-visible surface.",
                    data = new
                    {
                        toolSurfaceMode = s_ToolSurfaceMode,
                        activeToolPacks = m_ActiveToolPacks,
                        requestedPacks,
                        unchanged = true,
                        manifestKind = "static_all_noop",
                        exportedToolCount = m_ToolCache.Count,
                        expectedTools,
                        matchedExpectedTools = staticMatchedExpectedTools,
                        missingFromServerSurface = staticMissingExpectedTools,
                        missingFromClient = staticMissingExpectedTools,
                        toolsListChangedNotificationSent = false,
                        clientSurface = new
                        {
                            serverSurfaceVerified = staticVerificationSucceeded,
                            clientCallableVerified = false,
                            clientCallableState = "unknown",
                            expectedRefresh = false,
                            note = staticVerificationSucceeded
                                ? "The MCP host is already exposing the full enabled Lens tool surface. The host cannot prove the current client turn has indexed those tools as callable."
                                : "One or more expected tools were not present in the MCP host tool cache while static_all mode was active."
                        },
                        workaroundHint = staticVerificationSucceeded
                            ? "Call the real native tools directly when they are available; if the client tool table is stale, use Unity.Tools.Describe, Invoke-UnityMcpBatch, or helper scripts as the fallback."
                            : "Use Unity.Tools.Describe to inspect the missing tool names and confirm they are enabled in Lens."
                    }
                }, m_JsonOptions), isError: !staticVerificationSucceeded);
            }

            var manifestEnvelope = await m_BridgeClient!.SetToolPacksAsync(
                requestedPacks,
                includeSchemas: false,
                cancellationToken,
                reason: "activate_and_verify",
                toolSurfaceMode: s_ToolSurfaceMode).ConfigureAwait(false);
            if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            {
                return BuildToolCallResult(CreateErrorPayload(manifestEnvelope.Error ?? "Failed to activate Unity tool packs."), isError: true);
            }

            bool unchanged = string.Equals(manifestEnvelope.Result.Kind, "unchanged", StringComparison.OrdinalIgnoreCase);
            await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
            bool toolsListChangedNotificationSent = false;
            if (!unchanged && m_ClientInitialized)
            {
                await SendToolsListChangedNotificationAsync(cancellationToken).ConfigureAwait(false);
                toolsListChangedNotificationSent = true;
            }

            string[] hostToolNames = m_ToolCache.Keys
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] matchedExpectedTools = expectedTools
                .Where(expected => hostToolNames.Any(actual => ToolNamesMatch(actual, expected)))
                .ToArray();
            string[] missingExpectedTools = expectedTools
                .Where(expected => !hostToolNames.Any(actual => ToolNamesMatch(actual, expected)))
                .ToArray();
            bool verificationSucceeded = missingExpectedTools.Length == 0;

            return BuildToolCallResult(JsonSerializer.SerializeToElement(new
            {
                success = verificationSucceeded,
                message = verificationSucceeded
                    ? "Activated Unity MCP tool packs and verified expected host-visible tools."
                    : "Activated Unity MCP tool packs, but expected tools were missing from the host-visible surface.",
                data = new
                {
                    activeToolPacks = manifestEnvelope.Result.ActiveToolPacks,
                    toolSurfaceMode = s_ToolSurfaceMode,
                    manifestVersion = manifestEnvelope.Result.ManifestVersion,
                    bridgeSessionId = manifestEnvelope.Result.BridgeSessionId,
                    profileCatalogVersion = manifestEnvelope.Result.ProfileCatalogVersion,
                    unchanged,
                    manifestKind = manifestEnvelope.Result.Kind,
                    exportedToolCount = m_ToolCache.Count,
                    expectedTools,
                    matchedExpectedTools,
                    missingFromServerSurface = missingExpectedTools,
                    missingFromClient = missingExpectedTools,
                    toolsListChangedNotificationSent,
                    clientSurface = new
                    {
                        serverSurfaceVerified = verificationSucceeded,
                        clientCallableVerified = false,
                        clientCallableState = "unknown",
                        expectedRefresh = toolsListChangedNotificationSent,
                        note = verificationSucceeded
                            ? "Expected tools are present in the MCP host tool cache. The host cannot prove the current client turn has indexed those tools as callable; if direct calls are missing, classify that as client dynamic-indexing drift and use this tool or Invoke-UnityMcpBatch as the fallback."
                            : "One or more expected tools were not present in the MCP host tool cache after activation."
                    },
                    workaroundHint = verificationSucceeded
                        ? "If the MCP client callable list remains stale, keep using Unity.Tools.Describe, foundation fallbacks, or the batch helper and record dynamic-indexing drift."
                        : "Activate only packs that contain the missing tools, then rerun verification."
                }
            }, m_JsonOptions), isError: !verificationSucceeded);
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
            IsSameBridgeGeneration(m_BridgeConnection, currentDiscovery) &&
            (!IsStaticAllToolSurface || ActivePacksAreStaticAll(m_ActiveToolPacks)))
        {
            return;
        }

        string[] desiredActivePacks = IsStaticAllToolSurface
            ? GetDefaultActivePacksForSurfaceMode()
            : (m_ActiveToolPacks.Length > 0 ? m_ActiveToolPacks : GetDefaultActivePacksForSurfaceMode());
        if (m_BridgeClient != null)
            await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);

        BridgeDiscoveryResult? discoveryResult = currentDiscovery ?? FindCurrentBridge();
        if (discoveryResult == null)
            discoveryResult = await WaitForMatchingBridgeAfterReloadAsync(cancellationToken).ConfigureAwait(false);
        if (discoveryResult == null)
            throw CreateNoMatchingBridgeException();

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

    async Task<BridgeDiscoveryResult?> WaitForMatchingBridgeAfterReloadAsync(CancellationToken cancellationToken)
    {
        if (m_LastBridgeDiscoverySnapshot?.RequireProjectMatch != true)
            return null;

        DateTime deadlineUtc = DateTime.UtcNow.Add(s_BridgeDiscoveryReloadRetryWindow);
        while (DateTime.UtcNow < deadlineUtc)
        {
            await Task.Delay(s_BridgeDiscoveryReloadRetryPollInterval, cancellationToken).ConfigureAwait(false);
            BridgeDiscoveryResult? retryDiscovery = FindCurrentBridge();
            if (retryDiscovery != null)
                return retryDiscovery;

            if (m_LastBridgeDiscoverySnapshot?.RequireProjectMatch != true)
                return null;
        }

        return null;
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
            m_ActiveToolPacks = GetDefaultActivePacksForSurfaceMode();
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
        m_LastBridgeDiscoverySnapshot = BridgeDiscovery.FindBridgeSnapshot(projectPathHint, GetActiveQuarantineIds(), requireProjectMatch);
        BridgeDiscoveryResult? selected = m_LastBridgeDiscoverySnapshot.Selected;
        if (!string.IsNullOrWhiteSpace(m_SelectedProjectPathHint) &&
            m_SelectedProjectRequireFreshBridge &&
            selected?.IsFresh != true)
        {
            return null;
        }

        return selected;
    }

    string ResolveProjectPathHint(out bool requireProjectMatch)
    {
        if (!string.IsNullOrWhiteSpace(m_SelectedProjectPathHint))
        {
            requireProjectMatch = true;
            return m_SelectedProjectPathHint;
        }

        string? projectPath = Environment.GetEnvironmentVariable("UNITY_MCP_PROJECT_PATH");
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            requireProjectMatch = true;
            return NormalizeProjectPathHint(projectPath);
        }

        if (TryFindUnityProjectRoot(Directory.GetCurrentDirectory(), out string discoveredProjectRoot))
        {
            requireProjectMatch = true;
            return discoveredProjectRoot;
        }

        requireProjectMatch = false;
        return NormalizeProjectPathHint(Directory.GetCurrentDirectory());
    }

    static bool TryFindUnityProjectRoot(string startDirectory, out string projectRoot)
    {
        projectRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(startDirectory))
            return false;

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
        }
        catch
        {
            return false;
        }

        while (directory != null)
        {
            string assetsPath = Path.Combine(directory.FullName, "Assets");
            string projectSettingsPath = Path.Combine(directory.FullName, "ProjectSettings");
            string manifestPath = Path.Combine(directory.FullName, "Packages", "manifest.json");
            if (Directory.Exists(assetsPath) &&
                Directory.Exists(projectSettingsPath) &&
                File.Exists(manifestPath))
            {
                projectRoot = NormalizeProjectPathHint(directory.FullName);
                return true;
            }

            directory = directory.Parent;
        }

        return false;
    }

    static string NormalizeProjectPathHint(string path)
    {
        try
        {
            string normalized = Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(Path.GetFileName(normalized), "Assets", StringComparison.OrdinalIgnoreCase))
                return Path.GetFullPath(Path.GetDirectoryName(normalized) ?? normalized)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return normalized;
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    BridgeDiscoveryException CreateNoMatchingBridgeException()
    {
        BridgeDiscoverySnapshot? snapshot = m_LastBridgeDiscoverySnapshot;
        if (snapshot == null)
        {
            string projectPathHint = ResolveProjectPathHint(out bool requireProjectMatch);
            snapshot = BridgeDiscovery.FindBridgeSnapshot(projectPathHint, GetActiveQuarantineIds(), requireProjectMatch);
        }

        string message = DescribeBridgeDiscoveryFailure(snapshot);
        return new BridgeDiscoveryException(message, snapshot);
    }

    static string DescribeBridgeDiscoveryFailure(BridgeDiscoverySnapshot snapshot)
    {
        if (snapshot.RequireProjectMatch)
        {
            return $"No matching Unity MCP bridge was found for project '{snapshot.ProjectPathHint}'. " +
                $"Refusing to select a mismatched bridge; {snapshot.Candidates.Length} candidate status file(s) were inspected.";
        }

        return $"No fresh active Unity MCP bridge status file was found in '{snapshot.StatusDirectory}'. " +
            $"{snapshot.Candidates.Length} candidate status file(s) were inspected.";
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

        var restoreEnvelope = await m_BridgeClient.SetToolPacksAsync(
            desiredAdditionalPacks,
            includeSchemas: false,
            cancellationToken,
            reason: IsStaticAllToolSurface ? "static_all_restore" : "dynamic_pack_restore",
            toolSurfaceMode: s_ToolSurfaceMode).ConfigureAwait(false);
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

    JsonElement CreateStaticAllSetToolPacksNoopPayload(JsonElement argumentsElement)
    {
        string[] requestedPacks = ExtractPacks(argumentsElement);
        return JsonSerializer.SerializeToElement(new
        {
            success = true,
            message = "Unity.SetToolPacks is a host-local compatibility no-op in static_all tool surface mode.",
            data = new
            {
                toolSurfaceMode = s_ToolSurfaceMode,
                activeToolPacks = GetDefaultActivePacksForSurfaceMode(),
                requestedPacks,
                unchanged = true,
                toolsListChangedNotificationSent = false,
                clientSurface = new
                {
                    expectedRefresh = false
                },
                bridgeTouched = false
            }
        }, m_JsonOptions);
    }

    JsonElement CreateBridgeListConnectionsPayload(JsonElement argumentsElement)
    {
        string? explicitProjectPath = ExtractString(argumentsElement, "projectPath", "ProjectPath");
        string projectPathHint;
        bool requireProjectMatch;
        if (!string.IsNullOrWhiteSpace(explicitProjectPath))
        {
            projectPathHint = NormalizeProjectPathHint(explicitProjectPath);
            requireProjectMatch = true;
        }
        else
        {
            projectPathHint = ResolveProjectPathHint(out requireProjectMatch);
        }

        bool includeStale = ExtractBool(argumentsElement, true, "includeStale", "IncludeStale");
        int maxEntries = Math.Clamp(ExtractInt(argumentsElement, 12, "maxEntries", "MaxEntries"), 1, 100);
        BridgeDiscoverySnapshot snapshot = BridgeDiscovery.FindBridgeSnapshot(projectPathHint, GetActiveQuarantineIds(), requireProjectMatch);
        m_LastBridgeDiscoverySnapshot = snapshot;

        BridgeDiscoveryCandidate[] visibleCandidates = (includeStale
                ? snapshot.Candidates
                : snapshot.Candidates.Where(candidate => candidate.IsSelectable).ToArray())
            .Take(maxEntries)
            .ToArray();

        return JsonSerializer.SerializeToElement(new
        {
            success = true,
            message = snapshot.Selected == null
                ? "No matching Unity MCP bridge connection was selected."
                : "Unity MCP bridge connection candidates listed.",
            data = new
            {
                projectPathHint = snapshot.ProjectPathHint,
                requireProjectMatch = snapshot.RequireProjectMatch,
                statusDirectory = snapshot.StatusDirectory,
                selected = snapshot.Selected == null ? null : CreateBridgeDiscoveryResultDiagnostics(snapshot.Selected),
                candidateCount = snapshot.Candidates.Length,
                editorHealthCandidateCount = snapshot.EditorHealthCandidates.Length,
                unmatchedEditorHealthCandidateCount = snapshot.UnmatchedEditorHealthCandidates.Length,
                returnedCandidateCount = visibleCandidates.Length,
                includeStale,
                candidates = visibleCandidates.Select(CreateBridgeCandidateDiagnostics).ToArray(),
                unmatchedEditorHealthCandidates = snapshot.UnmatchedEditorHealthCandidates
                    .Take(maxEntries)
                    .Select(CreateEditorHealthDiagnostics)
                    .ToArray()
            }
        }, m_JsonOptions);
    }

    async Task<JsonElement> CreateSelectProjectPayloadAsync(JsonElement argumentsElement, CancellationToken cancellationToken)
    {
        string? explicitProjectPath = ExtractString(argumentsElement, "projectPath", "ProjectPath");
        if (string.IsNullOrWhiteSpace(explicitProjectPath))
            return CreateErrorPayload("projectPath is required.", "UNITY_MCP_PROJECT_PATH_REQUIRED");

        string projectPathHint = NormalizeProjectPathHint(explicitProjectPath);
        bool requireFreshBridge = ExtractBool(argumentsElement, true, "requireFreshBridge", "RequireFreshBridge");
        bool connect = ExtractBool(argumentsElement, true, "connect", "Connect");
        int maxCandidates = Math.Clamp(ExtractInt(argumentsElement, 12, "maxCandidates", "MaxCandidates"), 1, 100);

        BridgeDiscoverySnapshot snapshot = BridgeDiscovery.FindBridgeSnapshot(projectPathHint, GetActiveQuarantineIds(), requireProjectMatch: true);
        m_LastBridgeDiscoverySnapshot = snapshot;

        BridgeDiscoveryResult? selected = snapshot.Selected;
        bool freshEnough = selected != null && (!requireFreshBridge || selected.IsFresh);
        if (!freshEnough)
        {
            return CreateErrorPayload(
                selected == null
                    ? $"No matching Unity MCP bridge was found for project '{projectPathHint}'."
                    : $"A matching Unity MCP bridge was found for project '{projectPathHint}', but it is not fresh.",
                "UNITY_MCP_NO_MATCHING_BRIDGE",
                new
                {
                    requestedProjectPath = projectPathHint,
                    requireFreshBridge,
                    discovery = BuildBridgeDiscoveryDiagnostics(snapshot, maxCandidates)
                });
        }

        string? previousSelectedProjectPath = m_SelectedProjectPathHint;
        bool connectionChanged = m_BridgeConnection != null &&
            !IsSameBridgeGeneration(m_BridgeConnection, selected!);

        m_SelectedProjectPathHint = projectPathHint;
        m_SelectedProjectRequireFreshBridge = requireFreshBridge;

        if (connectionChanged)
            await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);

        string? connectError = null;
        if (connect)
        {
            try
            {
                await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                connectError = ex.Message;
            }
        }

        bool connected = m_BridgeConnection != null &&
            string.Equals(m_BridgeConnection.ProjectRoot, selected!.ProjectRoot, StringComparison.OrdinalIgnoreCase);

        return JsonSerializer.SerializeToElement(new
        {
            success = connectError == null,
            message = connectError == null
                ? "Selected Unity MCP project for this host session."
                : "Selected Unity MCP project, but connecting to the bridge failed.",
            data = new
            {
                selectedProjectPath = m_SelectedProjectPathHint,
                previousSelectedProjectPath,
                requireFreshBridge = m_SelectedProjectRequireFreshBridge,
                connectRequested = connect,
                connected,
                connectError,
                activeToolPacks = m_ActiveToolPacks,
                toolSurfaceMode = s_ToolSurfaceMode,
                exportedToolCount = m_ToolCache.Count,
                host = CreateHostDiagnostics(),
                selectedBridge = CreateBridgeDiscoveryResultDiagnostics(selected!),
                discovery = BuildBridgeDiscoveryDiagnostics(snapshot, maxCandidates)
            }
        }, m_JsonOptions);
    }

    async Task<JsonElement> CreateSyncScriptsReadyPayloadAsync(JsonElement argumentsElement, CancellationToken cancellationToken)
    {
        int timeoutMs = ExtractInt(argumentsElement, 0, "timeoutMs", "TimeoutMs");
        if (timeoutMs <= 0)
            timeoutMs = Math.Max(1, ExtractInt(argumentsElement, 120, "timeoutSeconds", "TimeoutSeconds")) * 1000;
        timeoutMs = Math.Clamp(timeoutMs, 1000, 600000);

        int pollIntervalMs = Math.Clamp(ExtractInt(argumentsElement, 250, "pollIntervalMs", "PollIntervalMs"), 50, 5000);
        int stablePollCount = Math.Max(1, ExtractInt(argumentsElement, 2, "stablePollCount", "StablePollCount"));
        int postStableDelayMs = Math.Clamp(ExtractInt(argumentsElement, 500, "postStableDelayMs", "PostStableDelayMs"), 0, 10000);
        bool waitForCompile = ExtractBool(argumentsElement, true, "waitForCompile", "WaitForCompile");
        bool captureConsoleDelta = ExtractBool(argumentsElement, true, "captureConsoleDelta", "CaptureConsoleDelta");

        DateTime startedUtc = DateTime.UtcNow;
        DateTime deadlineUtc = startedUtc.AddMilliseconds(timeoutMs);
        string[] startingActivePacks = m_ActiveToolPacks.ToArray();
        object? packActivation = null;
        JsonElement syncRequest = default;
        HostSyncReadyResult? ready = null;
        bool hostWaitAttempted = false;

        try
        {
            packActivation = await EnsureScriptSyncPacksActiveAsync(cancellationToken).ConfigureAwait(false);
            syncRequest = await CallBridgeToolResultAsync(
                "Unity.Editor.SyncScripts",
                argumentsElement,
                cancellationToken).ConfigureAwait(false);

            bool hasNativeData = TryGetNestedProperty(syncRequest, out var nativeData, "data");
            bool nativeSuccess = GetJsonBool(syncRequest, false, "success");
            string? nativeStatus = GetJsonString(nativeData, "status");
            bool nativeReadyForFollowUp = GetJsonBool(nativeData, false, "readyForFollowUp");
            bool nativeRefreshScheduled = GetJsonBool(nativeData, false, "refreshScheduledAfterResponse");
            bool nativeRefused = GetJsonBool(nativeData, false, "refused");
            bool nativeTimedOut = GetJsonBool(nativeData, false, "timedOut");
            bool nativeNewConsoleErrorsDetected = GetJsonBool(nativeData, false, "newConsoleErrorsDetected", "consoleErrorsDetected");
            int initialConsoleErrorCount = GetJsonInt(nativeData, 0, "initialConsoleErrorCount");
            int nativeFinalConsoleErrorCount = GetJsonInt(
                nativeData,
                initialConsoleErrorCount,
                "finalConsoleErrorCount",
                "consoleErrorCount");
            int nativeNewConsoleErrorCount = GetJsonInt(
                nativeData,
                Math.Max(0, nativeFinalConsoleErrorCount - initialConsoleErrorCount),
                "newConsoleErrorCount");

            bool nativeBusyButWaitable =
                string.Equals(nativeStatus, "busy", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(nativeStatus, "pending_refresh", StringComparison.OrdinalIgnoreCase);
            bool shouldWaitForReady = waitForCompile &&
                hasNativeData &&
                !nativeRefused &&
                !nativeTimedOut &&
                !nativeNewConsoleErrorsDetected &&
                (nativeRefreshScheduled || (!nativeReadyForFollowUp && nativeBusyButWaitable));

            if (shouldWaitForReady)
            {
                hostWaitAttempted = true;
                ready = await WaitForScriptSyncReadyFromHostAsync(
                    deadlineUtc,
                    pollIntervalMs,
                    stablePollCount,
                    postStableDelayMs,
                    initialConsoleErrorCount,
                    nativeFinalConsoleErrorCount,
                    captureConsoleDelta,
                    cancellationToken).ConfigureAwait(false);
            }

            bool finalReadyForFollowUp = hostWaitAttempted
                ? ready?.Success == true
                : nativeSuccess && nativeReadyForFollowUp && !nativeNewConsoleErrorsDetected;
            bool finalTimedOut = hostWaitAttempted ? ready?.TimedOut == true : nativeTimedOut;
            bool consoleCheckSucceeded = !hostWaitAttempted || ready?.ConsoleCheckSucceeded == true;
            int finalConsoleErrorCount = hostWaitAttempted
                ? ready?.FinalConsoleErrorCount ?? nativeFinalConsoleErrorCount
                : nativeFinalConsoleErrorCount;
            int newConsoleErrorCount = hostWaitAttempted
                ? ready?.NewConsoleErrorCount ?? nativeNewConsoleErrorCount
                : nativeNewConsoleErrorCount;
            bool newConsoleErrorsDetected = newConsoleErrorCount > 0;
            bool staleConsoleErrorsPresent = finalConsoleErrorCount > 0 && !newConsoleErrorsDetected;
            string finalStatus = finalReadyForFollowUp
                ? "ready"
                : !consoleCheckSucceeded
                    ? "console_check_failed"
                    : newConsoleErrorsDetected
                        ? "console_errors"
                        : finalTimedOut
                            ? "timed_out"
                            : nativeRefused
                                ? "refused"
                                : nativeStatus ?? "failed";
            int elapsedMs = (int)Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds);
            object[] nativeWarnings = CloneJsonArray(nativeData, "warnings") ?? [];
            var warnings = new List<object>(nativeWarnings);
            if (hostWaitAttempted && ready?.ConsoleCheckSucceeded == false)
            {
                warnings.Add(new
                {
                    kind = "post_refresh_console_check_failed",
                    message = "The editor became idle after script refresh, but Lens could not read a post-refresh console summary."
                });
            }

            return JsonSerializer.SerializeToElement(new
            {
                success = finalReadyForFollowUp,
                message = finalReadyForFollowUp
                    ? "Unity script sync completed and the editor is ready for follow-up Unity actions."
                    : "Unity script sync did not reach a follow-up-ready state.",
                data = new
                {
                    status = finalStatus,
                    readyForFollowUp = finalReadyForFollowUp,
                    noChangesDetected = GetJsonBool(nativeData, false, "noChangesDetected"),
                    changedPaths = CloneJsonProperty(nativeData, "changedPaths"),
                    relevantChangedPaths = CloneJsonProperty(nativeData, "relevantChangedPaths"),
                    force = GetJsonBool(nativeData, false, "force"),
                    waitForCompile,
                    refreshRequested = GetJsonBool(nativeData, false, "refreshRequested"),
                    refreshScheduledAfterResponse = nativeRefreshScheduled && !hostWaitAttempted,
                    refreshWasScheduledAfterResponse = nativeRefreshScheduled,
                    hostWaitAttempted,
                    hostWaitCompleted = hostWaitAttempted && ready?.EditorIdle == true,
                    compileStarted = GetJsonBool(nativeData, false, "compileStarted"),
                    compileObserved = GetJsonBool(nativeData, false, "compileObserved"),
                    editorIdle = hostWaitAttempted ? ready?.EditorIdle == true : GetJsonBool(nativeData, false, "editorIdle"),
                    timedOut = finalTimedOut,
                    initialConsoleErrorCount,
                    finalConsoleErrorCount,
                    consoleErrorCount = finalConsoleErrorCount,
                    newConsoleErrorCount,
                    newConsoleErrorsDetected,
                    staleConsoleErrorsPresent,
                    consoleErrorsDetected = newConsoleErrorsDetected,
                    consoleCheckSucceeded,
                    elapsedMs,
                    timeoutMs,
                    pollIntervalMs,
                    stablePollCount,
                    postStableDelayMs,
                    captureConsoleDelta,
                    warningCount = warnings.Count,
                    warnings = warnings.ToArray(),
                    finalState = hostWaitAttempted ? ready?.LastState : CloneJsonProperty(nativeData, "finalState"),
                    postRefreshConsole = hostWaitAttempted ? ready?.FinalConsole : null,
                    pollAttemptCount = (hostWaitAttempted ? ready?.Attempts.Count ?? 0 : 0) +
                        GetJsonInt(nativeData, 0, "pollAttemptCount"),
                    hostReadyWait = hostWaitAttempted
                        ? new
                        {
                            ready?.Success,
                            ready?.Message,
                            ready?.EditorIdle,
                            ready?.TimedOut,
                            ready?.ConsoleCheckSucceeded,
                            ready?.FinalConsoleErrorCount,
                            ready?.NewConsoleErrorCount,
                            attemptCount = ready?.Attempts.Count ?? 0,
                            ready?.Attempts,
                            ready?.LastState,
                            ready?.LastError
                        }
                        : null,
                    nativeStatus,
                    nativeReadyForFollowUp,
                    nativeRefreshScheduledAfterResponse = nativeRefreshScheduled,
                    nativeSyncRequest = syncRequest,
                    packActivation,
                    startingActivePacks,
                    activeToolPacks = m_ActiveToolPacks,
                    host = CreateHostDiagnostics()
                }
            }, m_JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorPayload(
                $"Unity script sync readiness workflow failed: {ex.Message}",
                "UNITY_MCP_SYNC_SCRIPTS_READY_FAILED",
                new
                {
                    exceptionType = ex.GetType().Name,
                    timeoutMs,
                    pollIntervalMs,
                    stablePollCount,
                    postStableDelayMs,
                    waitForCompile,
                    captureConsoleDelta,
                    syncRequest,
                    packActivation,
                    startingActivePacks,
                    activeToolPacks = m_ActiveToolPacks,
                    host = CreateHostDiagnostics()
                });
        }
    }

    async Task<object> EnsureScriptSyncPacksActiveAsync(CancellationToken cancellationToken)
    {
        string[] before = m_ActiveToolPacks.ToArray();
        if (IsStaticAllToolSurface)
        {
            return new
            {
                changed = false,
                reason = "static_all_surface",
                before,
                activeToolPacks = m_ActiveToolPacks,
                toolsListChangedNotificationSent = false
            };
        }

        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        string[] activeAdditional = NormalizeAdditionalToolPacks(m_ActiveToolPacks);
        bool scriptingActive = activeAdditional.Any(pack => string.Equals(pack, "scripting", StringComparison.OrdinalIgnoreCase));
        bool consoleActive = activeAdditional.Any(pack => string.Equals(pack, "console", StringComparison.OrdinalIgnoreCase));
        if (scriptingActive && consoleActive)
        {
            return new
            {
                changed = false,
                reason = "scripting_and_console_already_active",
                before,
                activeToolPacks = m_ActiveToolPacks,
                toolsListChangedNotificationSent = false
            };
        }

        string[] desired = ["scripting", "console"];
        var manifestEnvelope = await m_BridgeClient!.SetToolPacksAsync(
            desired,
            includeSchemas: false,
            cancellationToken,
            reason: "script_sync_ready",
            toolSurfaceMode: s_ToolSurfaceMode).ConfigureAwait(false);
        if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            throw new InvalidOperationException(manifestEnvelope.Error ?? "Unity bridge did not activate the scripting and console tool packs.");

        bool unchanged = string.Equals(manifestEnvelope.Result.Kind, "unchanged", StringComparison.OrdinalIgnoreCase);
        await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
        bool toolsListChangedNotificationSent = false;
        if (!unchanged && m_ClientInitialized)
        {
            await SendToolsListChangedNotificationAsync(cancellationToken).ConfigureAwait(false);
            toolsListChangedNotificationSent = true;
        }

        return new
        {
            changed = !unchanged,
            reason = "scripting_console_packs_activated",
            before,
            requestedAdditionalPacks = desired,
            activeToolPacks = m_ActiveToolPacks,
            toolsListChangedNotificationSent
        };
    }

    async Task<JsonElement> CreatePlayModeEnterReadyPayloadAsync(JsonElement argumentsElement, CancellationToken cancellationToken)
    {
        int timeoutMs = ExtractInt(argumentsElement, 0, "timeoutMs", "TimeoutMs");
        if (timeoutMs <= 0)
            timeoutMs = Math.Max(1, ExtractInt(argumentsElement, 30, "timeoutSeconds", "TimeoutSeconds")) * 1000;
        timeoutMs = Math.Clamp(timeoutMs, 1000, 600000);

        int pollIntervalMs = Math.Clamp(ExtractInt(argumentsElement, 500, "pollIntervalMs", "PollIntervalMs"), 100, 5000);
        int warmupFrames = Math.Max(0, ExtractInt(argumentsElement, 0, "warmupFrames", "WarmupFrames"));
        double warmupSeconds = Math.Max(0d, ExtractDouble(argumentsElement, 1.0d, "warmupSeconds", "WarmupSeconds"));
        if (warmupFrames > 0)
            warmupSeconds = Math.Max(warmupSeconds, warmupFrames / 60.0d);

        bool stopFirst = ExtractBool(argumentsElement, false, "stopFirst", "StopFirst");
        bool clearPause = ExtractBool(argumentsElement, true, "clearPause", "ClearPause", "unpauseBeforeExit", "UnpauseBeforeExit");
        bool captureConsoleDelta = ExtractBool(argumentsElement, true, "captureConsoleDelta", "CaptureConsoleDelta");
        string? scenePath = ExtractString(argumentsElement, "scenePath", "ScenePath");

        DateTime startedUtc = DateTime.UtcNow;
        DateTime deadlineUtc = startedUtc.AddMilliseconds(timeoutMs);
        string[] startingActivePacks = m_ActiveToolPacks.ToArray();
        object? runtimePackActivation = null;
        object? sceneLoad = null;
        object? stopResult = null;
        object? preConsole = null;
        object? postConsole = null;
        object? playRequest = null;
        string? playRequestError = null;
        bool requestAccepted = false;
        bool reconnectExpected = false;

        try
        {
            runtimePackActivation = await EnsureRuntimePackActiveForEnterReadyAsync(
                includeScenePack: !string.IsNullOrWhiteSpace(scenePath),
                cancellationToken).ConfigureAwait(false);

            if (captureConsoleDelta)
                preConsole = await TryReadConsoleErrorSummaryAsync(cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                sceneLoad = await LoadSceneForEnterReadyAsync(scenePath!, cancellationToken).ConfigureAwait(false);
                if (sceneLoad is JsonElement sceneLoadElement && IsToolLevelError(sceneLoadElement))
                {
                    return CreateErrorPayload(
                        $"Could not load scene '{scenePath}' before entering play mode.",
                        "UNITY_MCP_PLAY_MODE_SCENE_LOAD_FAILED",
                        new
                        {
                            scenePath,
                            sceneLoad,
                            startingActivePacks,
                            activeToolPacks = m_ActiveToolPacks
                        });
                }
            }

            if (stopFirst)
            {
                stopResult = await CallBridgeToolResultAsync(
                    "Unity.Editor.SetPlayMode",
                    new
                    {
                        mode = "exit",
                        timeoutSeconds = Math.Max(1, timeoutMs / 1000),
                        waitForRuntimeAdvance = false,
                        unpauseBeforeExit = clearPause
                    },
                    cancellationToken).ConfigureAwait(false);
            }

            try
            {
                JsonElement playRequestElement = await CallBridgeToolResultAsync(
                    "Unity.Editor.SetPlayMode",
                    new
                    {
                        mode = "enter",
                        stopFirst,
                        waitForRuntimeAdvance = true,
                        warmupSeconds,
                        timeoutSeconds = Math.Max(1, (int)Math.Ceiling(timeoutMs / 1000.0d)),
                        unpauseBeforeExit = clearPause
                    },
                    cancellationToken).ConfigureAwait(false);

                playRequest = playRequestElement.Clone();
                string playRequestJson = playRequestElement.GetRawText();
                requestAccepted = playRequestJson.IndexOf("\"requested\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    playRequestJson.IndexOf("\"transitionState\":\"already_playing\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    playRequestJson.IndexOf("\"transitionState\":\"entered_play_mode\"", StringComparison.OrdinalIgnoreCase) >= 0;
                reconnectExpected = playRequestJson.IndexOf("\"reconnectExpected\":true", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    playRequestJson.IndexOf("\"enter_requested_after_response\"", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch (BridgeTransportException ex)
            {
                playRequestError = ex.Message;
                requestAccepted = ex.RequestSent;
                reconnectExpected = ex.RequestSent;
                await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);
            }

            HostPlayReadyResult ready = await WaitForPlayModeReadyFromHostAsync(
                deadlineUtc,
                pollIntervalMs,
                warmupSeconds,
                cancellationToken).ConfigureAwait(false);

            if (captureConsoleDelta)
                postConsole = await TryReadConsoleErrorSummaryAsync(cancellationToken).ConfigureAwait(false);

            int? preConsoleErrors = ExtractConsoleErrorCount(preConsole);
            int? postConsoleErrors = ExtractConsoleErrorCount(postConsole);
            int? consoleErrorDelta = preConsoleErrors.HasValue && postConsoleErrors.HasValue
                ? Math.Max(0, postConsoleErrors.Value - preConsoleErrors.Value)
                : null;
            int elapsedMs = (int)Math.Round((DateTime.UtcNow - startedUtc).TotalMilliseconds);

            return JsonSerializer.SerializeToElement(new
            {
                success = ready.Success,
                message = ready.Success
                    ? "Play mode entered and runtime is ready for runtime tools."
                    : "Play mode did not become ready for runtime tools before timeout.",
                data = new
                {
                    requestAccepted,
                    editorStable = ready.EditorIdle,
                    isPlaying = ready.IsPlaying,
                    runtimeAdvanced = ready.RuntimeAdvanced,
                    readyForRuntimeTools = ready.Success,
                    activeScene = ready.ActiveScene,
                    frameCounts = new
                    {
                        update = ready.UpdateCount,
                        fixedUpdate = ready.FixedUpdateCount,
                        unscaledTime = ready.UnscaledTime
                    },
                    timeoutMs,
                    pollIntervalMs,
                    warmupSeconds,
                    warmupFrames,
                    elapsedMs,
                    stopFirst,
                    clearPause,
                    captureConsoleDelta,
                    reconnectExpected,
                    playRequestWasReconnectProne = reconnectExpected,
                    playRequestError,
                    playRequest,
                    stopResult,
                    scenePath,
                    sceneLoad,
                    runtimePackActivation,
                    startingActivePacks,
                    activeToolPacks = m_ActiveToolPacks,
                    consoleDelta = captureConsoleDelta
                        ? new
                        {
                            beforeErrors = preConsoleErrors,
                            afterErrors = postConsoleErrors,
                            errorDelta = consoleErrorDelta,
                            before = preConsole,
                            after = postConsole
                        }
                        : null,
                    attemptCount = ready.Attempts.Count,
                    attempts = ready.Attempts,
                    finalState = ready.LastState,
                    lastError = ready.LastError,
                    host = CreateHostDiagnostics()
                }
            }, m_JsonOptions);
        }
        catch (Exception ex)
        {
            return CreateErrorPayload(
                $"Unity play-mode readiness workflow failed: {ex.Message}",
                "UNITY_MCP_PLAY_MODE_ENTER_READY_FAILED",
                new
                {
                    exceptionType = ex.GetType().Name,
                    timeoutMs,
                    pollIntervalMs,
                    warmupSeconds,
                    warmupFrames,
                    stopFirst,
                    clearPause,
                    scenePath,
                    requestAccepted,
                    reconnectExpected,
                    playRequestError,
                    playRequest,
                    stopResult,
                    sceneLoad,
                    runtimePackActivation,
                    startingActivePacks,
                    activeToolPacks = m_ActiveToolPacks,
                    host = CreateHostDiagnostics()
                });
        }
    }

    async Task<object> EnsureRuntimePackActiveForEnterReadyAsync(bool includeScenePack, CancellationToken cancellationToken)
    {
        string[] before = m_ActiveToolPacks.ToArray();
        if (IsStaticAllToolSurface)
        {
            return new
            {
                changed = false,
                reason = "static_all_surface",
                before,
                activeToolPacks = m_ActiveToolPacks,
                toolsListChangedNotificationSent = false
            };
        }

        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        string[] activeAdditional = NormalizeAdditionalToolPacks(m_ActiveToolPacks);
        bool runtimeActive = activeAdditional.Any(pack => string.Equals(pack, "runtime", StringComparison.OrdinalIgnoreCase));
        bool sceneActive = activeAdditional.Any(pack => string.Equals(pack, "scene", StringComparison.OrdinalIgnoreCase));
        if (runtimeActive && (!includeScenePack || sceneActive))
        {
            return new
            {
                changed = false,
                reason = includeScenePack ? "runtime_and_scene_already_active" : "runtime_already_active",
                before,
                activeToolPacks = m_ActiveToolPacks,
                toolsListChangedNotificationSent = false
            };
        }

        var desired = new List<string> { "runtime" };
        if (includeScenePack)
            desired.Add("scene");
        foreach (string pack in activeAdditional)
        {
            if (desired.Count >= 2)
                break;
            if (!desired.Contains(pack, StringComparer.OrdinalIgnoreCase))
                desired.Add(pack);
        }

        var manifestEnvelope = await m_BridgeClient!.SetToolPacksAsync(
            desired.ToArray(),
            includeSchemas: false,
            cancellationToken,
            reason: "play_mode_enter_ready",
            toolSurfaceMode: s_ToolSurfaceMode).ConfigureAwait(false);
        if (!string.Equals(manifestEnvelope.Status, "success", StringComparison.OrdinalIgnoreCase) || manifestEnvelope.Result == null)
            throw new InvalidOperationException(manifestEnvelope.Error ?? "Unity bridge did not activate the runtime tool pack.");

        bool unchanged = string.Equals(manifestEnvelope.Result.Kind, "unchanged", StringComparison.OrdinalIgnoreCase);
        await ApplyManifestAsync(manifestEnvelope.Result, shouldFetchSchemas: true, cancellationToken).ConfigureAwait(false);
        bool toolsListChangedNotificationSent = false;
        if (!unchanged && m_ClientInitialized)
        {
            await SendToolsListChangedNotificationAsync(cancellationToken).ConfigureAwait(false);
            toolsListChangedNotificationSent = true;
        }

        return new
        {
            changed = !unchanged,
            reason = "runtime_pack_activated",
            before,
            requestedAdditionalPacks = desired.ToArray(),
            activeToolPacks = m_ActiveToolPacks,
            toolsListChangedNotificationSent
        };
    }

    async Task<JsonElement> LoadSceneForEnterReadyAsync(string scenePath, CancellationToken cancellationToken)
    {
        string normalizedScenePath = scenePath.Replace('\\', '/').Trim();
        if (normalizedScenePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            normalizedScenePath = normalizedScenePath["Assets/".Length..];
        normalizedScenePath = normalizedScenePath.TrimStart('/');

        if (!normalizedScenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
        {
            return CreateErrorPayload(
                "scenePath must point to a .unity scene under Assets.",
                "UNITY_MCP_INVALID_SCENE_PATH",
                new { scenePath });
        }

        string sceneName = Path.GetFileNameWithoutExtension(normalizedScenePath);
        string? directory = Path.GetDirectoryName(normalizedScenePath)?.Replace('\\', '/');
        return await CallBridgeToolResultAsync(
            "Unity.ManageScene",
            new
            {
                action = "Load",
                name = sceneName,
                path = string.IsNullOrWhiteSpace(directory) ? string.Empty : directory
            },
            cancellationToken).ConfigureAwait(false);
    }

    async Task<JsonElement> CallBridgeToolResultAsync(string toolName, JsonElement argumentsElement, CancellationToken cancellationToken)
    {
        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        var envelope = await m_BridgeClient!.CallToolAsync(toolName, argumentsElement, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(envelope.Status, "success", StringComparison.OrdinalIgnoreCase))
            return CreateErrorPayload(envelope.Error ?? $"Tool '{toolName}' failed.");

        return envelope.Result.Clone();
    }

    async Task<JsonElement> CallBridgeToolResultAsync(string toolName, object arguments, CancellationToken cancellationToken)
    {
        await EnsureBridgeReadyAsync(cancellationToken).ConfigureAwait(false);
        JsonElement argumentElement = JsonSerializer.SerializeToElement(arguments, m_JsonOptions);
        return await CallBridgeToolResultAsync(toolName, argumentElement, cancellationToken).ConfigureAwait(false);
    }

    async Task<object> TryReadConsoleErrorSummaryAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await CallBridgeToolResultAsync(
                "Unity.ReadConsole",
                new
                {
                    action = "Get",
                    types = new[] { "Error" },
                    count = 100,
                    format = "Summary",
                    excludeMcpNoise = true,
                    includeStacktrace = false
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new
            {
                success = false,
                error = ex.Message,
                exceptionType = ex.GetType().Name
            };
        }
    }

    async Task<HostSyncReadyResult> WaitForScriptSyncReadyFromHostAsync(
        DateTime deadlineUtc,
        int pollIntervalMs,
        int stablePollCount,
        int postStableDelayMs,
        int initialConsoleErrorCount,
        int fallbackFinalConsoleErrorCount,
        bool captureConsoleDelta,
        CancellationToken cancellationToken)
    {
        var attempts = new List<object>();
        object? lastState = null;
        string? lastError = null;

        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                int remainingMs = (int)Math.Max(1000d, Math.Min(30000d, (deadlineUtc - DateTime.UtcNow).TotalMilliseconds));
                JsonElement state = await CallBridgeToolResultAsync(
                    "Unity.ManageEditor",
                    new
                    {
                        action = "WaitForStableEditor",
                        timeoutMs = remainingMs,
                        pollIntervalMs,
                        stablePollCount,
                        postStableDelayMs
                    },
                    cancellationToken).ConfigureAwait(false);

                object attempt = CreateSyncReadyAttemptFromWait(state);
                attempts.Add(attempt);
                lastState = attempt;

                JsonElement attemptElement = JsonSerializer.SerializeToElement(attempt, m_JsonOptions);
                bool editorIdle = GetJsonBool(attemptElement, false, "editorIdle");
                bool timedOut = GetJsonBool(attemptElement, false, "timedOut");
                if (editorIdle)
                {
                    object? finalConsole = null;
                    int finalConsoleErrorCount = fallbackFinalConsoleErrorCount;
                    bool consoleCheckSucceeded = true;
                    if (captureConsoleDelta)
                    {
                        finalConsole = await TryReadConsoleErrorSummaryAsync(cancellationToken).ConfigureAwait(false);
                        int? extractedFinalConsoleErrorCount = ExtractConsoleErrorCount(finalConsole);
                        JsonElement finalConsoleElement = JsonSerializer.SerializeToElement(finalConsole, m_JsonOptions);
                        consoleCheckSucceeded = GetJsonBool(finalConsoleElement, false, "success") &&
                            extractedFinalConsoleErrorCount.HasValue;
                        if (extractedFinalConsoleErrorCount.HasValue)
                            finalConsoleErrorCount = extractedFinalConsoleErrorCount.Value;
                    }

                    int newConsoleErrorCount = Math.Max(0, finalConsoleErrorCount - initialConsoleErrorCount);
                    bool success = consoleCheckSucceeded && newConsoleErrorCount == 0;
                    return new HostSyncReadyResult
                    {
                        Success = success,
                        Message = success
                            ? "Editor is idle and no new console errors were detected after script sync."
                            : consoleCheckSucceeded
                                ? "Editor is idle, but new console errors were detected after script sync."
                                : "Editor is idle, but the post-refresh console check failed.",
                        EditorIdle = true,
                        TimedOut = false,
                        ConsoleCheckSucceeded = consoleCheckSucceeded,
                        FinalConsoleErrorCount = finalConsoleErrorCount,
                        NewConsoleErrorCount = newConsoleErrorCount,
                        FinalConsole = finalConsole,
                        Attempts = attempts,
                        LastState = lastState,
                        LastError = lastError
                    };
                }

                if (timedOut)
                {
                    return new HostSyncReadyResult
                    {
                        Success = false,
                        Message = "Editor did not become idle after script sync before timeout.",
                        EditorIdle = false,
                        TimedOut = true,
                        ConsoleCheckSucceeded = !captureConsoleDelta,
                        FinalConsoleErrorCount = fallbackFinalConsoleErrorCount,
                        NewConsoleErrorCount = Math.Max(0, fallbackFinalConsoleErrorCount - initialConsoleErrorCount),
                        Attempts = attempts,
                        LastState = lastState,
                        LastError = lastError
                    };
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                attempts.Add(new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    success = false,
                    error = ex.Message,
                    exceptionType = ex.GetType().Name
                });

                if (IsBridgeTransportFailure(ex))
                    await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);
            }

            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            int delayMs = (int)Math.Min(pollIntervalMs, Math.Max(1, remaining.TotalMilliseconds));
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return new HostSyncReadyResult
        {
            Success = false,
            Message = "Editor did not become idle after script sync before timeout.",
            EditorIdle = false,
            TimedOut = true,
            ConsoleCheckSucceeded = !captureConsoleDelta,
            FinalConsoleErrorCount = fallbackFinalConsoleErrorCount,
            NewConsoleErrorCount = Math.Max(0, fallbackFinalConsoleErrorCount - initialConsoleErrorCount),
            Attempts = attempts,
            LastState = lastState,
            LastError = lastError
        };
    }

    object CreateSyncReadyAttemptFromWait(JsonElement result)
    {
        JsonElement data = TryGetNestedProperty(result, out var dataElement, "data") ? dataElement : default;
        JsonElement editorState = TryGetNestedProperty(data, out var editorStateElement, "EditorState") ? editorStateElement :
            TryGetNestedProperty(data, out editorStateElement, "editorState") ? editorStateElement :
            default;
        bool success = GetJsonBool(result, false, "success");
        bool isStable = GetJsonBool(data, false, "IsStable", "isStable");
        bool timedOut = GetJsonBool(data, false, "TimedOut", "timedOut");

        return new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            success,
            editorIdle = success && isStable,
            isStable,
            timedOut,
            waitedMilliseconds = GetJsonInt(data, 0, "WaitedMilliseconds", "waitedMilliseconds"),
            stablePollCountReached = GetJsonInt(data, 0, "StablePollCountReached", "stablePollCountReached"),
            attemptCount = GetJsonInt(data, 0, "AttemptCount", "attemptCount"),
            stableAttemptCount = GetJsonInt(data, 0, "StableAttemptCount", "stableAttemptCount"),
            blockingReasons = CloneJsonProperty(data, "BlockingReasons", "blockingReasons"),
            editorState = editorState.ValueKind == JsonValueKind.Undefined ? null : (object)editorState.Clone(),
            errorKind = GetJsonString(data, "errorKind")
        };
    }

    async Task<HostPlayReadyResult> WaitForPlayModeReadyFromHostAsync(
        DateTime deadlineUtc,
        int pollIntervalMs,
        double warmupSeconds,
        CancellationToken cancellationToken)
    {
        var attempts = new List<object>();
        double? previousUnscaledTime = null;
        object? lastState = null;
        string? lastError = null;

        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                int remainingTimeoutSeconds = Math.Max(
                    1,
                    (int)Math.Ceiling(Math.Min(10d, Math.Max(1d, (deadlineUtc - DateTime.UtcNow).TotalSeconds))));
                JsonElement state = await CallBridgeToolResultAsync(
                    "Unity.Editor.SetPlayMode",
                    new
                    {
                        mode = "enter",
                        stopFirst = false,
                        waitForRuntimeAdvance = true,
                        warmupSeconds = 0d,
                        timeoutSeconds = remainingTimeoutSeconds,
                        unpauseBeforeExit = true
                    },
                    cancellationToken).ConfigureAwait(false);

                object attempt = CreatePlayReadyAttemptFromSetPlayMode(state, previousUnscaledTime, out bool playReady, out double unscaledTime);
                attempts.Add(attempt);
                lastState = attempt;
                previousUnscaledTime = unscaledTime;

                if (playReady)
                {
                    if (warmupSeconds > 0d)
                        await Task.Delay((int)Math.Round(warmupSeconds * 1000d), cancellationToken).ConfigureAwait(false);

                    JsonElement finalState = await CallBridgeToolResultAsync(
                        "Unity.Editor.SetPlayMode",
                        new
                        {
                            mode = "enter",
                            stopFirst = false,
                            waitForRuntimeAdvance = true,
                            warmupSeconds = 0d,
                            timeoutSeconds = 5,
                            unpauseBeforeExit = true
                        },
                        cancellationToken).ConfigureAwait(false);
                    object finalAttempt = CreatePlayReadyAttemptFromSetPlayMode(finalState, previousUnscaledTime, out bool finalReady, out _);
                    attempts.Add(finalAttempt);

                    return BuildHostPlayReadyResult(
                        finalReady,
                        finalReady
                            ? "Play mode entered and runtime reached a settled advancing state."
                            : "Play mode advanced, but the final warmup probe was not ready.",
                        finalAttempt,
                        attempts,
                        finalReady ? null : lastError);
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                attempts.Add(new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    success = false,
                    error = ex.Message,
                    exceptionType = ex.GetType().Name
                });

                if (IsBridgeTransportFailure(ex))
                    await ResetBridgeClientAsync(preserveActivePacks: true, clearToolCache: true).ConfigureAwait(false);
            }

            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                break;

            int delayMs = (int)Math.Min(pollIntervalMs, Math.Max(1, remaining.TotalMilliseconds));
            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return BuildHostPlayReadyResult(
            success: false,
            message: "Play mode did not reach a settled advancing runtime state before timeout.",
            lastState,
            attempts,
            lastError);
    }

    object CreatePlayReadyAttemptFromSetPlayMode(JsonElement result, double? previousUnscaledTime, out bool playReady, out double unscaledTime)
    {
        JsonElement data = TryGetNestedProperty(result, out var dataElement, "data") ? dataElement : default;
        JsonElement finalState = TryGetNestedProperty(data, out var finalStateElement, "finalState") ? finalStateElement : default;
        JsonElement runtimeAdvance = TryGetNestedProperty(data, out var runtimeAdvanceElement, "runtimeAdvance") ? runtimeAdvanceElement : default;
        JsonElement finalProbe =
            TryGetNestedProperty(runtimeAdvance, out var runtimeAdvanceProbe, "finalProbe") ? runtimeAdvanceProbe :
            TryGetNestedProperty(finalState, out var finalStateProbe, "runtimeProbe") ? finalStateProbe :
            default;

        bool success = GetJsonBool(result, false, "success");
        bool requestAccepted = GetJsonBool(data, false, "requested") ||
            string.Equals(GetJsonString(data, "transitionState"), "already_playing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(GetJsonString(data, "transitionState"), "entered_play_mode", StringComparison.OrdinalIgnoreCase);
        bool isPlaying = GetJsonBool(finalState, false, "isPlaying", "IsPlaying");
        bool isPaused = GetJsonBool(finalState, false, "isPaused", "IsPaused");
        bool isCompiling = GetJsonBool(finalState, false, "isCompiling", "IsCompiling");
        bool isUpdating = GetJsonBool(finalState, false, "isUpdating", "IsUpdating");
        bool isBuildingPlayer = GetJsonBool(finalState, false, "isBuildingPlayer", "IsBuildingPlayer");
        bool isTransitioning = GetJsonBool(finalState, false, "isPlayingOrWillChangePlaymode", "IsPlayingOrWillChangePlaymode");
        bool runtimeProbeAvailable = GetJsonBool(finalProbe, false, "IsAvailable", "isAvailable");
        bool runtimeProbeHasAdvancedFrames = GetJsonBool(finalProbe, false, "HasAdvancedFrames", "hasAdvancedFrames");
        int updateCount = GetJsonInt(finalProbe, 0, "UpdateCount", "updateCount");
        int fixedUpdateCount = GetJsonInt(finalProbe, 0, "FixedUpdateCount", "fixedUpdateCount");
        unscaledTime = GetJsonDouble(finalProbe, 0d, "UnscaledTime", "unscaledTime");
        string activeSceneName = GetJsonString(finalProbe, "ActiveSceneName", "activeSceneName") ?? string.Empty;
        bool runtimeAdvancedByTime = previousUnscaledTime.HasValue && unscaledTime > previousUnscaledTime.Value;
        bool runtimeAdvanced = GetJsonBool(data, false, "runtimeAdvanced") ||
            (isPlaying && runtimeProbeAvailable && runtimeProbeHasAdvancedFrames && (updateCount >= 10 || runtimeAdvancedByTime));
        bool transitionPending = GetJsonBool(data, false, "transitionPending");
        bool editorIdle = success && !isCompiling && !isUpdating && !isBuildingPlayer && !transitionPending;
        bool readyForRuntimeTools = GetJsonBool(data, false, "readyForRuntimeTools") || (editorIdle && runtimeAdvanced);
        playReady = readyForRuntimeTools;

        return new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            success,
            requestAccepted,
            transitionState = GetJsonString(data, "transitionState"),
            reconnectExpected = GetJsonBool(data, false, "reconnectExpected"),
            editorIdle,
            isPlaying,
            isPaused,
            isCompiling,
            isUpdating,
            isBuildingPlayer,
            isTransitioning,
            transitionPending,
            runtimeProbeAvailable,
            runtimeProbeHasAdvancedFrames,
            runtimeProbeUpdateCount = updateCount,
            runtimeProbeFixedUpdateCount = fixedUpdateCount,
            runtimeProbeUnscaledTime = unscaledTime,
            runtimeAdvancedByTime,
            runtimeAdvanced,
            activeSceneName,
            readyForRuntimeTools,
            playReady,
            consoleErrorCount = GetJsonInt(data, 0, "consoleErrorCount"),
            timedOut = GetJsonBool(data, false, "timedOut")
        };
    }

    HostPlayReadyResult BuildHostPlayReadyResult(
        bool success,
        string message,
        object? lastState,
        List<object> attempts,
        string? lastError)
    {
        JsonElement lastStateElement = JsonSerializer.SerializeToElement(lastState ?? new { }, m_JsonOptions);
        return new HostPlayReadyResult
        {
            Success = success,
            Message = message,
            EditorIdle = GetJsonBool(lastStateElement, false, "editorIdle"),
            IsPlaying = GetJsonBool(lastStateElement, false, "isPlaying"),
            RuntimeAdvanced = GetJsonBool(lastStateElement, false, "runtimeAdvanced"),
            RuntimeProbeAvailable = GetJsonBool(lastStateElement, false, "runtimeProbeAvailable"),
            UpdateCount = GetJsonInt(lastStateElement, 0, "runtimeProbeUpdateCount"),
            FixedUpdateCount = GetJsonInt(lastStateElement, 0, "runtimeProbeFixedUpdateCount"),
            UnscaledTime = GetJsonDouble(lastStateElement, 0d, "runtimeProbeUnscaledTime"),
            ActiveScene = GetJsonString(lastStateElement, "activeSceneName") ?? string.Empty,
            Attempts = attempts,
            LastState = lastState,
            LastError = lastError
        };
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

    static string? ExtractString(JsonElement argumentsElement, params string[] names)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return null;

        foreach (string name in names)
        {
            if (argumentsElement.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String)
                return element.GetString();
        }

        return null;
    }

    static bool ExtractBool(JsonElement argumentsElement, bool fallback, params string[] names)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return fallback;

        foreach (string name in names)
        {
            if (!argumentsElement.TryGetProperty(name, out var element))
                continue;

            if (element.ValueKind == JsonValueKind.True)
                return true;
            if (element.ValueKind == JsonValueKind.False)
                return false;
        }

        return fallback;
    }

    static int ExtractInt(JsonElement argumentsElement, int fallback, params string[] names)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return fallback;

        foreach (string name in names)
        {
            if (argumentsElement.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out int value))
                return value;
        }

        return fallback;
    }

    static double ExtractDouble(JsonElement argumentsElement, double fallback, params string[] names)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return fallback;

        foreach (string name in names)
        {
            if (argumentsElement.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out double value))
                return value;
        }

        return fallback;
    }

    static string[] ExtractExpectedTools(JsonElement argumentsElement)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return [];

        if (argumentsElement.TryGetProperty("expectedTools", out var expectedToolsElement) || argumentsElement.TryGetProperty("ExpectedTools", out expectedToolsElement))
        {
            return expectedToolsElement.ValueKind == JsonValueKind.Array
                ? expectedToolsElement.EnumerateArray().Select(value => value.GetString()).Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>().ToArray()
                : [];
        }

        return [];
    }

    static string[] NormalizeToolNames(IEnumerable<string> toolNames)
    {
        return (toolNames ?? [])
            .Select(CanonicalizeToolName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    static string ExtractRefId(JsonElement argumentsElement)
    {
        if (argumentsElement.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (argumentsElement.TryGetProperty("refId", out var refIdElement) || argumentsElement.TryGetProperty("RefId", out refIdElement))
            return refIdElement.GetString() ?? string.Empty;

        return string.Empty;
    }

    static bool TryGetPropertyIgnoreCase(JsonElement element, out JsonElement value, params string[] names)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out value))
                return true;
        }

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    static bool TryGetNestedProperty(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;
        foreach (string name in path)
        {
            if (!TryGetPropertyIgnoreCase(value, out value, name))
                return false;
        }

        return true;
    }

    static object? CloneJsonProperty(JsonElement element, params string[] names)
    {
        return TryGetPropertyIgnoreCase(element, out var value, names)
            ? value.Clone()
            : null;
    }

    static object[]? CloneJsonArray(JsonElement element, params string[] names)
    {
        if (!TryGetPropertyIgnoreCase(element, out var value, names) || value.ValueKind != JsonValueKind.Array)
            return null;

        return value.EnumerateArray()
            .Select(item => (object)item.Clone())
            .ToArray();
    }

    static bool GetJsonBool(JsonElement element, bool fallback, params string[] names)
    {
        if (!TryGetPropertyIgnoreCase(element, out var value, names))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    static int GetJsonInt(JsonElement element, int fallback, params string[] names)
    {
        return TryGetPropertyIgnoreCase(element, out var value, names) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int result)
            ? result
            : fallback;
    }

    static double GetJsonDouble(JsonElement element, double fallback, params string[] names)
    {
        return TryGetPropertyIgnoreCase(element, out var value, names) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out double result)
            ? result
            : fallback;
    }

    static string? GetJsonString(JsonElement element, params string[] names)
    {
        return TryGetPropertyIgnoreCase(element, out var value, names) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    static int? ExtractConsoleErrorCount(object? consoleSummary)
    {
        if (consoleSummary == null)
            return null;

        JsonElement element = JsonSerializer.SerializeToElement(consoleSummary);
        if (TryGetNestedProperty(element, out var errorCount, "data", "typeCounts", "error") &&
            errorCount.ValueKind == JsonValueKind.Number &&
            errorCount.TryGetInt32(out int count))
        {
            return count;
        }

        if (TryGetNestedProperty(element, out var entryCount, "data", "entryCount") &&
            entryCount.ValueKind == JsonValueKind.Number &&
            entryCount.TryGetInt32(out count))
        {
            return count;
        }

        return null;
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
            selectedProjectPath = m_SelectedProjectPathHint,
            selectedProjectRequiresFreshBridge = !string.IsNullOrWhiteSpace(m_SelectedProjectPathHint)
                ? m_SelectedProjectRequireFreshBridge
                : (bool?)null,
            assemblyVersion = typeof(UnityMcpLensHost).Assembly.GetName().Version?.ToString(),
            informationalVersion = s_HostVersion,
            fileVersion = ResolveFileVersion(Environment.ProcessPath)
        };
    }

    JsonElement CreateBridgeDiscoveryErrorPayload(BridgeDiscoveryException exception)
    {
        return CreateErrorPayload(
            exception.Message,
            "UNITY_MCP_NO_MATCHING_BRIDGE",
            new
            {
                host = CreateHostDiagnostics(),
                discovery = BuildBridgeDiscoveryDiagnostics(exception.Snapshot, maxCandidates: 12)
            });
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
            failedConnectionPath = recoveryState.FailedConnectionPath,
            discovery = m_LastBridgeDiscoverySnapshot == null
                ? null
                : BuildBridgeDiscoveryDiagnostics(m_LastBridgeDiscoverySnapshot, maxCandidates: 8)
        };
    }

    static object BuildBridgeDiscoveryDiagnostics(BridgeDiscoverySnapshot snapshot, int maxCandidates)
    {
        BridgeDiscoveryCandidate[] candidates = snapshot.Candidates.Take(Math.Max(1, maxCandidates)).ToArray();
        return new
        {
            projectPathHint = snapshot.ProjectPathHint,
            requireProjectMatch = snapshot.RequireProjectMatch,
            statusDirectory = snapshot.StatusDirectory,
            selected = snapshot.Selected == null ? null : CreateBridgeDiscoveryResultDiagnostics(snapshot.Selected),
            candidateCount = snapshot.Candidates.Length,
            editorHealthCandidateCount = snapshot.EditorHealthCandidates.Length,
            unmatchedEditorHealthCandidateCount = snapshot.UnmatchedEditorHealthCandidates.Length,
            returnedCandidateCount = candidates.Length,
            candidates = candidates.Select(CreateBridgeCandidateDiagnostics).ToArray(),
            unmatchedEditorHealthCandidates = snapshot.UnmatchedEditorHealthCandidates
                .Take(Math.Max(1, maxCandidates))
                .Select(CreateEditorHealthDiagnostics)
                .ToArray()
        };
    }

    static object CreateBridgeDiscoveryResultDiagnostics(BridgeDiscoveryResult result)
    {
        return new
        {
            statusPath = result.StatusPath,
            connectionPath = result.ConnectionPath,
            projectRoot = result.ProjectRoot,
            projectRootMatch = result.IsProjectMatch,
            status = result.StatusFile.Status,
            heartbeatAgeSeconds = result.HeartbeatAge == TimeSpan.MaxValue ? (double?)null : Math.Round(result.HeartbeatAge.TotalSeconds, 3),
            lastHeartbeatUtc = result.LastHeartbeatUtc == DateTime.MinValue ? null : result.LastHeartbeatUtc.ToString("O"),
            editorPid = result.EditorPid,
            editorPidAlive = result.EditorPidAlive,
            fresh = result.IsFresh,
            basicHealth = result.BasicHealth,
            editorHealth = result.EditorHealth == null ? null : CreateEditorHealthDiagnostics(result.EditorHealth),
            supportsToolSyncLens = result.StatusFile.SupportsToolSyncLens,
            bridgeSessionId = result.StatusFile.BridgeSessionId,
            manifestVersion = result.StatusFile.ManifestVersion
        };
    }

    static object CreateBridgeCandidateDiagnostics(BridgeDiscoveryCandidate candidate)
    {
        return new
        {
            statusPath = candidate.StatusPath,
            connectionPath = candidate.ConnectionPath,
            projectRoot = candidate.ProjectRoot,
            projectRootMatch = candidate.IsProjectMatch,
            status = candidate.Status,
            heartbeatAgeSeconds = candidate.HeartbeatAge == TimeSpan.MaxValue ? (double?)null : Math.Round(candidate.HeartbeatAge.TotalSeconds, 3),
            lastHeartbeatUtc = candidate.LastHeartbeatUtc == DateTime.MinValue ? null : candidate.LastHeartbeatUtc.ToString("O"),
            editorPid = candidate.EditorPid,
            editorPidAlive = candidate.EditorPidAlive,
            fresh = candidate.IsFresh,
            basicHealth = candidate.BasicHealth,
            editorHealth = candidate.EditorHealth == null ? null : CreateEditorHealthDiagnostics(candidate.EditorHealth),
            supportsToolSyncLens = candidate.SupportsToolSyncLens,
            quarantined = candidate.IsQuarantined,
            selectable = candidate.IsSelectable,
            exclusionReasons = candidate.ExclusionReasons,
            error = candidate.Error
        };
    }

    static object CreateEditorHealthDiagnostics(UnityMcpLens.Shared.EditorHealthCandidate candidate)
    {
        return new
        {
            healthPath = candidate.HealthPath,
            projectRoot = candidate.ProjectRoot,
            projectRootMatch = candidate.IsProjectMatch,
            basicHealth = candidate.BasicHealth,
            heartbeatAgeSeconds = candidate.HeartbeatAge == TimeSpan.MaxValue ? (double?)null : Math.Round(candidate.HeartbeatAge.TotalSeconds, 3),
            editorHeartbeatUtc = candidate.EditorHeartbeatUtc == DateTime.MinValue ? null : candidate.EditorHeartbeatUtc.ToString("O"),
            stateCapturedUtc = candidate.StateCapturedUtc == DateTime.MinValue ? null : candidate.StateCapturedUtc.ToString("O"),
            editorPid = candidate.EditorPid,
            editorPidAlive = candidate.EditorPidAlive,
            editorProcessStartUtc = candidate.EditorProcessStartUtc == DateTime.MinValue ? null : candidate.EditorProcessStartUtc.ToString("O"),
            pidStartMatches = candidate.PidStartMatches,
            fresh = candidate.IsFresh,
            lifecycleState = candidate.HealthFile?.LifecycleState,
            unityVersion = candidate.HealthFile?.UnityVersion,
            isCompiling = candidate.HealthFile?.IsCompiling,
            isImporting = candidate.HealthFile?.IsImporting,
            isUpdating = candidate.HealthFile?.IsUpdating,
            isPlaying = candidate.HealthFile?.IsPlaying,
            isPaused = candidate.HealthFile?.IsPaused,
            isPlayingOrWillChangePlaymode = candidate.HealthFile?.IsPlayingOrWillChangePlaymode,
            isBuildingPlayer = candidate.HealthFile?.IsBuildingPlayer,
            activeSceneName = candidate.HealthFile?.ActiveSceneName,
            activeScenePath = candidate.HealthFile?.ActiveScenePath,
            captureError = candidate.HealthFile?.CaptureError,
            error = candidate.Error
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

    static bool IsStaticAllToolSurface =>
        string.Equals(s_ToolSurfaceMode, StaticAllToolSurfaceMode, StringComparison.OrdinalIgnoreCase);

    static string[] GetDefaultActivePacksForSurfaceMode()
    {
        return IsStaticAllToolSurface
            ? ["foundation", "full"]
            : ["foundation"];
    }

    static bool ActivePacksAreStaticAll(IEnumerable<string> packs)
    {
        return (packs ?? Array.Empty<string>()).Any(pack => string.Equals(pack, "full", StringComparison.OrdinalIgnoreCase));
    }

    static string ResolveToolSurfaceMode()
    {
        string? rawMode = Environment.GetEnvironmentVariable(ToolSurfaceModeEnvVar);
        if (string.IsNullOrWhiteSpace(rawMode))
            return DynamicPacksToolSurfaceMode;

        string mode = rawMode.Trim();
        if (string.Equals(mode, DynamicPacksToolSurfaceMode, StringComparison.OrdinalIgnoreCase))
            return DynamicPacksToolSurfaceMode;
        if (string.Equals(mode, StaticAllToolSurfaceMode, StringComparison.OrdinalIgnoreCase))
            return StaticAllToolSurfaceMode;

        Console.Error.WriteLine($"[unity-mcp-lens] Unknown {ToolSurfaceModeEnvVar} value '{rawMode}'. Falling back to {DynamicPacksToolSurfaceMode}.");
        return DynamicPacksToolSurfaceMode;
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
