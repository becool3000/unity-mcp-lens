param(
    [string]$ProjectPath = (Get-Location).Path,
    [string[]]$Roots = @("Assets", "Packages"),
    [int]$MaxIssues = 20
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$result = Test-UnitySourceFileIntegrity -ProjectPath $resolvedProjectPath -Roots $Roots -MaxIssues $MaxIssues
$result | ConvertTo-Json -Depth 30

if ($result.success) {
    exit 0
}

exit 1
