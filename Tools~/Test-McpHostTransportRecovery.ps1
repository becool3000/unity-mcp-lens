param(
    [string]$HostPath
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($HostPath)) {
    $HostPath = Join-Path $repoRoot "UnityMcpLensApp~\src\UnityMcpLens\bin\Debug\net8.0\UnityMcpLens.exe"
}

if (-not (Test-Path -LiteralPath $HostPath)) {
    throw "Unity MCP Lens host was not found at '$HostPath'. Build UnityMcpLens.csproj before running this test."
}

$env:UNITY_MCP_LENS_HOST = [System.IO.Path]::GetFullPath($HostPath)
node (Join-Path $PSScriptRoot "Test-McpHostTransportRecovery.js")
if ($LASTEXITCODE -ne 0) {
    throw "MCP host transport recovery tests failed with exit code $LASTEXITCODE."
}

function Invoke-CommandCenterStatusScannerSmoke {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("LensCommandCenterScannerSmoke-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    try {
        $scannerProject = Join-Path $tempRoot "ScannerSmoke.csproj"
        $programFile = Join-Path $tempRoot "Program.cs"
        $commandCenterProject = Join-Path $repoRoot "UnityMcpLensApp~\src\UnityMcpLens.CommandCenter\UnityMcpLens.CommandCenter.csproj"

        Set-Content -LiteralPath $scannerProject -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$commandCenterProject" />
  </ItemGroup>
</Project>
"@

        Set-Content -LiteralPath $programFile -Encoding UTF8 -Value @'
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityMcpLens.CommandCenter;

static string ProjectHashForStatusFile(string projectRoot)
{
    string assetsPath = Path.Combine(projectRoot, "Assets").Replace('\\', '/');
    byte[] hash = SHA1.HashData(Encoding.UTF8.GetBytes(assetsPath));
    return Convert.ToHexString(hash).Substring(0, 8).ToLowerInvariant();
}

string statusDir = Path.Combine(Path.GetTempPath(), "LensScannerStatus-" + Guid.NewGuid().ToString("N"));
string projectRoot = Path.Combine(Path.GetTempPath(), "LensScannerProject-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(statusDir);
Directory.CreateDirectory(Path.Combine(projectRoot, "Assets"));

try
{
    int pid = Environment.ProcessId;
    string processStartUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O");
    string nowUtc = DateTime.UtcNow.ToString("O");
    string hash = ProjectHashForStatusFile(projectRoot);
    var options = new JsonSerializerOptions { WriteIndented = true };

    string healthPath = Path.Combine(statusDir, $"editor-health-{hash}-{pid}.json");
    File.WriteAllText(healthPath, JsonSerializer.Serialize(new
    {
        health_schema_version = 1,
        editor_heartbeat_utc = nowUtc,
        state_captured_utc = nowUtc,
        editor_pid = pid,
        editor_process_start_utc = processStartUtc,
        project_path = Path.Combine(projectRoot, "Assets"),
        project_root = projectRoot,
        unity_version = "scanner-smoke",
        lifecycle_state = "active",
        is_compiling = false,
        is_importing = false,
        is_updating = false,
        is_playing = false,
        is_paused = false,
        is_playing_or_will_change_playmode = false,
        is_building_player = false,
        active_scene_name = "ScannerSmoke",
        active_scene_path = "Assets/ScannerSmoke.unity",
        capture_error = (string?)null
    }, options));

    string bridgePath = Path.Combine(statusDir, $"bridge-status-{hash}-{pid}.json");
    File.WriteAllText(bridgePath, JsonSerializer.Serialize(new
    {
        connection_type = "named_pipe",
        connection_path = @"\\.\pipe\lens-scanner-smoke",
        status = "ready",
        tool_count = 12,
        command_health = "ok",
        manifest_version = 1,
        project_path = Path.Combine(projectRoot, "Assets"),
        project_root = projectRoot,
        last_heartbeat = nowUtc,
        protocol_version = "2.0",
        editor_pid = pid
    }, options));

    string malformedPath = Path.Combine(statusDir, $"bridge-status-{hash}-malformed.json");
    File.WriteAllText(malformedPath, "not json");
    File.SetLastWriteTimeUtc(malformedPath, DateTime.UtcNow.AddMinutes(-5));

    BridgeStatusItem[] rows = new StatusScanner(statusDir, projectRoot).Scan().ToArray();
    BridgeStatusItem? healthy = rows.FirstOrDefault(row => string.Equals(row.StatusPath, bridgePath, StringComparison.OrdinalIgnoreCase));
    BridgeStatusItem? ignored = rows.FirstOrDefault(row => string.Equals(row.StatusPath, malformedPath, StringComparison.OrdinalIgnoreCase));

    if (healthy == null || healthy.BasicHealth != "fresh")
        throw new InvalidOperationException("Command Center scanner did not preserve the fresh healthy bridge row.");
    if (ignored == null || !ignored.IgnoredMalformed || ignored.BasicHealth != "ignored_malformed_status")
        throw new InvalidOperationException("Command Center scanner did not report the stale malformed file as an ignored warning row.");
    if (ignored.MalformedIgnoreReason != "stale_malformed_status")
        throw new InvalidOperationException($"Expected stale_malformed_status, got '{ignored.MalformedIgnoreReason}'.");

    Console.WriteLine("Command Center status scanner smoke passed.");
}
finally
{
    Directory.Delete(statusDir, recursive: true);
    Directory.Delete(projectRoot, recursive: true);
}
'@

        dotnet run --project $scannerProject
        if ($LASTEXITCODE -ne 0) {
            throw "Command Center status scanner smoke failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-CommandCenterStatusScannerSmoke

function Invoke-CommandCenterTelemetryScannerSmoke {
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("LensCommandCenterTelemetrySmoke-" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $tempRoot | Out-Null
    try {
        $scannerProject = Join-Path $tempRoot "TelemetrySmoke.csproj"
        $programFile = Join-Path $tempRoot "Program.cs"
        $commandCenterProject = Join-Path $repoRoot "UnityMcpLensApp~\src\UnityMcpLens.CommandCenter\UnityMcpLens.CommandCenter.csproj"

        Set-Content -LiteralPath $scannerProject -Encoding UTF8 -Value @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$commandCenterProject" />
  </ItemGroup>
</Project>
"@

        Set-Content -LiteralPath $programFile -Encoding UTF8 -Value @'
using System.Text.Json;
using UnityMcpLens.CommandCenter;

static void Assert(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

string projectRoot = Path.Combine(Path.GetTempPath(), "LensTelemetryProject-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(projectRoot);

try
{
    var scanner = new TelemetryScanner(projectRoot);
    TelemetrySnapshot missing = scanner.Scan();
    Assert(!missing.Exists, "Missing telemetry file should return Exists=false.");

    string libraryPath = Path.Combine(projectRoot, "Library");
    Directory.CreateDirectory(libraryPath);
    string statsPath = Path.Combine(libraryPath, "AI.Gateway.PayloadStats.jsonl");
    File.WriteAllText(statsPath, string.Empty);
    TelemetrySnapshot empty = scanner.Scan();
    Assert(empty.Exists && empty.IsEmpty, "Empty telemetry file should be reported distinctly.");

    DateTimeOffset now = DateTimeOffset.UtcNow;
    var rows = new List<string>
    {
        "not json",
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "payload",
            timestampUtc = now.AddSeconds(1).ToString("O"),
            stage = "result_shaping",
            name = "Unity.RunCommand",
            rawBytes = 1000,
            shapedBytes = 400,
            durationMs = 50,
            success = true,
            payloadClass = "tool_result"
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "payload",
            timestampUtc = now.AddSeconds(2).ToString("O"),
            stage = "console",
            name = "Unity.ReadConsole",
            rawBytes = 200,
            shapedBytes = 200,
            durationMs = 20,
            success = true,
            payloadClass = "tool_result"
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "payload",
            timestampUtc = now.AddSeconds(3).ToString("O"),
            stage = "runtime",
            name = "Unity.BadProbe",
            rawBytes = 100,
            shapedBytes = 50,
            durationMs = 100,
            success = false,
            errorKind = "boom",
            payloadClass = "tool_result"
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(4).ToString("O"),
            stage = "coverage_bridge_command_request",
            name = "register_client",
            commandType = "register_client",
            connectionId = "c1",
            requestId = "r1",
            requestBytes = 10,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(5).ToString("O"),
            stage = "coverage_bridge_command_response",
            name = "register_client",
            commandType = "register_client",
            connectionId = "c1",
            requestId = "r1",
            responseBytes = 100,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(6).ToString("O"),
            stage = "coverage_bridge_command_request",
            name = "get_manifest",
            commandType = "get_manifest",
            connectionId = "c1",
            requestId = "r2",
            requestBytes = 20,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(7).ToString("O"),
            stage = "coverage_bridge_command_response",
            name = "get_manifest",
            commandType = "get_manifest",
            connectionId = "c1",
            requestId = "r2",
            responseBytes = 200,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(8).ToString("O"),
            stage = "coverage_bridge_command_request",
            name = "get_tool_schema",
            commandType = "get_tool_schema",
            connectionId = "c1",
            requestId = "r3",
            requestBytes = 30,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(9).ToString("O"),
            stage = "coverage_bridge_command_response",
            name = "get_tool_schema",
            commandType = "get_tool_schema",
            connectionId = "c1",
            requestId = "r3",
            responseBytes = 300,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(10).ToString("O"),
            stage = "coverage_bridge_command_request",
            name = "set_tool_packs",
            commandType = "set_tool_packs",
            connectionId = "c1",
            requestId = "r4",
            requestBytes = 40,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(11).ToString("O"),
            stage = "coverage_bridge_command_response",
            name = "set_tool_packs",
            commandType = "set_tool_packs",
            connectionId = "c1",
            requestId = "r4",
            responseBytes = 400,
            activeToolPacks = new[] { "foundation", "debug" },
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "bridge_coverage",
            timestampUtc = now.AddSeconds(12).ToString("O"),
            stage = "coverage_bridge_command_request",
            name = "Unity.HungProbe",
            commandType = "Unity.HungProbe",
            connectionId = "c1",
            requestId = "missing",
            requestBytes = 90,
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "tool_snapshot",
            timestampUtc = now.AddSeconds(13).ToString("O"),
            stage = "tool_snapshot",
            name = "snapshot",
            snapshotHashMinimal = "same",
            snapshotHashFull = "a",
            success = true
        }),
        JsonSerializer.Serialize(new
        {
            schemaVersion = "unity-mcp-lens.payload-stats.v1",
            eventKind = "tool_snapshot",
            timestampUtc = now.AddSeconds(14).ToString("O"),
            stage = "tool_snapshot",
            name = "snapshot",
            snapshotHashMinimal = "same",
            snapshotHashFull = "b",
            success = true
        })
    };

    File.WriteAllLines(statsPath, rows);
    TelemetrySnapshot snapshot = scanner.Scan();

    Assert(snapshot.Exists && !snapshot.IsEmpty, "Telemetry should load after rows are written.");
    Assert(snapshot.SkippedLineCount == 1, $"Expected 1 skipped line, got {snapshot.SkippedLineCount}.");
    Assert(snapshot.PayloadEntryCount == 5, $"Expected 5 non-coverage rows, got {snapshot.PayloadEntryCount}.");
    Assert(snapshot.CoverageEntryCount == 9, $"Expected 9 coverage rows, got {snapshot.CoverageEntryCount}.");
    Assert(snapshot.RawBytes == 1300, $"Expected 1300 raw bytes, got {snapshot.RawBytes}.");
    Assert(snapshot.ShapedBytes == 650, $"Expected 650 shaped bytes, got {snapshot.ShapedBytes}.");
    Assert(snapshot.PayloadRowsWithSavings == 2, $"Expected 2 rows with savings, got {snapshot.PayloadRowsWithSavings}.");
    Assert(snapshot.BridgeRequestCount == 5, $"Expected 5 bridge requests, got {snapshot.BridgeRequestCount}.");
    Assert(snapshot.BridgeResponseCount == 4, $"Expected 4 bridge responses, got {snapshot.BridgeResponseCount}.");
    Assert(snapshot.BridgeConnectionCount == 1, $"Expected 1 bridge connection, got {snapshot.BridgeConnectionCount}.");
    Assert(snapshot.SetupCycleCount == 1, $"Expected 1 setup cycle, got {snapshot.SetupCycleCount}.");
    Assert(snapshot.UnmatchedRequestCount == 1, $"Expected 1 unmatched request, got {snapshot.UnmatchedRequestCount}.");
    Assert(snapshot.PackSetTransitionCount == 1, $"Expected 1 pack transition, got {snapshot.PackSetTransitionCount}.");
    Assert(snapshot.ToolSnapshotCount == 2, $"Expected 2 tool snapshot rows, got {snapshot.ToolSnapshotCount}.");
    Assert(snapshot.MinimalHashTransitions == 0, $"Expected 0 minimal transitions, got {snapshot.MinimalHashTransitions}.");
    Assert(snapshot.FullHashTransitions == 1, $"Expected 1 full transition, got {snapshot.FullHashTransitions}.");
    Assert(snapshot.FalseStableMinimalTransitions == 1, $"Expected 1 false-stable transition, got {snapshot.FalseStableMinimalTransitions}.");
    Assert(snapshot.TopSavings.First().Label.Contains("Unity.RunCommand"), "Top savings should include Unity.RunCommand.");
    Assert(snapshot.FailureClasses.Any(row => row.ErrorKind == "boom"), "Failure classes should include boom.");
    Assert(snapshot.SlowOperations.Any(row => row.Label.Contains("Unity.BadProbe")), "Slow operations should include Unity.BadProbe.");
    Assert(snapshot.UnmatchedRequests.Any(row => row.Command == "Unity.HungProbe"), "Unmatched requests should include Unity.HungProbe.");
    Assert(snapshot.BuildClipboardSummary().Contains("Unity MCP Lens Telemetry"), "Clipboard summary should include title.");

    Console.WriteLine("Command Center telemetry scanner smoke passed.");
}
finally
{
    Directory.Delete(projectRoot, recursive: true);
}
'@

        dotnet run --project $scannerProject
        if ($LASTEXITCODE -ne 0) {
            throw "Command Center telemetry scanner smoke failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-CommandCenterTelemetryScannerSmoke
