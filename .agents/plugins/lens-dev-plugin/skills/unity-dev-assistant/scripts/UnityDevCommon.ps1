function Get-UnityBridgeSkillPath {
    (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..\..\unity-mcp-bridge")).Path
}

. (Join-Path (Get-UnityBridgeSkillPath) "scripts\Wait-UnityEditorStatus.ps1")

function Get-UnityWrapperDiagnosticsPath {
    Join-Path $env:USERPROFILE ".codex\cache\unity-mcp-wrapper-status.json"
}

function Get-UnityWrapperDiagnostics {
    $path = Get-UnityWrapperDiagnosticsPath
    if (-not (Test-Path -LiteralPath $path)) {
        return $null
    }

    try {
        return (Get-Content -LiteralPath $path -Raw | ConvertFrom-Json)
    }
    catch {
        return [pscustomobject]@{
            Path  = $path
            Error = $_.Exception.Message
        }
    }
}

function Get-ScreenshotSkillPath {
    Join-Path $env:USERPROFILE ".codex\skills\screenshot"
}

function Resolve-UnityProjectPath {
    param([string]$ProjectPath)

    if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
        return (Get-Location).Path
    }

    return (Resolve-Path -Path $ProjectPath).Path
}

function Get-UnitySkillStateDirectory {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    return (Join-Path $projectRoot "Temp\CodexUnity")
}

function Get-UnityExpectedReloadMarkerPath {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    return (Join-Path (Get-UnitySkillStateDirectory -ProjectPath $ProjectPath) "expected-reload.json")
}

function Resolve-UnityRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $projectRootNormalized = $projectRoot.TrimEnd("\", "/")
    $candidate = $PathValue

    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $projectRoot $candidate
    }

    try {
        if (Test-Path -LiteralPath $candidate) {
            $candidate = (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    catch {
    }

    $candidateNormalized = $candidate -replace "\\", "/"
    $projectRootForCompare = $projectRootNormalized -replace "\\", "/"

    if ($candidateNormalized.StartsWith("$projectRootForCompare/", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $candidateNormalized.Substring($projectRootForCompare.Length + 1)
    }

    if ($candidateNormalized.Equals($projectRootForCompare, [System.StringComparison]::OrdinalIgnoreCase)) {
        return "."
    }

    return ($PathValue -replace "\\", "/")
}

function Get-UnityExpectedReloadState {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [switch]$IncludeExpired
    )

    $markerPath = Get-UnityExpectedReloadMarkerPath -ProjectPath $ProjectPath
    $state = [ordered]@{
        Path          = $markerPath
        Exists        = $false
        IsActive      = $false
        IsExpired     = $false
        Reason        = $null
        ChangedPaths  = @()
        CreatedAtUtc  = $null
        ExpiresAtUtc  = $null
        TtlSeconds    = $null
        Error         = $null
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
    $state.IsActive = -not $isExpired -or $IncludeExpired
    $state.Reason = $rawState.Reason
    $state.ChangedPaths = @($rawState.ChangedPaths)
    $state.CreatedAtUtc = if ($createdAt) { $createdAt.ToString("o") } else { $rawState.CreatedAtUtc }
    $state.ExpiresAtUtc = if ($expiresAt) { $expiresAt.ToString("o") } else { $rawState.ExpiresAtUtc }
    $state.TtlSeconds = $rawState.TtlSeconds

    return [pscustomobject]$state
}

function Set-UnityExpectedReloadState {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Reason,
        [Parameter()][string[]]$ChangedPaths = @(),
        [Parameter()][int]$TtlSeconds = 120
    )

    $createdAt = [DateTimeOffset]::UtcNow
    $expiresAt = $createdAt.AddSeconds($TtlSeconds)
    $normalizedPaths = @()

    foreach ($pathValue in $ChangedPaths) {
        $normalized = Resolve-UnityRelativePath -ProjectPath $ProjectPath -PathValue $pathValue
        if (-not [string]::IsNullOrWhiteSpace($normalized) -and $normalizedPaths -notcontains $normalized) {
            $normalizedPaths += $normalized
        }
    }

    $data = [ordered]@{
        Reason       = $Reason
        ChangedPaths = $normalizedPaths
        CreatedAtUtc = $createdAt.ToString("o")
        ExpiresAtUtc = $expiresAt.ToString("o")
        TtlSeconds   = $TtlSeconds
    }

    Save-JsonFile -Path (Get-UnityExpectedReloadMarkerPath -ProjectPath $ProjectPath) -Data $data
    return (Get-UnityExpectedReloadState -ProjectPath $ProjectPath)
}

function Clear-UnityExpectedReloadState {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $markerPath = Get-UnityExpectedReloadMarkerPath -ProjectPath $ProjectPath
    if (Test-Path -LiteralPath $markerPath) {
        Remove-Item -LiteralPath $markerPath -Force -ErrorAction SilentlyContinue
    }
}

function Test-UnityCompileAffectingPath {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string]$PathValue
    )

    $relativePath = Resolve-UnityRelativePath -ProjectPath $ProjectPath -PathValue $PathValue
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        return $false
    }

    $normalized = $relativePath.ToLowerInvariant()
    if ($normalized -match '\.(cs|asmdef|asmref|rsp)$') {
        return $true
    }

    if ($normalized -eq "packages/manifest.json" -or $normalized -eq "packages/packages-lock.json") {
        return $true
    }

    if ($normalized -like "packages/*/package.json") {
        return $true
    }

    return $false
}

function Get-UnityCompileAffectingChanges {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string[]]$ChangedPaths = @()
    )

    $relevantPaths = New-Object System.Collections.Generic.List[string]
    foreach ($pathValue in $ChangedPaths) {
        if (-not (Test-UnityCompileAffectingPath -ProjectPath $ProjectPath -PathValue $pathValue)) {
            continue
        }

        $normalized = Resolve-UnityRelativePath -ProjectPath $ProjectPath -PathValue $pathValue
        if (-not [string]::IsNullOrWhiteSpace($normalized) -and -not $relevantPaths.Contains($normalized)) {
            $relevantPaths.Add($normalized)
        }
    }

    return @($relevantPaths)
}

