param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter()][string]$StepsJson,
    [Parameter()][string]$StepsPath,
    [int]$TimeoutSeconds = 45
)

. "$PSScriptRoot\UnityDevCommon.ps1"

if ([string]::IsNullOrWhiteSpace($StepsJson) -and [string]::IsNullOrWhiteSpace($StepsPath)) {
    throw "Provide -StepsJson or -StepsPath."
}

$nodePath = (Get-Command node -ErrorAction Stop).Source
$bridgeScriptsDir = Join-Path (Get-UnityBridgeSkillPath) "scripts"
$scriptPath = Join-Path $bridgeScriptsDir "Invoke-UnityMcpBatch.js"
$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath

$scriptArgs = @(
    $scriptPath,
    "--ProjectPath", $resolvedProjectPath,
    "--TimeoutSeconds", [string]$TimeoutSeconds
)

if (-not [string]::IsNullOrWhiteSpace($StepsPath)) {
    $scriptArgs += @("--StepsPath", (Resolve-Path -LiteralPath $StepsPath).Path)
}
else {
    $scriptArgs += @("--StepsJson", $StepsJson)
}

& $nodePath @scriptArgs
exit $LASTEXITCODE
