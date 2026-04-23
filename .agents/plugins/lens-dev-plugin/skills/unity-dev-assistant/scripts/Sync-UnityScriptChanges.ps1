param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$ChangedPaths = @(),
    [int]$NaturalDetectTimeoutSeconds = 6,
    [int]$ForcedDetectTimeoutSeconds = 20,
    [int]$ReloadTimeoutSeconds = 120,
    [int]$ReloadMarkerTtlSeconds = 120,
    [int]$ForceRefreshTimeoutSeconds = 30,
    [int]$IdleStablePollCount = 3,
    [double]$PollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Sync-UnityScriptChanges.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--NaturalDetectTimeoutSeconds", [string]$NaturalDetectTimeoutSeconds,
    "--ForcedDetectTimeoutSeconds", [string]$ForcedDetectTimeoutSeconds,
    "--ReloadTimeoutSeconds", [string]$ReloadTimeoutSeconds,
    "--ReloadMarkerTtlSeconds", [string]$ReloadMarkerTtlSeconds,
    "--ForceRefreshTimeoutSeconds", [string]$ForceRefreshTimeoutSeconds,
    "--IdleStablePollCount", [string]$IdleStablePollCount,
    "--PollIntervalSeconds", [string]$PollIntervalSeconds,
    "--PostIdleDelaySeconds", [string]$PostIdleDelaySeconds
)

foreach ($changedPath in $ChangedPaths) {
    if (-not [string]::IsNullOrWhiteSpace($changedPath)) {
        $scriptArgs += @("--ChangedPaths", $changedPath)
    }
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
