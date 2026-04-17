param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$Types = @("Error", "Warning", "Log"),
    [int]$Count = 100,
    [string]$FilterText = "",
    [string]$SinceTimestamp = "",
    [string]$Format = "Detailed",
    [switch]$ExcludeMcpNoise,
    [switch]$IncludeStacktrace,
    [int]$TimeoutSeconds = 30
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$excludeMcpNoiseValue = if ($PSBoundParameters.ContainsKey("ExcludeMcpNoise")) { $ExcludeMcpNoise.IsPresent } else { $true }
$includeStacktraceValue = if ($PSBoundParameters.ContainsKey("IncludeStacktrace")) { $IncludeStacktrace.IsPresent } else { $true }
$result = Get-UnityConsoleEntries `
    -ProjectPath $resolvedProjectPath `
    -Types $Types `
    -Count $Count `
    -FilterText $FilterText `
    -SinceTimestamp $SinceTimestamp `
    -Format $Format `
    -ExcludeMcpNoise:$excludeMcpNoiseValue `
    -IncludeStacktrace:$includeStacktraceValue `
    -TimeoutSeconds $TimeoutSeconds

$result | ConvertTo-Json -Depth 30
