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

. "$PSScriptRoot\UnityDevCommon.ps1"

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

if (-not [string]::IsNullOrWhiteSpace($CodePath)) {
    $Code = Get-Content -Path $CodePath -Raw
}

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$idleResult = $null
$PausePlayMode = ConvertTo-BoolFlag -Value $PausePlayMode -Default $false
$RestorePauseState = ConvertTo-BoolFlag -Value $RestorePauseState -Default $true
$monitorBuild = -not [string]::IsNullOrWhiteSpace($MonitorBuildMode)

if ($monitorBuild -and $MonitorBuildMode -ne "WebGL") {
    throw "Unsupported -MonitorBuildMode '$MonitorBuildMode'. Supported values: WebGL."
}

if ($WaitForEditorIdle) {
    $idleResult = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
    if (-not $idleResult.success) {
        [ordered]@{
            success    = $false
            error      = $idleResult.message
            editorIdle = $idleResult
        } | ConvertTo-Json -Depth 30
        exit 1
    }
}

$result = $null
$exitCode = 0

try {
    $result = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $Code -Title $Title -Usings $Using -TimeoutSeconds $TimeoutSeconds -PausePlayMode $PausePlayMode -StepFrames $StepFrames -RestorePauseState $RestorePauseState
}
catch {
    if (-not $monitorBuild) {
        throw
    }

    $runCommandError = $_.Exception.Message
    $initialMonitorState = Get-UnityBuildMonitorState -ProjectPath $resolvedProjectPath -Mode $MonitorBuildMode -BuildOutputPath $BuildOutputPath -BuildReportPath $BuildReportPath -SuccessArtifactPath $SuccessArtifactPath

    if ($initialMonitorState.Status -eq "Succeeded") {
        $result = [ordered]@{
            success             = $true
            monitorFallbackUsed = $true
            message             = "Unity build completed after the MCP response path became unavailable."
            runCommandError     = $runCommandError
            buildMonitor        = $initialMonitorState
        }
    }
    elseif ($initialMonitorState.Status -eq "Failed") {
        $result = [ordered]@{
            success             = $false
            monitorFallbackUsed = $true
            error               = $initialMonitorState.Summary
            runCommandError     = $runCommandError
            buildMonitor        = $initialMonitorState
        }
        $exitCode = 1
    }
    elseif ($initialMonitorState.Status -eq "InProgress") {
        $monitorWait = Wait-UnityBuildMonitor -ProjectPath $resolvedProjectPath -Mode $MonitorBuildMode -BuildOutputPath $BuildOutputPath -BuildReportPath $BuildReportPath -SuccessArtifactPath $SuccessArtifactPath -TimeoutSeconds $BuildTimeoutSeconds -PollIntervalSeconds $BuildPollIntervalSeconds
        $result = [ordered]@{
            success             = $monitorWait.success
            monitorFallbackUsed = $true
            message             = $monitorWait.message
            runCommandError     = $runCommandError
            buildMonitor        = $monitorWait
        }
        if (-not $monitorWait.success) {
            $result.error = $monitorWait.message
            $exitCode = 1
        }
    }
    else {
        throw
    }
}

if ($null -ne $idleResult) {
    $result | Add-Member -NotePropertyName editorIdle -NotePropertyValue $idleResult -Force
}
$result | ConvertTo-Json -Depth 30
exit $exitCode