function Test-UnityExpectedReloadError {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return $false
    }

    foreach ($pattern in @(
        "timed out after",
        "produced no stdout output",
        "connection disconnected",
        "connection closed",
        "disposed object",
        "failed to parse unity mcp output"
    )) {
        if ($Message.IndexOf($pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-UnityCommandRunnable {
    param(
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter()][string[]]$Arguments = @(),
        [Parameter()][int]$TimeoutSeconds = 5
    )

    $commandInfo = $null
    try {
        $commandInfo = Get-Command $Command -ErrorAction Stop | Select-Object -First 1
    }
    catch {
        return [pscustomobject]@{
            Available    = $false
            Runnable     = $false
            Path         = $null
            ExitCode     = $null
            StdOut       = $null
            StdErr       = $null
            TimedOut     = $false
            AccessDenied = $false
            Error        = $_.Exception.Message
        }
    }

    $quotedArguments = foreach ($argument in $Arguments) {
        if ($argument -match '\s') {
            '"' + $argument.Replace('"', '\"') + '"'
        }
        else {
            $argument
        }
    }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $commandInfo.Source
    $psi.Arguments = ($quotedArguments -join " ")
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    try {
        $process = [System.Diagnostics.Process]::Start($psi)
    }
    catch {
        $message = $_.Exception.Message
        return [pscustomobject]@{
            Available    = $true
            Runnable     = $false
            Path         = $commandInfo.Source
            ExitCode     = $null
            StdOut       = $null
            StdErr       = $null
            TimedOut     = $false
            AccessDenied = $message.IndexOf("Access is denied", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
            Error        = $message
        }
    }

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        return [pscustomobject]@{
            Available    = $true
            Runnable     = $false
            Path         = $commandInfo.Source
            ExitCode     = $null
            StdOut       = $null
            StdErr       = $null
            TimedOut     = $true
            AccessDenied = $false
            Error        = "Timed out waiting for '$Command' to exit."
        }
    }

    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $exitCode = $process.ExitCode
    $runnable = $exitCode -eq 0
    $accessDenied = $stderr.IndexOf("Access is denied", [System.StringComparison]::OrdinalIgnoreCase) -ge 0

    return [pscustomobject]@{
        Available    = $true
        Runnable     = $runnable
        Path         = $commandInfo.Source
        ExitCode     = $exitCode
        StdOut       = $stdout
        StdErr       = $stderr
        TimedOut     = $false
        AccessDenied = $accessDenied
        Error        = if ($runnable) { $null } else { "Command exited with code $exitCode." }
    }
}

function Get-UnityTextSearchStrategy {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    if ($null -eq $script:UnityTextSearchStrategyCache) {
        $script:UnityTextSearchStrategyCache = @{}
    }

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    if ($script:UnityTextSearchStrategyCache.ContainsKey($projectRoot)) {
        return $script:UnityTextSearchStrategyCache[$projectRoot]
    }

    $rgCheck = Test-UnityCommandRunnable -Command "rg" -Arguments @("--version") -TimeoutSeconds 5
    $strategy = if ($rgCheck.Runnable) { "rg" } else { "powershell" }
    $details = [pscustomobject]@{
        ProjectPath = $projectRoot
        Strategy    = $strategy
        Probe       = $rgCheck
    }

    $script:UnityTextSearchStrategyCache[$projectRoot] = $details
    return $details
}

function Search-UnityRepositoryText {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter()][string[]]$Include = @("*"),
        [switch]$CaseSensitive
    )

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $searchStrategy = Get-UnityTextSearchStrategy -ProjectPath $projectRoot

    if ($searchStrategy.Strategy -eq "rg") {
        $rgArgs = @("--line-number", "--no-heading")
        if (-not $CaseSensitive) {
            $rgArgs += "--ignore-case"
        }

        foreach ($glob in $Include) {
            $rgArgs += @("-g", $glob)
        }

        $rgArgs += @($Pattern, $projectRoot)

        try {
            return & $searchStrategy.Probe.Path $rgArgs
        }
        catch {
        }
    }

    $files = foreach ($glob in $Include) {
        Get-ChildItem -Path $projectRoot -Recurse -File -Filter $glob -ErrorAction SilentlyContinue
    }

    return $files | Select-String -Pattern $Pattern -CaseSensitive:$CaseSensitive.IsPresent
}

function Resolve-UnityAbsolutePath {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string]$PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $candidate = $PathValue
    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $projectRoot $candidate
    }

    try {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    catch {
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Get-UnityEditorLogPath {
    return (Join-Path $env:LOCALAPPDATA "Unity\Editor\Editor.log")
}

function Get-UnityEditorLogTailLines {
    param(
        [Parameter()][string]$EditorLogPath = (Get-UnityEditorLogPath),
        [Parameter()][int]$TailLines = 400
    )

    if ([string]::IsNullOrWhiteSpace($EditorLogPath) -or -not (Test-Path -LiteralPath $EditorLogPath)) {
        return @()
    }

    $lines = @(Get-Content -LiteralPath $EditorLogPath -Tail $TailLines -ErrorAction SilentlyContinue)
    if ($lines.Count -eq 1 -and [string]::IsNullOrWhiteSpace($lines[0])) {
        return @()
    }

    return $lines
}

function Find-UnityRegexSignalMatch {
    param(
        [string[]]$Lines,
        [Parameter(Mandatory = $true)][string[]]$Patterns
    )

    if ($null -eq $Lines -or $Lines.Count -eq 0 -or ($Lines.Count -eq 1 -and [string]::IsNullOrWhiteSpace($Lines[0]))) {
        return $null
    }

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

function Get-UnityBuildReportSummary {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string]$BuildReportPath
    )

    $resolvedPath = Resolve-UnityAbsolutePath -ProjectPath $ProjectPath -PathValue $BuildReportPath
    $summary = [ordered]@{
        Path      = $resolvedPath
        Exists    = $false
        Parsed    = $false
        Result    = $null
        Summary   = $null
        Errors    = @()
        RawPreview = $null
    }

    if ([string]::IsNullOrWhiteSpace($resolvedPath) -or -not (Test-Path -LiteralPath $resolvedPath)) {
        return [pscustomobject]$summary
    }

    $summary.Exists = $true
    $text = Get-Content -LiteralPath $resolvedPath -Raw -ErrorAction SilentlyContinue
    if ([string]::IsNullOrWhiteSpace($text)) {
        return [pscustomobject]$summary
    }

    $lines = @($text -split "`r?`n")
    $summary.RawPreview = (($lines | Select-Object -First 10) -join "`n")

    if ($text -match '(?im)^\s*Result\s*:\s*(?<result>[A-Za-z]+)\s*$') {
        $summary.Result = $matches["result"]
        $summary.Parsed = $true
    }

    if ($text -match '(?im)^\s*Summary\s*:\s*(?<summary>.+?)\s*$') {
        $summary.Summary = $matches["summary"].Trim()
        $summary.Parsed = $true
    }

    $collectErrors = $false
    $errors = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^\s*Errors\s*:\s*$') {
            $collectErrors = $true
            continue
        }

        if (-not $collectErrors) {
            continue
        }

        if ($line -match '^\s*[-*]\s*(?<error>.+?)\s*$') {
            $errors.Add($matches["error"].Trim())
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($line)) {
            break
        }
    }

    $summary.Errors = @($errors)
    return [pscustomobject]$summary
}

function Get-UnityBuildMonitorState {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][ValidateSet("WebGL")][string]$Mode = "WebGL",
        [Parameter()][string]$BuildOutputPath,
        [Parameter()][string]$BuildReportPath,
        [Parameter()][string]$SuccessArtifactPath,
        [Parameter()][string]$EditorLogPath = (Get-UnityEditorLogPath),
        [Parameter()][int]$EditorLogTailLines = 400
    )

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $resolvedOutputPath = Resolve-UnityAbsolutePath -ProjectPath $projectRoot -PathValue $BuildOutputPath
    $resolvedArtifactPath = Resolve-UnityAbsolutePath -ProjectPath $projectRoot -PathValue $SuccessArtifactPath
    $logLines = Get-UnityEditorLogTailLines -EditorLogPath $EditorLogPath -TailLines $EditorLogTailLines
    $buildReport = Get-UnityBuildReportSummary -ProjectPath $projectRoot -BuildReportPath $BuildReportPath

    $outputExists = $false
    $outputSampleFiles = @()
    if (-not [string]::IsNullOrWhiteSpace($resolvedOutputPath) -and (Test-Path -LiteralPath $resolvedOutputPath)) {
        $outputExists = $true
        $outputItem = Get-Item -LiteralPath $resolvedOutputPath -ErrorAction SilentlyContinue
        if ($outputItem -and $outputItem.PSIsContainer) {
            $outputSampleFiles = @(
                Get-ChildItem -LiteralPath $resolvedOutputPath -File -Recurse -ErrorAction SilentlyContinue |
                    Select-Object -First 5 -ExpandProperty FullName
            )
        }
        elseif ($outputItem) {
            $outputSampleFiles = @($outputItem.FullName)
        }
    }

    $successArtifactExists = -not [string]::IsNullOrWhiteSpace($resolvedArtifactPath) -and (Test-Path -LiteralPath $resolvedArtifactPath)
    $activePatterns = @()
    $successPatterns = @()
    $failurePatterns = @()

    switch ($Mode) {
        "WebGL" {
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
        }
    }

    $activeMatch = Find-UnityRegexSignalMatch -Lines $logLines -Patterns $activePatterns
    $successMatch = Find-UnityRegexSignalMatch -Lines $logLines -Patterns $successPatterns
    $failureMatch = Find-UnityRegexSignalMatch -Lines $logLines -Patterns $failurePatterns

    $status = "Idle"
    $summary = "No active $Mode build markers were detected."
    $terminalSignal = $null

    if ($buildReport.Result -match '^(Failed|Cancelled)$') {
        $status = "Failed"
        $summary = if ($buildReport.Summary) {
            "Build report marked the $Mode build as $($buildReport.Result): $($buildReport.Summary)"
        }
        else {
            "Build report marked the $Mode build as $($buildReport.Result)."
        }
        $terminalSignal = [pscustomobject]@{
            Source    = "BuildReport"
            LineIndex = $null
            Pattern   = "Result:$($buildReport.Result)"
            Line      = $buildReport.Summary
        }
    }
    elseif ($successArtifactExists) {
        $status = "Succeeded"
        $summary = "Success artifact detected for the $Mode build."
        $terminalSignal = [pscustomobject]@{
            Source    = "SuccessArtifact"
            LineIndex = $null
            Pattern   = $resolvedArtifactPath
            Line      = $resolvedArtifactPath
        }
    }
    elseif ($buildReport.Result -eq "Succeeded") {
        $status = "Succeeded"
        $summary = if ($buildReport.Summary) {
            "Build report marked the $Mode build as Succeeded: $($buildReport.Summary)"
        }
        else {
            "Build report marked the $Mode build as Succeeded."
        }
        $terminalSignal = [pscustomobject]@{
            Source    = "BuildReport"
            LineIndex = $null
            Pattern   = "Result:Succeeded"
            Line      = $buildReport.Summary
        }
    }
    elseif ($successMatch -and (-not $failureMatch -or $successMatch.LineIndex -ge $failureMatch.LineIndex)) {
        $status = "Succeeded"
        $summary = "Editor.log reports a completed successful $Mode build."
        $terminalSignal = $successMatch
    }
    elseif ($failureMatch -and (-not $successMatch -or $failureMatch.LineIndex -gt $successMatch.LineIndex)) {
        $status = "Failed"
        $summary = "Editor.log reports a failed $Mode build."
        $terminalSignal = $failureMatch
    }
    elseif ($activeMatch) {
        $status = "InProgress"
        $summary = "Editor.log still shows active $Mode Bee/wasm build progress with no later terminal build marker."
    }

    return [pscustomobject]@{
        Mode                 = $Mode
        Status               = $status
        Summary              = $summary
        EditorLogPath        = $EditorLogPath
        EditorLogTailLines   = $EditorLogTailLines
        BuildOutputPath      = $resolvedOutputPath
        BuildOutputExists    = $outputExists
        BuildOutputSampleFiles = $outputSampleFiles
        BuildReport          = $buildReport
        SuccessArtifactPath  = $resolvedArtifactPath
        SuccessArtifactExists = $successArtifactExists
        ActiveSignal         = $activeMatch
        TerminalSignal       = $terminalSignal
    }
}

