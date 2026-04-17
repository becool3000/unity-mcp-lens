<#
Usage:
  .\Tools~\Write-ResearchBaselineManifest.ps1
  .\Tools~\Write-ResearchBaselineManifest.ps1 -HostProjectRoot C:\Path\To\UnityProject
  .\Tools~\Write-ResearchBaselineManifest.ps1 -HostProjectRoot C:\Path\To\UnityProject -ScenarioName prompt-repeat,tool-poll,idle-bridge
  .\Tools~\Write-ResearchBaselineManifest.ps1 -OutputPath C:\temp\patched-baseline-manifest.json
#>
[CmdletBinding()]
param(
    [string]$HostProjectRoot,
    [string]$OutputPath,
    [string[]]$ScenarioName = @()
)

$ErrorActionPreference = "Stop"
$PSNativeCommandUseErrorActionPreference = $false

function Get-RepoRoot {
    if (-not $PSScriptRoot) {
        throw "PSScriptRoot is unavailable."
    }

    return (Split-Path -Parent $PSScriptRoot)
}

function Invoke-Git {
    param(
        [string[]]$Arguments,
        [switch]$AllowFailure
    )

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.Arguments = ($Arguments -join " ")
    $psi.WorkingDirectory = $repoRoot
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    $result = @($stdout -split "(`r`n|`n|`r)" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $warnings = @($stderr -split "(`r`n|`n|`r)" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_ -notmatch '^warning:' })
    if ($process.ExitCode -ne 0 -and -not $AllowFailure) {
        throw "git $($Arguments -join ' ') failed."
    }

    if ($warnings.Count -gt 0 -and -not $AllowFailure) {
        throw ($warnings -join [Environment]::NewLine)
    }

    return @($result)
}

function Get-UnityVersion {
    param([string]$ProjectRoot)

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        return $null
    }

    $versionFile = Join-Path $ProjectRoot "ProjectSettings\ProjectVersion.txt"
    if (-not (Test-Path -LiteralPath $versionFile)) {
        return $null
    }

    $match = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(.+)$' | Select-Object -First 1
    if ($match) {
        return $match.Matches[0].Groups[1].Value.Trim()
    }

    return $null
}

function Get-ManifestDependency {
    param(
        [string]$ProjectRoot,
        [string]$PackageName
    )

    if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        return $null
    }

    $manifestPath = Join-Path $ProjectRoot "Packages\manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $null
    }

    $manifestJson = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    return $manifestJson.dependencies.$PackageName
}

$repoRoot = Get-RepoRoot
$resolvedHostProjectRoot = if ([string]::IsNullOrWhiteSpace($HostProjectRoot)) { $null } else { [System.IO.Path]::GetFullPath($HostProjectRoot) }
$resolvedOutputPath = if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    Join-Path $repoRoot "Documentation~\benchmarks\lens-baseline-manifest.json"
} else {
    [System.IO.Path]::GetFullPath($OutputPath)
}

$packageJson = Get-Content -LiteralPath (Join-Path $repoRoot "package.json") -Raw | ConvertFrom-Json
$head = (Invoke-Git -Arguments @("rev-parse", "HEAD"))[0]
$branch = (Invoke-Git -Arguments @("branch", "--show-current"))[0]
$status = Invoke-Git -Arguments @("status", "--short")
$diffStat = Invoke-Git -Arguments @("diff", "--stat=200", "--", ".")
$diffStatStaged = Invoke-Git -Arguments @("diff", "--cached", "--stat=200", "--", ".")
$repoRelativePath = "."

$manifestDependency = Get-ManifestDependency -ProjectRoot $resolvedHostProjectRoot -PackageName "com.becool3000.unity-mcp-lens"
$unityVersion = Get-UnityVersion -ProjectRoot $resolvedHostProjectRoot
$hostStatsPath = if ($resolvedHostProjectRoot) {
    Join-Path $resolvedHostProjectRoot "Library\AI.Gateway.PayloadStats.jsonl"
} else {
    $null
}

$manifest = [ordered]@{
    schemaVersion = "patched-baseline.v1"
    generatedUtc = (Get-Date).ToUniversalTime().ToString("O")
    baselineKind = "patched-baseline-frozen"
    package = [ordered]@{
        name = $packageJson.name
        displayName = $packageJson.displayName
        version = $packageJson.version
        unity = $packageJson.unity
        unityRelease = $packageJson.unityRelease
        repositoryUrl = $packageJson.repository.url
        localPackageSource = $repoRelativePath
        readmePatchPath = "README-CODEX-PATCH.md"
    }
    repo = [ordered]@{
        root = $repoRoot
        head = $head
        branch = $branch
        isDirty = ($status.Count -gt 0)
        dirtyFiles = @($status)
        diffStat = @($diffStat)
        stagedDiffStat = @($diffStatStaged)
    }
    hostProject = [ordered]@{
        root = $resolvedHostProjectRoot
        unityVersion = $unityVersion
        manifestDependency = $manifestDependency
        manifestPath = if ($resolvedHostProjectRoot) { Join-Path $resolvedHostProjectRoot "Packages\manifest.json" } else { $null }
        payloadStatsPath = $hostStatsPath
    }
    telemetry = [ordered]@{
        payloadStatsSchema = "ai.gateway.payload-stats.v2"
        payloadStatsPath = "Library/AI.Gateway.PayloadStats.jsonl"
        traceEnabled = $null
        traceNotes = @(
            "Populate traceEnabled from EditorUserSettings before freezing a benchmark tag.",
            "Keep measurement-only changes isolated from optimization changes."
        )
    }
    study = [ordered]@{
        primaryBaseline = "patched fork"
        optionalComparison = "official 2.3.0-pre.2 overlap-only"
        scenarioMatrix = @($ScenarioName)
        nextActions = @(
            "Fill in the host project manifest dependency if the package is consumed via file: path.",
            "Record MCP settings and relay settings before tagging the baseline.",
            "Run the scenario matrix on the frozen baseline before enabling new measurement hooks."
        )
    }
}

$json = $manifest | ConvertTo-Json -Depth 8
$outputDirectory = Split-Path -Parent $resolvedOutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

Set-Content -LiteralPath $resolvedOutputPath -Value $json -Encoding UTF8
Write-Host "Wrote baseline manifest to $resolvedOutputPath"
