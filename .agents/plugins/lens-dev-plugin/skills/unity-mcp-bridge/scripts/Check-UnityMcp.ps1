param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$ServerName = "unity-mcp",
    [int]$EditorLogTail = 400,
    [switch]$IncludeDiagnostics,
    [int]$MaxWrapperStatusInstances = 5
)

. (Join-Path $PSScriptRoot "..\..\unity-dev-assistant\scripts\UnityDevCommon.ps1")
. "$PSScriptRoot\UnityEditorStatusBeacon.ps1"
. "$PSScriptRoot\Wait-UnityEditorStatus.ps1"

function Get-UnityWrapperStatusInstanceDirectory {
    return (Join-Path $env:USERPROFILE ".codex\cache\unity-mcp-wrapper-status")
}

function Get-UnityWrapperStatusInstances {
    $statusDir = Get-UnityWrapperStatusInstanceDirectory
    if (-not (Test-Path -LiteralPath $statusDir)) {
        return @()
    }

    $instances = @()
    foreach ($file in Get-ChildItem -LiteralPath $statusDir -Filter "unity-mcp-wrapper-status-*.json" -File -ErrorAction SilentlyContinue) {
        try {
            $data = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            $updatedAt = $null
            try {
                if (-not [string]::IsNullOrWhiteSpace($data.updatedAt)) {
                    $updatedAt = [DateTimeOffset]::Parse($data.updatedAt)
                }
            }
            catch {
            }

            if ($null -eq $data.Path) {
                $data | Add-Member -NotePropertyName Path -NotePropertyValue $file.FullName
            }

            $ageSeconds = if ($updatedAt) {
                [Math]::Round(([DateTimeOffset]::UtcNow - $updatedAt.ToUniversalTime()).TotalSeconds, 3)
            }
            else {
                [double]::PositiveInfinity
            }

            $sortKey = if ($updatedAt) {
                $updatedAt.ToUnixTimeMilliseconds()
            }
            else {
                [int64]0
            }

            $data | Add-Member -NotePropertyName AgeSeconds -NotePropertyValue $ageSeconds -Force
            $data | Add-Member -NotePropertyName UpdatedAtSort -NotePropertyValue $sortKey -Force
            $instances += $data
        }
        catch {
            $instances += [pscustomobject]@{
                Path          = $file.FullName
                Error         = $_.Exception.Message
                AgeSeconds    = [double]::PositiveInfinity
                UpdatedAtSort = [int64]0
            }
        }
    }

    return @($instances | Sort-Object UpdatedAtSort -Descending)
}

function Test-UnityWrapperTransportFailureReason {
    param([string]$Reason)

    if ([string]::IsNullOrWhiteSpace($Reason)) {
        return $false
    }

    $normalized = $Reason.ToLowerInvariant()
    return $normalized.Contains("transport closed") -or
        $normalized.Contains("connection closed") -or
        $normalized.Contains("session reset") -or
        $normalized.Contains("disconnected") -or
        $normalized.Contains("timeout")
}

function Get-UnityWrapperDiagnostics {
    param([int]$MaxStatusInstances = 5)

    $wrapperStatusPath = Join-Path $env:USERPROFILE ".codex\cache\unity-mcp-wrapper-status.json"
    $instances = @(Get-UnityWrapperStatusInstances)
    $limitedInstances = if ($MaxStatusInstances -gt 0) {
        @($instances | Select-Object -First $MaxStatusInstances)
    }
    else {
        $instances
    }
    $recentInstances = @($instances | Where-Object { $_.AgeSeconds -lt 180 })
    $recentFailure = $recentInstances |
        Where-Object {
            ($_.lastToolCallSucceeded -eq $false -and (Test-UnityWrapperTransportFailureReason -Reason $_.lastToolCallError)) -or
            (Test-UnityWrapperTransportFailureReason -Reason $_.lastToolsError)
        } |
        Select-Object -First 1
    $recentSuccess = $recentInstances |
        Where-Object { $_.lastToolCallSucceeded -eq $true } |
        Select-Object -First 1

    $data = $null
    if (Test-Path -LiteralPath $wrapperStatusPath) {
        try {
            $data = Get-Content -LiteralPath $wrapperStatusPath -Raw | ConvertFrom-Json
            if ($null -eq $data.Path) {
                $data | Add-Member -NotePropertyName Path -NotePropertyValue $wrapperStatusPath
            }
        }
        catch {
            $data = [pscustomobject]@{
                Path  = $wrapperStatusPath
                Error = $_.Exception.Message
            }
        }
    }

    if ($null -eq $data -and $instances.Count -eq 0) {
        return $null
    }

    if ($null -eq $data) {
        $data = [pscustomobject]@{
            Path = $wrapperStatusPath
        }
    }

    $data | Add-Member -NotePropertyName StatusInstances -NotePropertyValue $limitedInstances -Force
    $data | Add-Member -NotePropertyName TotalStatusInstances -NotePropertyValue $instances.Count -Force
    $data | Add-Member -NotePropertyName RecentTransportFailure -NotePropertyValue $recentFailure -Force
    $data | Add-Member -NotePropertyName RecentWrapperSuccess -NotePropertyValue $recentSuccess -Force
    return $data
}

function Normalize-ProjectPath {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $candidate = $PathValue
    if (Test-Path -LiteralPath $candidate) {
        try {
            $candidate = (Resolve-Path -LiteralPath $candidate).Path
        }
        catch {
        }
    }

    $candidate = $candidate -replace "\\", "/"
    $candidate = $candidate.TrimEnd("/")

    if ($candidate.EndsWith("/Assets", [System.StringComparison]::OrdinalIgnoreCase)) {
        $candidate = $candidate.Substring(0, $candidate.Length - 7)
    }

    return $candidate.ToLowerInvariant()
}

function Find-SignalMatch {
    param(
        [string[]]$Lines,
        [string[]]$Patterns
    )

    $lastMatch = $null
    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++) {
        foreach ($pattern in $Patterns) {
            if ($Lines[$lineIndex].Contains($pattern)) {
                $lastMatch = [pscustomobject]@{
                    Pattern   = $pattern
                    LineIndex = $lineIndex
                }
            }
        }
    }

    return $lastMatch
}

function Find-RegexSignalMatch {
    param(
        [string[]]$Lines,
        [string[]]$Patterns
    )

    $lastMatch = $null
    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++) {
        foreach ($pattern in $Patterns) {
            if ($Lines[$lineIndex] -match $pattern) {
                $lastMatch = [pscustomobject]@{
                    Pattern   = $pattern
                    LineIndex = $lineIndex
                    Line      = $Lines[$lineIndex]
                }
            }
        }
    }

    return $lastMatch
}

