param(
    [string]$ProjectPath = (Get-Location).Path,
    [int]$ProcessId = 0,
    [switch]$IncludeButtons,
    [int]$MaxItems = 8,
    [string[]]$KnownPatterns = @(),
    [int]$TimeoutSeconds = 8
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Get-UnityNativeModal.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--MaxItems", [string]$MaxItems,
    "--TimeoutSeconds", [string]$TimeoutSeconds
)

if ($ProcessId -gt 0) {
    $scriptArgs += @("--ProcessId", [string]$ProcessId)
}
if ($IncludeButtons) {
    $scriptArgs += @("--IncludeButtons", "true")
}
foreach ($pattern in $KnownPatterns) {
    if (-not [string]::IsNullOrWhiteSpace($pattern)) {
        $scriptArgs += @("--KnownPatterns", $pattern)
    }
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
