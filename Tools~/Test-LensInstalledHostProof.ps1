param(
    [string]$ProjectPath,
    [string]$HostPath,
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$ExpectedVersion,
    [string[]]$ExpectedTools = @(),
    [string]$OutputPath,
    [int]$TimeoutMs = 20000,
    [switch]$ReportOnly,
    [switch]$SkipListFacade
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = $env:UNITY_MCP_PROJECT_PATH
}
if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = (Get-Location).Path
}

if ([string]::IsNullOrWhiteSpace($HostPath)) {
    if ($IsWindows -or $env:OS -eq "Windows_NT") {
        $HostPath = Join-Path $env:USERPROFILE ".unity\unity-mcp-lens\unity_mcp_lens_win.exe"
    }
    elseif ($IsMacOS) {
        $hostName = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            "unity_mcp_lens_mac_arm64"
        }
        else {
            "unity_mcp_lens_mac_x64"
        }
        $HostPath = Join-Path $HOME ".unity/unity-mcp-lens/$hostName"
    }
    else {
        $HostPath = Join-Path $HOME ".unity/unity-mcp-lens/unity_mcp_lens_linux"
    }
}

if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $metadataPath = Join-Path (Join-Path $PackageRoot "UnityMcpLensApp~") "unity-mcp-lens.json"
    if (Test-Path -LiteralPath $metadataPath -PathType Leaf) {
        try {
            $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
            $ExpectedVersion = [string]$metadata.version
        }
        catch {
            $ExpectedVersion = ""
        }
    }
}

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Test-LensInstalledHostProof.js"
$scriptArgs = @(
    $scriptPath,
    "--host-path", ([System.IO.Path]::GetFullPath($HostPath)),
    "--project-path", ([System.IO.Path]::GetFullPath($ProjectPath)),
    "--timeout-ms", [string]$TimeoutMs
)

if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $scriptArgs += @("--expected-version", $ExpectedVersion)
}

foreach ($toolName in $ExpectedTools) {
    if (-not [string]::IsNullOrWhiteSpace($toolName)) {
        $scriptArgs += @("--expected-tools", $toolName)
    }
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $scriptArgs += @("--output-path", ([System.IO.Path]::GetFullPath($OutputPath))
)
}

if ($ReportOnly) {
    $scriptArgs += "--report-only"
}

if ($SkipListFacade) {
    $scriptArgs += @("--call-list-facade", "false")
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