function Wait-UnityBuildMonitor {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][ValidateSet("WebGL")][string]$Mode = "WebGL",
        [Parameter()][string]$BuildOutputPath,
        [Parameter()][string]$BuildReportPath,
        [Parameter()][string]$SuccessArtifactPath,
        [Parameter()][string]$EditorLogPath = (Get-UnityEditorLogPath),
        [Parameter()][int]$EditorLogTailLines = 400,
        [Parameter()][int]$TimeoutSeconds = 1800,
        [Parameter()][double]$PollIntervalSeconds = 5.0
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempts = New-Object System.Collections.Generic.List[object]

    while ((Get-Date) -lt $deadline) {
        $state = Get-UnityBuildMonitorState -ProjectPath $ProjectPath -Mode $Mode -BuildOutputPath $BuildOutputPath -BuildReportPath $BuildReportPath -SuccessArtifactPath $SuccessArtifactPath -EditorLogPath $EditorLogPath -EditorLogTailLines $EditorLogTailLines
        $attempts.Add($state)

        if ($state.Status -eq "Succeeded") {
            return [ordered]@{
                success             = $true
                status              = "Succeeded"
                message             = $state.Summary
                timeoutSeconds      = $TimeoutSeconds
                pollIntervalSeconds = $PollIntervalSeconds
                attempts            = $attempts
                lastState           = $state
            }
        }

        if ($state.Status -eq "Failed") {
            return [ordered]@{
                success             = $false
                status              = "Failed"
                message             = $state.Summary
                timeoutSeconds      = $TimeoutSeconds
                pollIntervalSeconds = $PollIntervalSeconds
                attempts            = $attempts
                lastState           = $state
            }
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    $lastState = if ($attempts.Count -gt 0) { $attempts[$attempts.Count - 1] } else { $null }
    return [ordered]@{
        success             = $false
        status              = "TimedOut"
        message             = "Unity build monitor timed out before a terminal build result was observed."
        timeoutSeconds      = $TimeoutSeconds
        pollIntervalSeconds = $PollIntervalSeconds
        attempts            = $attempts
        lastState           = $lastState
    }
}

function Get-UnityEditorBuildSettingsPath {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    return (Join-Path (Resolve-UnityProjectPath -ProjectPath $ProjectPath) "ProjectSettings\EditorBuildSettings.asset")
}

function Get-UnityEditorBuildSettingsScenes {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $settingsPath = Get-UnityEditorBuildSettingsPath -ProjectPath $ProjectPath
    $exists = $false
    $error = $null
    if (-not (Test-Path -LiteralPath $settingsPath)) {
        return (New-Object PSObject -Property ([ordered]@{
            Path          = $settingsPath
            Exists        = $exists
            Scenes        = @()
            EnabledScenes = @()
            Error         = $error
        }))
    }

    $exists = $true

    try {
        $lines = @(Get-Content -LiteralPath $settingsPath -ErrorAction Stop)
    }
    catch {
        $error = $_.Exception.Message
        return (New-Object PSObject -Property ([ordered]@{
            Path          = $settingsPath
            Exists        = $exists
            Scenes        = @()
            EnabledScenes = @()
            Error         = $error
        }))
    }

    $inScenes = $false
    $currentScene = $null
    $scenes = New-Object System.Collections.Generic.List[object]

    foreach ($line in $lines) {
        if (-not $inScenes) {
            if ($line -match '^\s*m_Scenes:\s*$') {
                $inScenes = $true
            }
            continue
        }

        if ($line -match '^[A-Za-z]' -and $line -notmatch '^\s*m_Scenes:\s*$') {
            break
        }

        if ($line -match '^\s*-\s*enabled:\s*(?<enabled>[01])\s*$') {
            if ($currentScene -and -not [string]::IsNullOrWhiteSpace($currentScene.Path)) {
                $scenes.Add([pscustomobject]$currentScene)
            }

            $currentScene = [ordered]@{
                Enabled = [int]$matches["enabled"] -eq 1
                Path    = $null
            }
            continue
        }

        if ($line -match '^\s*enabled:\s*(?<enabled>[01])\s*$') {
            if (-not $currentScene) {
                $currentScene = [ordered]@{
                    Enabled = $false
                    Path    = $null
                }
            }

            $currentScene.Enabled = [int]$matches["enabled"] -eq 1
            continue
        }

        if ($line -match '^\s*path:\s*(?<path>.*?)\s*$') {
            if (-not $currentScene) {
                $currentScene = [ordered]@{
                    Enabled = $false
                    Path    = $null
                }
            }

            $pathValue = $matches["path"].Trim()
            if ($pathValue.Length -ge 2) {
                if (($pathValue.StartsWith('"') -and $pathValue.EndsWith('"')) -or ($pathValue.StartsWith("'") -and $pathValue.EndsWith("'"))) {
                    $pathValue = $pathValue.Substring(1, $pathValue.Length - 2)
                }
            }

            $currentScene.Path = ($pathValue -replace "\\", "/")
        }
    }

    if ($currentScene -and -not [string]::IsNullOrWhiteSpace($currentScene.Path)) {
        $scenes.Add([pscustomobject]$currentScene)
    }

    $sceneArray = $scenes.ToArray()

    return (New-Object PSObject -Property ([ordered]@{
        Path          = $settingsPath
        Exists        = $exists
        Scenes        = $sceneArray
        EnabledScenes = @($sceneArray | Where-Object { $_.Enabled } | ForEach-Object { $_.Path })
        Error         = $error
    }))
}

function Test-UnityBuildSceneList {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string[]]$ExpectedScenes
    )

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $settings = Get-UnityEditorBuildSettingsScenes -ProjectPath $projectRoot
    $expectedOrdered = @()
    foreach ($scene in $ExpectedScenes) {
        if ([string]::IsNullOrWhiteSpace($scene)) {
            continue
        }

        $expectedOrdered += (($scene -replace "\\", "/").Trim())
    }

    $enabledScenes = @($settings.EnabledScenes | ForEach-Object { ($_ -replace "\\", "/").Trim() })
    $missingScenes = @()
    foreach ($scene in $expectedOrdered) {
        if ($enabledScenes -notcontains $scene) {
            $missingScenes += $scene
        }
    }

    $unexpectedEnabledScenes = @()
    foreach ($scene in $enabledScenes) {
        if ($expectedOrdered -notcontains $scene) {
            $unexpectedEnabledScenes += $scene
        }
    }

    $orderDifferences = @()
    $sameMembership = ($missingScenes.Count -eq 0) -and ($unexpectedEnabledScenes.Count -eq 0) -and ($expectedOrdered.Count -eq $enabledScenes.Count)
    if ($sameMembership) {
        for ($index = 0; $index -lt $expectedOrdered.Count; $index++) {
            if ($expectedOrdered[$index] -ne $enabledScenes[$index]) {
                $orderDifferences += [pscustomobject]@{
                    Index    = $index
                    Expected = $expectedOrdered[$index]
                    Actual   = $enabledScenes[$index]
                }
            }
        }
    }

    $orderMismatch = $orderDifferences.Count -gt 0
    $exactMatch = $sameMembership -and -not $orderMismatch

    return [pscustomobject]@{
        success                = $true
        projectPath            = $projectRoot
        editorBuildSettingsPath = $settings.Path
        expectedScenes         = $expectedOrdered
        enabledScenes          = $enabledScenes
        missingScenes          = $missingScenes
        unexpectedEnabledScenes = $unexpectedEnabledScenes
        orderMismatch          = $orderMismatch
        orderDifferences       = $orderDifferences
        exactMatch             = $exactMatch
        buildSettingsReadError = $settings.Error
    }
}

