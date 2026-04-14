<#
Usage:
  .\Tools~\Analyze-PayloadStats.ps1
  .\Tools~\Analyze-PayloadStats.ps1 -StatsPath C:\Path\To\UnityProject\Library\AI.Gateway.PayloadStats.jsonl
  .\Tools~\Analyze-PayloadStats.ps1 -AsJson
#>
[CmdletBinding()]
param(
    [string]$StatsPath,
    [int]$Top = 10,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"

function Get-ProjectRoot {
    if ($PSScriptRoot) {
        return (Split-Path -Parent $PSScriptRoot)
    }

    return (Get-Location).Path
}

function Get-ByteString {
    param([double]$Bytes)

    $units = @("B", "KB", "MB", "GB")
    $value = [double]$Bytes
    $unitIndex = 0

    while ($value -ge 1024 -and $unitIndex -lt ($units.Length - 1)) {
        $value /= 1024
        $unitIndex++
    }

    return ("{0:N2} {1}" -f $value, $units[$unitIndex])
}

function Get-Percent {
    param(
        [double]$Part,
        [double]$Whole
    )

    if ($Whole -le 0) {
        return 0.0
    }

    return [math]::Round(($Part / $Whole) * 100.0, 2)
}

function Get-NumberValue {
    param(
        $Value,
        [double]$Default = 0
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $Default
    }

    return [double]$Value
}

function Get-StringValue {
    param(
        $Value,
        [string]$Default = ""
    )

    if ($null -eq $Value) {
        return $Default
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $Default
    }

    return $text
}

function Get-BoolValue {
    param($Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return $null
    }

    if ($Value -is [bool]) {
        return [bool]$Value
    }

    $parsed = $false
    if ([bool]::TryParse([string]$Value, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Get-MeasureSum {
    param(
        $InputObject,
        [string]$Property
    )

    $measure = @($InputObject | Measure-Object -Property $Property -Sum)
    $sum = if ($measure.Count -gt 0) { $measure[0].Sum } else { 0 }
    return (Get-NumberValue -Value $sum)
}

function Get-Percentile {
    param(
        [double[]]$Values,
        [double]$Percentile
    )

    if (-not $Values -or $Values.Count -eq 0) {
        return 0
    }

    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 1) {
        return [math]::Round($sorted[0], 2)
    }

    $rank = ($Percentile / 100.0) * ($sorted.Count - 1)
    $lowerIndex = [math]::Floor($rank)
    $upperIndex = [math]::Ceiling($rank)
    if ($lowerIndex -eq $upperIndex) {
        return [math]::Round($sorted[$lowerIndex], 2)
    }

    $weight = $rank - $lowerIndex
    $value = ($sorted[$lowerIndex] * (1.0 - $weight)) + ($sorted[$upperIndex] * $weight)
    return [math]::Round($value, 2)
}

function New-SummaryRow {
    param(
        [string]$Label,
        [int]$Count,
        [double]$RawBytes,
        [double]$ShapedBytes
    )

    $rawTokens = [math]::Ceiling($RawBytes / 4.0)
    $shapedTokens = [math]::Ceiling($ShapedBytes / 4.0)
    $savedBytes = [math]::Max(0, $RawBytes - $ShapedBytes)
    $savedTokens = [math]::Max(0, $rawTokens - $shapedTokens)

    [pscustomobject]@{
        Label        = $Label
        Count        = $Count
        RawBytes     = [int64][math]::Round($RawBytes)
        ShapedBytes  = [int64][math]::Round($ShapedBytes)
        SavedBytes   = [int64][math]::Round($savedBytes)
        RawTokens    = [int64]$rawTokens
        ShapedTokens = [int64]$shapedTokens
        SavedTokens  = [int64]$savedTokens
        SavingsPct   = Get-Percent -Part $savedBytes -Whole $RawBytes
    }
}

function New-LatencySummary {
    param($Rows)

    $values = @($Rows | Where-Object { $_.DurationMs -gt 0 } | ForEach-Object { [double]$_.DurationMs })
    if ($values.Count -eq 0) {
        return [pscustomobject]@{
            Count = 0
            MeanMs = 0
            P50Ms = 0
            P95Ms = 0
            P99Ms = 0
            MaxMs = 0
        }
    }

    return [pscustomobject]@{
        Count  = $values.Count
        MeanMs = [math]::Round((($values | Measure-Object -Average).Average), 2)
        P50Ms  = Get-Percentile -Values $values -Percentile 50
        P95Ms  = Get-Percentile -Values $values -Percentile 95
        P99Ms  = Get-Percentile -Values $values -Percentile 99
        MaxMs  = [math]::Round(($values | Measure-Object -Maximum).Maximum, 2)
    }
}

$projectRoot = Get-ProjectRoot
if (-not $StatsPath) {
    $StatsPath = Join-Path $projectRoot "Library\AI.Gateway.PayloadStats.jsonl"
}

$resolvedStatsPath = [System.IO.Path]::GetFullPath($StatsPath)
if (-not (Test-Path -LiteralPath $resolvedStatsPath)) {
    throw "Stats file not found: $resolvedStatsPath`nIf this package is being used from another Unity project, pass that host project's Library\\AI.Gateway.PayloadStats.jsonl via -StatsPath."
}

$rows = New-Object System.Collections.Generic.List[object]
$skippedLines = 0

Get-Content -LiteralPath $resolvedStatsPath | ForEach-Object {
    $line = $_
    if ([string]::IsNullOrWhiteSpace($line)) {
        return
    }

    try {
        $entry = $line | ConvertFrom-Json
        $timestamp = $null
        if ($entry.timestampUtc) {
            $timestamp = [datetimeoffset]::Parse($entry.timestampUtc)
        }

        $rawBytes = Get-NumberValue -Value $entry.rawBytes
        $shapedBytes = Get-NumberValue -Value $entry.shapedBytes
        $requestBytes = if ($entry.requestBytes) { Get-NumberValue -Value $entry.requestBytes } else { Get-NumberValue -Value $entry.meta.payloadBytes }
        $responseBytes = Get-NumberValue -Value $entry.responseBytes
        $origin = if ($entry.origin) { Get-StringValue -Value $entry.origin } else { Get-StringValue -Value $entry.meta.origin }
        $commandType = if ($entry.commandType) { Get-StringValue -Value $entry.commandType } else { Get-StringValue -Value $entry.name }
        $discoveryMode = if ($entry.discoveryMode) { Get-StringValue -Value $entry.discoveryMode } else { Get-StringValue -Value $entry.toolDiscoveryMode }
        $snapshotReason = if ($entry.snapshotReason) { Get-StringValue -Value $entry.snapshotReason } else { Get-StringValue -Value $entry.toolDiscoveryReason }

        $rows.Add([pscustomobject]@{
            TimestampUtc        = $timestamp
            SchemaVersion       = Get-StringValue -Value $entry.schemaVersion
            EventKind           = Get-StringValue -Value $entry.eventKind
            Stage               = Get-StringValue -Value $entry.stage
            Name                = Get-StringValue -Value $entry.name
            RawBytes            = $rawBytes
            ShapedBytes         = $shapedBytes
            RawChars            = Get-NumberValue -Value $entry.rawChars
            ShapedChars         = Get-NumberValue -Value $entry.shapedChars
            RawTokens           = [math]::Ceiling($rawBytes / 4.0)
            ShapedTokens        = [math]::Ceiling($shapedBytes / 4.0)
            Hash                = Get-StringValue -Value $entry.hash
            NormalizedHash      = Get-StringValue -Value $entry.normalizedHash
            DynamicFieldFlags   = Get-StringValue -Value $entry.dynamicFieldFlags
            RunId               = Get-StringValue -Value $entry.runId
            TaskId              = Get-StringValue -Value $entry.taskId
            OperationId         = Get-StringValue -Value $entry.operationId
            ConversationId      = Get-StringValue -Value $entry.conversationId
            ConnectionId        = Get-StringValue -Value $entry.connectionId
            RequestId           = Get-StringValue -Value $entry.requestId
            ProviderId          = Get-StringValue -Value $entry.providerId
            WorkflowKind        = Get-StringValue -Value $entry.workflowKind
            Origin              = $origin
            DurationMs          = Get-NumberValue -Value $entry.durationMs
            Success             = Get-BoolValue -Value $entry.success
            ErrorKind           = Get-StringValue -Value $entry.errorKind
            ErrorMessageShort   = Get-StringValue -Value $entry.errorMessageShort
            RepresentationKind  = Get-StringValue -Value $entry.representationKind
            PayloadClass        = Get-StringValue -Value $entry.payloadClass
            Cached              = Get-BoolValue -Value $entry.cached
            Unchanged           = Get-BoolValue -Value $entry.unchanged
            CacheReuseClass     = Get-StringValue -Value $entry.cacheReuseClass
            CommandType         = $commandType
            RequestBytes        = $requestBytes
            ResponseBytes       = $responseBytes
            DiscoveryMode       = $discoveryMode
            SnapshotReason      = $snapshotReason
            SnapshotHashMinimal = Get-StringValue -Value $entry.snapshotHashMinimal
            SnapshotHashFull    = Get-StringValue -Value $entry.snapshotHashFull
            EnabledToolCount    = Get-NumberValue -Value $entry.enabledToolCount
            RegisteredToolCount = Get-NumberValue -Value $entry.registeredToolCount
            Meta                = $entry.meta
        })
    }
    catch {
        $skippedLines++
    }
}

if ($rows.Count -eq 0) {
    throw "No valid payload stat rows were found in $resolvedStatsPath"
}

$coverageRows = @($rows | Where-Object { $_.Stage.StartsWith("coverage_") -or $_.EventKind -eq "coverage" -or $_.EventKind -eq "bridge_coverage" })
$payloadRows = @($rows | Where-Object { -not ($_.Stage.StartsWith("coverage_") -or $_.EventKind -eq "coverage" -or $_.EventKind -eq "bridge_coverage") })
$sortedRows = @($rows | Sort-Object TimestampUtc)

$totalRawBytes = Get-MeasureSum -InputObject $payloadRows -Property RawBytes
$totalShapedBytes = Get-MeasureSum -InputObject $payloadRows -Property ShapedBytes
$totalRawTokens = Get-MeasureSum -InputObject $payloadRows -Property RawTokens
$totalShapedTokens = Get-MeasureSum -InputObject $payloadRows -Property ShapedTokens

$exactDuplicateGroups = $payloadRows |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.Hash) } |
    Group-Object { "{0}|{1}|{2}" -f $_.Stage, $_.Name, $_.Hash } |
    Where-Object { $_.Count -gt 1 }

$normalizedDuplicateGroups = $payloadRows |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.NormalizedHash) } |
    Group-Object { "{0}|{1}|{2}" -f $_.Stage, $_.Name, $_.NormalizedHash } |
    Where-Object { $_.Count -gt 1 }

