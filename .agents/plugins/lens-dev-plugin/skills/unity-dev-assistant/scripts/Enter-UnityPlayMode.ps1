param(
    [string]$ProjectPath = (Get-Location).Path,
    [int]$TimeoutSeconds = 25,
    [double]$PollIntervalSeconds = 1.0,
    [double]$WarmupSeconds = 1.0,
    [switch]$StopFirst,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$PlayRequestTimeoutSeconds = 180
)

. "$PSScriptRoot\UnityDevCommon.ps1"

function Test-UnityPlayReadyDegradedFallback {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 10,
        [Parameter()][double]$PollIntervalSeconds = 1.0,
        [Parameter()][double]$WarmupSeconds = 1.0
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempts = New-Object System.Collections.Generic.List[object]
    $lastState = $null
    $previousUnscaledTime = $null
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $state = Get-UnityEditorState -ProjectPath $ProjectPath -TimeoutSeconds 15
            $lastState = $state
            $snapshot = Get-UnityReadinessSnapshot -EditorState $state

            $timeAdvanced = $false
            if ($null -ne $previousUnscaledTime -and $snapshot.RuntimeProbeUnscaledTime -gt $previousUnscaledTime) {
                $timeAdvanced = $true
            }

            $snapshot.PlayReady = $snapshot.Success -and $snapshot.IsPlaying -and $snapshot.RuntimeProbeAvailable -and $snapshot.RuntimeProbeHasAdvancedFrames -and ($snapshot.RuntimeProbeUpdateCount -ge 10 -or $timeAdvanced)
            $snapshot.RuntimeAdvancedByTime = $timeAdvanced
            $snapshot.DegradedFallback = $true
            $attempts.Add($snapshot)

            if ($snapshot.PlayReady) {
                if ($WarmupSeconds -gt 0) {
                    Start-Sleep -Seconds $WarmupSeconds
                }

                return [ordered]@{
                    success             = $true
                    message             = "Play mode entered and runtime advanced after a delayed reconnect-prone transition."
                    timeoutSeconds      = $TimeoutSeconds
                    pollIntervalSeconds = $PollIntervalSeconds
                    warmupSeconds       = $WarmupSeconds
                    attempts            = $attempts
                    lastState           = $lastState
                    degradedFallback    = $true
                }
            }

            $previousUnscaledTime = $snapshot.RuntimeProbeUnscaledTime
        }
        catch {
            $lastError = $_.Exception.Message
            $attempts.Add([ordered]@{
                Timestamp        = (Get-Date).ToString("o")
                Success          = $false
                Error            = $lastError
                DegradedFallback = $true
            })
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return [ordered]@{
        success             = $false
        message             = "Play mode did not settle during the degraded-path fallback probe."
        timeoutSeconds      = $TimeoutSeconds
        pollIntervalSeconds = $PollIntervalSeconds
        warmupSeconds       = $WarmupSeconds
        attempts            = $attempts
        lastState           = $lastState
        lastError           = $lastError
        degradedFallback    = $true
    }
}

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath

if ($StopFirst) {
    try {
        Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_ManageEditor" -Arguments @{ Action = "Stop" } -TimeoutSeconds 15 | Out-Null
    }
    catch {
    }
}

$idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
if (-not $idleWait.success) {
    [ordered]@{
        success  = $false
        message  = "Unity editor did not become idle before play."
        idleWait = $idleWait
    } | ConvertTo-Json -Depth 30
    exit 1
}

$playResponse = $null
$playError = $null

try {
    $playResponse = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_ManageEditor" -Arguments @{ Action = "Play" } -TimeoutSeconds $PlayRequestTimeoutSeconds
}
catch {
    $playError = $_.Exception.Message
}

$playReady = Wait-UnityPlayReady -ProjectPath $resolvedProjectPath -TimeoutSeconds $TimeoutSeconds -PollIntervalSeconds $PollIntervalSeconds -WarmupSeconds $WarmupSeconds
$playResponseObject = if ($null -ne $playResponse) { Get-UnityToolObject -Response $playResponse } else { $null }
$playRequestErrorMessage = if (-not [string]::IsNullOrWhiteSpace($playError)) { $playError } elseif ($playResponseObject -and -not [string]::IsNullOrWhiteSpace($playResponseObject.error)) { [string]$playResponseObject.error } else { $null }
$playRequestWasReconnectProne = (-not [string]::IsNullOrWhiteSpace($playRequestErrorMessage) -and $playRequestErrorMessage.IndexOf("Connection disconnected", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or ($playResponseObject -and $playResponseObject.data -and ($playResponseObject.data.ReconnectExpected -eq $true -or $playResponseObject.data.TransitionState -eq "transitioning_to_play"))
$degradedPath = $false
$finalMessage = $playReady.message
$degradedFallback = $null

if (-not $playReady.success -and $playRequestWasReconnectProne) {
    $fallbackTimeoutSeconds = [Math]::Max(6, [Math]::Ceiling([Math]::Max($WarmupSeconds, 1.0) + 6))
    $degradedFallback = Test-UnityPlayReadyDegradedFallback -ProjectPath $resolvedProjectPath -TimeoutSeconds $fallbackTimeoutSeconds -PollIntervalSeconds $PollIntervalSeconds -WarmupSeconds $WarmupSeconds
    if ($degradedFallback.success) {
        $playReady = $degradedFallback
        $degradedPath = $true
        $finalMessage = $degradedFallback.message
    }
}

if ($playReady.success) {
    if (-not $degradedPath -and -not [string]::IsNullOrWhiteSpace($playRequestErrorMessage) -and $playRequestErrorMessage.IndexOf("Connection disconnected", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        $degradedPath = $true
        $finalMessage = "Play mode entered and runtime advanced after the initial play request disconnected."
    }
    elseif (-not $degradedPath -and $playResponseObject -and $playResponseObject.data -and $playResponseObject.data.ReconnectExpected -eq $true) {
        $degradedPath = $true
        $finalMessage = "Play mode entered and runtime advanced after an expected reconnect-prone play transition."
    }
}

[ordered]@{
    success      = $playReady.success
    message      = $finalMessage
    idleWait     = $idleWait
    degradedPath = $degradedPath
    playRequestTimeoutSeconds = $PlayRequestTimeoutSeconds
    playRequestWasReconnectProne = $playRequestWasReconnectProne
    playRequestErrorMessage = $playRequestErrorMessage
    playResponse = $playResponseObject
    playError    = $playError
    playReady    = $playReady
    degradedFallback = $degradedFallback
} | ConvertTo-Json -Depth 30

if ($playReady.success) {
    exit 0
}

exit 1
