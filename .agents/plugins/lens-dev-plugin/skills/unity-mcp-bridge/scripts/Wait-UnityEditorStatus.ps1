. "$PSScriptRoot\UnityEditorStatusBeacon.ps1"

function Get-UnityEditorBeaconPollDelayMilliseconds {
    param([Parameter()][double]$PollIntervalSeconds = 0.25)

    return [Math]::Max(1, [int][Math]::Round([Math]::Max(0.01, $PollIntervalSeconds) * 1000))
}

function Test-UnityEditorBeaconBuildSnapshot {
    param([Parameter()]$Snapshot)

    if ($null -eq $Snapshot) {
        return $false
    }

    return $Snapshot.Phase -eq "Building" -or ($Snapshot.Fresh -and $Snapshot.Classification -eq "BeaconBuilding")
}

function Test-UnityEditorBeaconTransitionSnapshot {
    param([Parameter()]$Snapshot)

    if ($null -eq $Snapshot) {
        return $false
    }

    $transitionPhases = @("Starting", "Importing", "Compiling", "ReloadingAssemblies", "EnteringPlayMode", "ExitingPlayMode")
    return ($Snapshot.Phase -in $transitionPhases) -or ($Snapshot.Fresh -and $Snapshot.Classification -eq "BeaconTransitioning")
}

function New-UnityEditorBeaconAttemptRecord {
    param([Parameter(Mandatory = $true)]$Snapshot)

    return [ordered]@{
        Timestamp      = (Get-Date).ToString("o")
        Classification = $Snapshot.Classification
        Phase          = $Snapshot.Phase
        Fresh          = $Snapshot.Fresh
        Stale          = $Snapshot.Stale
        Exists         = $Snapshot.Exists
        Error          = $Snapshot.Error
    }
}

function New-UnityEditorBeaconWaitResult {
    param(
        [Parameter(Mandatory = $true)][bool]$Success,
        [Parameter(Mandatory = $true)][bool]$TimedOut,
        [Parameter()][string]$Message,
        [Parameter()][object[]]$Attempts = @(),
        [Parameter()]$LastSnapshot,
        [Parameter(Mandatory = $true)][string]$Source
    )

    return [ordered]@{
        success       = $Success
        timedOut      = $TimedOut
        classification = if ($LastSnapshot) { $LastSnapshot.Classification } else { $null }
        phase         = if ($LastSnapshot) { $LastSnapshot.Phase } else { $null }
        message       = $Message
        attempts      = $Attempts
        lastSnapshot  = $LastSnapshot
        source        = $Source
    }
}

function Wait-UnityEditorBeaconStable {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 6,
        [Parameter()][double]$PollIntervalSeconds = 0.25
    )

    $source = "EditorStatusBeacon.WaitStable"
    $deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
    $attempts = New-Object System.Collections.Generic.List[object]
    $lastSnapshot = $null
    $stableClassifications = @("BeaconIdle", "BeaconPlaying")
    $transitionPhases = @("Starting", "Importing", "Compiling", "ReloadingAssemblies", "EnteringPlayMode", "ExitingPlayMode", "Building")
    $pollDelayMilliseconds = Get-UnityEditorBeaconPollDelayMilliseconds -PollIntervalSeconds $PollIntervalSeconds

    do {
        $snapshot = Get-UnityEditorStatusBeacon -ProjectPath $ProjectPath -IncludeRaw
        $lastSnapshot = $snapshot
        $attempts.Add((New-UnityEditorBeaconAttemptRecord -Snapshot $snapshot))

        if ($snapshot.Fresh -and $stableClassifications -contains $snapshot.Classification) {
            return (New-UnityEditorBeaconWaitResult -Success $true -TimedOut $false -Message "Editor status beacon reached a stable phase." -Attempts $attempts -LastSnapshot $snapshot -Source $source)
        }

        $staleTransitionState = $snapshot.Classification -eq "BeaconStale" -and $snapshot.Phase -in $transitionPhases
        if (-not $staleTransitionState -and $snapshot.Classification -in @("BeaconMissing", "BeaconStale")) {
            return (New-UnityEditorBeaconWaitResult -Success $false -TimedOut $false -Message "Editor status beacon is unavailable or stale." -Attempts $attempts -LastSnapshot $snapshot -Source $source)
        }

        if ((Get-Date) -ge $deadline) {
            break
        }

        Start-Sleep -Milliseconds $pollDelayMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    return (New-UnityEditorBeaconWaitResult -Success $false -TimedOut $true -Message "Editor status beacon did not reach a stable phase before timeout." -Attempts $attempts -LastSnapshot $lastSnapshot -Source $source)
}

function Wait-UnityEditorBeaconTransition {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 6,
        [Parameter()][double]$PollIntervalSeconds = 0.25
    )

    $source = "EditorStatusBeacon.WaitTransition"
    $deadline = (Get-Date).AddSeconds([Math]::Max(0, $TimeoutSeconds))
    $attempts = New-Object System.Collections.Generic.List[object]
    $lastSnapshot = $null
    $transitionClassifications = @("BeaconTransitioning", "BeaconBuilding")
    $transitionPhases = @("Starting", "Importing", "Compiling", "ReloadingAssemblies", "EnteringPlayMode", "ExitingPlayMode", "Building")
    $pollDelayMilliseconds = Get-UnityEditorBeaconPollDelayMilliseconds -PollIntervalSeconds $PollIntervalSeconds

    do {
        $snapshot = Get-UnityEditorStatusBeacon -ProjectPath $ProjectPath -IncludeRaw
        $lastSnapshot = $snapshot
        $attempts.Add((New-UnityEditorBeaconAttemptRecord -Snapshot $snapshot))

        if ($snapshot.Fresh -and $transitionClassifications -contains $snapshot.Classification) {
            return (New-UnityEditorBeaconWaitResult -Success $true -TimedOut $false -Message "Editor status beacon observed an active transition." -Attempts $attempts -LastSnapshot $snapshot -Source $source)
        }

        if ($snapshot.Classification -eq "BeaconStale" -and $snapshot.Phase -in $transitionPhases) {
            return (New-UnityEditorBeaconWaitResult -Success $true -TimedOut $false -Message "Editor status beacon last-known phase still indicates an active transition." -Attempts $attempts -LastSnapshot $snapshot -Source $source)
        }

        if ($snapshot.Classification -in @("BeaconMissing", "BeaconStale")) {
            return (New-UnityEditorBeaconWaitResult -Success $false -TimedOut $false -Message "Editor status beacon is unavailable or stale." -Attempts $attempts -LastSnapshot $snapshot -Source $source)
        }

        if ((Get-Date) -ge $deadline) {
            break
        }

        Start-Sleep -Milliseconds $pollDelayMilliseconds
    }
    while ((Get-Date) -lt $deadline)

    return (New-UnityEditorBeaconWaitResult -Success $false -TimedOut $true -Message "Editor status beacon did not report a transition before timeout." -Attempts $attempts -LastSnapshot $lastSnapshot -Source $source)
}
