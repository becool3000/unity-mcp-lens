param(
    [string]$ProjectPath,
    [string]$HostPath,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = $env:UNITY_MCP_PROJECT_PATH
}
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Get-Location).Path
}

if ([string]::IsNullOrWhiteSpace($HostPath)) {
    $HostPath = Join-Path $env:USERPROFILE ".unity\unity-mcp-lens\unity_mcp_lens_win.exe"
}

if (-not (Test-Path -LiteralPath $HostPath)) {
    throw "Unity MCP Lens installed host was not found at '$HostPath'. Run the package Install/Refresh Lens Server action first."
}
if (-not (Test-Path -LiteralPath $ProjectPath)) {
    throw "Unity project path was not found at '$ProjectPath'."
}

$nodeArgs = @(
    (Join-Path $PSScriptRoot "Export-McpDynamicToolIndexingEvidence.js"),
    "--host-path",
    ([System.IO.Path]::GetFullPath($HostPath)),
    "--project-path",
    ([System.IO.Path]::GetFullPath($ProjectPath))
)

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $nodeArgs += @("--output-path", ([System.IO.Path]::GetFullPath($OutputPath)))
}

node @nodeArgs