function Get-WebGlBuildProgressState {
    param([string[]]$Lines)

    $activePatterns = @(
        'Link_WebGL_wasm',
        'C_WebGL_wasm',
        '\[\d+/\d+\].*(WebGL|wasm)'
    )
    $successPatterns = @(
        "Build completed with a result of 'Succeeded'",
        '^\s*Result\s*:\s*Succeeded\s*$'
    )
    $failurePatterns = @(
        "Build completed with a result of 'Failed'",
        "Build completed with a result of 'Cancelled'",
        'BuildPlayerWindow\+BuildMethodException',
        '^\s*Result\s*:\s*(Failed|Cancelled)\s*$'
    )

    $activeMatch = Find-RegexSignalMatch -Lines $Lines -Patterns $activePatterns
    $successMatch = Find-RegexSignalMatch -Lines $Lines -Patterns $successPatterns
    $failureMatch = Find-RegexSignalMatch -Lines $Lines -Patterns $failurePatterns

    $status = "Idle"
    $summary = "No active WebGL build markers were detected in the latest Unity log tail."
    $terminalSignal = $null

    if ($successMatch -and (-not $failureMatch -or $successMatch.LineIndex -ge $failureMatch.LineIndex)) {
        $status = "Succeeded"
        $summary = "Editor.log reports a completed successful WebGL build."
        $terminalSignal = $successMatch
    }
    elseif ($failureMatch -and (-not $successMatch -or $failureMatch.LineIndex -gt $successMatch.LineIndex)) {
        $status = "Failed"
        $summary = "Editor.log reports a failed WebGL build."
        $terminalSignal = $failureMatch
    }
    elseif ($activeMatch) {
        $status = "InProgress"
        $summary = "Editor.log still shows active WebGL Bee/wasm progress with no later terminal build marker."
    }

    return [pscustomobject]@{
        Status         = $status
        Summary        = $summary
        ActiveSignal   = $activeMatch
        TerminalSignal = $terminalSignal
    }
}

function Get-CodexUnityMcpConfig {
    $configPath = Join-Path $env:USERPROFILE ".codex\\config.toml"
    if (-not (Test-Path -LiteralPath $configPath)) {
        return $null
    }

    $inUnitySection = $false
    $command = $null
    $argsLine = $null

    foreach ($line in Get-Content -LiteralPath $configPath -ErrorAction SilentlyContinue) {
        $trimmed = $line.Trim()

        if ($trimmed -match '^\[mcp_servers\."unity-mcp"\]$') {
            $inUnitySection = $true
            continue
        }

        if ($inUnitySection -and $trimmed.StartsWith("[")) {
            break
        }

        if (-not $inUnitySection) {
            continue
        }

        if ($trimmed -match '^command\s*=\s*["''](?<value>.+?)["'']$') {
            $command = $matches["value"]
            continue
        }

        if ($trimmed -match '^args\s*=\s*(?<value>\[.+\])$') {
            $argsLine = $matches["value"]
        }
    }

    if (-not $inUnitySection) {
        return $null
    }

    $usesWrapper = $false
    $usesRawRelay = $false
    $commandLeaf = if ([string]::IsNullOrWhiteSpace($command)) { $null } else { [System.IO.Path]::GetFileName($command).ToLowerInvariant() }
    if (($commandLeaf -eq "node" -or $commandLeaf -eq "node.exe") -and $argsLine -like "*unity-mcp-stdio-wrapper.js*") {
        $usesWrapper = $true
    }

    if (($command -match "relay(_win)?\\.exe") -or ($argsLine -match "--mcp")) {
        $usesRawRelay = $true
    }

    return [pscustomobject]@{
        Path         = $configPath
        Command      = $command
        Args         = $argsLine
        UsesWrapper  = $usesWrapper
        UsesRawRelay = $usesRawRelay
    }
}

function Get-UnityMcpLocalSettings {
    $settingsPath = Join-Path $env:USERPROFILE ".codex\unity-mcp-settings.json"
    $defaults = [ordered]@{
        Path                    = $settingsPath
        WrapperMode             = "thin"
        AllowManualWrapper      = $false
        AllowCachedToolsFallback = $false
        DirectRelayExperimental = $false
        EagerConnectOnInitialize = $false
        ToolsCacheTtlMs         = 300000
        ReloadWaitTimeoutMs     = 5000
        ReloadPollIntervalMs    = 400
    }

    if (-not (Test-Path -LiteralPath $settingsPath)) {
        return [pscustomobject]$defaults
    }

    try {
        $parsed = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        foreach ($key in @("WrapperMode", "AllowManualWrapper", "AllowCachedToolsFallback", "DirectRelayExperimental", "EagerConnectOnInitialize", "ToolsCacheTtlMs", "ReloadWaitTimeoutMs", "ReloadPollIntervalMs")) {
            if ($null -ne $parsed.$key) {
                $defaults[$key] = $parsed.$key
            }
        }
    }
    catch {
        $defaults["Error"] = $_.Exception.Message
    }

    return [pscustomobject]$defaults
}

function Get-UnityCompactEditorStateSummary {
    param([object]$EditorState)

    if ($null -eq $EditorState -or $EditorState.success -ne $true -or $null -eq $EditorState.data) {
        return $null
    }

    $probe = $EditorState.data.RuntimeProbe
    return [pscustomobject]@{
        IsPlaying         = ($EditorState.data.IsPlaying -eq $true)
        IsCompiling       = ($EditorState.data.IsCompiling -eq $true)
        IsUpdating        = ($EditorState.data.IsUpdating -eq $true)
        BridgeStatus      = $EditorState.data.BridgeStatus
        ToolDiscoveryMode = $EditorState.data.ToolDiscoveryMode
        ActiveSceneName   = if ($probe) { $probe.ActiveSceneName } else { $null }
        RuntimeProbeReady = ($probe -and $probe.IsAvailable -eq $true -and $probe.HasAdvancedFrames -eq $true)
        RuntimeUpdateCount = if ($probe -and $null -ne $probe.UpdateCount) { [int]$probe.UpdateCount } else { 0 }
    }
}

function Get-UnityCompactWrapperSummary {
    param(
        [object]$WrapperDiagnostics,
        [object]$RecentTransportFailure,
        [object]$RecentHealthySuccess
    )

    if ($null -eq $WrapperDiagnostics -and $null -eq $RecentTransportFailure -and $null -eq $RecentHealthySuccess) {
        return $null
    }

    return [pscustomobject]@{
        ToolDiscoveryMode         = if ($WrapperDiagnostics) { $WrapperDiagnostics.toolDiscoveryMode } else { $null }
        LastToolSource            = if ($WrapperDiagnostics) { $WrapperDiagnostics.lastToolSource } else { $null }
        CachedToolCount           = if ($WrapperDiagnostics) { $WrapperDiagnostics.cachedToolCount } else { $null }
        TotalStatusInstances      = if ($WrapperDiagnostics) { $WrapperDiagnostics.TotalStatusInstances } else { 0 }
        RecentTransportFailure    = if ($RecentTransportFailure) { $RecentTransportFailure.Reason } else { $null }
        RecentTransportFailureAge = if ($RecentTransportFailure) { $RecentTransportFailure.AgeSeconds } else { $null }
        RecentHealthySuccessAge   = if ($RecentHealthySuccess) { $RecentHealthySuccess.AgeSeconds } else { $null }
    }
}

