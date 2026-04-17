param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$ChangedPaths = @(),
    [int]$NaturalDetectTimeoutSeconds = 6,
    [int]$ForcedDetectTimeoutSeconds = 20,
    [int]$ReloadTimeoutSeconds = 120,
    [int]$ReloadMarkerTtlSeconds = 120,
    [int]$ForceRefreshTimeoutSeconds = 30,
    [int]$IdleStablePollCount = 3,
    [double]$PollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
$normalizedChangedPaths = @()
foreach ($pathValue in $ChangedPaths) {
    $normalized = Resolve-UnityRelativePath -ProjectPath $resolvedProjectPath -PathValue $pathValue
    if (-not [string]::IsNullOrWhiteSpace($normalized) -and $normalizedChangedPaths -notcontains $normalized) {
        $normalizedChangedPaths += $normalized
    }
}

$relevantChangedPaths = if ($ChangedPaths.Count -gt 0) {
    @(Get-UnityCompileAffectingChanges -ProjectPath $resolvedProjectPath -ChangedPaths $ChangedPaths)
}
else {
    @()
}

$relevantChangesDetected = if ($ChangedPaths.Count -eq 0) {
    $true
}
else {
    $relevantChangedPaths.Count -gt 0
}

$result = [ordered]@{
    success                         = $false
    message                         = $null
    projectPath                     = $resolvedProjectPath
    changedPaths                    = @($normalizedChangedPaths)
    relevantChangedPaths            = @($relevantChangedPaths)
    relevantChangesDetected         = $relevantChangesDetected
    compileObserved                 = $false
    likelyStartedByTransientFailure = $false
    forcedRefresh                   = $false
    transientMcpFailureObserved     = $false
    markerPath                      = Get-UnityExpectedReloadMarkerPath -ProjectPath $resolvedProjectPath
    durationSeconds                 = 0
    naturalCycle                    = $null
    forcedCycle                     = $null
    forceRefreshResult              = $null
    forceRefreshError               = $null
    editorIdle                      = $null
    expectedReloadState             = $null
    fallbackClassification          = $null
    directHealthFallback            = $null
}

