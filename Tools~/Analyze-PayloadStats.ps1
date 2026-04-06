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
        Label         = $Label
        Count         = $Count
        RawBytes      = [int64][math]::Round($RawBytes)
        ShapedBytes   = [int64][math]::Round($ShapedBytes)
        SavedBytes    = [int64][math]::Round($savedBytes)
        RawTokens     = [int64]$rawTokens
        ShapedTokens  = [int64]$shapedTokens
        SavedTokens   = [int64]$savedTokens
        SavingsPct    = Get-Percent -Part $savedBytes -Whole $RawBytes
    }
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

function Get-MeasureSum {
    param(
        $InputObject,
        [string]$Property
    )

    $measure = @($InputObject | Measure-Object -Property $Property -Sum)
    $sum = if ($measure.Count -gt 0) { $measure[0].Sum } else { 0 }
    return (Get-NumberValue -Value $sum)
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
$coverageRows = New-Object System.Collections.Generic.List[object]
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

        $rows.Add([pscustomobject]@{
            TimestampUtc = $timestamp
            Stage        = [string]$entry.stage
            Name         = [string]$entry.name
            RawBytes     = $rawBytes
            ShapedBytes  = $shapedBytes
            RawTokens    = [math]::Ceiling($rawBytes / 4.0)
            ShapedTokens = [math]::Ceiling($shapedBytes / 4.0)
            Hash         = [string]$entry.hash
            Meta         = $entry.meta
        })
    }
    catch {
        $skippedLines++
    }
}

if ($rows.Count -eq 0) {
    throw "No valid payload stat rows were found in $resolvedStatsPath"
}

$payloadRows = @($rows | Where-Object { -not $_.Stage.StartsWith("coverage_") })
$coverageRows = @($rows | Where-Object { $_.Stage.StartsWith("coverage_") })

if ($payloadRows.Count -eq 0) {
    $payloadRows = @()
}

$sortedRows = if ($payloadRows.Count -gt 0) { @($payloadRows | Sort-Object TimestampUtc) } else { @() }
$totalRawBytes = Get-MeasureSum -InputObject $payloadRows -Property RawBytes
$totalShapedBytes = Get-MeasureSum -InputObject $payloadRows -Property ShapedBytes
$totalRawTokens = Get-MeasureSum -InputObject $payloadRows -Property RawTokens
$totalShapedTokens = Get-MeasureSum -InputObject $payloadRows -Property ShapedTokens

$duplicateGroups = $payloadRows |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.Hash) } |
    Group-Object { "{0}|{1}|{2}" -f $_.Stage, $_.Name, $_.Hash } |
    Where-Object { $_.Count -gt 1 }

$repeatedRawBytes = 0.0
$repeatedShapedBytes = 0.0
$repeatedEntries = 0

foreach ($group in $duplicateGroups) {
    $duplicates = $group.Group | Sort-Object TimestampUtc | Select-Object -Skip 1
    if ($duplicates) {
        $repeatedEntries += @($duplicates).Count
        $repeatedRawBytes += Get-MeasureSum -InputObject $duplicates -Property RawBytes
        $repeatedShapedBytes += Get-MeasureSum -InputObject $duplicates -Property ShapedBytes
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

$largestEntries = $payloadRows |
    Sort-Object RawBytes -Descending |
    Select-Object -First $Top TimestampUtc, Stage, Name, RawBytes, ShapedBytes, RawTokens, ShapedTokens, Hash

$bridgeRequestRows = @($coverageRows | Where-Object { $_.Stage -eq "coverage_bridge_command_request" })
$bridgeResponseRows = @($coverageRows | Where-Object { $_.Stage -eq "coverage_bridge_command_response" })
$bridgeRequestBytes = 0.0
$bridgeResponseBytes = 0.0
$bridgeDirectRequests = 0
$bridgeGatewayRequests = 0

foreach ($row in $bridgeRequestRows) {
    $bridgeRequestBytes += Get-NumberValue -Value $row.Meta.payloadBytes
    if ([string]$row.Meta.origin -eq "direct") { $bridgeDirectRequests++ }
    if ([string]$row.Meta.origin -eq "gateway") { $bridgeGatewayRequests++ }
}

foreach ($row in $bridgeResponseRows) {
    $bridgeResponseBytes += Get-NumberValue -Value $row.Meta.payloadBytes
}

$bridgeTopCommands = @($bridgeRequestRows |
    Group-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            Label = $_.Name
            Count = $_.Count
            RequestBytes = [int64][math]::Round((Get-MeasureSum -InputObject ($_.Group | ForEach-Object { [pscustomobject]@{ Bytes = (Get-NumberValue -Value $_.Meta.payloadBytes) } }) -Property Bytes))
        }
    } |
    Sort-Object -Property @{ Expression = "Count"; Descending = $true }, @{ Expression = "RequestBytes"; Descending = $true } |
    Select-Object -First $Top)

