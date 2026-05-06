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
$temporaryStepsPath = $null

try {
    $scriptArgs = @(
        $scriptPath,
        "--ProjectPath", $resolvedProjectPath,
        "--TimeoutSeconds", [string]$TimeoutSeconds
    )

    if (-not [string]::IsNullOrWhiteSpace($StepsPath)) {
        $scriptArgs += @("--StepsPath", (Resolve-Path -LiteralPath $StepsPath).Path)
    }
    else {
        $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "codex-unity"
        New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
        $temporaryStepsPath = Join-Path $tempDirectory ("unity-mcp-batch-steps-" + [guid]::NewGuid().ToString("N") + ".json")
        Set-Content -LiteralPath $temporaryStepsPath -Value $StepsJson -Encoding UTF8
        $scriptArgs += @("--StepsPath", $temporaryStepsPath)
    }

    & $nodePath @scriptArgs
    exit $LASTEXITCODE
}
finally {
    if ($temporaryStepsPath -and (Test-Path -LiteralPath $temporaryStepsPath)) {
        Remove-Item -LiteralPath $temporaryStepsPath -Force -ErrorAction SilentlyContinue
    }
}