function Get-UnityExpectedReloadMarkerPath {
    param([string]$ProjectPath)

    $projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectPath)) { (Get-Location).Path } else { (Resolve-Path -Path $ProjectPath).Path }
    return (Join-Path $projectRoot "Temp\CodexUnity\expected-reload.json")
}

function Get-UnityExpectedReloadState {
    param([string]$ProjectPath)

    $markerPath = Get-UnityExpectedReloadMarkerPath -ProjectPath $ProjectPath
    $state = [ordered]@{
        Path         = $markerPath
        Exists       = $false
        IsActive     = $false
        IsExpired    = $false
        Reason       = $null
        ChangedPaths = @()
        CreatedAtUtc = $null
        ExpiresAtUtc = $null
        TtlSeconds   = $null
        Error        = $null
    }

    if (-not (Test-Path -LiteralPath $markerPath)) {
        return [pscustomobject]$state
    }

    try {
        $rawState = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
    }
    catch {
        $state.Exists = $true
        $state.Error = $_.Exception.Message
        return [pscustomobject]$state
    }

    $createdAt = $null
    $expiresAt = $null
    try {
        if (-not [string]::IsNullOrWhiteSpace($rawState.CreatedAtUtc)) {
            $createdAt = [DateTimeOffset]::Parse($rawState.CreatedAtUtc)
        }
    }
    catch {
    }

    try {
        if (-not [string]::IsNullOrWhiteSpace($rawState.ExpiresAtUtc)) {
            $expiresAt = [DateTimeOffset]::Parse($rawState.ExpiresAtUtc)
        }
    }
    catch {
    }

    if (-not $expiresAt -and $createdAt -and $null -ne $rawState.TtlSeconds) {
        try {
            $expiresAt = $createdAt.AddSeconds([int]$rawState.TtlSeconds)
        }
        catch {
        }
    }

    $isExpired = $false
    if ($expiresAt) {
        $isExpired = $expiresAt -le [DateTimeOffset]::UtcNow
    }

    $state.Exists = $true
    $state.IsExpired = $isExpired
    $state.IsActive = -not $isExpired
    $state.Reason = $rawState.Reason
    $state.ChangedPaths = @($rawState.ChangedPaths)
    $state.CreatedAtUtc = if ($createdAt) { $createdAt.ToString("o") } else { $rawState.CreatedAtUtc }
    $state.ExpiresAtUtc = if ($expiresAt) { $expiresAt.ToString("o") } else { $rawState.ExpiresAtUtc }
    $state.TtlSeconds = $rawState.TtlSeconds

    return [pscustomobject]$state
}

function Get-UnityObservedDirectFailurePath {
    param([string]$ProjectPath)

    $projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectPath)) { (Get-Location).Path } else { (Resolve-Path -Path $ProjectPath).Path }
    return (Join-Path $projectRoot "Temp\CodexUnity\observed-direct-mcp-failure.json")
}

function Get-UnityObservedDirectFailure {
    param([string]$ProjectPath)

    $statePath = Get-UnityObservedDirectFailurePath -ProjectPath $ProjectPath
    if (-not (Test-Path -LiteralPath $statePath)) {
        return $null
    }

    try {
        $rawState = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    catch {
        return [pscustomobject]@{
            Path = $statePath
            Exists = $true
            IsActive = $false
            Error = $_.Exception.Message
        }
    }

    $createdAt = $null
    $expiresAt = $null
    try {
        if (-not [string]::IsNullOrWhiteSpace($rawState.CreatedAtUtc)) {
            $createdAt = [DateTimeOffset]::Parse($rawState.CreatedAtUtc)
        }
    }
    catch {
    }

    try {
        if (-not [string]::IsNullOrWhiteSpace($rawState.ExpiresAtUtc)) {
            $expiresAt = [DateTimeOffset]::Parse($rawState.ExpiresAtUtc)
        }
    }
    catch {
    }

    if (-not $expiresAt -and $createdAt -and $null -ne $rawState.TtlSeconds) {
        try {
            $expiresAt = $createdAt.AddSeconds([int]$rawState.TtlSeconds)
        }
        catch {
        }
    }

    $now = [DateTimeOffset]::UtcNow
    $isExpired = $false
    if ($expiresAt) {
        $isExpired = $expiresAt -le $now
    }

    if ($isExpired) {
        try {
            Remove-Item -LiteralPath $statePath -Force
        }
        catch {
        }
    }

    return [pscustomobject]@{
        Path         = $statePath
        Exists       = $true
        IsActive     = -not $isExpired
        IsExpired    = $isExpired
        Reason       = $rawState.Reason
        CreatedAtUtc = if ($createdAt) { $createdAt.ToString("o") } else { $rawState.CreatedAtUtc }
        ExpiresAtUtc = if ($expiresAt) { $expiresAt.ToString("o") } else { $rawState.ExpiresAtUtc }
        TtlSeconds   = $rawState.TtlSeconds
        Error        = $null
    }
}

function Get-UnityToolObject {
    param([Parameter(Mandatory = $true)][string]$ResponseText)

    $response = $ResponseText | ConvertFrom-Json
    if ($null -ne $response.result -and $null -ne $response.result.structuredContent) {
        return $response.result.structuredContent
    }

    $text = $null
    if ($null -ne $response.result -and $null -ne $response.result.content -and $response.result.content.Count -gt 0) {
        $text = $response.result.content[0].text
    }
    elseif ($null -ne $response.error) {
        return [ordered]@{
            success = $false
            error   = $response.error.message
        }
    }

    if ([string]::IsNullOrWhiteSpace($text)) {
        return $null
    }

    try {
        return ($text | ConvertFrom-Json)
    }
    catch {
        return [ordered]@{
            rawText = $text
        }
    }
}

function Get-UnityEditorStateViaWrapper {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [switch]$ExpectReload,
        [switch]$AllowManualWrapper,
        [switch]$UseDirectRelay,
        [Parameter()][int]$TimeoutSeconds = 12
    )

    if (-not $AllowManualWrapper -and -not $UseDirectRelay) {
        throw "Manual Unity MCP fallback probe is disabled by policy."
    }

    $projectRoot = (Resolve-Path -Path $ProjectPath).Path
    $invokeScript = Join-Path $PSScriptRoot "Invoke-UnityMcpTool.js"
    $nodePath = (Get-Command node -ErrorAction Stop).Source
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $nodePath
    $psi.Arguments = ('"{0}" "Unity_ManageEditor"' -f $invokeScript.Replace('"', '\"'))
    $psi.WorkingDirectory = $projectRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables["UNITY_MCP_TOOL_ARGS_JSON"] = '{"Action":"GetState"}'
    if ($AllowManualWrapper) {
        $psi.EnvironmentVariables["UNITY_MCP_ALLOW_MANUAL_WRAPPER"] = "1"
    }
    if ($UseDirectRelay) {
        $psi.EnvironmentVariables["UNITY_MCP_DIRECT_RELAY_EXPERIMENTAL"] = "1"
    }

    if ($ExpectReload) {
        $psi.EnvironmentVariables["UNITY_MCP_EXPECT_RELOAD"] = "1"
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        throw "Unity_ManageEditor GetState timed out after $TimeoutSeconds seconds."
    }

    $outputText = $process.StandardOutput.ReadToEnd()
    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ([string]::IsNullOrWhiteSpace($outputText)) {
        if (-not [string]::IsNullOrWhiteSpace($errorText)) {
            throw "Unity_ManageEditor GetState produced no stdout output. stderr: $errorText"
        }

        throw "Unity_ManageEditor GetState produced no stdout output."
    }

    return (Get-UnityToolObject -ResponseText $outputText)
}