$repeatedRawBytes = 0.0
$repeatedEntries = 0
foreach ($group in $exactDuplicateGroups) {
    $duplicates = $group.Group | Sort-Object TimestampUtc | Select-Object -Skip 1
    if ($duplicates) {
        $repeatedEntries += @($duplicates).Count
        $repeatedRawBytes += Get-MeasureSum -InputObject $duplicates -Property RawBytes
    }
}

$normalizedRepeatedBytes = 0.0
$normalizedRepeatedEntries = 0
foreach ($group in $normalizedDuplicateGroups) {
    $duplicates = $group.Group | Sort-Object TimestampUtc | Select-Object -Skip 1
    if ($duplicates) {
        $normalizedRepeatedEntries += @($duplicates).Count
        $normalizedRepeatedBytes += Get-MeasureSum -InputObject $duplicates -Property RawBytes
    }
}

$byStage = $payloadRows |
    Group-Object Stage |
    ForEach-Object {
        New-SummaryRow -Label $_.Name `
            -Count $_.Count `
            -RawBytes (Get-MeasureSum -InputObject $_.Group -Property RawBytes) `
            -ShapedBytes (Get-MeasureSum -InputObject $_.Group -Property ShapedBytes)
    } |
    Sort-Object RawBytes -Descending

$byName = $payloadRows |
    Group-Object Name |
    ForEach-Object {
        New-SummaryRow -Label $_.Name `
            -Count $_.Count `
            -RawBytes (Get-MeasureSum -InputObject $_.Group -Property RawBytes) `
            -ShapedBytes (Get-MeasureSum -InputObject $_.Group -Property ShapedBytes)
    } |
    Sort-Object RawBytes -Descending

