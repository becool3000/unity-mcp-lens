param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$Code,
    [string]$CodePath,
    [string]$Title = "",
    [string[]]$Using = @(),
    [int]$TimeoutSeconds = 60,
    [object]$PausePlayMode = $false,
    [int]$StepFrames = 0,
    [object]$RestorePauseState = $true,
    [bool]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [string]$MonitorBuildMode = "",
    [string]$BuildOutputPath,
    [string]$BuildReportPath,
    [string]$SuccessArtifactPath,
    [int]$BuildTimeoutSeconds = 1800,
    [double]$BuildPollIntervalSeconds = 5.0
)

function ConvertTo-BoolFlag {
    param(
        [object]$Value,
        [bool]$Default = $false
    )

    if ($null -eq $Value) {
        return $Default
    }

    if ($Value -is [bool]) {
        return [bool]$Value
    }

    if ($Value -is [int]) {
        return $Value -ne 0
    }

    if ($Value -is [System.Management.Automation.SwitchParameter]) {
        return $Value.IsPresent
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $Default
    }

    switch ($text.Trim().ToLowerInvariant()) {
        "1" { return $true }
        "true" { return $true }
        "yes" { return $true }
        "on" { return $true }
        "0" { return $false }
        "false" { return $false }
        "no" { return $false }
        "off" { return $false }
        default { return $Default }
    }
}

if ([string]::IsNullOrWhiteSpace($Code) -and [string]::IsNullOrWhiteSpace($CodePath)) {
    throw "Provide -Code or -CodePath."
}

$nodePath = (Get-Command node -ErrorAction Stop).Source
$scriptPath = Join-Path $PSScriptRoot "Invoke-UnityRunCommand.js"
$temporaryCodePath = $null

try {
    $effectiveCodePath = $CodePath
    if ([string]::IsNullOrWhiteSpace($effectiveCodePath)) {
        $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "codex-unity"
        New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
        $temporaryCodePath = Join-Path $tempDirectory ("run-command-" + [guid]::NewGuid().ToString("N") + ".csx")
        Set-Content -LiteralPath $temporaryCodePath -Value $Code -Encoding UTF8
        $effectiveCodePath = $temporaryCodePath
    }

    $scriptArgs = @(
        $scriptPath,
        "--ProjectPath", $ProjectPath,
        "--CodePath", $effectiveCodePath,
        "--TimeoutSeconds", [string]$TimeoutSeconds,
        "--PausePlayMode", [string](ConvertTo-BoolFlag -Value $PausePlayMode -Default $false),
        "--StepFrames", [string]$StepFrames,
        "--RestorePauseState", [string](ConvertTo-BoolFlag -Value $RestorePauseState -Default $true),
        "--WaitForEditorIdle", [string]$WaitForEditorIdle,
        "--IdleTimeoutSeconds", [string]$IdleTimeoutSeconds,
        "--IdleStablePollCount", [string]$IdleStablePollCount,
        "--IdlePollIntervalSeconds", [string]$IdlePollIntervalSeconds,
        "--PostIdleDelaySeconds", [string]$PostIdleDelaySeconds,
        "--BuildTimeoutSeconds", [string]$BuildTimeoutSeconds,
        "--BuildPollIntervalSeconds", [string]$BuildPollIntervalSeconds
    )

    if (-not [string]::IsNullOrWhiteSpace($Title)) {
        $scriptArgs += @("--Title", $Title)
    }
    foreach ($usingEntry in $Using) {
        if (-not [string]::IsNullOrWhiteSpace($usingEntry)) {
            $scriptArgs += @("--Using", $usingEntry)
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

    & $nodePath @scriptArgs
    exit $LASTEXITCODE
}
finally {
    if ($temporaryCodePath -and (Test-Path -LiteralPath $temporaryCodePath)) {
        Remove-Item -LiteralPath $temporaryCodePath -Force -ErrorAction SilentlyContinue
    }
}
