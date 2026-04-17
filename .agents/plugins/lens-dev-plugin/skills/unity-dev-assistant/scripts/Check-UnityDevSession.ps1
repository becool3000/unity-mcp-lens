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

. "$PSScriptRoot\UnityDevCommon.ps1"
. (Join-Path (Get-UnityBridgeSkillPath) "scripts\UnityEditorStatusBeacon.ps1")

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$monitorBuild = -not [string]::IsNullOrWhiteSpace($MonitorBuildMode)
if ($monitorBuild -and $MonitorBuildMode -ne "WebGL") {
    throw "Unsupported -MonitorBuildMode '$MonitorBuildMode'. Supported values: WebGL."
}

$bridgeCheckScript = Join-Path (Get-UnityBridgeSkillPath) "scripts\Check-UnityMcp.ps1"
$bridgeCheckArguments = @(
    "-ExecutionPolicy", "Bypass",
    "-File", $bridgeCheckScript,
    "-ProjectPath", $resolvedProjectPath,
    "-MaxWrapperStatusInstances", [string]$MaxWrapperStatusInstances
)
if ($IncludeDiagnostics) {
    $bridgeCheckArguments += "-IncludeDiagnostics"
}

$bridgeCheckRaw = & powershell @bridgeCheckArguments
$bridgeCheck = $bridgeCheckRaw | ConvertFrom-Json

$wrapperHealthy = $false
$wrapperError = $null
$editorState = $null
$editorIdleSnapshot = $null
$playReadySnapshot = $null
$lensHealthOverridesBeacon = ($bridgeCheck.LensHealth -and $bridgeCheck.LensHealth.OverridesBeacon -eq $true)
$expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $resolvedProjectPath
$editorStatusBeacon = if ($bridgeCheck.EditorStatusBeacon) { $bridgeCheck.EditorStatusBeacon } else { Get-UnityEditorStatusBeacon -ProjectPath $resolvedProjectPath -IncludeRaw }
$beaconWait = $bridgeCheck.BeaconWait
$buildScenePreflight = $null
$buildMonitor = $null
$wrapperDiagnostics = Get-UnityWrapperDiagnostics
$assistantPackageState = Get-UnityAssistantPackageState -ProjectPath $resolvedProjectPath

if ($ExpectedScenes.Count -gt 0) {
    $buildScenePreflight = Test-UnityBuildSceneList -ProjectPath $resolvedProjectPath -ExpectedScenes $ExpectedScenes
}

if ($bridgeCheck.Classification -eq "BuildInProgress" -or $monitorBuild) {
    $buildMonitorMode = if ($monitorBuild) { $MonitorBuildMode } else { "WebGL" }
    $buildMonitor = Get-UnityBuildMonitorState -ProjectPath $resolvedProjectPath -Mode $buildMonitorMode -BuildOutputPath $BuildOutputPath -BuildReportPath $BuildReportPath -SuccessArtifactPath $SuccessArtifactPath
}

if ($bridgeCheck.Classification -eq "BuildInProgress") {
    $wrapperError = "Skipped wrapper editor-state probe because Check-UnityMcp classified the session as BuildInProgress."
}
elseif ($bridgeCheck.Classification -eq "EditorReloadingExpected") {
    $wrapperError = if ($beaconWait -and -not [string]::IsNullOrWhiteSpace($beaconWait.message)) {
        "Skipped wrapper editor-state probe because Check-UnityMcp classified the session as EditorReloadingExpected. $($beaconWait.message)"
    }
    else {
        "Skipped wrapper editor-state probe because Check-UnityMcp classified the session as EditorReloadingExpected."
    }
}
elseif (-not $lensHealthOverridesBeacon -and $editorStatusBeacon.Fresh -and $editorStatusBeacon.Classification -eq "BeaconTransitioning") {
    $wrapperError = if ($beaconWait -and -not [string]::IsNullOrWhiteSpace($beaconWait.message)) {
        "Skipped wrapper editor-state probe because the editor status beacon still reports phase '$($editorStatusBeacon.Phase)' after shared wait handling. $($beaconWait.message)"
    }
    else {
        "Skipped wrapper editor-state probe because the editor status beacon reports phase '$($editorStatusBeacon.Phase)'."
    }
}
else {
    try {
        $editorState = Get-UnityEditorState -ProjectPath $resolvedProjectPath -TimeoutSeconds 20
        $wrapperHealthy = $editorState.success -eq $true
        if ($wrapperHealthy) {
            $readiness = Get-UnityReadinessSnapshot -EditorState $editorState
            $editorIdleSnapshot = [ordered]@{
                Ready                 = $readiness.IdleReady
                StablePollRequirement = 3
                PollIntervalSeconds   = 0.5
                PostIdleDelaySeconds  = 1.0
                Snapshot              = $readiness
            }
            $playReadySnapshot = [ordered]@{
                Ready                = $readiness.IsPlaying -and $readiness.RuntimeProbeAvailable -and $readiness.RuntimeProbeHasAdvancedFrames -and $readiness.RuntimeProbeUpdateCount -ge 10
                WarmupSeconds        = 1.0
                UpdateCountThreshold = 10
                Snapshot             = $readiness
            }
        }
    }
    catch {
        $wrapperError = $_.Exception.Message
    }
}

