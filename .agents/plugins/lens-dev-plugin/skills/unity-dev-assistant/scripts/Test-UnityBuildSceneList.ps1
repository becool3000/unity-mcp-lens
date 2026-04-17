param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$ExpectedScenes = @()
)

. "$PSScriptRoot\UnityDevCommon.ps1"

if ($ExpectedScenes.Count -eq 0) {
    throw "Provide -ExpectedScenes with the exact enabled scene list to validate."
}

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$result = Test-UnityBuildSceneList -ProjectPath $resolvedProjectPath -ExpectedScenes $ExpectedScenes
$result | ConvertTo-Json -Depth 20
