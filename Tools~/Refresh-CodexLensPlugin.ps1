param(
    [switch]$InstallCache,
    [switch]$PruneOlderCacheVersions,
    [string]$PluginRoot,
    [string]$CacheRoot
)

$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if ([string]::IsNullOrWhiteSpace($PluginRoot)) {
    $PluginRoot = Join-Path $repoRoot ".agents\plugins\lens-dev-plugin"
}

if ([string]::IsNullOrWhiteSpace($CacheRoot)) {
    $CacheRoot = Join-Path $env:USERPROFILE ".codex\plugins\cache\unity-mcp-lens\lens-dev-plugin"
}

$pluginRootFull = [System.IO.Path]::GetFullPath($PluginRoot)
$cacheRootFull = [System.IO.Path]::GetFullPath($CacheRoot)
$manifestPath = Join-Path $pluginRootFull ".codex-plugin\plugin.json"

if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Plugin manifest not found at '$manifestPath'."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.version
$displayName = [string]$manifest.interface.displayName
$shortDescription = [string]$manifest.interface.shortDescription
$longDescription = [string]$manifest.interface.longDescription

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Plugin manifest has no version."
}

foreach ($field in @(
    @{ Name = "interface.displayName"; Value = $displayName },
    @{ Name = "interface.shortDescription"; Value = $shortDescription },
    @{ Name = "interface.longDescription"; Value = $longDescription }
)) {
    if ($field.Value -notmatch [regex]::Escape($version)) {
        throw "Plugin manifest '$($field.Name)' must include version '$version' so Codex can show whether the plugin is current."
    }
}

Write-Host "Repo Lens plugin:"
Write-Host "  Root: $pluginRootFull"
Write-Host "  Version: $version"
Write-Host "  Display: $displayName"
Write-Host "  Short: $shortDescription"

if ($InstallCache) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $cacheRootFull $version))
    $expectedPrefix = $cacheRootFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $target.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to copy plugin outside cache root. Target '$target' is not under '$cacheRootFull'."
    }

    New-Item -ItemType Directory -Path $cacheRootFull -Force | Out-Null
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }

    Copy-Item -LiteralPath $pluginRootFull -Destination $target -Recurse
    Write-Host "Installed cache copy:"
    Write-Host "  Target: $target"

    if ($PruneOlderCacheVersions) {
        Get-ChildItem -LiteralPath $cacheRootFull -Directory |
            Where-Object { $_.Name -ne $version } |
            ForEach-Object {
                $stalePath = [System.IO.Path]::GetFullPath($_.FullName)
                if (-not $stalePath.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    throw "Refusing to prune cache directory outside cache root: '$stalePath'."
                }

                Remove-Item -LiteralPath $stalePath -Recurse -Force
                Write-Host "  Pruned older cache copy: $stalePath"
            }
    }
}
else {
    Write-Host "InstallCache: false"
    Write-Host "  Re-run with -InstallCache to create/update the versioned Codex plugin cache copy."
}

if (Test-Path -LiteralPath $cacheRootFull) {
    Write-Host "Cached Lens plugin versions:"
    Get-ChildItem -LiteralPath $cacheRootFull -Directory |
        Sort-Object Name |
        ForEach-Object {
            $cachedManifestPath = Join-Path $_.FullName ".codex-plugin\plugin.json"
            if (Test-Path -LiteralPath $cachedManifestPath) {
                $cachedManifest = Get-Content -LiteralPath $cachedManifestPath -Raw | ConvertFrom-Json
                Write-Host ("  {0}: {1}" -f $_.Name, $cachedManifest.interface.displayName)
            }
            else {
                Write-Host ("  {0}: missing manifest" -f $_.Name)
            }
        }
}
else {
    Write-Host "Cached Lens plugin versions: none"
}