function Invoke-UnityMcpToolJson {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter()][object]$Arguments = @{},
        [Parameter()][int]$TimeoutSeconds = 45
    )

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $invokeScript = Join-Path (Get-UnityBridgeSkillPath) "scripts\Invoke-UnityMcpTool.js"
    $argumentsJson = if ($Arguments -is [string]) {
        $Arguments
    }
    else {
        $Arguments | ConvertTo-Json -Compress -Depth 20
    }

    $nodePath = (Get-Command node).Source
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $nodePath
    $psi.Arguments = ('"{0}" "{1}"' -f $invokeScript.Replace('"', '\"'), $ToolName.Replace('"', '\"'))
    $psi.WorkingDirectory = $projectRoot
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables["UNITY_MCP_TOOL_ARGS_JSON"] = $argumentsJson

    $expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $projectRoot
    if ($expectedReloadState.IsActive) {
        $psi.EnvironmentVariables["UNITY_MCP_EXPECT_RELOAD"] = "1"
        $psi.EnvironmentVariables["UNITY_MCP_EXPECT_RELOAD_UNTIL_UTC"] = $expectedReloadState.ExpiresAtUtc
        $psi.EnvironmentVariables["UNITY_MCP_EXPECT_RELOAD_REASON"] = $expectedReloadState.Reason
    }

    $process = [System.Diagnostics.Process]::Start($psi)
    $outputTask = $process.StandardOutput.ReadToEndAsync()
    $errorTask = $process.StandardError.ReadToEndAsync()

    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $process.Kill()
        }
        catch {
        }

        if ($ToolName -eq "Unity_RunCommand") {
            throw "Unity MCP tool '$ToolName' timed out after $TimeoutSeconds seconds. Verify on-disk or scene state before retrying because Unity may have applied part of the command before the transport died."
        }

        throw "Unity MCP tool '$ToolName' timed out after $TimeoutSeconds seconds."
    }

    $process.WaitForExit()
    $outputText = $outputTask.GetAwaiter().GetResult()
    $errorText = $errorTask.GetAwaiter().GetResult()

    if ([string]::IsNullOrWhiteSpace($outputText)) {
        if (-not [string]::IsNullOrWhiteSpace($errorText)) {
            throw "Unity MCP tool '$ToolName' produced no stdout output. stderr: $errorText"
        }

        throw "Unity MCP tool '$ToolName' produced no stdout output."
    }

    try {
        return ($outputText | ConvertFrom-Json)
    }
    catch {
        throw "Failed to parse Unity MCP output for '$ToolName': $($_.Exception.Message)"
    }
}

function Get-UnityToolObject {
    param([Parameter(Mandatory = $true)]$Response)

    if ($null -ne $Response.result -and $null -ne $Response.result.structuredContent) {
        return $Response.result.structuredContent
    }

    $text = $null
    if ($null -ne $Response.result -and $null -ne $Response.result.content -and $Response.result.content.Count -gt 0) {
        $text = $Response.result.content[0].text
    }
    elseif ($null -ne $Response.error) {
        return [ordered]@{
            success = $false
            error   = $Response.error.message
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

function Get-UnityEditorState {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 30
    )

    $response = Invoke-UnityMcpToolJson -ProjectPath $ProjectPath -ToolName "Unity_ManageEditor" -Arguments @{ Action = "GetState" } -TimeoutSeconds $TimeoutSeconds
    return (Get-UnityToolObject -Response $response)
}

function Get-UnityConsoleEntries {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string[]]$Types = @("Error", "Warning", "Log"),
        [Parameter()][int]$Count = 100,
        [Parameter()][string]$FilterText = "",
        [Parameter()][string]$SinceTimestamp = "",
        [Parameter()][string]$Format = "Detailed",
        [Parameter()][bool]$ExcludeMcpNoise = $true,
        [Parameter()][bool]$IncludeStacktrace = $true,
        [Parameter()][int]$TimeoutSeconds = 30
    )

    $arguments = @{
        Action            = "Get"
        Types             = $Types
        Count             = $Count
        FilterText        = $FilterText
        SinceTimestamp    = $SinceTimestamp
        Format            = $Format
        ExcludeMcpNoise   = $ExcludeMcpNoise
        IncludeStacktrace = $IncludeStacktrace
    }

    $response = Invoke-UnityMcpToolJson -ProjectPath $ProjectPath -ToolName "Unity_ReadConsole" -Arguments $arguments -TimeoutSeconds $TimeoutSeconds
    return (Get-UnityToolObject -Response $response)
}

function Invoke-UnityMenuItemTool {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][ValidateSet("Execute", "List", "Exists", "Refresh")][string]$Action,
        [Parameter()][string]$MenuPath = "",
        [Parameter()][string]$Search = "",
        [Parameter()][bool]$Refresh = $false,
        [Parameter()][int]$TimeoutSeconds = 30
    )

    $arguments = @{
        Action  = $Action
        Refresh = $Refresh
    }

    if (-not [string]::IsNullOrWhiteSpace($MenuPath)) {
        $arguments.MenuPath = $MenuPath
    }

    if (-not [string]::IsNullOrWhiteSpace($Search)) {
        $arguments.Search = $Search
    }

    $response = Invoke-UnityMcpToolJson -ProjectPath $ProjectPath -ToolName "Unity_ManageMenuItem" -Arguments $arguments -TimeoutSeconds $TimeoutSeconds
    return (Get-UnityToolObject -Response $response)
}