function Get-UnityLensHealth {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 8
    )

    $projectRoot = (Resolve-Path -Path $ProjectPath).Path
    $invokeScript = Join-Path $PSScriptRoot "Invoke-UnityMcpTool.js"
    $nodePath = (Get-Command node -ErrorAction Stop).Source
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $nodePath
    $psi.Arguments = ('"{0}" "Unity.GetLensHealth"' -f $invokeScript.Replace('"', '\"'))
    $psi.WorkingDirectory = $projectRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $process = [System.Diagnostics.Process]::Start($psi)
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        throw "Unity.GetLensHealth timed out after $TimeoutSeconds seconds."
    }

    $outputText = $process.StandardOutput.ReadToEnd()
    $errorText = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ([string]::IsNullOrWhiteSpace($outputText)) {
        if (-not [string]::IsNullOrWhiteSpace($errorText)) {
            throw "Unity.GetLensHealth produced no stdout output. stderr: $errorText"
        }

        throw "Unity.GetLensHealth produced no stdout output."
    }

    return (Get-UnityToolObject -ResponseText $outputText)
}

$normalizedProjectPath = Normalize-ProjectPath -PathValue $ProjectPath
$bridgeDirectory = Join-Path $env:USERPROFILE ".unity\\mcp\\connections"
$editorLogPath = Join-Path $env:LOCALAPPDATA "Unity\\Editor\\Editor.log"
$codexConfig = Get-CodexUnityMcpConfig
$unityMcpSettings = Get-UnityMcpLocalSettings
$manualFallbackProbeAllowed = $unityMcpSettings.AllowManualWrapper -eq $true
$directRelayExperimental = $unityMcpSettings.DirectRelayExperimental -eq $true
$fallbackProbeEnabled = $manualFallbackProbeAllowed -or $directRelayExperimental
$assistantPackageState = Get-UnityAssistantPackageState -ProjectPath $ProjectPath
$unityProcesses = @(Get-Process Unity -ErrorAction SilentlyContinue)
$unityRunning = $unityProcesses.Count -gt 0
$expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $ProjectPath
$observedDirectFailure = Get-UnityObservedDirectFailure -ProjectPath $ProjectPath
$editorStatusBeacon = Get-UnityEditorStatusBeacon -ProjectPath $ProjectPath -IncludeRaw
$beaconWait = $null
$beaconWaitTimeoutSeconds = 2
$beaconWaitPollIntervalSeconds = 0.25
if ($unityRunning -and ((Test-UnityEditorBeaconTransitionSnapshot -Snapshot $editorStatusBeacon) -or (Test-UnityEditorBeaconBuildSnapshot -Snapshot $editorStatusBeacon))) {
    $beaconWait = Wait-UnityEditorBeaconStable -ProjectPath $ProjectPath -TimeoutSeconds $beaconWaitTimeoutSeconds -PollIntervalSeconds $beaconWaitPollIntervalSeconds
    if ($beaconWait.lastSnapshot) {
        $editorStatusBeacon = $beaconWait.lastSnapshot
    }
}
$editorState = $null
$editorStateError = $null
$editorIsCompiling = $false
$editorIsUpdating = $false
$editorStateUnavailableDuringExpectedReload = $false
$editorStateSkippedForBuild = $false
$editorStateSkippedForBeacon = $false
$lensHealth = $null
$lensHealthError = $null
$lensHealthOverridesBeacon = $false
$beaconIndicatesTransition = (Test-UnityEditorBeaconTransitionSnapshot -Snapshot $editorStatusBeacon) -or ($beaconWait -and (Test-UnityEditorBeaconTransitionSnapshot -Snapshot $beaconWait.lastSnapshot))
$beaconIndicatesBuild = (Test-UnityEditorBeaconBuildSnapshot -Snapshot $editorStatusBeacon) -or ($beaconWait -and (Test-UnityEditorBeaconBuildSnapshot -Snapshot $beaconWait.lastSnapshot))

