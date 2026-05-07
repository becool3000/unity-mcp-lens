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
node (Join-Path $PSScriptRoot "Test-McpDynamicToolExposure.js")