function Get-UnityReadinessSnapshot {
    param([Parameter(Mandatory = $true)]$EditorState)

    $probe = $null
    if ($EditorState -and $EditorState.data -and $EditorState.data.RuntimeProbe) {
        $probe = $EditorState.data.RuntimeProbe
    }

    $success = $EditorState.success -eq $true
    $isCompiling = $success -and $EditorState.data.IsCompiling -eq $true
    $isUpdating = $success -and $EditorState.data.IsUpdating -eq $true
    $isPlaying = $success -and $EditorState.data.IsPlaying -eq $true
    $probeAvailable = $probe -and $probe.IsAvailable -eq $true
    $probeAdvanced = $probe -and $probe.HasAdvancedFrames -eq $true
    $updateCount = if ($probe -and $null -ne $probe.UpdateCount) { [int]$probe.UpdateCount } else { 0 }
    $fixedUpdateCount = if ($probe -and $null -ne $probe.FixedUpdateCount) { [int]$probe.FixedUpdateCount } else { 0 }
    $unscaledTime = if ($probe -and $null -ne $probe.UnscaledTime) { [double]$probe.UnscaledTime } else { 0.0 }

    return [ordered]@{
        Timestamp                     = (Get-Date).ToString("o")
        Success                       = $success
        IsCompiling                   = $isCompiling
        IsUpdating                    = $isUpdating
        IsPlaying                     = $isPlaying
        RuntimeProbeAvailable         = $probeAvailable
        RuntimeProbeHasAdvancedFrames = $probeAdvanced
        RuntimeProbeUpdateCount       = $updateCount
        RuntimeProbeFixedUpdateCount  = $fixedUpdateCount
        RuntimeProbeUnscaledTime      = $unscaledTime
        IdleReady                     = $success -and -not $isCompiling -and -not $isUpdating
        PlayReadyByCount              = $success -and $isPlaying -and $probeAvailable -and $probeAdvanced -and $updateCount -ge 10
        RuntimeProbe                  = $probe
    }
}