$representationMix = @($payloadRows |
    Group-Object { if ([string]::IsNullOrWhiteSpace($_.RepresentationKind)) { "(none)" } else { $_.RepresentationKind } } |
    ForEach-Object {
        [pscustomobject]@{
            Representation = $_.Name
            Count = $_.Count
            RawBytes = [int64][math]::Round((Get-MeasureSum -InputObject $_.Group -Property RawBytes))
            ShapedBytes = [int64][math]::Round((Get-MeasureSum -InputObject $_.Group -Property ShapedBytes))
        }
    } |
    Sort-Object RawBytes -Descending)

$runSummaries = @($rows |
    Group-Object { if ([string]::IsNullOrWhiteSpace($_.RunId)) { "(none)" } else { $_.RunId } } |
    ForEach-Object {
        $groupRows = @($_.Group)
        [pscustomobject]@{
            RunId = $_.Name
            Count = $groupRows.Count
            PayloadRows = @($groupRows | Where-Object { -not $_.Stage.StartsWith("coverage_") }).Count
            CoverageRows = @($groupRows | Where-Object { $_.Stage.StartsWith("coverage_") }).Count
            RawBytes = [int64][math]::Round((Get-MeasureSum -InputObject $groupRows -Property RawBytes))
            DurationCount = @($groupRows | Where-Object { $_.DurationMs -gt 0 }).Count
            FailureCount = @($groupRows | Where-Object { $_.Success -eq $false -or -not [string]::IsNullOrWhiteSpace($_.ErrorKind) }).Count
        }
    } |
    Sort-Object Count -Descending |
    Select-Object -First $Top)