try {
    if (-not $relevantChangesDetected) {
        $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $ReloadTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $PollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
        $result.success = $idleWait.success
        $result.message = if ($idleWait.success) {
            "No compile-affecting changes were detected; Unity editor is idle."
        }
        else {
            "No compile-affecting changes were detected, but Unity editor did not reach stable idle before timeout."
        }
        $result.editorIdle = $idleWait
    }
    else {
        Set-UnityExpectedReloadState -ProjectPath $resolvedProjectPath -Reason "ExternalScriptChanges" -ChangedPaths $relevantChangedPaths -TtlSeconds $ReloadMarkerTtlSeconds | Out-Null
        $naturalCycle = Wait-UnityCompileReloadCycle -ProjectPath $resolvedProjectPath -StartTimeoutSeconds $NaturalDetectTimeoutSeconds -TimeoutSeconds $ReloadTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $PollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds -ClearExpectedReloadOnSuccess
        $result.naturalCycle = $naturalCycle

        if ($naturalCycle.compileObserved -or $naturalCycle.likelyStartedByTransientFailure) {
            $result.compileObserved = $naturalCycle.compileObserved
            $result.likelyStartedByTransientFailure = $naturalCycle.likelyStartedByTransientFailure
            $result.transientMcpFailureObserved = $naturalCycle.transientExpectedReloadFailures -gt 0
            $result.success = $naturalCycle.success
            $result.editorIdle = $naturalCycle.idleWait
            $result.message = if ($naturalCycle.success) {
                if ($naturalCycle.compileObserved) {
                    "Unity picked up the external script changes and returned to stable idle."
                }
                else {
                    "Unity entered an expected reload window after the external script changes and returned to stable idle."
                }
            }
            else {
                "Unity began handling the external script changes, but did not return to stable idle before timeout."
            }
        }
        else {
            $result.forcedRefresh = $true
            Set-UnityExpectedReloadState -ProjectPath $resolvedProjectPath -Reason "ForcedScriptRefresh" -ChangedPaths $relevantChangedPaths -TtlSeconds $ReloadMarkerTtlSeconds | Out-Null

            $forceRefreshCode = @'
UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
result.Log("SYNC::requested-refresh-and-script-compilation");
'@

            try {
                $result.forceRefreshResult = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $forceRefreshCode -Title "Sync external Unity script changes" -TimeoutSeconds $ForceRefreshTimeoutSeconds
            }
            catch {
                $result.forceRefreshError = $_.Exception.Message
            }

            $forcedCycle = Wait-UnityCompileReloadCycle -ProjectPath $resolvedProjectPath -StartTimeoutSeconds $ForcedDetectTimeoutSeconds -TimeoutSeconds $ReloadTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $PollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds -ClearExpectedReloadOnSuccess
            $result.forcedCycle = $forcedCycle
            $result.compileObserved = $forcedCycle.compileObserved
            $result.likelyStartedByTransientFailure = $forcedCycle.likelyStartedByTransientFailure
            $result.transientMcpFailureObserved = ($result.forceRefreshError -ne $null) -or ($forcedCycle.transientExpectedReloadFailures -gt 0) -or ($naturalCycle.transientExpectedReloadFailures -gt 0)
            $result.success = $forcedCycle.success
            $result.editorIdle = $forcedCycle.idleWait
            $result.message = if ($forcedCycle.success) {
                if ($forcedCycle.compileObserved) {
                    "Forced Unity refresh/recompile completed and the editor returned to stable idle."
                }
                elseif ($forcedCycle.likelyStartedByTransientFailure) {
                    "Forced Unity refresh triggered an expected reload window and the editor returned to stable idle."
                }
                else {
                    "Forced Unity refresh completed and the editor returned to stable idle, but no compile/update was observed."
                }
            }
            else {
                if ($forcedCycle.compileObserved -or $forcedCycle.likelyStartedByTransientFailure) {
                    "Forced Unity refresh triggered reload handling, but the editor did not return to stable idle before timeout."
                }
                else {
                    "Forced Unity refresh did not lead to a settled compile/reload cycle before timeout."
                }
            }
        }
    }
}
finally {
    $stopwatch.Stop()
    $result.durationSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 3)

    if (-not $result.success) {
        try {
            $directHealth = Test-UnityDirectEditorHealthy -ProjectPath $resolvedProjectPath -TimeoutSeconds 20 -ConsecutiveHealthyPolls 2 -PollIntervalSeconds $PollIntervalSeconds
            $result.directHealthFallback = $directHealth

            if ($directHealth.success) {
                $result.success = $true
                $result.fallbackClassification = "WrapperUnhealthyDirectMcpOk"
                $result.message = "Sync wrapper did not settle cleanly, but direct Unity editor health checks are healthy and idle."
                if ($null -eq $result.editorIdle) {
                    $result.editorIdle = $directHealth
                }
                Clear-UnityExpectedReloadState -ProjectPath $resolvedProjectPath
            }
            elseif ($null -ne $directHealth.lastState -and
                    $directHealth.lastState.success -eq $true -and
                    $directHealth.lastState.data.IsCompiling -ne $true -and
                    $directHealth.lastState.data.IsUpdating -ne $true -and
                    $directHealth.consecutiveHealthyObserved -ge 1) {
                $result.success = $true
                $result.fallbackClassification = "WrapperUnhealthyDirectMcpOk"
                $result.message = "Sync wrapper did not settle cleanly, but direct Unity editor health recovered to an idle state before the fallback window ended."
                if ($null -eq $result.editorIdle) {
                    $result.editorIdle = $directHealth
                }
                Clear-UnityExpectedReloadState -ProjectPath $resolvedProjectPath
            }
        }
        catch {
        }
    }

    if (-not $result.success) {
        try {
            $currentState = Get-UnityEditorState -ProjectPath $resolvedProjectPath -TimeoutSeconds 10
            if ($currentState.success -eq $true -and $currentState.data.IsCompiling -ne $true -and $currentState.data.IsUpdating -ne $true) {
                Clear-UnityExpectedReloadState -ProjectPath $resolvedProjectPath
            }
        }
        catch {
        }
    }

    $result.expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $resolvedProjectPath -IncludeExpired
    $result | ConvertTo-Json -Depth 30

    if ($result.success) {
        exit 0
    }

    exit 1
}