$recommendedPath = if ($buildScenePreflight -and -not $buildScenePreflight.exactMatch) {
    "FixBuildSceneList"
}
elseif ($bridgeCheck.Classification -eq "BuildInProgress") {
    "MonitorActiveBuild"
}
elseif ($bridgeCheck.Classification -eq "EditorReloadingExpected") {
    "WaitForExpectedReload"
}
elseif ($bridgeCheck.Classification -eq "WrapperUnhealthyDirectMcpOk") {
    "ProceedWithBuiltInOrManualWrapper"
}
elseif ($bridgeCheck.Classification -ne "Ready") {
    "RepairBridge"
}
elseif ($expectedReloadState.IsActive -and $editorState -and $editorState.success -eq $true -and ($editorState.data.IsCompiling -eq $true -or $editorState.data.IsUpdating -eq $true)) {
    "WaitForExpectedReload"
}
elseif ($wrapperHealthy) {
    "ProceedWithBuiltInOrManualWrapper"
}
else {
    "InvestigateManualWrapper"
}

$compactResult = [pscustomobject]@{
    ProjectPath = $resolvedProjectPath
    RecommendedPath = $recommendedPath
    Bridge = [pscustomobject]@{
        Classification = $bridgeCheck.Classification
        Summary = $bridgeCheck.Summary
        RecommendedAction = $bridgeCheck.RecommendedAction
        UserActionRequired = $bridgeCheck.UserActionRequired
    }
    Editor = [pscustomobject]@{
        ManualWrapperHealthy = $wrapperHealthy
        Error = $wrapperError
        IdleReady = if ($editorIdleSnapshot) { $editorIdleSnapshot.Ready } else { $null }
        PlayReady = if ($playReadySnapshot) { $playReadySnapshot.Ready } else { $null }
        State = if ($editorState -and $editorState.success -eq $true) {
            [pscustomobject]@{
                IsPlaying = ($editorState.data.IsPlaying -eq $true)
                IsCompiling = ($editorState.data.IsCompiling -eq $true)
                IsUpdating = ($editorState.data.IsUpdating -eq $true)
                BridgeStatus = $editorState.data.BridgeStatus
                ToolDiscoveryMode = $editorState.data.ToolDiscoveryMode
                ActiveSceneName = if ($editorState.data.RuntimeProbe) { $editorState.data.RuntimeProbe.ActiveSceneName } else { $null }
                RuntimeProbeReady = if ($editorState.data.RuntimeProbe) { $editorState.data.RuntimeProbe.IsAvailable -eq $true -and $editorState.data.RuntimeProbe.HasAdvancedFrames -eq $true } else { $false }
            }
        } else { $null }
    }
    Build = [pscustomobject]@{
        SceneList = if ($buildScenePreflight) {
            [pscustomobject]@{
                ExactMatch = $buildScenePreflight.exactMatch
                Summary = $buildScenePreflight.message
            }
        } else { $null }
        Monitor = if ($buildMonitor) {
            [pscustomobject]@{
                Status = $buildMonitor.Status
                Summary = $buildMonitor.Summary
            }
        } else { $null }
    }
    AssistantPackage = [pscustomobject]@{
        Mode = $assistantPackageState.Mode
        Summary = $assistantPackageState.Summary
        Dependency = $assistantPackageState.DependencyValue
        Path = $assistantPackageState.ResolvedFileDependencyPath
    }
    DiagnosticsHint = "Rerun with -IncludeDiagnostics for the full Unity session payload."
}

$diagnosticResult = [ordered]@{
    ProjectPath           = $resolvedProjectPath
    BridgeCheck           = $bridgeCheck
    EditorStatusBeacon    = $editorStatusBeacon
    BeaconWait            = $beaconWait
    ManualWrapperHealthy  = $wrapperHealthy
    ManualWrapperError    = $wrapperError
    EditorState           = $editorState
    ExpectedReloadState   = $expectedReloadState
    BuildScenePreflight   = $buildScenePreflight
    BuildMonitor          = $buildMonitor
    WrapperDiagnostics    = $wrapperDiagnostics
    EditorIdleSnapshot    = $editorIdleSnapshot
    PlayReadySnapshot     = $playReadySnapshot
    AssistantPackageState = $assistantPackageState
    AssistantDependency   = $assistantPackageState.DependencyValue
    AssistantPackageMode  = $assistantPackageState.Mode
    HasLocalAssistantFork = $assistantPackageState.Mode -in @("LocalFolderDependency", "ExternalPatchSource")
    ReadyForLongBuild     = ($bridgeCheck.Classification -in @("Ready", "WrapperUnhealthyDirectMcpOk")) -and (($null -eq $buildScenePreflight) -or $buildScenePreflight.exactMatch)
    RecommendedPath       = $recommendedPath
}

if ($IncludeDiagnostics) {
    $diagnosticResult | ConvertTo-Json -Depth 30
}
else {
    $compactResult | ConvertTo-Json -Depth 10
}
