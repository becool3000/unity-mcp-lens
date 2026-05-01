param(
    [string]$ProjectPath = (Get-Location).Path,
    [int]$ProcessId = 0,
    [bool]$IncludeWindows = $true,
    [bool]$IncludeBridgeStatus = $true,
    [int]$StaleReadySeconds = 30,
    [int]$MaxItems = 8,
    [int]$TimeoutSeconds = 8
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Get-UnityFrozenEditor.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--IncludeWindows", ($IncludeWindows.ToString().ToLowerInvariant()),
    "--IncludeBridgeStatus", ($IncludeBridgeStatus.ToString().ToLowerInvariant()),
    "--StaleReadySeconds", [string]$StaleReadySeconds,
    "--MaxItems", [string]$MaxItems,
    "--TimeoutSeconds", [string]$TimeoutSeconds
)

if ($ProcessId -gt 0) {
    $scriptArgs += @("--ProcessId", [string]$ProcessId)
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