$latencyRows = @($rows | Where-Object { $_.DurationMs -gt 0 })
$latencySummary = New-LatencySummary -Rows $latencyRows
$slowOperations = @($latencyRows |
    Group-Object { "{0}|{1}" -f $_.Stage, $_.Name } |
    ForEach-Object {
        $durations = @($_.Group | ForEach-Object { [double]$_.DurationMs })
        [pscustomobject]@{
            Label = $_.Name
            Count = $_.Count
            MeanMs = [math]::Round((($durations | Measure-Object -Average).Average), 2)
            P95Ms = Get-Percentile -Values $durations -Percentile 95
            MaxMs = [math]::Round((($durations | Measure-Object -Maximum).Maximum), 2)
        }
    } |
    Sort-Object MeanMs -Descending |
    Select-Object -First $Top)

$failureRows = @($rows | Where-Object { $_.Success -eq $false -or -not [string]::IsNullOrWhiteSpace($_.ErrorKind) })
$failureClasses = @($failureRows |
    Group-Object { "{0}|{1}|{2}" -f $_.Stage, $_.Name, $(if ([string]::IsNullOrWhiteSpace($_.ErrorKind)) { "(unknown)" } else { $_.ErrorKind }) } |
    ForEach-Object {
        [pscustomobject]@{
            Label = $_.Name
            Count = $_.Count
        }
    } |
    Sort-Object Count -Descending |
    Select-Object -First $Top)

$bridgeRequestRows = @($coverageRows | Where-Object { $_.Stage -eq "coverage_bridge_command_request" })
$bridgeResponseRows = @($coverageRows | Where-Object { $_.Stage -eq "coverage_bridge_command_response" })
$bridgeRequestBytes = Get-MeasureSum -InputObject $bridgeRequestRows -Property RequestBytes
$bridgeResponseBytes = Get-MeasureSum -InputObject $bridgeResponseRows -Property ResponseBytes
$bridgeTopCommands = @($bridgeRequestRows |
    Group-Object CommandType |
    ForEach-Object {
        [pscustomobject]@{
            Label = $_.Name
            Count = $_.Count
            RequestBytes = [int64][math]::Round((Get-MeasureSum -InputObject $_.Group -Property RequestBytes))
        }
    } |
    Sort-Object -Property @{ Expression = "Count"; Descending = $true }, @{ Expression = "RequestBytes"; Descending = $true } |
    Select-Object -First $Top)

