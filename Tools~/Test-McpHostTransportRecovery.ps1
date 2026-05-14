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
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Invoke-CommandCenterStatusScannerSmoke
