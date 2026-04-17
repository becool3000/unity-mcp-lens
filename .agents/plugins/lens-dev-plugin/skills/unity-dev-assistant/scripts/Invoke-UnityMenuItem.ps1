param(
    [string]$ProjectPath = (Get-Location).Path,
    [ValidateSet("Execute", "List", "Exists", "Refresh")]
    [string]$Action = "Execute",
    [string]$MenuPath = "",
    [string]$Search = "",
    [switch]$Refresh,
    [int]$TimeoutSeconds = 30
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$result = Invoke-UnityMenuItemTool `
    -ProjectPath $resolvedProjectPath `
    -Action $Action `
    -MenuPath $MenuPath `
    -Search $Search `
    -Refresh:$Refresh.IsPresent `
    -TimeoutSeconds $TimeoutSeconds

$result | ConvertTo-Json -Depth 30
