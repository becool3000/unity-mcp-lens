param(
    [string]$ProjectPath = (Get-Location).Path,
    [int]$TimeoutSeconds = 60,
    [double]$PollIntervalSeconds = 0.5,
    [int]$StablePollCount = 3,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$ExitRequestTimeoutSeconds = 30
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$exitResponse = $null
$exitResult = $null
$exitError = $null

try {
    $exitResponse = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Editor_ExitPlayMode" -Arguments @{
        WaitForStableEditor = $false
        TimeoutMs           = [Math]::Max(1000, $ExitRequestTimeoutSeconds * 1000)
        PollIntervalMs      = [Math]::Max(50, [int][Math]::Round($PollIntervalSeconds * 1000))
        StablePollCount     = [Math]::Max(1, $StablePollCount)
        PostStableDelayMs   = [Math]::Max(0, [int][Math]::Round($PostIdleDelaySeconds * 1000))
        UnpauseBeforeExit   = $true
    } -TimeoutSeconds $ExitRequestTimeoutSeconds
    $exitResult = Get-UnityToolObject -Response $exitResponse
}
catch {
    $exitError = $_.Exception.Message
}

$idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $TimeoutSeconds -StablePollCount $StablePollCount -PollIntervalSeconds $PollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
$recoveredTransition = (-not [string]::IsNullOrWhiteSpace($exitError)) -and $idleWait.success
$exitSucceeded = ($exitResult -and $exitResult.success -eq $true) -or $recoveredTransition
$success = $exitSucceeded -and $idleWait.success

$message = if ($success -and $recoveredTransition) {
    "Play-mode exit completed after a recoverable transport closure."
}
elseif ($success) {
    "Play-mode exit completed and Unity reached a stable editor state."
}
elseif (-not $idleWait.success) {
    "Unity did not reach a stable editor state after play-mode exit request."
}
else {
    "Play-mode exit request failed."
}

[ordered]@{
    success             = $success
    message             = $message
    recoveredTransition = $recoveredTransition
    exitRequestTimeoutSeconds = $ExitRequestTimeoutSeconds
    exitResult          = $exitResult
    exitError           = $exitError
    editorIdle          = $idleWait
} | ConvertTo-Json -Depth 30

if ($success) {
    exit 0
}

exit 1
