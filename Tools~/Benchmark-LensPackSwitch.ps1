<#
Usage:
  .\Tools~\Benchmark-LensPackSwitch.ps1 -ProjectPath C:\Path\To\UnityProject
  .\Tools~\Benchmark-LensPackSwitch.ps1 -ProjectPath C:\Path\To\UnityProject -ServerPath C:\Users\you\.unity\unity-mcp-lens\unity_mcp_lens_win.exe
  .\Tools~\Benchmark-LensPackSwitch.ps1 -ProjectPath C:\Path\To\UnityProject -AsJson

Notes:
  - The benchmark targets the owned unity-mcp-lens stdio server.
  - Run it while the Unity editor is idle and the host project is not producing unrelated MCP traffic.
  - The wrapper shells out to a small net8.0 helper app under Tools~.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [string]$ServerPath,
    [switch]$AsJson,
    [int]$RpcTimeoutSeconds = 30,
    [int]$StatsSettleMilliseconds = 800
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    if ($PSScriptRoot) {
        return (Split-Path -Parent $PSScriptRoot)
    }

    return (Get-Location).Path
}

$repoRoot = Get-RepoRoot
$resolvedProjectPath = [System.IO.Path]::GetFullPath($ProjectPath)
if (-not (Test-Path -LiteralPath $resolvedProjectPath)) {
    throw "Project path not found: $resolvedProjectPath"
}

$resolvedServerPath = if ([string]::IsNullOrWhiteSpace($ServerPath)) {
    [System.IO.Path]::GetFullPath((Join-Path $env:USERPROFILE ".unity\unity-mcp-lens\unity_mcp_lens_win.exe"))
}
else {
    [System.IO.Path]::GetFullPath($ServerPath)
}

if (-not (Test-Path -LiteralPath $resolvedServerPath)) {
    throw "unity-mcp-lens server not found: $resolvedServerPath"
}

$benchmarkProject = Join-Path $repoRoot "Tools~\UnityMcpLensPackSwitchBenchApp~\UnityMcpLensPackSwitchBench.csproj"
if (-not (Test-Path -LiteralPath $benchmarkProject)) {
    throw "Benchmark helper project not found: $benchmarkProject"
}

$arguments = @(
    "run",
    "--project", $benchmarkProject,
    "-c", "Release",
    "-p:UseAppHost=false",
    "--",
    "--project-path", $resolvedProjectPath,
    "--server-path", $resolvedServerPath,
    "--rpc-timeout-seconds", [string]$RpcTimeoutSeconds,
    "--stats-settle-ms", [string]$StatsSettleMilliseconds
)

if ($AsJson) {
    $arguments += "--json"
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Benchmark helper failed with exit code $LASTEXITCODE."
}
