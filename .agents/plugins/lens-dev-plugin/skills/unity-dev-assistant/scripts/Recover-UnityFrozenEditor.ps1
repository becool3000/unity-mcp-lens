param(
    [string]$ProjectPath = (Get-Location).Path,
    [ValidateSet("DetectOnly", "Kill", "KillAndReopen")]
    [string]$Action = "DetectOnly",
    [int]$ProcessId = 0,
    [string]$UnityEditorPath = "",
    [bool]$WaitForBridgeReady = $true,
    [int]$TimeoutSeconds = 90,
    [ValidateSet("DetectOnly", "UseDisk", "RecoverBackup")]
    [string]$StartupPromptAction = "DetectOnly",
    [ValidateSet("DetectOnly", "Reload", "Ignore", "Auto")]
    [string]$SceneReloadPromptAction = "DetectOnly",
    [string[]]$ExpectedChangedPaths = @()
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Recover-UnityFrozenEditor.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--Action", $Action,
    "--WaitForBridgeReady", ($WaitForBridgeReady.ToString().ToLowerInvariant()),
    "--TimeoutSeconds", [string]$TimeoutSeconds,
    "--StartupPromptAction", $StartupPromptAction,
    "--SceneReloadPromptAction", $SceneReloadPromptAction
)

if ($ProcessId -gt 0) {
    $scriptArgs += @("--ProcessId", [string]$ProcessId)
}
if (-not [string]::IsNullOrWhiteSpace($UnityEditorPath)) {
    $scriptArgs += @("--UnityEditorPath", $UnityEditorPath)
}
foreach ($path in $ExpectedChangedPaths) {
    if (-not [string]::IsNullOrWhiteSpace($path)) {
        $scriptArgs += @("--ExpectedChangedPaths", $path)
    }
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