$bridgeLatencySummary = New-LatencySummary -Rows $bridgeResponseRows

$toolSnapshotRows = @($rows | Where-Object { $_.EventKind -eq "tool_snapshot" -or $_.Stage -eq "tool_snapshot" })
$orderedSnapshotRows = @($toolSnapshotRows | Sort-Object TimestampUtc)
$minimalTransitions = 0
$fullTransitions = 0
$falseStableMinimal = 0
$previousSnapshotRow = $null
foreach ($row in $orderedSnapshotRows) {
    if ($previousSnapshotRow -ne $null) {
        if ($row.SnapshotHashMinimal -ne $previousSnapshotRow.SnapshotHashMinimal) { $minimalTransitions++ }
        if ($row.SnapshotHashFull -ne $previousSnapshotRow.SnapshotHashFull) { $fullTransitions++ }
        if ($row.SnapshotHashMinimal -eq $previousSnapshotRow.SnapshotHashMinimal -and
            $row.SnapshotHashFull -ne $previousSnapshotRow.SnapshotHashFull) {
            $falseStableMinimal++
        }
    }

    $previousSnapshotRow = $row
}

$toolSnapshotModes = @($toolSnapshotRows |
    Group-Object { if ([string]::IsNullOrWhiteSpace($_.DiscoveryMode)) { "(none)" } else { $_.DiscoveryMode } } |
    ForEach-Object {
        [pscustomobject]@{
            Mode = $_.Name
            Count = $_.Count
        }
    } |
    Sort-Object Count -Descending)

$cacheReuseRows = @($rows |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.CacheReuseClass) } |
    Group-Object CacheReuseClass |
    ForEach-Object {
        [pscustomobject]@{
            CacheReuseClass = $_.Name
            Count = $_.Count
        }
    } |
    Sort-Object Count -Descending)

$largestEntries = @($payloadRows |
    Sort-Object RawBytes -Descending |
    Select-Object -First $Top TimestampUtc, Stage, Name, RawBytes, ShapedBytes, RepresentationKind, PayloadClass, Hash, NormalizedHash)

$report = [pscustomobject]@{
    StatsPath = $resolvedStatsPath
    EntryCount = $rows.Count
    PayloadEntryCount = $payloadRows.Count
    CoverageEntryCount = $coverageRows.Count
    SkippedLines = $skippedLines
    DateRange = [pscustomobject]@{
        First = if ($sortedRows.Count -gt 0) { $sortedRows[0].TimestampUtc } else { $null }
        Last = if ($sortedRows.Count -gt 0) { $sortedRows[-1].TimestampUtc } else { $null }
    }
    Totals = [pscustomobject]@{
        RawBytes = [int64][math]::Round($totalRawBytes)
        ShapedBytes = [int64][math]::Round($totalShapedBytes)
        SavedBytes = [int64][math]::Round([math]::Max(0, $totalRawBytes - $totalShapedBytes))
        RawTokens = [int64]$totalRawTokens
        ShapedTokens = [int64]$totalShapedTokens
        SavedTokens = [int64][math]::Max(0, $totalRawTokens - $totalShapedTokens)
        SavingsPct = Get-Percent -Part ([math]::Max(0, $totalRawBytes - $totalShapedBytes)) -Whole $totalRawBytes
    }
    RepeatedContextEstimate = [pscustomobject]@{
        ExactDuplicateGroups = @($exactDuplicateGroups).Count
        ExactRepeatedEntries = $repeatedEntries
        ExactRepeatedRawBytes = [int64][math]::Round($repeatedRawBytes)
        ExactRepeatedRawPct = Get-Percent -Part $repeatedRawBytes -Whole $totalRawBytes
        NormalizedDuplicateGroups = @($normalizedDuplicateGroups).Count
        NormalizedRepeatedEntries = $normalizedRepeatedEntries
        NormalizedRepeatedRawBytes = [int64][math]::Round($normalizedRepeatedBytes)
        NormalizedRepeatedRawPct = Get-Percent -Part $normalizedRepeatedBytes -Whole $totalRawBytes
    }
    RepresentationMix = @($representationMix)
    RunSummaries = @($runSummaries)
    Latency = [pscustomobject]@{
        Overall = $latencySummary
        SlowOperations = @($slowOperations)
    }
    BridgeCoverage = [pscustomobject]@{
        RequestCount = $bridgeRequestRows.Count
        ResponseCount = $bridgeResponseRows.Count
        DirectRequests = @($bridgeRequestRows | Where-Object { $_.Origin -eq "direct" }).Count
        GatewayRequests = @($bridgeRequestRows | Where-Object { $_.Origin -eq "gateway" }).Count
        RequestBytes = [int64][math]::Round($bridgeRequestBytes)
        ResponseBytes = [int64][math]::Round($bridgeResponseBytes)
        Latency = $bridgeLatencySummary
        TopCommands = @($bridgeTopCommands)
    }
    ToolSnapshotChurn = [pscustomobject]@{
        Count = $toolSnapshotRows.Count
        MinimalHashTransitions = $minimalTransitions
        FullHashTransitions = $fullTransitions
        FalseStableMinimalTransitions = $falseStableMinimal
        Modes = @($toolSnapshotModes)
    }
    CacheReuse = @($cacheReuseRows)
    FailureClasses = @($failureClasses)
    TopStages = @($byStage | Select-Object -First $Top)
    TopNames = @($byName | Select-Object -First $Top)
    LargestEntries = @($largestEntries)
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 8
    exit 0
}

