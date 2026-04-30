param(
    [string]$ProjectPath = (Get-Location).Path,
    [ValidateSet("DetectOnly", "Reload", "Ignore", "Auto")]
    [string]$Action = "DetectOnly",
    [int]$ProcessId = 0,
    [string[]]$ExpectedChangedPaths = @(),
    [int]$TimeoutSeconds = 10,
    [bool]$WaitForBridgeReady = $true
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Resolve-UnitySceneReloadPrompt.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--Action", $Action,
    "--TimeoutSeconds", [string]$TimeoutSeconds,
    "--WaitForBridgeReady", ($WaitForBridgeReady.ToString().ToLowerInvariant())
)

if ($ProcessId -gt 0) {
    $scriptArgs += @("--ProcessId", [string]$ProcessId)
}
foreach ($path in $ExpectedChangedPaths) {
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        $scriptArgs += @("--ExpectedChangedPaths", $path)
    }
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
