param(
    [string]$HostPath,
    [string]$OutputDir,
    [int]$FakeFullToolCount = 80
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($HostPath)) {
    $HostPath = Join-Path (Split-Path -Parent $PSScriptRoot) "UnityMcpLensApp~\src\UnityMcpLens\bin\Debug\net8.0\UnityMcpLens.exe"
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path (Split-Path -Parent $PSScriptRoot) "artifacts"
}

if (-not (Test-Path -LiteralPath $HostPath)) {
    throw "Unity MCP Lens host was not found at '$HostPath'. Run dotnet build unity-mcp-lens.sln first."
}

$nodeArgs = @(
    (Join-Path $PSScriptRoot "Export-McpToolSurfaceModeEvidence.js"),
    "--host-path",
    ([System.IO.Path]::GetFullPath($HostPath)),
    "--output-dir",
    ([System.IO.Path]::GetFullPath($OutputDir)),
    "--fake-full-tool-count",
    $FakeFullToolCount
)

node @nodeArgs