Write-Host ""
Write-Host "Payload stats report"
Write-Host "Stats file: $resolvedStatsPath"
Write-Host "Rows: $($report.EntryCount)  Payload rows: $($report.PayloadEntryCount)  Coverage rows: $($report.CoverageEntryCount)  Skipped lines: $($report.SkippedLines)"
Write-Host ("Window: {0} -> {1}" -f $report.DateRange.First, $report.DateRange.Last)
Write-Host ""
Write-Host "Overall"
Write-Host ("  Raw bytes:      {0} ({1})" -f $report.Totals.RawBytes, (Get-ByteString $report.Totals.RawBytes))
Write-Host ("  Shaped bytes:   {0} ({1})" -f $report.Totals.ShapedBytes, (Get-ByteString $report.Totals.ShapedBytes))
Write-Host ("  Saved bytes:    {0} ({1})" -f $report.Totals.SavedBytes, (Get-ByteString $report.Totals.SavedBytes))
Write-Host ("  Raw tokens:     {0}" -f $report.Totals.RawTokens)
Write-Host ("  Shaped tokens:  {0}" -f $report.Totals.ShapedTokens)
Write-Host ("  Saved tokens:   {0}" -f $report.Totals.SavedTokens)
Write-Host ("  Savings:        {0}%" -f $report.Totals.SavingsPct)
Write-Host ""
Write-Host "Repeated context estimate"
Write-Host ("  Exact duplicate groups:      {0}" -f $report.RepeatedContextEstimate.ExactDuplicateGroups)
Write-Host ("  Exact repeated entries:      {0}" -f $report.RepeatedContextEstimate.ExactRepeatedEntries)
Write-Host ("  Exact repeated raw bytes:    {0} ({1})" -f $report.RepeatedContextEstimate.ExactRepeatedRawBytes, (Get-ByteString $report.RepeatedContextEstimate.ExactRepeatedRawBytes))
Write-Host ("  Exact repeated raw pct:      {0}%" -f $report.RepeatedContextEstimate.ExactRepeatedRawPct)
Write-Host ("  Normalized duplicate groups: {0}" -f $report.RepeatedContextEstimate.NormalizedDuplicateGroups)
Write-Host ("  Normalized repeated entries: {0}" -f $report.RepeatedContextEstimate.NormalizedRepeatedEntries)
Write-Host ("  Normalized repeated raw:     {0} ({1})" -f $report.RepeatedContextEstimate.NormalizedRepeatedRawBytes, (Get-ByteString $report.RepeatedContextEstimate.NormalizedRepeatedRawBytes))
Write-Host ("  Normalized repeated raw pct: {0}%" -f $report.RepeatedContextEstimate.NormalizedRepeatedRawPct)
Write-Host ""
Write-Host "Latency"
Write-Host ("  Rows with duration: {0}" -f $report.Latency.Overall.Count)
Write-Host ("  Mean / P50 / P95 / P99 / Max ms: {0} / {1} / {2} / {3} / {4}" -f $report.Latency.Overall.MeanMs, $report.Latency.Overall.P50Ms, $report.Latency.Overall.P95Ms, $report.Latency.Overall.P99Ms, $report.Latency.Overall.MaxMs)
Write-Host ""
Write-Host "Representation mix"
$report.RepresentationMix |
    Format-Table Representation, Count, RawBytes, ShapedBytes -AutoSize |
    Out-String |
    Write-Host

