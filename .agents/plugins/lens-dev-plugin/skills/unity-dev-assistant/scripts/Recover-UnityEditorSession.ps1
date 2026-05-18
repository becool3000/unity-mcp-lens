param(
    [string]$ProjectPath = (Get-Location).Path,
    [int]$RecoveryWaitSeconds = 30,
    [int]$ReacquireTimeoutSeconds = 180,
    [double]$PollIntervalSeconds = 1.0,
    [int]$StablePollCount = 3,
    [double]$PostIdleDelaySeconds = 2.0,
    [switch]$AllowKillUnity,
    [switch]$AllowScratchCleanup,
    [switch]$DiagnoseOnly
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

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$startedAt = Get-Date
$recoveryResponse = $null
$recoveryResult = $null
$recoveryError = $null
$idleWait = $null

try {
    $recoveryResponse = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Editor_RecoverFromHang" -Arguments @{
        ProjectPath          = $resolvedProjectPath
        DiagnoseOnly         = [bool]$DiagnoseOnly
        AllowKillUnity       = [bool]$AllowKillUnity
        AllowRestartUnity    = -not [bool]$DiagnoseOnly
        AllowScratchCleanup  = [bool]$AllowScratchCleanup
        WaitMs               = [Math]::Max(0, $RecoveryWaitSeconds * 1000)
    } -TimeoutSeconds ([Math]::Max(5, $RecoveryWaitSeconds + 15))
    $recoveryResult = Get-UnityToolObject -Response $recoveryResponse
}
catch {
    $recoveryError = $_.Exception.Message
}

$data = if ($recoveryResult) { Get-ObjectPropertyValue -InputObject $recoveryResult -Names @("data", "Data") } else { $null }
$state = Get-ObjectPropertyValue -InputObject $data -Names @("state", "State")
$safeToContinue = (Get-ObjectPropertyValue -InputObject $data -Names @("safeToContinue", "SafeToContinue")) -eq $true
$recoverySucceeded = ($recoveryResult -and $recoveryResult.success -eq $true)
$shouldWaitForReacquire = (-not [bool]$DiagnoseOnly) -and (
    $safeToContinue -or
    [string]::Equals([string]$state, "still_opening", [System.StringComparison]::OrdinalIgnoreCase) -or
    [string]::Equals([string]$state, "recovered", [System.StringComparison]::OrdinalIgnoreCase)
)

if ($shouldWaitForReacquire) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $ReacquireTimeoutSeconds -StablePollCount $StablePollCount -PollIntervalSeconds $PollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds -ClearExpectedReloadOnSuccess
}

$success = if ([bool]$DiagnoseOnly) {
    $recoverySucceeded
}
else {
    $recoverySucceeded -and $idleWait -and $idleWait.success -eq $true
}

$message = if ([bool]$DiagnoseOnly) {
    "Unity editor session diagnosis completed."
}
elseif ($success) {
    "Unity editor session recovered and Lens reacquired a stable editor."
}
elseif ($recoveryError) {
    "Unity editor session recovery request failed."
}
elseif ($recoverySucceeded -and -not $idleWait) {
    "Unity editor recovery returned, but no reacquire wait was attempted."
}
else {
    "Unity editor session did not recover to a stable Lens-ready state."
}

[ordered]@{
    success                 = $success
    message                 = $message
    projectPath             = $resolvedProjectPath
    diagnoseOnly            = [bool]$DiagnoseOnly
    allowKillUnity          = [bool]$AllowKillUnity
    allowScratchCleanup     = [bool]$AllowScratchCleanup
    recoveryWaitSeconds     = $RecoveryWaitSeconds
    reacquireTimeoutSeconds = $ReacquireTimeoutSeconds
    elapsedSeconds          = [Math]::Round(((Get-Date) - $startedAt).TotalSeconds, 2)
    recoveryState           = $state
    recoverySafeToContinue  = $safeToContinue
    recoveryResult          = $recoveryResult
    recoveryError           = $recoveryError
    editorIdle              = $idleWait
} | ConvertTo-Json -Depth 40

if ($success) {
    exit 0
}

exit 1