$statusCandidates = @()
if (Test-Path -LiteralPath $bridgeDirectory) {
    $statusFiles = Get-ChildItem -LiteralPath $bridgeDirectory -Filter "bridge-status-*.json" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending

    foreach ($statusFile in $statusFiles) {
        try {
            $rawStatus = Get-Content -LiteralPath $statusFile.FullName -Raw | ConvertFrom-Json
            $expectedRecoveryExpiresUtc = $null
            $expectedRecoveryActive = $rawStatus.expected_recovery -eq $true
            $expectedRecoveryExpired = $false

            if (-not [string]::IsNullOrWhiteSpace($rawStatus.expected_recovery_expires_utc)) {
                try {
                    $expectedRecoveryExpiresUtc = [datetime]::Parse($rawStatus.expected_recovery_expires_utc).ToUniversalTime()
                    if ($expectedRecoveryActive -and $expectedRecoveryExpiresUtc -le [datetime]::UtcNow) {
                        $expectedRecoveryActive = $false
                        $expectedRecoveryExpired = $true
                    }
                }
                catch {
                    $expectedRecoveryExpiresUtc = $null
                }
            }

            $statusProjectRoot = if (-not [string]::IsNullOrWhiteSpace($rawStatus.project_root)) { $rawStatus.project_root } else { $rawStatus.project_path }
            $statusCandidates += [pscustomobject]@{
                FilePath       = $statusFile.FullName
                LastWriteTime  = $statusFile.LastWriteTime
                Status         = $rawStatus.status
                Reason         = $rawStatus.reason
                ExpectedRecovery = $expectedRecoveryActive
                ExpectedRecoveryExpiresUtc = $expectedRecoveryExpiresUtc
                ExpectedRecoveryExpired = $expectedRecoveryExpired
                ToolDiscoveryMode = $rawStatus.tool_discovery_mode
                ToolCount      = $rawStatus.tool_count
                ToolsHash      = $rawStatus.tools_hash
                ToolDiscoveryReason = $rawStatus.tool_discovery_reason
                ToolSnapshotUtc = $rawStatus.tool_snapshot_utc
                CommandHealth  = $rawStatus.command_health
                LastCommandSuccessUtc = $rawStatus.last_command_success_utc
                LastCommandFailureUtc = $rawStatus.last_command_failure_utc
                LastCommandFailureReason = $rawStatus.last_command_failure_reason
                ProjectPath    = $rawStatus.project_path
                ProjectRoot    = $rawStatus.project_root
                ConnectionPath = $rawStatus.connection_path
                LastHeartbeat  = $rawStatus.last_heartbeat
                MatchesProject = (Normalize-ProjectPath -PathValue $statusProjectRoot) -eq $normalizedProjectPath
            }
        }
        catch {
        }
    }
}

$selectedStatus = $statusCandidates | Where-Object { $_.MatchesProject } | Select-Object -First 1
if (-not $selectedStatus) {
    $selectedStatus = $statusCandidates | Select-Object -First 1
}

if ($unityRunning -and $beaconIndicatesTransition -and -not $beaconIndicatesBuild -and $selectedStatus -and $selectedStatus.Status -eq "ready") {
    try {
        $lensHealth = Get-UnityLensHealth -ProjectPath $ProjectPath
        if ($lensHealth -and $lensHealth.success -eq $true -and
            $lensHealth.data.bridgeStatus.status -eq "ready" -and
            $lensHealth.data.editorStability.isStable -eq $true -and
            $lensHealth.data.expectedRecovery.isActive -ne $true) {
            $lensHealthOverridesBeacon = $true
            $beaconIndicatesTransition = $false
        }
    }
    catch {
        $lensHealthError = $_.Exception.Message
    }
}

$editorLogTailLines = @()
if (Test-Path -LiteralPath $editorLogPath) {
    $editorLogTailLines = @(Get-Content -LiteralPath $editorLogPath -Tail $EditorLogTail -ErrorAction SilentlyContinue)
}
$webGlBuildState = Get-WebGlBuildProgressState -Lines $editorLogTailLines
$wrapperDiagnostics = Get-UnityWrapperDiagnostics -MaxStatusInstances $MaxWrapperStatusInstances

$signalPatterns = [ordered]@{
    ApprovalPending = @("Awaiting user approval", "approval_pending", "Validation: Pending")
    HandshakeFailed = @("Handshake failed", "Connection closed during write")
    Disconnected    = @("disconnected", "Connection closed")
    ReadDisconnect  = @("Connection closed during read")
    DisposedTransport = @("Failed to write response: Cannot access a disposed object.", "Object name: 'NamedPipeTransport'.")
    AutoApproved    = @("Connection auto-approved")
    Connected       = @("Client connected")
    PipeReady       = @("Created secure pipe")
    AuthWarning     = @("Project ID request failed", "401 (401)")
}

$detectedSignals = @()
foreach ($entry in $signalPatterns.GetEnumerator()) {
    $match = Find-SignalMatch -Lines $editorLogTailLines -Patterns $entry.Value
    if ($null -ne $match) {
        $detectedSignals += [pscustomobject]@{
            Name      = $entry.Key
            Pattern   = $match.Pattern
            LineIndex = $match.LineIndex
        }
    }
}

$approvalSignal = $detectedSignals | Where-Object { $_.Name -eq "ApprovalPending" } | Select-Object -First 1
$handshakeSignal = $detectedSignals | Where-Object { $_.Name -eq "HandshakeFailed" } | Select-Object -First 1
$reloadTransportSignal = $detectedSignals | Where-Object { $_.Name -in @("HandshakeFailed", "Disconnected", "ReadDisconnect", "DisposedTransport") } | Select-Object -First 1