function Classify-UnityConsoleNoise {
    param([string]$Message)

    if ([string]::IsNullOrWhiteSpace($Message)) {
        return [pscustomobject]@{
            IsKnownNoise = $false
            Category     = $null
            Pattern      = $null
        }
    }

    $patterns = @(
        [pscustomobject]@{ Category = "mcp-validation"; Pattern = "Connection validation is DISABLED" },
        [pscustomobject]@{ Category = "mcp-bridge"; Pattern = "[UnityMCPBridge]" },
        [pscustomobject]@{ Category = "mcp-bridge"; Pattern = "MCP Bridge V2 started" },
        [pscustomobject]@{ Category = "mcp-bridge"; Pattern = "Saved connection info to" },
        [pscustomobject]@{ Category = "mcp-client"; Pattern = "Client connected:" },
        [pscustomobject]@{ Category = "mcp-client"; Pattern = "Client disconnected:" },
        [pscustomobject]@{ Category = "mcp-handshake"; Pattern = "Sent handshake (unity-mcp protocol v2.0)" },
        [pscustomobject]@{ Category = "mcp-tools"; Pattern = "Sending tools response with" },
        [pscustomobject]@{ Category = "mcp-tools"; Pattern = "Tools changed:" },
        [pscustomobject]@{ Category = "relay"; Pattern = "[RelayService]" },
        [pscustomobject]@{ Category = "mcp-approval"; Pattern = "[MCP Approval]" }
    )

    foreach ($entry in $patterns) {
        if ($Message.IndexOf($entry.Pattern, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return [pscustomobject]@{
                IsKnownNoise = $true
                Category     = $entry.Category
                Pattern      = $entry.Pattern
            }
        }
    }

    return [pscustomobject]@{
        IsKnownNoise = $false
        Category     = $null
        Pattern      = $null
    }
}

function Test-UnityDirectEditorHealthy {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 12,
        [Parameter()][int]$ConsecutiveHealthyPolls = 2,
        [Parameter()][double]$PollIntervalSeconds = 0.5
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $healthyPolls = 0
    $attempts = New-Object System.Collections.Generic.List[object]
    $lastState = $null
    $lastError = $null

    while ((Get-Date) -lt $deadline) {
        try {
            $state = Get-UnityEditorState -ProjectPath $ProjectPath -TimeoutSeconds 15
            $lastState = $state
            $snapshot = Get-UnityReadinessSnapshot -EditorState $state
            $snapshot.DirectHealthy = $snapshot.Success -and -not $snapshot.IsCompiling -and -not $snapshot.IsUpdating
            $attempts.Add($snapshot)

            if ($snapshot.DirectHealthy) {
                $healthyPolls += 1
                if ($healthyPolls -ge $ConsecutiveHealthyPolls) {
                    return [ordered]@{
                        success                     = $true
                        message                     = "Direct Unity editor health checks succeeded and the editor is idle."
                        timeoutSeconds              = $TimeoutSeconds
                        pollIntervalSeconds         = $PollIntervalSeconds
                        consecutiveHealthyRequired  = $ConsecutiveHealthyPolls
                        consecutiveHealthyObserved  = $healthyPolls
                        attempts                    = $attempts
                        lastState                   = $lastState
                    }
                }
            }
            else {
                $healthyPolls = 0
            }
        }
        catch {
            $lastError = $_.Exception.Message
            $healthyPolls = 0
            $attempts.Add([ordered]@{
                Timestamp = (Get-Date).ToString("o")
                Success   = $false
                Error     = $lastError
            })
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return [ordered]@{
        success                     = $false
        message                     = "Direct Unity editor health checks did not prove an idle editor before timeout."
        timeoutSeconds              = $TimeoutSeconds
        pollIntervalSeconds         = $PollIntervalSeconds
        consecutiveHealthyRequired  = $ConsecutiveHealthyPolls
        consecutiveHealthyObserved  = $healthyPolls
        attempts                    = $attempts
        lastState                   = $lastState
        lastError                   = $lastError
    }
}

function Invoke-UnityWaitForStableEditor {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 60,
        [Parameter()][double]$PollIntervalSeconds = 0.5
    )

    $timeoutMs = [Math]::Max(1000, $TimeoutSeconds * 1000)
    $pollIntervalMs = [Math]::Max(100, [int][Math]::Round($PollIntervalSeconds * 1000))

    try {
        $response = Invoke-UnityMcpToolJson -ProjectPath $ProjectPath -ToolName "Unity_ManageEditor" -Arguments @{
            Action            = "WaitForStableEditor"
            WaitForCompletion = $true
            TimeoutMs         = $timeoutMs
            PollIntervalMs    = $pollIntervalMs
        } -TimeoutSeconds ([Math]::Max(15, $TimeoutSeconds + 10))
    }
    catch {
        return [ordered]@{
            supported      = $false
            transportError = $_.Exception.Message
            toolResult     = $null
        }
    }

    $toolResult = Get-UnityToolObject -Response $response
    if ($null -eq $toolResult) {
        return [ordered]@{
            supported      = $false
            transportError = "Unity_ManageEditor WaitForStableEditor returned no structured data."
            toolResult     = $null
        }
    }

    $errorText = @(
        $toolResult.code
        $toolResult.error
        $toolResult.message
    ) -join " "

    $unsupported = $toolResult.success -eq $false -and (
        $errorText.IndexOf("Unknown action", [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or
        $errorText.IndexOf("Supported actions include", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
    )

    return [ordered]@{
        supported      = -not $unsupported
        unsupported    = $unsupported
        transportError = $null
        toolResult     = $toolResult
    }
}

function Wait-UnityEditorIdle {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 60,
        [Parameter()][int]$StablePollCount = 3,
        [Parameter()][double]$PollIntervalSeconds = 0.5,
        [Parameter()][double]$PostIdleDelaySeconds = 1.0,
        [switch]$ClearExpectedReloadOnSuccess
    )

    $startedAt = Get-Date
    $beaconWait = $null
    $beaconPollIntervalSeconds = [Math]::Min([double]$PollIntervalSeconds, 0.25)
    $initialBeaconSnapshot = Get-UnityEditorStatusBeacon -ProjectPath $ProjectPath -IncludeRaw
    if ((Test-UnityEditorBeaconTransitionSnapshot -Snapshot $initialBeaconSnapshot) -or (Test-UnityEditorBeaconBuildSnapshot -Snapshot $initialBeaconSnapshot)) {
        $beaconWait = Wait-UnityEditorBeaconStable -ProjectPath $ProjectPath -TimeoutSeconds $TimeoutSeconds -PollIntervalSeconds $beaconPollIntervalSeconds
        if ($beaconWait.timedOut) {
            return [ordered]@{
                success                    = $false
                message                    = $beaconWait.message
                timeoutSeconds             = $TimeoutSeconds
                pollIntervalSeconds        = $PollIntervalSeconds
                stablePollCountRequired    = $StablePollCount
                stablePollCountReached     = 0
                postIdleDelaySeconds       = $PostIdleDelaySeconds
                expectedReloadFailureCount = 0
                attempts                   = @($beaconWait.attempts)
                lastState                  = $null
                lastError                  = $null
                source                     = $beaconWait.source
                beaconWait                 = $beaconWait
            }
        }
    }

    $elapsedSeconds = [Math]::Max(0.0, ((Get-Date) - $startedAt).TotalSeconds)
    $remainingTimeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling($TimeoutSeconds - $elapsedSeconds))

    $toolWait = Invoke-UnityWaitForStableEditor -ProjectPath $ProjectPath -TimeoutSeconds $remainingTimeoutSeconds -PollIntervalSeconds $PollIntervalSeconds
    if ($toolWait.supported -eq $true -and $null -ne $toolWait.toolResult) {
        if ($toolWait.toolResult.success -and $ClearExpectedReloadOnSuccess) {
            Clear-UnityExpectedReloadState -ProjectPath $ProjectPath
        }

        return [ordered]@{
            success                 = $toolWait.toolResult.success -eq $true
            message                 = if (-not [string]::IsNullOrWhiteSpace($toolWait.toolResult.message)) {
                $toolWait.toolResult.message
            }
            elseif (-not [string]::IsNullOrWhiteSpace($toolWait.toolResult.error)) {
                $toolWait.toolResult.error
            }
            else {
                "Unity editor wait completed."
            }
            timeoutSeconds          = $remainingTimeoutSeconds
            pollIntervalSeconds     = $PollIntervalSeconds
            stablePollCountRequired = $StablePollCount
            stablePollCountReached  = if ($toolWait.toolResult.data -and $null -ne $toolWait.toolResult.data.StablePollCountReached) { $toolWait.toolResult.data.StablePollCountReached } else { $null }
            postIdleDelaySeconds    = $PostIdleDelaySeconds
            expectedReloadFailureCount = 0
            attempts                = if ($toolWait.toolResult.data -and $null -ne $toolWait.toolResult.data.Attempts) { $toolWait.toolResult.data.Attempts } else { @() }
            lastState               = if ($toolWait.toolResult.data -and $null -ne $toolWait.toolResult.data.EditorState) {
                [ordered]@{
                    success = $toolWait.toolResult.success -eq $true
                    data    = $toolWait.toolResult.data.EditorState
                }
            }
            else {
                $null
            }
            lastError               = if ($toolWait.toolResult.success -eq $true) { $null } else { $toolWait.toolResult.error }
            source                  = if ($beaconWait) { "EditorStatusBeacon.WaitStable+Unity_ManageEditor.WaitForStableEditor" } else { "Unity_ManageEditor.WaitForStableEditor" }
            toolResult              = $toolWait.toolResult
            beaconWait              = $beaconWait
        }
    }

    $deadline = (Get-Date).AddSeconds($remainingTimeoutSeconds)
    $stablePolls = 0
    $attempts = New-Object System.Collections.Generic.List[object]
    $lastState = $null
    $lastError = $null
    $expectedReloadFailureCount = 0

    while ((Get-Date) -lt $deadline) {
        try {
            $state = Get-UnityEditorState -ProjectPath $ProjectPath -TimeoutSeconds 20
            $lastState = $state
            $snapshot = Get-UnityReadinessSnapshot -EditorState $state
            $expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $ProjectPath
            $snapshot.ExpectedReloadActive = $expectedReloadState.IsActive
            $snapshot.ExpectedReloadReason = $expectedReloadState.Reason
            if ($snapshot.Success -ne $true -and $expectedReloadState.IsActive) {
                $expectedReloadFailureCount += 1
            }
            $attempts.Add($snapshot)

            if ($snapshot.IdleReady) {
                $stablePolls += 1
                if ($stablePolls -ge $StablePollCount) {
                    if ($PostIdleDelaySeconds -gt 0) {
                        Start-Sleep -Seconds $PostIdleDelaySeconds
                    }

                    if ($ClearExpectedReloadOnSuccess) {
                        Clear-UnityExpectedReloadState -ProjectPath $ProjectPath
                    }

                    return [ordered]@{
                        success                 = $true
                        message                 = "Unity editor reached a stable idle state."
                        timeoutSeconds          = $TimeoutSeconds
                        pollIntervalSeconds     = $PollIntervalSeconds
                        stablePollCountRequired = $StablePollCount
                        stablePollCountReached  = $stablePolls
                        postIdleDelaySeconds    = $PostIdleDelaySeconds
                        expectedReloadFailureCount = $expectedReloadFailureCount
                        attempts                = $attempts
                        lastState               = $lastState
                        beaconWait              = $beaconWait
                        source                  = if ($beaconWait) { "EditorStatusBeacon.WaitStable+DirectEditorState" } else { "DirectEditorState" }
                    }
                }
            }
            else {
                $stablePolls = 0
            }
        }
        catch {
            $lastError = $_.Exception.Message
            $stablePolls = 0
            $expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $ProjectPath
            $expectedReloadError = $expectedReloadState.IsActive -and (Test-UnityExpectedReloadError -Message $lastError)
            if ($expectedReloadError) {
                $expectedReloadFailureCount += 1
            }

            $attempts.Add([ordered]@{
                Timestamp            = (Get-Date).ToString("o")
                Success              = $false
                Error                = $lastError
                ExpectedReloadActive = $expectedReloadState.IsActive
                ExpectedReloadReason = $expectedReloadState.Reason
                ExpectedReloadError  = $expectedReloadError
            })
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return [ordered]@{
        success                 = $false
        message                 = "Unity editor did not reach a stable idle state before timeout."
        timeoutSeconds          = $TimeoutSeconds
        pollIntervalSeconds     = $PollIntervalSeconds
        stablePollCountRequired = $StablePollCount
        stablePollCountReached  = $stablePolls
        postIdleDelaySeconds    = $PostIdleDelaySeconds
        expectedReloadFailureCount = $expectedReloadFailureCount
        attempts                = $attempts
        lastState               = $lastState
        lastError               = $lastError
        beaconWait              = $beaconWait
        source                  = if ($beaconWait) { "EditorStatusBeacon.WaitStable+DirectEditorState" } else { "DirectEditorState" }
    }
}

function Wait-UnityCompileOrUpdateStart {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 6,
        [Parameter()][double]$PollIntervalSeconds = 0.5
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $attempts = New-Object System.Collections.Generic.List[object]
    $lastState = $null
    $lastError = $null
    $transientExpectedReloadFailures = 0

    while ((Get-Date) -lt $deadline) {
        try {
            $state = Get-UnityEditorState -ProjectPath $ProjectPath -TimeoutSeconds 15
            $lastState = $state
            $snapshot = Get-UnityReadinessSnapshot -EditorState $state
            $expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $ProjectPath
            $snapshot.ExpectedReloadActive = $expectedReloadState.IsActive
            $snapshot.ExpectedReloadReason = $expectedReloadState.Reason
            if ($snapshot.Success -ne $true -and $expectedReloadState.IsActive) {
                $transientExpectedReloadFailures += 1
            }
            $attempts.Add($snapshot)

            if ($snapshot.IsCompiling -or $snapshot.IsUpdating) {
                return [ordered]@{
                    success                          = $true
                    started                          = $true
                    likelyStartedByTransientFailure  = $false
                    transientExpectedReloadFailures  = $transientExpectedReloadFailures
                    timeoutSeconds                   = $TimeoutSeconds
                    pollIntervalSeconds              = $PollIntervalSeconds
                    attempts                         = $attempts
                    lastState                        = $lastState
                    lastError                        = $lastError
                    message                          = "Unity compile or update started."
                }
            }
        }
        catch {
            $lastError = $_.Exception.Message
            $expectedReloadState = Get-UnityExpectedReloadState -ProjectPath $ProjectPath
            $expectedReloadError = $expectedReloadState.IsActive -and (Test-UnityExpectedReloadError -Message $lastError)
            if ($expectedReloadError) {
                $transientExpectedReloadFailures += 1
            }

            $attempts.Add([ordered]@{
                Timestamp            = (Get-Date).ToString("o")
                Success              = $false
                Error                = $lastError
                ExpectedReloadActive = $expectedReloadState.IsActive
                ExpectedReloadReason = $expectedReloadState.Reason
                ExpectedReloadError  = $expectedReloadError
            })
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return [ordered]@{
        success                         = $false
        started                         = $false
        likelyStartedByTransientFailure = $transientExpectedReloadFailures -gt 0
        transientExpectedReloadFailures = $transientExpectedReloadFailures
        timeoutSeconds                  = $TimeoutSeconds
        pollIntervalSeconds             = $PollIntervalSeconds
        attempts                        = $attempts
        lastState                       = $lastState
        lastError                       = $lastError
        message                         = "Unity compile or update did not start before timeout."
    }
}

function Wait-UnityCompileReloadCycle {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$StartTimeoutSeconds = 6,
        [Parameter()][int]$TimeoutSeconds = 120,
        [Parameter()][int]$StablePollCount = 3,
        [Parameter()][double]$PollIntervalSeconds = 0.5,
        [Parameter()][double]$PostIdleDelaySeconds = 1.0,
        [switch]$ClearExpectedReloadOnSuccess
    )

    $beaconStartWaitTimeoutSeconds = [Math]::Min($StartTimeoutSeconds, 2)
    $beaconStartWait = Wait-UnityEditorBeaconTransition -ProjectPath $ProjectPath -TimeoutSeconds $beaconStartWaitTimeoutSeconds -PollIntervalSeconds ([Math]::Min([double]$PollIntervalSeconds, 0.25))
    $startWait = $null
    $shouldWaitForIdle = $false
    $idleWait = $null

    if ($beaconStartWait.success) {
        $shouldWaitForIdle = $true
        $startWait = [ordered]@{
            success                         = $true
            started                         = $true
            likelyStartedByTransientFailure = $false
            transientExpectedReloadFailures = 0
            timeoutSeconds                  = $beaconStartWaitTimeoutSeconds
            pollIntervalSeconds             = $PollIntervalSeconds
            attempts                        = @($beaconStartWait.attempts)
            lastState                       = $null
            lastError                       = $null
            message                         = "Unity transition observed from the editor status beacon."
            source                          = $beaconStartWait.source
        }
    }
    else {
        $startWait = Wait-UnityCompileOrUpdateStart -ProjectPath $ProjectPath -TimeoutSeconds $StartTimeoutSeconds -PollIntervalSeconds $PollIntervalSeconds
        $shouldWaitForIdle = $startWait.started -or $startWait.likelyStartedByTransientFailure
    }

    if ($shouldWaitForIdle) {
        $idleWait = Wait-UnityEditorIdle -ProjectPath $ProjectPath -TimeoutSeconds $TimeoutSeconds -StablePollCount $StablePollCount -PollIntervalSeconds $PollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds -ClearExpectedReloadOnSuccess:$ClearExpectedReloadOnSuccess
    }

    $transientFailureCount = [int]$startWait.transientExpectedReloadFailures
    if ($idleWait -and $null -ne $idleWait.expectedReloadFailureCount) {
        $transientFailureCount += [int]$idleWait.expectedReloadFailureCount
    }

    return [ordered]@{
        success                         = $shouldWaitForIdle -and $idleWait -and $idleWait.success
        message                         = if ($shouldWaitForIdle) { if ($idleWait.success) { "Unity compile/reload cycle settled back to idle." } else { $idleWait.message } } else { $startWait.message }
        compileObserved                 = $startWait.started
        likelyStartedByTransientFailure = $startWait.likelyStartedByTransientFailure
        transientExpectedReloadFailures = $transientFailureCount
        beaconStartWait                 = $beaconStartWait
        startWait                       = $startWait
        idleWait                        = $idleWait
    }
}

function Wait-UnityPlayReady {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][int]$TimeoutSeconds = 25,
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
            $beaconSnapshot = Get-UnityEditorStatusBeacon -ProjectPath $ProjectPath -IncludeRaw
            if ((Test-UnityEditorBeaconTransitionSnapshot -Snapshot $beaconSnapshot) -or (Test-UnityEditorBeaconBuildSnapshot -Snapshot $beaconSnapshot)) {
                $remainingTimeoutSeconds = [Math]::Max(1, [int][Math]::Ceiling(($deadline - (Get-Date)).TotalSeconds))
                $beaconWaitSeconds = [Math]::Min($remainingTimeoutSeconds, 2)
                $beaconWait = Wait-UnityEditorBeaconStable -ProjectPath $ProjectPath -TimeoutSeconds $beaconWaitSeconds -PollIntervalSeconds ([Math]::Min([double]$PollIntervalSeconds, 0.25))
                $attempts.Add([ordered]@{
                    Timestamp         = (Get-Date).ToString("o")
                    Success           = $beaconWait.success
                    SkippedDirectProbe = $true
                    BeaconWait        = $beaconWait
                })

                if ($beaconWait.timedOut -and (Get-Date) -ge $deadline) {
                    break
                }

                continue
            }

            $state = Get-UnityEditorState -ProjectPath $ProjectPath -TimeoutSeconds 15
            $lastState = $state
            $snapshot = Get-UnityReadinessSnapshot -EditorState $state

            $timeAdvanced = $false
            if ($null -ne $previousUnscaledTime -and $snapshot.RuntimeProbeUnscaledTime -gt $previousUnscaledTime) {
                $timeAdvanced = $true
            }

            $snapshot.PlayReady = $snapshot.Success -and $snapshot.IsPlaying -and $snapshot.RuntimeProbeAvailable -and $snapshot.RuntimeProbeHasAdvancedFrames -and ($snapshot.RuntimeProbeUpdateCount -ge 10 -or $timeAdvanced)
            $snapshot.RuntimeAdvancedByTime = $timeAdvanced

            $attempts.Add($snapshot)

            if ($snapshot.PlayReady) {
                if ($WarmupSeconds -gt 0) {
                    Start-Sleep -Seconds $WarmupSeconds
                }

                return [ordered]@{
                    success             = $true
                    message             = "Play mode entered and runtime reached a settled advancing state."
                    timeoutSeconds      = $TimeoutSeconds
                    pollIntervalSeconds = $PollIntervalSeconds
                    warmupSeconds       = $WarmupSeconds
                    attempts            = $attempts
                    lastState           = $lastState
                }
            }

            $previousUnscaledTime = $snapshot.RuntimeProbeUnscaledTime
        }
        catch {
            $lastError = $_.Exception.Message
            $attempts.Add([ordered]@{
                Timestamp = (Get-Date).ToString("o")
                Success   = $false
                Error     = $lastError
            })
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    return [ordered]@{
        success             = $false
        message             = "Play mode did not reach a settled advancing runtime state before timeout."
        timeoutSeconds      = $TimeoutSeconds
        pollIntervalSeconds = $PollIntervalSeconds
        warmupSeconds       = $WarmupSeconds
        attempts            = $attempts
        lastState           = $lastState
        lastError           = $lastError
    }
}

function Normalize-UnityUsingList {
    param(
        [Parameter()][string[]]$Usings = @()
    )

    $normalizedLines = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $Usings) {
        if ([string]::IsNullOrWhiteSpace($entry)) {
            continue
        }

        foreach ($segment in ([string]$entry -split ',')) {
            $normalized = $segment.Trim()
            if ([string]::IsNullOrWhiteSpace($normalized)) {
                continue
            }

            if ($normalized.StartsWith("using ")) {
                $normalized = $normalized.Substring(6).Trim()
            }

            $normalized = $normalized.TrimEnd(';').Trim()
            if ([string]::IsNullOrWhiteSpace($normalized)) {
                continue
            }

            $line = "using $normalized;"
            if (-not $normalizedLines.Contains($line)) {
                $normalizedLines.Add($line)
            }
        }
    }

    return $normalizedLines.ToArray()
}

function Convert-ToUnityRunCommandScript {
    param(
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter()][string[]]$Usings = @()
    )

    if ($Code -match "IRunCommand" -and $Code -match "CommandScript") {
        return $Code
    }

    $baseUsingLines = Normalize-UnityUsingList -Usings @(
        "UnityEngine",
        "UnityEditor",
        "Becool.UnityMcpLens.Editor.Tools.RunCommandSupport"
    )
    $additionalUsingLines = Normalize-UnityUsingList -Usings $Usings
    $usingLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in @($baseUsingLines + $additionalUsingLines)) {
        if (-not $usingLines.Contains($line)) {
            $usingLines.Add($line)
        }
    }

    $bodyLines = $Code -split "`r?`n"
    $normalizedBodyLines = New-Object System.Collections.Generic.List[string]
    $parsingLeadingUsings = $true

    foreach ($line in $bodyLines) {
        $trimmed = $line.Trim()

        if ($parsingLeadingUsings -and [string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($parsingLeadingUsings -and $trimmed -match '^using\s+[^;]+;\s*$') {
            foreach ($usingLine in (Normalize-UnityUsingList -Usings @($trimmed))) {
                if (-not $usingLines.Contains($usingLine)) {
                    $usingLines.Add($usingLine)
                }
            }

            continue
        }

        $parsingLeadingUsings = $false
        $normalizedBodyLines.Add($line)
    }

    $scriptLines = @()
    $scriptLines += $usingLines
    $scriptLines += ""
    $scriptLines += "namespace Becool.UnityMcpLens.Agent.Dynamic.Extension.Editor"
    $scriptLines += "{"
    $scriptLines += "    internal class CommandScript : IRunCommand"
    $scriptLines += "    {"
    $scriptLines += "        public void Execute(ExecutionResult result)"
    $scriptLines += "        {"
    foreach ($line in $normalizedBodyLines) {
        $scriptLines += "            $line"
    }
    $scriptLines += "        }"
    $scriptLines += "    }"
    $scriptLines += "}"
    return ($scriptLines -join "`n")
}

function Invoke-UnityRunCommandObject {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Code,
        [Parameter()][string]$Title = "",
        [Parameter()][string[]]$Usings = @(),
        [Parameter()][int]$TimeoutSeconds = 60,
        [Parameter()][bool]$PausePlayMode = $false,
        [Parameter()][int]$StepFrames = 0,
        [Parameter()][bool]$RestorePauseState = $true
    )

    $payload = [ordered]@{
        code = Convert-ToUnityRunCommandScript -Code $Code -Usings $Usings
    }

    if (-not [string]::IsNullOrWhiteSpace($Title)) {
        $payload.title = $Title
    }

    if ($PausePlayMode) {
        $payload.pausePlayMode = $true
    }

    if ($StepFrames -gt 0) {
        $payload.stepFrames = [Math]::Max(0, [int]$StepFrames)
    }

    if (-not $RestorePauseState) {
        $payload.restorePauseState = $false
    }

    $response = Invoke-UnityMcpToolJson -ProjectPath $ProjectPath -ToolName "Unity_RunCommand" -Arguments $payload -TimeoutSeconds $TimeoutSeconds
    return (Get-UnityToolObject -Response $response)
}

function Get-UnityRunCommandPlayModeExecution {
    param(
        [Parameter(Mandatory = $true)][object]$RunCommandResult
    )

    if ($null -eq $RunCommandResult -or $null -eq $RunCommandResult.data -or $null -eq $RunCommandResult.data.playModeExecution) {
        return $null
    }

    $execution = $RunCommandResult.data.playModeExecution
    $stepsRequested = if ($null -ne $execution.stepsRequested) { [int]$execution.stepsRequested } else { 0 }
    $pauseApplied = ($execution.pauseApplied -eq $true)

    return @{
        pauseRequested  = ($pauseApplied -or $stepsRequested -gt 0)
        pauseWasApplied = $pauseApplied
        stepsRequested  = $stepsRequested
        stepsApplied    = if ($null -ne $execution.stepsApplied) { [int]$execution.stepsApplied } else { 0 }
        wasPlaying      = ($execution.wasPlaying -eq $true)
        wasPaused       = ($execution.wasPaused -eq $true)
        isPausedAfter   = ($execution.isPausedAfter -eq $true)
        pauseStepOnly   = $false
    }
}

function Escape-CSharpString {
    param([Parameter(Mandatory = $true)][string]$Value)

    return $Value.Replace("\", "\\").Replace('"', '\"')
}

function New-UnityArtifactDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string]$Prefix = "unity-playtest"
    )

    $root = Join-Path ([System.IO.Path]::GetTempPath()) "codex-unity"
    $projectName = Split-Path -Path (Resolve-UnityProjectPath -ProjectPath $ProjectPath) -Leaf
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $directory = Join-Path $root "$Prefix-$projectName-$timestamp"
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    return $directory
}

function Resolve-UnityManifestFileDependencyPath {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter()][string]$DependencyValue
    )

    if ([string]::IsNullOrWhiteSpace($DependencyValue) -or -not $DependencyValue.StartsWith("file:", [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $manifestDirectory = Join-Path (Resolve-UnityProjectPath -ProjectPath $ProjectPath) "Packages"
    $candidate = $DependencyValue.Substring(5)
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        return $null
    }

    if (-not [System.IO.Path]::IsPathRooted($candidate)) {
        $candidate = Join-Path $manifestDirectory $candidate
    }

    try {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }
    catch {
    }

    return [System.IO.Path]::GetFullPath($candidate)
}

function Get-UnityAssistantPackageState {
    param([Parameter(Mandatory = $true)][string]$ProjectPath)

    $projectRoot = Resolve-UnityProjectPath -ProjectPath $ProjectPath
    $manifestPath = Join-Path $projectRoot "Packages\manifest.json"
    $embeddedPackagePath = Join-Path $projectRoot "Packages\com.unity.ai.assistant\package.json"
    $dependencyValue = $null
    $manifestError = $null

    if (Test-Path -LiteralPath $manifestPath) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $dependencyValue = $manifest.dependencies.'com.unity.ai.assistant'
        }
        catch {
            $manifestError = $_.Exception.Message
        }
    }

    $embeddedPackageExists = Test-Path -LiteralPath $embeddedPackagePath
    $resolvedFileDependencyPath = Resolve-UnityManifestFileDependencyPath -ProjectPath $projectRoot -DependencyValue $dependencyValue
    $projectRootNormalized = (($projectRoot -replace "\\", "/").TrimEnd("/")).ToLowerInvariant()
    $resolvedPathNormalized = if ($resolvedFileDependencyPath) { (($resolvedFileDependencyPath -replace "\\", "/").TrimEnd("/")).ToLowerInvariant() } else { $null }
    $isEmbeddedPath = $resolvedPathNormalized -and ($resolvedPathNormalized.StartsWith("$projectRootNormalized/", [System.StringComparison]::OrdinalIgnoreCase) -or $resolvedPathNormalized.Equals($projectRootNormalized, [System.StringComparison]::OrdinalIgnoreCase))

    $mode = "Missing"
    $summary = "Assistant dependency not found."

    if ($embeddedPackageExists) {
        $mode = "LocalFolderDependency"
        $summary = "Assistant package is embedded in the project Packages folder."
    }
    elseif ($resolvedFileDependencyPath) {
        if ($isEmbeddedPath) {
            $mode = "LocalFolderDependency"
            $summary = "Assistant dependency uses a project-local file path."
        }
        else {
            $mode = "ExternalPatchSource"
            $summary = "Assistant dependency points to an external patch source."
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace($dependencyValue)) {
        $mode = "RegistryDependency"
        $summary = "Assistant dependency comes from the Unity package registry."
    }

    return [pscustomobject]@{
        ManifestPath                = $manifestPath
        ManifestError               = $manifestError
        DependencyValue             = $dependencyValue
        EmbeddedPackagePath         = $embeddedPackagePath
        EmbeddedPackageExists       = $embeddedPackageExists
        ResolvedFileDependencyPath  = $resolvedFileDependencyPath
        Mode                        = $mode
        Summary                     = $summary
    }
}

function Save-JsonFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Data
    )

    $directory = Split-Path -Path $Path -Parent
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $Data | ConvertTo-Json -Depth 30 | Set-Content -Path $Path -Encoding UTF8
}
