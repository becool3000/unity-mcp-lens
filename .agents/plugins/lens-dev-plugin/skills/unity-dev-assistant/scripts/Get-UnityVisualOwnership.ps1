param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$SearchMethod = "by_name",
    [bool]$IncludeInactive = $true,
    [string]$CameraTarget = "",
    [string]$CameraSearchMethod = "by_name",
    [string]$ReferenceTarget = "",
    [string]$ReferenceSearchMethod = "by_name",
    [bool]$IncludeOwnership = $true,
    [bool]$SampleOverTime = $false,
    [int]$SampleDurationMs = 400,
    [int]$SampleIntervalMs = 50,
    [int]$ToolTimeoutSeconds = 45
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$payload = [ordered]@{
    Target               = $Target
    SearchMethod         = $SearchMethod
    IncludeInactive      = $IncludeInactive
    IncludeOwnership     = $IncludeOwnership
    SampleOverTime       = $SampleOverTime
    SampleDurationMs     = $SampleDurationMs
    SampleIntervalMs     = $SampleIntervalMs
}

if (-not [string]::IsNullOrWhiteSpace($CameraTarget)) {
    $payload.CameraTarget = $CameraTarget
    $payload.CameraSearchMethod = $CameraSearchMethod
}

if (-not [string]::IsNullOrWhiteSpace($ReferenceTarget)) {
    $payload.ReferenceTarget = $ReferenceTarget
    $payload.ReferenceSearchMethod = $ReferenceSearchMethod
}

$response = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Runtime_GetVisualBoundsSnapshot" -Arguments $payload -TimeoutSeconds $ToolTimeoutSeconds
$toolResult = Get-UnityToolObject -Response $response

[ordered]@{
    success = $toolResult.success -eq $true
    message = if ($toolResult.success -eq $true) { "Visual ownership snapshot captured." } else { $toolResult.error }
    payload = $payload
    result  = $toolResult
} | ConvertTo-Json -Depth 30

if ($toolResult.success -eq $true) {
    exit 0
}

exit 1
