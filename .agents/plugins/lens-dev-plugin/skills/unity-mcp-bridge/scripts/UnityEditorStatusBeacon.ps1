function Get-UnityEditorStatusBeaconPath {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $resolvedProjectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        (Get-Location).Path
    }
    else {
        (Resolve-Path -Path $ProjectPath).Path
    }

    return (Join-Path $resolvedProjectPath "Temp\UnityEditorStatus\status.json")
}

function Read-UnityEditorStatusBeaconDocument {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter()][int]$MaxAttempts = 5,
        [Parameter()][int]$RetryDelayMilliseconds = 75
    )

    $attempts = [Math]::Max(1, $MaxAttempts)
    $lastError = $null

    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        $stream = $null
        $reader = $null
        try {
            $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
            $reader = [System.IO.StreamReader]::new($stream)
            $rawText = $reader.ReadToEnd()
            if ([string]::IsNullOrWhiteSpace($rawText)) {
                throw "Editor status beacon file is empty."
            }

            return [pscustomobject]@{
                Success  = $true
                Attempts = $attempt
                Document = ($rawText | ConvertFrom-Json)
                Error    = $null
            }
        }
        catch {
            $lastError = $_.Exception.Message
            if ($attempt -lt $attempts) {
                Start-Sleep -Milliseconds $RetryDelayMilliseconds
            }
        }
        finally {
            if ($reader) {
                $reader.Dispose()
            }

            if ($stream) {
                $stream.Dispose()
            }
        }
    }

    return [pscustomobject]@{
        Success  = $false
        Attempts = $attempts
        Document = $null
        Error    = $lastError
    }
}

function Get-UnityEditorStatusBeacon {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [switch]$IncludeRaw
    )

    $path = Get-UnityEditorStatusBeaconPath -ProjectPath $ProjectPath
    $state = [ordered]@{
        Path                   = $path
        Exists                 = $false
        Fresh                  = $false
        Stale                  = $false
        Classification         = "BeaconMissing"
        Phase                  = $null
        Substate               = $null
        UpdatedAtUtc           = $null
        LastTransitionAtUtc    = $null
        LastTransitionReason   = $null
        AgeSeconds             = $null
        FreshnessWindowSeconds = $null
        IsStablePhase          = $false
        Error                  = $null
        Raw                    = $null
    }

    if (-not (Test-Path -LiteralPath $path)) {
        return [pscustomobject]$state
    }

    $state.Exists = $true
    $readResult = Read-UnityEditorStatusBeaconDocument -Path $path
    if (-not $readResult.Success) {
        $state.Stale = $true
        $state.Classification = "BeaconStale"
        $state.Error = "Failed to read editor status beacon after $($readResult.Attempts) attempt(s): $($readResult.Error)"
        return [pscustomobject]$state
    }
    $rawBeacon = $readResult.Document

    $phase = [string]$rawBeacon.phase
    $substate = [string]$rawBeacon.substate
    $updatedAt = $null
    try {
        if (-not [string]::IsNullOrWhiteSpace($rawBeacon.updatedAtUtc)) {
            $updatedAt = [DateTimeOffset]::Parse($rawBeacon.updatedAtUtc)
        }
    }
    catch {
    }

    $stablePhases = @("Idle", "Playing", "Paused")
    $state.Phase = $phase
    $state.Substate = $substate
    $state.UpdatedAtUtc = $rawBeacon.updatedAtUtc
    $state.LastTransitionAtUtc = $rawBeacon.lastTransitionAtUtc
    $state.LastTransitionReason = $rawBeacon.lastTransitionReason
    $state.IsStablePhase = $stablePhases -contains $phase
    $state.FreshnessWindowSeconds = if ($state.IsStablePhase) { 4 } else { 2 }

    if ($IncludeRaw) {
        $state.Raw = $rawBeacon
    }

    if ($null -eq $updatedAt) {
        $state.Stale = $true
        $state.Classification = "BeaconStale"
        $state.Error = "updatedAtUtc is missing or invalid."
        return [pscustomobject]$state
    }

    $ageSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $updatedAt).TotalSeconds, 3)
    $state.AgeSeconds = $ageSeconds

    if ($ageSeconds -gt $state.FreshnessWindowSeconds) {
        $state.Stale = $true
        $state.Classification = "BeaconStale"
        return [pscustomobject]$state
    }

    $state.Fresh = $true
    switch ($phase) {
        "Idle" {
            $state.Classification = "BeaconIdle"
            break
        }
        "Playing" {
            $state.Classification = "BeaconPlaying"
            break
        }
        "Paused" {
            $state.Classification = "BeaconPlaying"
            break
        }
        "Building" {
            $state.Classification = "BeaconBuilding"
            break
        }
        default {
            $state.Classification = "BeaconTransitioning"
            break
        }
    }

    return [pscustomobject]$state
}
