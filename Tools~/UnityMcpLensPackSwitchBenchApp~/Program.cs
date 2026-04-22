using System.Diagnostics;
using System.Text;
using System.Text.Json;

var options = BenchmarkOptions.Parse(args);
try
{
    if (options.MetadataAudit)
    {
        var audit = new MetadataAudit(options);
        var auditReport = await audit.RunAsync();

        if (options.AsJson)
        {
            Console.WriteLine(JsonSerializer.Serialize(auditReport, JsonOptions.Output));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine("Lens metadata audit");
            Console.WriteLine($"Project:    {auditReport.ProjectPath}");
            Console.WriteLine($"Server:     {auditReport.ServerPath}");
            Console.WriteLine($"Foundation: {auditReport.FoundationToolCount} tools");
            Console.WriteLine($"Scene:      {auditReport.SceneToolCount} tools");
            Console.WriteLine($"Debug:      {auditReport.DebugToolCount} tools");
            Console.WriteLine($"Result:     {(auditReport.Success ? "PASS" : "FAIL")}");

            foreach (var failure in auditReport.Failures)
                Console.WriteLine($"  - {failure}");

            Console.WriteLine();
        }

        if (!auditReport.Success)
            Environment.ExitCode = 1;

        return;
    }

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
}
catch (Exception ex)
{
    if (options.AsJson)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            success = false,
            error = ex.Message,
            detail = ex.ToString()
        }, JsonOptions.Output));
    }
    else
    {
        Console.Error.WriteLine(ex.ToString());
    }

    Environment.ExitCode = 1;
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
    public int ScenarioRetryCount { get; init; } = 2;
    public bool AsJson { get; init; }
    public bool MetadataAudit { get; init; }

    public static BenchmarkOptions Parse(string[] args)
    {
        string? projectPath = null;
        string? serverPath = null;
        int rpcTimeoutSeconds = 30;
        int statsSettleMilliseconds = 800;
        int scenarioRetryCount = 2;
        bool asJson = false;
        bool metadataAudit = false;

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
                case "--scenario-retry-count":
                    scenarioRetryCount = int.Parse(args[++i]);
                    break;
                case "--json":
                    asJson = true;
                    break;
                case "--metadata-audit":
                    metadataAudit = true;
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
            ScenarioRetryCount = Math.Max(1, scenarioRetryCount),
            AsJson = asJson,
            MetadataAudit = metadataAudit
        };
    }
}

sealed class MetadataAudit(BenchmarkOptions options)
{
    const int ExpectedFoundationToolCount = 12;
    const int ExpectedSceneToolCount = 24;

    static readonly string[] k_RequiredFoundationTools =
    [
        "Unity_GetLensHealth",
        "Unity_ListToolPacks",
        "Unity_SetToolPacks",
        "Unity_ReadDetailRef",
        "Unity_ReadConsole",
        "Unity_ListResources",
        "Unity_ReadResource",
        "Unity_FindInFile",
        "Unity_GetSha",
        "Unity_ValidateScript",
        "Unity_ManageScript_capabilities",
        "Unity_Project_GetInfo"
    ];

    static readonly string[] k_RequiredSceneTools =
    [
        "Unity_GameObject_Inspect",
        "Unity_GameObject_ListComponents",
        "Unity_GameObject_GetComponent",
        "Unity_GameObject_PreviewChanges",
        "Unity_GameObject_ApplyChanges",
        "Unity_ManageGameObject",
        "Unity_ManageScene",
        "Unity_Scene_SetSerializedProperties",
        "Unity_Scene_CaptureView",
        "Unity_Tilemap_Setup",
        "Unity_Tilemap_Paint",
        "Unity_Runtime_GetVisualBoundsSnapshot"
    ];

    static readonly string[] k_RequiredDebugTools =
    [
        "Unity_GetLensUsageReport"
    ];

