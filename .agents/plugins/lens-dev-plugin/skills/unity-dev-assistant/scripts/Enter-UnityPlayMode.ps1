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
    [int]$PlayRequestTimeoutSeconds = 180,
    [switch]$IncludeDetails
)

. "$PSScriptRoot\UnityDevCommon.ps1"

function Get-ObjectPropertyValue {
    param(
        [Parameter()][object]$InputObject,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    if ($null -eq $InputObject) {
        return $null
    }

    if ($InputObject -is [System.Collections.IDictionary]) {
        foreach ($name in $Names) {
            if ($InputObject.Contains($name)) {
                return $InputObject[$name]
            }
        }

        foreach ($name in $Names) {
            foreach ($key in $InputObject.Keys) {
                if ([string]::Equals([string]$key, $name, [System.StringComparison]::OrdinalIgnoreCase)) {
                    return $InputObject[$key]
                }
            }
        }
    }

    foreach ($name in $Names) {
        $property = $InputObject.PSObject.Properties[$name]
        if ($null -ne $property) {
            return $property.Value
        }
    }

    return $null
}

function Get-LastCollectionItem {
    param([Parameter()][object]$InputObject)

    if ($null -eq $InputObject -or $InputObject -is [string]) {
        return $null
    }

    if ($InputObject -is [System.Collections.IEnumerable]) {
        $last = $null
        foreach ($item in $InputObject) {
            $last = $item
        }

        return $last
    }

    return $null
}

function ConvertTo-UnityToolResultSummary {
    param([Parameter()][object]$ToolResult)

    if ($null -eq $ToolResult) {
        return $null
    }

    $data = Get-ObjectPropertyValue -InputObject $ToolResult -Names @("data", "Data")
    [ordered]@{
        success           = (Get-ObjectPropertyValue -InputObject $ToolResult -Names @("success", "Success")) -eq $true
        message           = (Get-ObjectPropertyValue -InputObject $ToolResult -Names @("message", "Message", "error", "Error"))
        transitionState   = Get-ObjectPropertyValue -InputObject $data -Names @("transitionState", "TransitionState")
        reconnectExpected = (Get-ObjectPropertyValue -InputObject $data -Names @("reconnectExpected", "ReconnectExpected")) -eq $true
        runtimeAdvanced   = (Get-ObjectPropertyValue -InputObject $data -Names @("runtimeAdvanced", "RuntimeAdvanced")) -eq $true
        timedOut          = (Get-ObjectPropertyValue -InputObject $data -Names @("timedOut", "TimedOut")) -eq $true
        consoleErrorCount = Get-ObjectPropertyValue -InputObject $data -Names @("consoleErrorCount", "ConsoleErrorCount")
    }
}

function ConvertTo-UnityPlayReadySummary {
    param([Parameter()][object]$Result)

    if ($null -eq $Result) {
        return $null
    }

    $finalState = Get-ObjectPropertyValue -InputObject $Result -Names @("finalState", "FinalState")
    $lastState = Get-ObjectPropertyValue -InputObject $Result -Names @("lastState", "LastState")
    $lastStateData = Get-ObjectPropertyValue -InputObject $lastState -Names @("data", "Data")
    $attempts = Get-ObjectPropertyValue -InputObject $Result -Names @("attempts", "Attempts")
    $lastAttempt = Get-LastCollectionItem -InputObject $attempts
    $editorIdle = Get-ObjectPropertyValue -InputObject $Result -Names @("editorIdle", "EditorIdle", "isEditorIdle", "IsEditorIdle")
    if ($null -eq $editorIdle) {
        $editorIdle = Get-ObjectPropertyValue -InputObject $lastAttempt -Names @("IdleReady", "idleReady")
    }

    $isPlaying = Get-ObjectPropertyValue -InputObject $Result -Names @("isPlaying", "IsPlaying")
    if ($null -eq $isPlaying) {
        $isPlaying = Get-ObjectPropertyValue -InputObject $lastStateData -Names @("IsPlaying", "isPlaying")
    }
    if ($null -eq $isPlaying) {
        $isPlaying = Get-ObjectPropertyValue -InputObject $lastAttempt -Names @("IsPlaying", "isPlaying")
    }

    $finalIsPlaying = Get-ObjectPropertyValue -InputObject $finalState -Names @("isPlaying", "IsPlaying")
    if ($null -eq $finalIsPlaying) {
        $finalIsPlaying = Get-ObjectPropertyValue -InputObject $lastStateData -Names @("IsPlaying", "isPlaying")
    }

    $runtimeAdvanced = Get-ObjectPropertyValue -InputObject $Result -Names @("runtimeAdvanced", "RuntimeAdvanced")
    if ($null -eq $runtimeAdvanced) {
        $runtimeAdvanced = Get-ObjectPropertyValue -InputObject $lastAttempt -Names @("PlayReady", "playReady", "RuntimeProbeHasAdvancedFrames", "runtimeProbeHasAdvancedFrames")
    }

    [ordered]@{
        success          = ConvertTo-UnityBool -Value (Get-ObjectPropertyValue -InputObject $Result -Names @("success", "Success")) -Default $false
        message          = Get-ObjectPropertyValue -InputObject $Result -Names @("message", "Message", "error", "Error")
        degradedFallback = (Get-ObjectPropertyValue -InputObject $Result -Names @("degradedFallback", "DegradedFallback")) -eq $true
        editorIdle       = $editorIdle
        isPlaying        = $isPlaying
        finalIsPlaying   = $finalIsPlaying
        runtimeAdvanced  = $runtimeAdvanced
    }
}

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
$sourceIntegrity = Test-UnitySourceFileIntegrity -ProjectPath $resolvedProjectPath
if (-not $sourceIntegrity.success) {
    [ordered]@{
        success         = $false
        message         = "Unity source integrity check failed before play-mode entry."
        sourceIntegrity = $sourceIntegrity
    } | ConvertTo-Json -Depth 30
    exit 1
}

if ($StopFirst) {
    try {
        Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Editor_SetPlayMode" -Arguments @{
            Mode                  = "exit"
            TimeoutSeconds        = [Math]::Max(1, $IdleTimeoutSeconds)
            WaitForRuntimeAdvance = $false
            UnpauseBeforeExit     = $true
        } -TimeoutSeconds ([Math]::Max(15, $IdleTimeoutSeconds + 10)) | Out-Null
    }
    catch {
    }
}

$idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
if (-not $idleWait.success) {
    [ordered]@{
        success         = $false
        message         = "Unity editor did not become idle before play."
        sourceIntegrity = $sourceIntegrity
        idleWait        = $idleWait
    } | ConvertTo-Json -Depth 30
    exit 1
}

$playResponse = $null
$playError = $null

try {
    $playResponse = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Editor_SetPlayMode" -Arguments @{
        Mode                  = "enter"
        StopFirst             = $StopFirst.IsPresent
        WaitForRuntimeAdvance = $true
        WarmupSeconds         = $WarmupSeconds
        TimeoutSeconds        = $TimeoutSeconds
        UnpauseBeforeExit     = $true
    } -TimeoutSeconds $PlayRequestTimeoutSeconds
}
catch {
    $playError = $_.Exception.Message
}

$playReady = Wait-UnityPlayReady -ProjectPath $resolvedProjectPath -TimeoutSeconds $TimeoutSeconds -PollIntervalSeconds $PollIntervalSeconds -WarmupSeconds $WarmupSeconds
$playResponseObject = if ($null -ne $playResponse) { Get-UnityToolObject -Response $playResponse } else { $null }
$playRequestErrorMessage = if (-not [string]::IsNullOrWhiteSpace($playError)) { $playError } elseif ($playResponseObject -and -not [string]::IsNullOrWhiteSpace($playResponseObject.error)) { [string]$playResponseObject.error } else { $null }
$playRequestWasReconnectProne = (-not [string]::IsNullOrWhiteSpace($playRequestErrorMessage) -and $playRequestErrorMessage.IndexOf("Connection disconnected", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) -or ($playResponseObject -and $playResponseObject.data -and ($playResponseObject.data.ReconnectExpected -eq $true -or $playResponseObject.data.TransitionState -eq "transitioning_to_play" -or $playResponseObject.data.TransitionState -eq "enter_requested_after_response"))
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

$playReadySucceeded = ConvertTo-UnityBool -Value (Get-ObjectPropertyValue -InputObject $playReady -Names @("success", "Success")) -Default $false

if ($playReadySucceeded) {
    if (-not $degradedPath -and -not [string]::IsNullOrWhiteSpace($playRequestErrorMessage) -and $playRequestErrorMessage.IndexOf("Connection disconnected", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
        $degradedPath = $true
        $finalMessage = "Play mode entered and runtime advanced after the initial play request disconnected."
    }
    elseif (-not $degradedPath -and $playResponseObject -and $playResponseObject.data -and $playResponseObject.data.ReconnectExpected -eq $true) {
        $degradedPath = $true
        $finalMessage = "Play mode entered and runtime advanced after an expected reconnect-prone play transition."
    }
}

$consoleErrors = $null
if (-not $playReadySucceeded) {
    try {
        $consoleErrors = Get-UnityConsoleEntries `
            -ProjectPath $resolvedProjectPath `
            -Types @("Error") `
            -Count 10 `
            -Format "Summary" `
            -IncludeStacktrace:$false `
            -TimeoutSeconds 20
    }
    catch {
        $consoleErrors = [pscustomobject]@{
            success = $false
            error   = $_.Exception.Message
        }
    }
}

$includeFullDetails = $IncludeDetails.IsPresent -or (-not $playReadySucceeded)
$detailMode = if ($includeFullDetails) { "full" } else { "compact" }
$playResponseOutput = if ($includeFullDetails) { $playResponseObject } else { ConvertTo-UnityToolResultSummary -ToolResult $playResponseObject }
$playReadyOutput = if ($includeFullDetails) { $playReady } else { ConvertTo-UnityPlayReadySummary -Result $playReady }
$degradedFallbackOutput = if ($includeFullDetails) { $degradedFallback } else { ConvertTo-UnityPlayReadySummary -Result $degradedFallback }

if ($playReadySucceeded -and $playReadyOutput -is [System.Collections.IDictionary]) {
    $playReadyOutput["success"] = $true
}

[ordered]@{
    success      = $playReadySucceeded
    message      = $finalMessage
    sourceIntegrity = $sourceIntegrity
    idleWait     = $idleWait
    detailMode   = $detailMode
    degradedPath = $degradedPath
    playRequestTimeoutSeconds = $PlayRequestTimeoutSeconds
    playRequestWasReconnectProne = $playRequestWasReconnectProne
    playRequestErrorMessage = $playRequestErrorMessage
    playResponse = $playResponseOutput
    playError    = $playError
    playReady    = $playReadyOutput
    degradedFallback = $degradedFallbackOutput
    consoleErrors = $consoleErrors
} | ConvertTo-Json -Depth 30

if ($playReadySucceeded) {
    exit 0
}

exit 1
