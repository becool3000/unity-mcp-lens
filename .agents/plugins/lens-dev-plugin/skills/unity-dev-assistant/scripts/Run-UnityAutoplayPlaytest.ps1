param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$HarnessObjectName = "AutoplayPickupPlaytest",
    [string]$HarnessTypeName = "BikeRunner.AutoplayPickupPlaytest",
    [int]$PollIntervalSeconds = 5,
    [int]$MaxRuntimeSeconds = 90,
    [ValidateSet("Timed", "Event", "Hybrid")]
    [string]$SnapshotMode = "Hybrid",
    [ValidateSet("Hybrid", "UnityOnly", "DesktopOnly")]
    [string]$CapturePathMode = "Hybrid",
    [string]$OutputDir,
    [switch]$StopOnFinish,
    [bool]$PausePlaymodeForCapture = $true,
    [int]$StepFramesBeforeCapture = 0,
    [bool]$CapturePauseAndStepOnly = $false,
    [int]$UnityCaptureTimeoutSeconds = 45
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = New-UnityArtifactDirectory -ProjectPath $resolvedProjectPath -Prefix "unity-autoplay"
}
else {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

$playScript = Join-Path $PSScriptRoot "Enter-UnityPlayMode.ps1"
$captureScript = Join-Path $PSScriptRoot "Capture-UnityPlaytestArtifacts.ps1"
$playStart = & powershell -ExecutionPolicy Bypass -File $playScript -ProjectPath $resolvedProjectPath
$playResult = $playStart | ConvertFrom-Json
if ($playResult.success -ne $true) {
    Save-JsonFile -Path (Join-Path $OutputDir "play-entry.json") -Data $playResult
    $playResult | ConvertTo-Json -Depth 20
    exit 1
}

& powershell -ExecutionPolicy Bypass -File $captureScript -ProjectPath $resolvedProjectPath -Label "play-start" -CapturePathMode $CapturePathMode -OutputDir $OutputDir -PausePlaymodeForCapture:$PausePlaymodeForCapture -StepFramesBeforeCapture $StepFramesBeforeCapture -CapturePauseAndStepOnly:$CapturePauseAndStepOnly -UnityCaptureTimeoutSeconds $UnityCaptureTimeoutSeconds | Out-Null

$deadline = (Get-Date).AddSeconds($MaxRuntimeSeconds)
$events = @()
$lastSignature = $null
$loopIndex = 0
$escapedHarnessObjectName = Escape-CSharpString -Value $HarnessObjectName
$supportsReflectionFreeProbe = $HarnessTypeName -eq "BikeRunner.AutoplayPickupPlaytest"
$loggedProbeUnsupported = $false

while ((Get-Date) -lt $deadline) {
    $loopIndex += 1
    Start-Sleep -Seconds $PollIntervalSeconds

    $captureTimed = $SnapshotMode -in @("Timed", "Hybrid")
    if (-not $supportsReflectionFreeProbe) {
        $captureTimed = $true
    }

    if ($captureTimed) {
        & powershell -ExecutionPolicy Bypass -File $captureScript -ProjectPath $resolvedProjectPath -Label ("tick-{0:D2}" -f $loopIndex) -CapturePathMode $CapturePathMode -OutputDir $OutputDir -PausePlaymodeForCapture:$PausePlaymodeForCapture -StepFramesBeforeCapture $StepFramesBeforeCapture -CapturePauseAndStepOnly:$CapturePauseAndStepOnly -UnityCaptureTimeoutSeconds $UnityCaptureTimeoutSeconds | Out-Null
    }

    if (-not $supportsReflectionFreeProbe) {
        if (-not $loggedProbeUnsupported) {
            $events += [ordered]@{
                Timestamp       = (Get-Date).ToString("o")
                Kind            = "ProbeUnsupported"
                HarnessTypeName = $HarnessTypeName
                Hint            = "Reflection-free autoplay probing only supports BikeRunner.AutoplayPickupPlaytest. Timed captures will continue."
            }
            $loggedProbeUnsupported = $true
        }

        continue
    }

    $statusCode = @"
using UnityEngine;
var harness = Object.FindFirstObjectByType<BikeRunner.AutoplayPickupPlaytest>();
if (harness == null)
{
    result.Log("status=missing;complete=False;mounted=-1;onFoot=-1;riderless=-1;summary=");
    return;
}

if (!string.IsNullOrWhiteSpace("$escapedHarnessObjectName") && harness.gameObject.name != "$escapedHarnessObjectName")
{
    var named = GameObject.Find("$escapedHarnessObjectName");
    if (named == null)
    {
        result.Log("status=missing;complete=False;mounted=-1;onFoot=-1;riderless=-1;summary=");
        return;
    }

    harness = named.GetComponent<BikeRunner.AutoplayPickupPlaytest>();
    if (harness == null)
    {
        result.Log("status=missing-component;complete=False;mounted=-1;onFoot=-1;riderless=-1;summary=");
        return;
    }
}

var summary = (harness.FinalSummary ?? string.Empty).Replace(";", "|");
result.Log("status={0};complete={1};mounted={2};onFoot={3};riderless={4};summary={5}",
    harness.Status ?? "Unknown",
    harness.IsComplete,
    harness.MountedPickupCount,
    harness.OnFootPickupCount,
    harness.RiderlessBikePickupCount,
    summary);
"@

    $probe = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $statusCode -Title "Autoplay status probe" -TimeoutSeconds 30 -PausePlayMode $true -RestorePauseState $true
    if ($probe.success -ne $true) {
        $events += [ordered]@{
            Timestamp = (Get-Date).ToString("o")
            Kind      = "ProbeFailure"
            Error     = $probe.error
        }
        continue
    }

    $logLine = [string]$probe.data.executionLogs
    if ([string]::IsNullOrWhiteSpace($logLine)) {
        $events += [ordered]@{
            Timestamp = (Get-Date).ToString("o")
            Kind      = "ProbeEmpty"
            Hint      = "Verify the autoplay harness exists and logs a status line."
        }
        continue
    }

    $match = [regex]::Match($logLine, 'status=\[(?<status>[^\]]*)\];complete=\[(?<complete>[^\]]*)\];mounted=\[(?<mounted>[^\]]*)\];onFoot=\[(?<onFoot>[^\]]*)\];riderless=\[(?<riderless>[^\]]*)\];summary=\[(?<summary>[^\]]*)\]')
    if (-not $match.Success) {
        $events += [ordered]@{
            Timestamp = (Get-Date).ToString("o")
            Kind      = "ParseFailure"
            RawLog    = $logLine
        }
        continue
    }

    $eventState = [ordered]@{
        Timestamp = (Get-Date).ToString("o")
        Status    = $match.Groups['status'].Value
        Complete  = [System.Convert]::ToBoolean($match.Groups['complete'].Value)
        Mounted   = [int]$match.Groups['mounted'].Value
        OnFoot    = [int]$match.Groups['onFoot'].Value
        Riderless = [int]$match.Groups['riderless'].Value
        Summary   = $match.Groups['summary'].Value
    }
    $events += $eventState

    $signature = "$($eventState.Status)|$($eventState.Mounted)|$($eventState.OnFoot)|$($eventState.Riderless)|$($eventState.Complete)"
    $captureEvent = $SnapshotMode -in @("Event", "Hybrid") -and $signature -ne $lastSignature

    if ($captureEvent) {
        $eventLabel = ($eventState.Status -replace '[^A-Za-z0-9._-]', '-')
        & powershell -ExecutionPolicy Bypass -File $captureScript -ProjectPath $resolvedProjectPath -Label ("event-{0:D2}-{1}" -f $loopIndex, $eventLabel) -CapturePathMode $CapturePathMode -OutputDir $OutputDir -PausePlaymodeForCapture:$PausePlaymodeForCapture -StepFramesBeforeCapture $StepFramesBeforeCapture -CapturePauseAndStepOnly:$CapturePauseAndStepOnly -UnityCaptureTimeoutSeconds $UnityCaptureTimeoutSeconds | Out-Null
    }

    $lastSignature = $signature

    if ($eventState.Complete) {
        break
    }
}

if ($StopOnFinish) {
    try {
        Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_ManageEditor" -Arguments @{ Action = "Stop" } -TimeoutSeconds 20 | Out-Null
    }
    catch {
    }
}

$summary = [ordered]@{
    ProjectPath       = $resolvedProjectPath
    OutputDir         = $OutputDir
    SnapshotMode      = $SnapshotMode
    CapturePathMode   = $CapturePathMode
    MaxRuntimeSeconds = $MaxRuntimeSeconds
    Events            = $events
    FinalEvent        = if ($events.Count -gt 0) { $events[-1] } else { $null }
}

Save-JsonFile -Path (Join-Path $OutputDir "autoplay-summary.json") -Data $summary
$summary | ConvertTo-Json -Depth 20
