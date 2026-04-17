param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$ManifestPath,
    [string]$StateFolder,
    [bool]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0
)

. "$PSScriptRoot\UnityDevCommon.ps1"

if ([string]::IsNullOrWhiteSpace($ManifestPath) -and [string]::IsNullOrWhiteSpace($StateFolder)) {
    throw "Provide -ManifestPath or -StateFolder."
}

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$resolvedManifestPath = $null
$resolvedStateFolder = $null
$manifest = $null

if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $resolvedManifestPath = (Resolve-Path -Path $ManifestPath).Path
    $manifest = Get-Content -Path $resolvedManifestPath -Raw | ConvertFrom-Json
    $resolvedStateFolder = Split-Path -Path $resolvedManifestPath -Parent
}
else {
    $resolvedStateFolder = (Resolve-Path -Path $StateFolder).Path
    $candidateManifest = Join-Path $resolvedStateFolder "unity-sprite-state.v1.json"
    if (Test-Path -Path $candidateManifest) {
        $resolvedManifestPath = $candidateManifest
        $manifest = Get-Content -Path $resolvedManifestPath -Raw | ConvertFrom-Json
    }
}

$flatFolder = if (Test-Path -Path (Join-Path $resolvedStateFolder "Flat")) {
    Join-Path $resolvedStateFolder "Flat"
}
else {
    $resolvedStateFolder
}

$pngFiles = Get-ChildItem -Path $flatFolder -Filter *.png -File | Sort-Object Name

function Convert-ToUnityAssetPath {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string]$ProjectRoot
    )

    $resolvedFilePath = (Resolve-Path -Path $FilePath).Path
    $normalizedProject = ([IO.Path]::GetFullPath($ProjectRoot)).TrimEnd('\') + '\'
    $normalizedFile = [IO.Path]::GetFullPath($resolvedFilePath)
    if (-not $normalizedFile.StartsWith($normalizedProject, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "File '$resolvedFilePath' is outside Unity project '$ProjectRoot'."
    }

    return $normalizedFile.Substring($normalizedProject.Length).Replace('\', '/')
}

$assetPaths = @()
foreach ($pngFile in $pngFiles) {
    $assetPaths += (Convert-ToUnityAssetPath -FilePath $pngFile.FullName -ProjectRoot $resolvedProjectPath)
}

$idleWait = $null
if ($WaitForEditorIdle) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
    if (-not $idleWait.success) {
        [ordered]@{
            success      = $false
            message      = $idleWait.message
            manifestPath = $resolvedManifestPath
            stateFolder  = $resolvedStateFolder
            editorIdle   = $idleWait
        } | ConvertTo-Json -Depth 30
        exit 1
    }
}

$assetPathLiterals = @()
foreach ($assetPath in $assetPaths) {
    $assetPathLiterals += ('"{0}"' -f (Escape-CSharpString -Value $assetPath))
}
$assetPathLiteral = $assetPathLiterals -join ",`n                "

$importCode = @"
string[] assetPaths = new[]
{
                $assetPathLiteral
};

foreach (var path in assetPaths)
{
    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
    if (importer == null)
    {
        result.Log("IMPORT_SUMMARY::{0}::{1}::{2}", path, -1, "missing-importer");
        continue;
    }

    importer.textureType = TextureImporterType.Sprite;
    importer.spriteImportMode = SpriteImportMode.Single;
    importer.spritePixelsPerUnit = 100f;
    importer.alphaIsTransparency = true;
    importer.mipmapEnabled = false;
    importer.filterMode = FilterMode.Point;
    importer.textureCompression = TextureImporterCompression.Uncompressed;
    importer.npotScale = TextureImporterNPOTScale.None;
    importer.SaveAndReimport();

    var assets = AssetDatabase.LoadAllAssetsAtPath(path);
    int spriteCount = 0;
    foreach (var asset in assets)
    {
        if (asset is Sprite)
        {
            spriteCount += 1;
        }
    }

    result.Log("IMPORT_SUMMARY::{0}::{1}::{2}", path, spriteCount, importer.spriteImportMode);
}
"@

$runResult = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $importCode -Title "Import Unity sprite state" -Usings @("UnityEditor") -TimeoutSeconds 180

$importedFiles = @()
$multiSpriteFiles = @()
$executionLogs = if ($runResult.data -and $runResult.data.executionLogs) { $runResult.data.executionLogs -split "`r?`n" } else { @() }
foreach ($line in $executionLogs) {
    if ($line -notmatch 'IMPORT_SUMMARY::') {
        continue
    }

    $summary = $line -replace '^\[Log\]\s*', ''
    $parts = $summary -split '::'
    if ($parts.Length -lt 4) {
        continue
    }

    $entry = [ordered]@{
        path             = $parts[1]
        spriteCount      = [int]$parts[2]
        spriteImportMode = $parts[3]
        exposesMultiple  = [int]$parts[2] -gt 1
    }
    $importedFiles += $entry
    if ($entry.exposesMultiple) {
        $multiSpriteFiles += $entry
    }
}

[ordered]@{
    success          = $runResult.success -eq $true
    message          = if ($runResult.success -eq $true) { "Sprite state import completed." } else { $runResult.error }
    manifestPath     = $resolvedManifestPath
    stateFolder      = $resolvedStateFolder
    assetPaths       = $assetPaths
    importedFiles    = $importedFiles
    multiSpriteFiles = $multiSpriteFiles
    editorIdle       = $idleWait
    manifest         = $manifest
    runResult        = $runResult
} | ConvertTo-Json -Depth 30

if ($runResult.success -eq $true) {
    exit 0
}

exit 1