    public async Task<MetadataAuditReport> RunAsync()
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= options.ScenarioRetryCount; attempt++)
        {
            try
            {
                return await RunAttemptAsync();
            }
            catch (Exception ex) when (attempt < options.ScenarioRetryCount && IsTransientFailure(ex))
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, attempt))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        return new MetadataAuditReport(
            options.ProjectPath,
            options.ServerPath,
            DateTimeOffset.UtcNow,
            false,
            0,
            0,
            0,
            [],
            [],
            [],
            [$"Metadata audit failed before validation: {lastError?.Message}"]);
    }

    async Task<MetadataAuditReport> RunAttemptAsync()
    {
        await using var session = await LensSession.StartAsync(options.ServerPath, options.ProjectPath, TimeSpan.FromSeconds(options.RpcTimeoutSeconds));
        await session.InitializeAsync();

        var foundationTools = await session.GetToolsAsync();
        _ = await session.SetToolPacksAsync(["scene"]);
        var sceneTools = await session.GetToolsAsync();
        _ = await session.SetToolPacksAsync(["debug"]);
        var debugTools = await session.GetToolsAsync();

        var failures = new List<string>();
        ValidateToolSet("foundation", foundationTools, ExpectedFoundationToolCount, k_RequiredFoundationTools, failures);
        ValidateToolSet("foundation+scene", sceneTools, ExpectedSceneToolCount, k_RequiredSceneTools, failures);
        ValidateToolSet("foundation+debug", debugTools, null, k_RequiredDebugTools, failures);
        ValidateReadOnlyHint(sceneTools, "Unity_GameObject_Inspect", expected: true, failures);
        ValidateReadOnlyHint(sceneTools, "Unity_GameObject_ListComponents", expected: true, failures);
        ValidateReadOnlyHint(sceneTools, "Unity_GameObject_GetComponent", expected: true, failures);
        ValidateReadOnlyHint(sceneTools, "Unity_GameObject_PreviewChanges", expected: true, failures);
        ValidateReadOnlyHint(sceneTools, "Unity_GameObject_ApplyChanges", expected: false, failures);
        ValidateReadOnlyHint(sceneTools, "Unity_ManageGameObject", expected: false, failures);
        ValidateReadOnlyHint(debugTools, "Unity_GetLensUsageReport", expected: true, failures);
        ValidateGameObjectSchemas(sceneTools, failures);
        ValidateLensUsageSchema(debugTools, failures);

        return new MetadataAuditReport(
            options.ProjectPath,
            options.ServerPath,
            DateTimeOffset.UtcNow,
            failures.Count == 0,
            foundationTools.Count,
            sceneTools.Count,
            debugTools.Count,
            foundationTools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            sceneTools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            debugTools.Select(tool => tool.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
            failures);
    }

    static void ValidateToolSet(string label, IReadOnlyList<ToolDescriptor> tools, int? expectedCount, string[] requiredTools, List<string> failures)
    {
        if (expectedCount.HasValue && tools.Count != expectedCount.Value)
            failures.Add($"{label} exported {tools.Count} tools; expected {expectedCount.Value}.");

        var toolNames = tools.Select(tool => tool.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var requiredTool in requiredTools)
        {
            if (!toolNames.Contains(requiredTool))
                failures.Add($"{label} missing required tool '{requiredTool}'.");
        }

        foreach (var tool in tools)
        {
            if (string.IsNullOrWhiteSpace(tool.Name))
                failures.Add($"{label} includes a tool with an empty name.");
            if (string.IsNullOrWhiteSpace(tool.Title))
                failures.Add($"{label} tool '{tool.Name}' has an empty title.");
            if (string.IsNullOrWhiteSpace(tool.Description))
                failures.Add($"{label} tool '{tool.Name}' has an empty description.");
            if (!tool.HasObjectInputSchema())
                failures.Add($"{label} tool '{tool.Name}' does not expose an object input schema.");
        }
    }

    static void ValidateLensUsageSchema(IReadOnlyList<ToolDescriptor> tools, List<string> failures)
    {
        var tool = FindTool(tools, "Unity_GetLensUsageReport");
        if (tool == null)
        {
            failures.Add("Cannot validate Unity_GetLensUsageReport schema because the tool is missing.");
            return;
        }

        foreach (var propertyName in new[] { "sinceLine", "sinceUtc", "lastRows", "maxItems", "includeDetails" })
        {
            if (!tool.HasInputProperty(propertyName))
                failures.Add($"Unity_GetLensUsageReport schema is missing property '{propertyName}'.");
        }
    }

    static void ValidateReadOnlyHint(IReadOnlyList<ToolDescriptor> tools, string toolName, bool expected, List<string> failures)
    {
        var tool = FindTool(tools, toolName);
        if (tool == null)
        {
            failures.Add($"Cannot validate readOnlyHint for missing tool '{toolName}'.");
            return;
        }

        if (!tool.TryGetAnnotationBool("readOnlyHint", out var actual))
        {
            failures.Add($"Tool '{toolName}' is missing annotations.readOnlyHint.");
            return;
        }

        if (actual != expected)
            failures.Add($"Tool '{toolName}' readOnlyHint was {actual}; expected {expected}.");
    }

    static void ValidateGameObjectSchemas(IReadOnlyList<ToolDescriptor> tools, List<string> failures)
    {
        ValidateSplitGameObjectSchema(
            tools,
            "Unity_GameObject_Inspect",
            ["mode", "target", "searchMethod", "searchTerm", "findAll", "searchInChildren", "searchInactive"],
            ["mode"],
            failures);
        ValidateSplitGameObjectSchema(
            tools,
            "Unity_GameObject_ListComponents",
            ["target", "searchMethod", "searchInactive"],
            ["target"],
            failures);
        ValidateSplitGameObjectSchema(
            tools,
            "Unity_GameObject_GetComponent",
            ["target", "componentName", "componentIndex", "searchMethod", "searchInactive", "includeNonPublicSerialized"],
            ["target", "componentName"],
            failures);
        ValidateSplitGameObjectSchema(
            tools,
            "Unity_GameObject_PreviewChanges",
            ["target", "searchMethod", "name", "setActive", "tag", "layer", "position", "positionType", "rotation", "scale", "parent"],
            ["target"],
            failures);
        ValidateSplitGameObjectSchema(
            tools,
            "Unity_GameObject_ApplyChanges",
            ["target", "searchMethod", "name", "setActive", "tag", "layer", "position", "positionType", "rotation", "scale", "parent"],
            ["target"],
            failures);

        var legacyTool = FindTool(tools, "Unity_ManageGameObject");
        if (legacyTool == null)
        {
            failures.Add("Cannot validate legacy Unity_ManageGameObject schema because the tool is missing.");
            return;
        }

        foreach (var propertyName in new[] { "action", "search_method", "set_active", "find_all", "search_in_children", "search_inactive" })
        {
            if (!legacyTool.HasInputProperty(propertyName))
                failures.Add($"Unity_ManageGameObject schema is missing legacy property '{propertyName}'.");
        }

        if (!legacyTool.HasRequiredProperty("action"))
            failures.Add("Unity_ManageGameObject schema no longer requires 'action'.");

        var actionEnum = legacyTool.GetInputPropertyEnum("action").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var expectedAction in new[] { "find", "get_selection", "get_bounds", "modify" })
        {
            if (!actionEnum.Contains(expectedAction))
                failures.Add($"Unity_ManageGameObject action enum is missing '{expectedAction}'.");
        }
    }

    static void ValidateSplitGameObjectSchema(IReadOnlyList<ToolDescriptor> tools, string toolName, string[] expectedProperties, string[] requiredProperties, List<string> failures)
    {
        var tool = FindTool(tools, toolName);
        if (tool == null)
        {
            failures.Add($"Cannot validate split GameObject schema because '{toolName}' is missing.");
            return;
        }

        var propertyNames = tool.GetInputPropertyNames();
        foreach (var propertyName in expectedProperties)
        {
            if (!propertyNames.Contains(propertyName, StringComparer.Ordinal))
                failures.Add($"{toolName} schema is missing property '{propertyName}'.");
        }

        foreach (var propertyName in propertyNames)
        {
            if (propertyName.Contains('_'))
                failures.Add($"{toolName} exposes non-lower-camel input '{propertyName}'.");
        }

        foreach (var requiredProperty in requiredProperties)
        {
            if (!tool.HasRequiredProperty(requiredProperty))
                failures.Add($"{toolName} schema no longer requires '{requiredProperty}'.");
        }
    }

    static ToolDescriptor? FindTool(IReadOnlyList<ToolDescriptor> tools, string toolName)
    {
        return tools.FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    static bool IsTransientFailure(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? string.Empty;
            if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("closed stdout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNITY_MCP_ERROR", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("No active Unity MCP bridge status file", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("ReconnectRequired", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
        Exception? lastError = null;
        for (var attempt = 1; attempt <= options.ScenarioRetryCount; attempt++)
        {
            try
            {
                return await RunScenarioAttemptAsync(definition);
            }
            catch (Exception ex) when (attempt < options.ScenarioRetryCount && IsTransientScenarioFailure(ex))
            {
                lastError = ex;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5, attempt))).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lastError = ex;
                break;
            }
        }

        throw new InvalidOperationException(
            $"Scenario '{definition.Name}' failed after {options.ScenarioRetryCount} attempt(s). Last error: {lastError?.Message}",
            lastError);
    }

    async Task<ScenarioResult> RunScenarioAttemptAsync(ScenarioDefinition definition)
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

    static bool IsTransientScenarioFailure(Exception ex)
    {
        for (Exception? current = ex; current != null; current = current.InnerException)
        {
            var message = current.Message ?? string.Empty;
            if (message.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("connection closed", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("closed stdout", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("UNITY_MCP_ERROR", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("No active Unity MCP bridge status file", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

sealed record MetadataAuditReport(
    string ProjectPath,
    string ServerPath,
    DateTimeOffset RanAtUtc,
    bool Success,
    int FoundationToolCount,
    int SceneToolCount,
    int DebugToolCount,
    IReadOnlyList<string> FoundationTools,
    IReadOnlyList<string> SceneTools,
    IReadOnlyList<string> DebugTools,
    IReadOnlyList<string> Failures);

sealed class ToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public JsonElement InputSchema { get; init; }
    public JsonElement Annotations { get; init; }

    public static ToolDescriptor FromJson(JsonElement element)
    {
        return new ToolDescriptor
        {
            Name = GetString(element, "name"),
            Title = GetString(element, "title"),
            Description = GetString(element, "description"),
            InputSchema = CloneProperty(element, "inputSchema"),
            Annotations = CloneProperty(element, "annotations")
        };
    }

    public bool HasObjectInputSchema()
    {
        if (InputSchema.ValueKind != JsonValueKind.Object)
            return false;

        return InputSchema.TryGetProperty("type", out var typeElement) &&
            typeElement.ValueKind == JsonValueKind.String &&
            string.Equals(typeElement.GetString(), "object", StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetAnnotationBool(string propertyName, out bool value)
    {
        value = false;
        if (Annotations.ValueKind != JsonValueKind.Object ||
            !Annotations.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }

    public bool HasInputProperty(string propertyName)
    {
        return TryGetInputProperties(out var properties) &&
            properties.TryGetProperty(propertyName, out _);
    }

    public bool HasRequiredProperty(string propertyName)
    {
        if (InputSchema.ValueKind != JsonValueKind.Object ||
            !InputSchema.TryGetProperty("required", out var requiredElement) ||
            requiredElement.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return requiredElement.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.String &&
            string.Equals(item.GetString(), propertyName, StringComparison.Ordinal));
    }

    public string[] GetInputPropertyNames()
    {
        if (!TryGetInputProperties(out var properties))
            return [];

        return properties.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }

    public string[] GetInputPropertyEnum(string propertyName)
    {
        if (!TryGetInputProperties(out var properties) ||
            !properties.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Object ||
            !property.TryGetProperty("enum", out var enumElement) ||
            enumElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return enumElement.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    bool TryGetInputProperties(out JsonElement properties)
    {
        properties = default;
        return InputSchema.ValueKind == JsonValueKind.Object &&
            InputSchema.TryGetProperty("properties", out properties) &&
            properties.ValueKind == JsonValueKind.Object;
    }

    static JsonElement CloneProperty(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            return property.Clone();
        }

        return default;
    }

    static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString() ?? string.Empty;
        }

        return string.Empty;
    }
}

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
        return (await GetToolsAsync()).Count;
    }

    public async Task<IReadOnlyList<ToolDescriptor>> GetToolsAsync()
    {
        var response = await SendRequestAsync(NextId(), "tools/list", new { });
        if (response.Response.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"tools/list failed: {error}");

        if (!response.Response.TryGetProperty("result", out var result) ||
            !result.TryGetProperty("tools", out var tools) ||
            tools.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"tools/list returned an unexpected payload: {response.Response}");
        }

        return tools.EnumerateArray()
            .Select(ToolDescriptor.FromJson)
            .ToArray();
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

        var result = response.Response.GetProperty("result");
        var structuredContent = result.GetProperty("structuredContent");
        if (TryGetToolError(structuredContent, out var toolError))
            throw new LensToolCallException("Unity_SetToolPacks", toolError, response.Response.GetRawText());

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

    static bool TryGetToolError(JsonElement structuredContent, out string error)
    {
        error = string.Empty;
        if (structuredContent.ValueKind != JsonValueKind.Object)
            return false;

        if (structuredContent.TryGetProperty("success", out var successElement) &&
            successElement.ValueKind == JsonValueKind.False)
        {
            if (structuredContent.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String)
                error = errorElement.GetString() ?? "Unity tool call failed.";
            else
                error = "Unity tool call failed.";

            return true;
        }

        return false;
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

sealed class LensToolCallException(string toolName, string message, string rawResponse)
    : InvalidOperationException($"{toolName} failed: {message}")
{
    public string ToolName { get; } = toolName;
    public string RawResponse { get; } = rawResponse;
}
