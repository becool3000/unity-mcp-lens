param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

function Get-NextLensVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    $trimmed = $Version.Trim()
    $match = [regex]::Match($trimmed, '^(?<base>\d+\.\d+\.\d+)(?:-(?<pre>[0-9A-Za-z.-]+))?(?:\+(?<build>[0-9A-Za-z.-]+))?$')
    if (-not $match.Success) {
        throw "Version '$Version' is not a supported semantic version."
    }

    $base = $match.Groups["base"].Value
    $pre = $match.Groups["pre"].Value
    if ([string]::IsNullOrWhiteSpace($pre)) {
        $parts = $base.Split(".")
        $parts[2] = ([int]$parts[2] + 1).ToString()
        return ($parts -join ".")
    }

    $segments = [System.Collections.Generic.List[string]]::new()
    foreach ($segment in $pre.Split(".")) {
        $segments.Add($segment)
    }

    for ($i = $segments.Count - 1; $i -ge 0; $i--) {
        $number = 0
        if ([int]::TryParse($segments[$i], [ref]$number)) {
            $segments[$i] = ($number + 1).ToString()
            return "$base-$($segments -join ".")"
        }
    }

    $segments.Add("1")
    return "$base-$($segments -join ".")"
}

function Read-JsonVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Version metadata file not found: $Path"
    }

    $json = Get-Content -Raw -LiteralPath $Path
    $match = [regex]::Match($json, '"version"\s*:\s*"(?<version>[^"]+)"')
    if (-not $match.Success) {
        throw "No version property found in $Path"
    }

    return $match.Groups["version"].Value
}

function Write-JsonVersion {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Version
    )

    $json = Get-Content -Raw -LiteralPath $Path
    $updated = [regex]::Replace(
        $json,
        '"version"\s*:\s*"[^"]+"',
        """version"": ""$Version""",
        1)

    $directory = Split-Path -Parent $Path
    $temp = Join-Path $directory ".$([System.IO.Path]::GetFileName($Path)).$([guid]::NewGuid().ToString("N")).tmp"
    [System.IO.File]::WriteAllText($temp, $updated, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

$resolvedRoot = (Resolve-Path -LiteralPath $ProjectRoot).Path
$packageJsonPath = Join-Path $resolvedRoot "package.json"
$hostMetadataPath = Join-Path $resolvedRoot "UnityMcpLensApp~\unity-mcp-lens.json"

$currentVersion = Read-JsonVersion -Path $packageJsonPath
$nextVersion = Get-NextLensVersion -Version $currentVersion

Write-JsonVersion -Path $packageJsonPath -Version $nextVersion
Write-JsonVersion -Path $hostMetadataPath -Version $nextVersion

Write-Output $nextVersion
