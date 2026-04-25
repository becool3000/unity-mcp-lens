param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$ExpectedScenes = @(),
    [string]$MonitorBuildMode = "",
    [string]$BuildOutputPath,
    [string]$BuildReportPath,
    [string]$SuccessArtifactPath,
    [switch]$IncludeDiagnostics,
    [int]$MaxWrapperStatusInstances = 5
)

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Check-UnityDevSession.js"
$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $ProjectPath,
    "--MaxWrapperStatusInstances", [string]$MaxWrapperStatusInstances
)

foreach ($scene in $ExpectedScenes) {
    if (-not [string]::IsNullOrWhiteSpace($scene)) {
        $scriptArgs += @("--ExpectedScenes", $scene)
    }
}

if (-not [string]::IsNullOrWhiteSpace($MonitorBuildMode)) {
    $scriptArgs += @("--MonitorBuildMode", $MonitorBuildMode)
}
if (-not [string]::IsNullOrWhiteSpace($BuildOutputPath)) {
    $scriptArgs += @("--BuildOutputPath", $BuildOutputPath)
}
if (-not [string]::IsNullOrWhiteSpace($BuildReportPath)) {
    $scriptArgs += @("--BuildReportPath", $BuildReportPath)
}
if (-not [string]::IsNullOrWhiteSpace($SuccessArtifactPath)) {
    $scriptArgs += @("--SuccessArtifactPath", $SuccessArtifactPath)
}
if ($IncludeDiagnostics) {
    $scriptArgs += "--IncludeDiagnostics"
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