$report = [pscustomobject]@{
    StatsPath = $resolvedStatsPath
    EntryCount = $payloadRows.Count
    CoverageEntryCount = $coverageRows.Count
    SkippedLines = $skippedLines
    DateRange = [pscustomobject]@{
        First = if ($sortedRows.Count -gt 0) { $sortedRows[0].TimestampUtc } else { $null }
        Last  = if ($sortedRows.Count -gt 0) { $sortedRows[-1].TimestampUtc } else { $null }
    }
    Totals = [pscustomobject]@{
        RawBytes      = [int64][math]::Round($totalRawBytes)
        ShapedBytes   = [int64][math]::Round($totalShapedBytes)
        SavedBytes    = [int64][math]::Round([math]::Max(0, $totalRawBytes - $totalShapedBytes))
        RawTokens     = [int64]$totalRawTokens
        ShapedTokens  = [int64]$totalShapedTokens
        SavedTokens   = [int64][math]::Max(0, $totalRawTokens - $totalShapedTokens)
        SavingsPct    = Get-Percent -Part ([math]::Max(0, $totalRawBytes - $totalShapedBytes)) -Whole $totalRawBytes
    }
    RepeatedContextEstimate = [pscustomobject]@{
        DuplicateGroups   = @($duplicateGroups).Count
        RepeatedEntries   = $repeatedEntries
        RepeatedRawBytes  = [int64][math]::Round($repeatedRawBytes)
        RepeatedShapedBytes = [int64][math]::Round($repeatedShapedBytes)
        RepeatedRawPct    = Get-Percent -Part $repeatedRawBytes -Whole $totalRawBytes
    }
    BridgeCoverage = [pscustomobject]@{
        RequestCount = $bridgeRequestRows.Count
        ResponseCount = $bridgeResponseRows.Count
        DirectRequests = $bridgeDirectRequests
        GatewayRequests = $bridgeGatewayRequests
        RequestBytes = [int64][math]::Round($bridgeRequestBytes)
        ResponseBytes = [int64][math]::Round($bridgeResponseBytes)
        TopCommands = @($bridgeTopCommands)
    }
    TopStages = @($byStage | Select-Object -First $Top)
    TopTools = @($byName | Select-Object -First $Top)
    LargestEntries = @($largestEntries)
}

if ($AsJson) {
    $report | ConvertTo-Json -Depth 6
    exit 0
}

Write-Host ""
Write-Host "Payload stats report"
Write-Host "Stats file: $resolvedStatsPath"
Write-Host "Payload rows: $($report.EntryCount)  Coverage rows: $($report.CoverageEntryCount)  Skipped lines: $($report.SkippedLines)"
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
Write-Host ("  Duplicate groups: {0}" -f $report.RepeatedContextEstimate.DuplicateGroups)
Write-Host ("  Repeated entries: {0}" -f $report.RepeatedContextEstimate.RepeatedEntries)
Write-Host ("  Repeated raw:     {0} ({1})" -f $report.RepeatedContextEstimate.RepeatedRawBytes, (Get-ByteString $report.RepeatedContextEstimate.RepeatedRawBytes))
Write-Host ("  Repeated raw %:   {0}%" -f $report.RepeatedContextEstimate.RepeatedRawPct)
Write-Host ""
Write-Host "Bridge coverage"
Write-Host ("  Requests:       {0}" -f $report.BridgeCoverage.RequestCount)
Write-Host ("  Responses:      {0}" -f $report.BridgeCoverage.ResponseCount)
Write-Host ("  Direct requests:{0}" -f $report.BridgeCoverage.DirectRequests)
Write-Host ("  Gateway requests:{0}" -f $report.BridgeCoverage.GatewayRequests)
Write-Host ("  Request bytes:  {0} ({1})" -f $report.BridgeCoverage.RequestBytes, (Get-ByteString $report.BridgeCoverage.RequestBytes))
Write-Host ("  Response bytes: {0} ({1})" -f $report.BridgeCoverage.ResponseBytes, (Get-ByteString $report.BridgeCoverage.ResponseBytes))
Write-Host ""
Write-Host "Top stages"
$report.TopStages |
    Format-Table Label, Count, RawBytes, ShapedBytes, SavedBytes, SavingsPct -AutoSize |
    Out-String |
    Write-Host

Write-Host "Top tools"
$report.TopTools |
    Format-Table Label, Count, RawBytes, ShapedBytes, SavedBytes, SavingsPct -AutoSize |
    Out-String |
    Write-Host

if ($report.BridgeCoverage.TopCommands.Count -gt 0) {
    Write-Host "Top bridge commands"
    $report.BridgeCoverage.TopCommands |
        Format-Table Label, Count, RequestBytes -AutoSize |
        Out-String |
        Write-Host
}

Write-Host "Largest entries"
$report.LargestEntries |
    Format-Table TimestampUtc, Stage, Name, RawBytes, ShapedBytes -AutoSize |
    Out-String |
    Write-Host
