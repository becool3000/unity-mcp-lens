param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$ServerName = "unity-mcp",
    [int]$EditorLogTail = 400,
    [switch]$IncludeDiagnostics,
    [int]$MaxWrapperStatusInstances = 5
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Check-UnityMcp.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--ServerName", $ServerName,
    "--EditorLogTail", [string]$EditorLogTail,
    "--MaxWrapperStatusInstances", [string]$MaxWrapperStatusInstances
)

if ($IncludeDiagnostics) {
    $scriptArgs += "--IncludeDiagnostics"
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
