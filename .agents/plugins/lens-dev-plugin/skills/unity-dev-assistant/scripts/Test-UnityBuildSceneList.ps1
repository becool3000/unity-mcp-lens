param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$ExpectedScenes = @(),
    [switch]$Strict
)

. "$PSScriptRoot\UnityDevCommon.ps1"

if ($ExpectedScenes.Count -eq 0) {
    throw "Provide -ExpectedScenes with the exact enabled scene list to validate."
}

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$result = Test-UnityBuildSceneList -ProjectPath $resolvedProjectPath -ExpectedScenes $ExpectedScenes -Strict:$Strict.IsPresent
$result | ConvertTo-Json -Depth 20

if ($result.success -eq $true) {
    exit 0
}

exit 1