if ($unityRunning -and -not $beaconIndicatesTransition -and -not $beaconIndicatesBuild -and $webGlBuildState.Status -ne "InProgress" -and $fallbackProbeEnabled) {
    try {
        $editorState = Get-UnityEditorStateViaWrapper `
            -ProjectPath $ProjectPath `
            -ExpectReload:$expectedReloadState.IsActive `
            -AllowManualWrapper:$manualFallbackProbeAllowed `
            -UseDirectRelay:$directRelayExperimental `
            -TimeoutSeconds 12
        if ($editorState -and $editorState.success -eq $true) {
            $editorIsCompiling = $editorState.data.IsCompiling -eq $true
            $editorIsUpdating = $editorState.data.IsUpdating -eq $true
        }
        elseif ($editorState -and $editorState.success -ne $true -and $expectedReloadState.IsActive) {
            $editorStateUnavailableDuringExpectedReload = $true
        }
    }
    catch {
        $editorStateError = $_.Exception.Message
    }
}
elseif ($unityRunning -and -not $beaconIndicatesTransition -and -not $beaconIndicatesBuild -and $webGlBuildState.Status -ne "InProgress" -and -not $fallbackProbeEnabled) {
    $editorStateError = "Manual Unity MCP fallback probe is disabled by policy."
}
elseif ($unityRunning -and ($beaconIndicatesTransition -or $beaconIndicatesBuild)) {
    $editorStateSkippedForBeacon = $true
    $editorStateError = "Skipped direct Unity editor-state probe because the editor status beacon reports phase '$($editorStatusBeacon.Phase)'."
}
elseif ($unityRunning -and $webGlBuildState.Status -eq "InProgress") {
    $editorStateSkippedForBuild = $true
}

$editorReloading = $editorIsCompiling -or $editorIsUpdating
$expectedReloadCanExplainFailure = $unityRunning -and ($expectedReloadState.IsActive -or $editorReloading) -and ($editorReloading -or $editorStateUnavailableDuringExpectedReload -or $reloadTransportSignal -or $editorStateError)
$emptyToolSurfacePattern = "Tool '.+' not found\. Available tools:\s*$"
$editorStateEmptyToolSurface = ($editorState -and $editorState.success -ne $true -and $editorState.error -match $emptyToolSurfacePattern)
$editorStateErrorEmptyToolSurface = (-not [string]::IsNullOrWhiteSpace($editorStateError) -and $editorStateError -match $emptyToolSurfacePattern)
$bridgeCommandHealthFailed = $selectedStatus -and $selectedStatus.CommandHealth -eq "failed"
$bridgeTransportRecovering = $selectedStatus -and $selectedStatus.Status -in @("transport_recovering", "transport_degraded")
$degradedAuthorityProbe = $null
$degradedAuthorityProbeError = $null
if ($unityRunning -and
    -not $beaconIndicatesBuild -and
    -not $beaconIndicatesTransition -and
    $webGlBuildState.Status -ne "InProgress" -and
    ($bridgeCommandHealthFailed -or $bridgeTransportRecovering)) {
    try {
        $degradedAuthorityProbe = Get-UnityLensHealth -ProjectPath $ProjectPath -TimeoutSeconds 30
    }
    catch {
        $degradedAuthorityProbeError = $_.Exception.Message
    }
}
$degradedAuthorityProbeOk = $degradedAuthorityProbe -and $degradedAuthorityProbe.success -eq $true
$wrapperDiagnosticsRelevant = ($manualFallbackProbeAllowed -or ($codexConfig -and $codexConfig.UsesWrapper))
$wrapperRecentTransportFailure = if ($wrapperDiagnostics) { $wrapperDiagnostics.RecentTransportFailure } else { $null }
$wrapperRecentTransportFailureReason = if ($wrapperRecentTransportFailure) {
    if (-not [string]::IsNullOrWhiteSpace($wrapperRecentTransportFailure.lastToolCallError)) {
        $wrapperRecentTransportFailure.lastToolCallError
    }
    else {
        $wrapperRecentTransportFailure.lastToolsError
    }
}
else {
    $null
}
$wrapperRecentHealthySuccess = if ($wrapperDiagnostics) { $wrapperDiagnostics.RecentWrapperSuccess } else { $null }
$observedDirectTransportFailure = $observedDirectFailure -and $observedDirectFailure.IsActive

$classification = "Unavailable"
$userActionRequired = $true
$recommendedAction = "Send a notification and pause Unity editor mutations until the bridge is healthy."
$summary = "Unity MCP is unavailable."
$exitCode = 12

if ($codexConfig -and $codexConfig.UsesRawRelay -and -not $directRelayExperimental) {
    $classification = "CodexConfigMismatch"
    $summary = "Codex is configured to launch the raw Unity relay directly instead of the stdio wrapper."
    $recommendedAction = "Either switch back to the stdio wrapper or enable direct relay experimental mode in $($unityMcpSettings.Path), then restart Codex to reload the MCP config."
    $exitCode = 13
}
elseif ($approvalSignal) {
    $classification = "ApprovalPending"
    $summary = "Unity MCP is waiting for user approval in the Unity Editor."
    $recommendedAction = "Approve the Unity MCP connection in the Unity Editor, then retry the MCP call."
    $exitCode = 10
}
elseif ($beaconIndicatesBuild) {
    $classification = "BuildInProgress"
    $userActionRequired = $false
    $summary = "The editor status beacon reports an active Unity player-build transition."
    $recommendedAction = "Stop retrying MCP recovery during the active build. Monitor Editor.log, build artifacts, or the beacon until the editor returns to idle."
    $exitCode = 15
}
elseif ($webGlBuildState.Status -eq "InProgress") {
    $classification = "BuildInProgress"
    $userActionRequired = $false
    $summary = $webGlBuildState.Summary
    $recommendedAction = "Stop retrying MCP recovery during the active WebGL build. Monitor Editor.log and build output artifacts until the build completes or fails."
    $exitCode = 15
}
elseif ($beaconIndicatesTransition) {
    $classification = "EditorReloadingExpected"
    $userActionRequired = $false
    $summary = "The editor status beacon reports phase '$($editorStatusBeacon.Phase)' and transient bridge churn is expected during this Unity transition."
    $recommendedAction = "Wait for the editor status beacon to return to idle or playing, then retry the MCP call. Do not notify the user unless the beacon goes stale and failures continue."
    $exitCode = 14
}
elseif ($expectedReloadCanExplainFailure) {
    $classification = "EditorReloadingExpected"
    $userActionRequired = $false
    $summary = if ($editorReloading) {
        "Unity is compiling or updating assets and MCP churn is expected during the current reload window."
    }
    else {
        "Unity is inside an expected compile/domain reload window and transient MCP disconnects are being treated as normal."
    }
    $recommendedAction = "Wait for Unity to settle back to idle, then retry the MCP call. Do not notify the user unless the reload window expires and failures continue."
    $exitCode = 14
}
elseif ($handshakeSignal) {
    $classification = "ReconnectRequired"
    $summary = "Unity MCP reached the bridge, but the handshake failed before tools became available."
    $recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call."
    $exitCode = 11
}
elseif ($unityRunning -and ($editorStateEmptyToolSurface -or $editorStateErrorEmptyToolSurface)) {
    $classification = "ReconnectRequired"
    $summary = "Bridge status may report ready, but the direct Unity MCP probe returned an empty tool surface."
    $recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call."
    $exitCode = 11
}
elseif ($unityRunning -and ($bridgeCommandHealthFailed -or $bridgeTransportRecovering) -and $degradedAuthorityProbeOk) {
    $classification = "Ready"
    $userActionRequired = $false
    $summary = "Bridge status was degraded, but a fresh lightweight Lens authority probe succeeded."
    $recommendedAction = "Proceed with Lens tools using normal idle gating and longer post-reload probe budgets."
    $exitCode = 0
}
elseif ($unityRunning -and
    $editorState -and $editorState.success -eq $true -and
    (($wrapperDiagnosticsRelevant -and $wrapperDiagnostics -and (($wrapperDiagnostics.toolDiscoveryMode -eq "cached-reload-fallback") -or ($wrapperDiagnostics.lastToolSource -eq "cached-reload-fallback") -or ($wrapperRecentTransportFailure -and $wrapperRecentHealthySuccess))) -or $bridgeCommandHealthFailed -or $bridgeTransportRecovering -or $observedDirectTransportFailure)) {
    $classification = "WrapperUnhealthyDirectMcpOk"
    $userActionRequired = $false
    if ($observedDirectTransportFailure) {
        $summary = if ($directRelayExperimental) {
            "A recent direct Unity MCP failure was observed, but the direct-relay recovery probe is healthy."
        }
        else {
            "A recent direct Unity MCP failure was observed, but the manual wrapper editor-state probe is healthy."
        }
        $recommendedAction = if ($directRelayExperimental) {
            "Keep the direct-relay experiment enabled, wait for Unity to settle, and avoid escalating unless the recovery probe also fails."
        }
        else {
            "Continue on the manual wrapper path only if you explicitly enabled it for this session and avoid raw direct MCP calls until a later direct probe succeeds."
        }
    }
    elseif ($wrapperRecentTransportFailure -and $wrapperRecentHealthySuccess) {
        $summary = if ($directRelayExperimental) {
            "A recent transport failure recovered after a healthy direct-relay editor-state probe."
        }
        else {
            "A recent wrapper instance reported a transport failure, but a healthy wrapper/manual editor-state probe recovered afterward."
        }
        $recommendedAction = if ($directRelayExperimental) {
            "Continue on the direct-relay recovery path for this session and avoid escalating unless the recovery probe also fails."
        }
        else {
            "Continue on the manual wrapper path only if you explicitly enabled it for this session and avoid raw direct MCP calls until a later direct probe succeeds."
        }
    }
    elseif ($bridgeCommandHealthFailed -or $bridgeTransportRecovering) {
        $summary = if ($directRelayExperimental) {
            "Bridge discovery is degraded, but the direct-relay recovery probe is still healthy."
        }
        else {
            "Bridge discovery is present, but the latest direct command health signal is degraded while wrapper/manual editor-state probes are still healthy."
        }
        $recommendedAction = if ($directRelayExperimental) {
            "Keep waiting through the reload window and avoid notifying the user unless the direct-relay recovery probe also fails."
        }
        else {
            "Continue on the manual wrapper path only if you explicitly enabled it and avoid notifying the user unless that path also fails."
        }
    }
    else {
        $summary = "The stdio wrapper is serving cached tools during a reload-prone window, but direct Unity editor state probes are healthy."
        $recommendedAction = if ($directRelayExperimental) {
            "Keep the direct-relay experiment enabled, wait for Unity to settle, and avoid notifying the user unless direct probes also fail."
        }
        else {
            "Wait for Unity to settle and avoid notifying the user unless direct probes also fail. Do not fall back to the manual wrapper path unless you explicitly enabled it."
        }
    }
    $exitCode = 16
}
elseif ($unityRunning -and ($bridgeCommandHealthFailed -or $bridgeTransportRecovering)) {
    $classification = "ReconnectRequired"
    $summary = if (-not [string]::IsNullOrWhiteSpace($degradedAuthorityProbeError)) {
        "Bridge status is degraded, and the fresh lightweight Lens authority probe failed: $degradedAuthorityProbeError"
    }
    else {
        "Bridge status exists, but the latest direct command health signal is degraded and no healthy wrapper/manual probe was available to take over."
    }
    $recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call."
    $exitCode = 11
}
elseif ($unityRunning -and $wrapperRecentTransportFailure) {
    $classification = "ReconnectRequired"
    $summary = "A recent wrapper instance reported a Unity transport failure and no healthy wrapper/manual recovery probe was available to take over."
    $recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call."
    $exitCode = 11
}
elseif ($unityRunning -and $observedDirectTransportFailure) {
    $classification = "ReconnectRequired"
    $summary = "A recent direct Unity MCP failure was observed and no healthy wrapper/manual recovery probe was available to take over."
    $recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call."
    $exitCode = 11
}
elseif ($selectedStatus -and $selectedStatus.ExpectedRecoveryExpired -and $selectedStatus.Status -in @("editor_reloading", "transport_recovering")) {
    $classification = "ReconnectRequired"
    $summary = "Bridge status remained in '$($selectedStatus.Status)' past its expected recovery window."
    $recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call."
    $exitCode = 11
}
elseif ($selectedStatus -and $selectedStatus.Status -eq "ready" -and $unityRunning) {
    $classification = "Ready"
    $userActionRequired = $false
    $summary = if ($lensHealthOverridesBeacon) {
        "Lens health confirmed the bridge is ready and the editor is stable, overriding a lagging reload beacon."
    }
    else {
        "Bridge status reports ready and no blocking failure signal was found in the latest Unity log tail."
    }
    $recommendedAction = "Proceed with a lightweight Unity MCP call."
    $exitCode = 0
}
elseif (-not $unityRunning) {
    $classification = "UnityNotRunning"
    $summary = "Unity editor is not running for this project."
    $recommendedAction = "Open the project in Unity and wait for the bridge to initialize."
}
elseif ($selectedStatus -and $selectedStatus.Status) {
    $classification = "BridgeNotReady"
    $summary = "Bridge status file exists, but the current status is '$($selectedStatus.Status)'."
    $recommendedAction = "Wait for the bridge to become ready or reconnect it, then retry the MCP call."
}

$result = [pscustomobject]@{
    ServerName         = $ServerName
    ProjectPath        = $ProjectPath
    NormalizedProjectPath = $normalizedProjectPath
    UnityRunning       = $unityRunning
    Classification     = $classification
    UserActionRequired = $userActionRequired
    Summary            = $summary
    RecommendedAction  = $recommendedAction
    CodexConfig        = if ($codexConfig) {
        [pscustomobject]@{
            Path         = $codexConfig.Path
            Command      = $codexConfig.Command
            Args         = $codexConfig.Args
            UsesWrapper  = $codexConfig.UsesWrapper
            UsesRawRelay = $codexConfig.UsesRawRelay
        }
    }
    else {
        $null
    }
    BridgeStatus       = if ($selectedStatus) {
        [pscustomobject]@{
            FilePath       = $selectedStatus.FilePath
            Status         = $selectedStatus.Status
            Reason         = $selectedStatus.Reason
            ExpectedRecovery = $selectedStatus.ExpectedRecovery
            ExpectedRecoveryExpiresUtc = $selectedStatus.ExpectedRecoveryExpiresUtc
            ExpectedRecoveryExpired = $selectedStatus.ExpectedRecoveryExpired
            ProjectPath    = $selectedStatus.ProjectPath
            ProjectRoot    = $selectedStatus.ProjectRoot
            ConnectionPath = $selectedStatus.ConnectionPath
            LastHeartbeat  = $selectedStatus.LastHeartbeat
            MatchesProject = $selectedStatus.MatchesProject
            ToolDiscoveryMode = $selectedStatus.ToolDiscoveryMode
            ToolCount      = $selectedStatus.ToolCount
            ToolsHash      = $selectedStatus.ToolsHash
            ToolDiscoveryReason = $selectedStatus.ToolDiscoveryReason
            ToolSnapshotUtc = $selectedStatus.ToolSnapshotUtc
            CommandHealth  = $selectedStatus.CommandHealth
            LastCommandSuccessUtc = $selectedStatus.LastCommandSuccessUtc
            LastCommandFailureUtc = $selectedStatus.LastCommandFailureUtc
            LastCommandFailureReason = $selectedStatus.LastCommandFailureReason
        }
    }
    else {
        $null
    }
    AssistantPackage = $assistantPackageState
    UnityMcpSettings = $unityMcpSettings
    DegradedAuthorityProbe = if ($degradedAuthorityProbe) {
        [pscustomobject]@{
            Success = $degradedAuthorityProbe.success -eq $true
            Error = $degradedAuthorityProbeError
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($degradedAuthorityProbeError)) {
        [pscustomobject]@{
            Success = $false
            Error = $degradedAuthorityProbeError
        }
    }
    else {
        $null
    }
    ObservedDirectFailure = $observedDirectFailure
    WrapperRecentTransportFailure = if ($wrapperRecentTransportFailure) {
        [pscustomobject]@{
            Path = $wrapperRecentTransportFailure.Path
            AgeSeconds = $wrapperRecentTransportFailure.AgeSeconds
            Reason = $wrapperRecentTransportFailureReason
            LastToolCallName = $wrapperRecentTransportFailure.lastToolCallName
            LastToolCallAt = $wrapperRecentTransportFailure.lastToolCallAt
        }
    }
    else {
        $null
    }
    WrapperRecentHealthySuccess = if ($wrapperRecentHealthySuccess) {
        [pscustomobject]@{
            Path = $wrapperRecentHealthySuccess.Path
            AgeSeconds = $wrapperRecentHealthySuccess.AgeSeconds
            LastToolCallName = $wrapperRecentHealthySuccess.lastToolCallName
            LastToolCallAt = $wrapperRecentHealthySuccess.lastToolCallAt
        }
    }
    else {
        $null
    }
    WrapperDiagnostics = $wrapperDiagnostics
    EditorStatusBeacon = $editorStatusBeacon
    BeaconWait = $beaconWait
    LensHealth = $lensHealth
    LensHealthError = $lensHealthError
    LensHealthOverridesBeacon = $lensHealthOverridesBeacon
    ExpectedReloadState = $expectedReloadState
    EditorState        = $editorState
    EditorStateError   = $editorStateError
    EditorReloading    = $editorReloading
    EditorStateUnavailableDuringExpectedReload = $editorStateUnavailableDuringExpectedReload
    EditorStateSkippedForBuild = $editorStateSkippedForBuild
    EditorStateSkippedForBeacon = $editorStateSkippedForBeacon
    DetectedSignals    = $detectedSignals
    WebGLBuildState    = $webGlBuildState
    EditorLogPath      = if (Test-Path -LiteralPath $editorLogPath) { $editorLogPath } else { $null }
}

$compactResult = [pscustomobject]@{
    ServerName         = $ServerName
    ProjectPath        = $ProjectPath
    Classification     = $classification
    UserActionRequired = $userActionRequired
    Summary            = $summary
    RecommendedAction  = $recommendedAction
    CodexConfig        = if ($codexConfig) {
        [pscustomobject]@{
            UsesWrapper  = $codexConfig.UsesWrapper
            UsesRawRelay = $codexConfig.UsesRawRelay
            Command      = $codexConfig.Command
        }
    }
    else {
        $null
    }
    BridgeStatus       = if ($selectedStatus) {
        [pscustomobject]@{
            Status            = $selectedStatus.Status
            Reason            = $selectedStatus.Reason
            ExpectedRecovery  = $selectedStatus.ExpectedRecovery
            ExpectedRecoveryExpiresUtc = $selectedStatus.ExpectedRecoveryExpiresUtc
            ExpectedRecoveryExpired = $selectedStatus.ExpectedRecoveryExpired
            ProjectPath       = $selectedStatus.ProjectPath
            ProjectRoot       = $selectedStatus.ProjectRoot
            MatchesProject    = $selectedStatus.MatchesProject
            ToolDiscoveryMode = $selectedStatus.ToolDiscoveryMode
            ToolCount         = $selectedStatus.ToolCount
            CommandHealth     = $selectedStatus.CommandHealth
        }
    }
    else {
        $null
    }
    Wrapper            = Get-UnityCompactWrapperSummary -WrapperDiagnostics $wrapperDiagnostics -RecentTransportFailure $result.WrapperRecentTransportFailure -RecentHealthySuccess $result.WrapperRecentHealthySuccess
    DegradedAuthorityProbe = $result.DegradedAuthorityProbe
    EditorState        = Get-UnityCompactEditorStateSummary -EditorState $editorState
    EditorStatusBeacon = if ($editorStatusBeacon) {
        [pscustomobject]@{
            Classification = $editorStatusBeacon.Classification
            Phase          = $editorStatusBeacon.Phase
            Fresh          = $editorStatusBeacon.Fresh
        }
    }
    else {
        $null
    }
    LensHealth = if ($lensHealth) {
        [pscustomobject]@{
            Success = ($lensHealth.success -eq $true)
            BridgeStatus = if ($lensHealth.data) { $lensHealth.data.bridgeStatus.status } else { $null }
            EditorStable = if ($lensHealth.data) { $lensHealth.data.editorStability.isStable -eq $true } else { $null }
            ExpectedRecoveryActive = if ($lensHealth.data) { $lensHealth.data.expectedRecovery.isActive -eq $true } else { $null }
            RecommendedNextAction = if ($lensHealth.data) { $lensHealth.data.recommendedNextAction } else { $null }
            OverridesBeacon = $lensHealthOverridesBeacon
            Error = $lensHealthError
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($lensHealthError)) {
        [pscustomobject]@{
            Success = $false
            Error = $lensHealthError
            OverridesBeacon = $false
        }
    }
    else {
        $null
    }
    ExpectedReloadState = if ($expectedReloadState) {
        [pscustomobject]@{
            IsActive = $expectedReloadState.IsActive
            Reason   = $expectedReloadState.Reason
        }
    }
    else {
        $null
    }
    AssistantPackage   = if ($assistantPackageState) {
        [pscustomobject]@{
            Mode       = $assistantPackageState.Mode
            Summary    = $assistantPackageState.Summary
            Dependency = $assistantPackageState.DependencyValue
            Path       = $assistantPackageState.ResolvedFileDependencyPath
        }
    }
    else {
        $null
    }
    UnityMcpSettings  = if ($unityMcpSettings) {
        [pscustomobject]@{
            WrapperMode             = $unityMcpSettings.WrapperMode
            AllowManualWrapper      = $unityMcpSettings.AllowManualWrapper
            AllowCachedToolsFallback = $unityMcpSettings.AllowCachedToolsFallback
            DirectRelayExperimental = $unityMcpSettings.DirectRelayExperimental
        }
    }
    else {
        $null
    }
    DiagnosticsHint    = "Rerun with -IncludeDiagnostics for the full bridge payload."
}

if ($IncludeDiagnostics) {
    $result | ConvertTo-Json -Depth 8
}
else {
    $compactResult | ConvertTo-Json -Depth 8
}
exit $exitCode