if ($report.RunSummaries.Count -gt 0) {
    Write-Host "Run summaries"
    $report.RunSummaries |
        Format-Table RunId, Count, PayloadRows, CoverageRows, RawBytes, FailureCount -AutoSize |
        Out-String |
        Write-Host
}

Write-Host "Bridge coverage"
Write-Host ("  Requests:        {0}" -f $report.BridgeCoverage.RequestCount)
Write-Host ("  Responses:       {0}" -f $report.BridgeCoverage.ResponseCount)
Write-Host ("  Direct requests: {0}" -f $report.BridgeCoverage.DirectRequests)
Write-Host ("  Gateway requests:{0}" -f $report.BridgeCoverage.GatewayRequests)
Write-Host ("  Request bytes:   {0} ({1})" -f $report.BridgeCoverage.RequestBytes, (Get-ByteString $report.BridgeCoverage.RequestBytes))
Write-Host ("  Response bytes:  {0} ({1})" -f $report.BridgeCoverage.ResponseBytes, (Get-ByteString $report.BridgeCoverage.ResponseBytes))
Write-Host ("  Response latency Mean / P95 ms: {0} / {1}" -f $report.BridgeCoverage.Latency.MeanMs, $report.BridgeCoverage.Latency.P95Ms)
if ($report.BridgeCoverage.TopCommands.Count -gt 0) {
    $report.BridgeCoverage.TopCommands |
        Format-Table Label, Count, RequestBytes -AutoSize |
        Out-String |
        Write-Host
}

if ($report.ToolSnapshotChurn.Count -gt 0) {
    Write-Host "Tool snapshot churn"
    Write-Host ("  Snapshot rows:               {0}" -f $report.ToolSnapshotChurn.Count)
    Write-Host ("  Minimal hash transitions:    {0}" -f $report.ToolSnapshotChurn.MinimalHashTransitions)
    Write-Host ("  Full hash transitions:       {0}" -f $report.ToolSnapshotChurn.FullHashTransitions)
    Write-Host ("  False-stable minimal hashes: {0}" -f $report.ToolSnapshotChurn.FalseStableMinimalTransitions)
    $report.ToolSnapshotChurn.Modes |
        Format-Table Mode, Count -AutoSize |
        Out-String |
        Write-Host
}

if ($report.CacheReuse.Count -gt 0) {
    Write-Host "Cache reuse"
    $report.CacheReuse |
        Format-Table CacheReuseClass, Count -AutoSize |
        Out-String |
        Write-Host
}

if ($report.FailureClasses.Count -gt 0) {
    Write-Host "Failure classes"
    $report.FailureClasses |
        Format-Table Label, Count -AutoSize |
        Out-String |
        Write-Host
}

Write-Host "Top stages"
$report.TopStages |
    Format-Table Label, Count, RawBytes, ShapedBytes, SavedBytes, SavingsPct -AutoSize |
    Out-String |
    Write-Host

Write-Host "Top names"
$report.TopNames |
    Format-Table Label, Count, RawBytes, ShapedBytes, SavedBytes, SavingsPct -AutoSize |
    Out-String |
    Write-Host

if ($report.Latency.SlowOperations.Count -gt 0) {
    Write-Host "Slow operations"
    $report.Latency.SlowOperations |
        Format-Table Label, Count, MeanMs, P95Ms, MaxMs -AutoSize |
        Out-String |
        Write-Host
}

Write-Host "Largest entries"
$report.LargestEntries |
    Format-Table TimestampUtc, Stage, Name, RawBytes, ShapedBytes, RepresentationKind, PayloadClass -AutoSize |
    Out-String |
    Write-Host
