param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$Target,
    [string]$SearchMethod = "by_name",
    [Parameter()][string]$AssignmentsJson,
    [Parameter()][string]$AssignmentsPath,
    [bool]$PreviewOnly = $false,
    [bool]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$TimeoutSeconds = 60
)

. "$PSScriptRoot\UnityDevCommon.ps1"

if ([string]::IsNullOrWhiteSpace($AssignmentsJson) -and [string]::IsNullOrWhiteSpace($AssignmentsPath)) {
    throw "Provide -AssignmentsJson or -AssignmentsPath."
}

$assignments = if (-not [string]::IsNullOrWhiteSpace($AssignmentsPath)) {
    Get-Content -LiteralPath (Resolve-Path -LiteralPath $AssignmentsPath).Path -Raw | ConvertFrom-Json
}
else {
    $AssignmentsJson | ConvertFrom-Json
}
$assignments = @($assignments)

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$idleWait = $null
if ($WaitForEditorIdle) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
    if (-not $idleWait.success) {
        [ordered]@{
            success    = $false
            message    = $idleWait.message
            target     = $Target
            editorIdle = $idleWait
        } | ConvertTo-Json -Depth 20
        exit 1
    }
}

$payload = [ordered]@{
    Target = $Target
    SearchMethod = $SearchMethod
    PreviewOnly = $PreviewOnly
    Assignments = @($assignments)
}

$response = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Scene_SetSerializedProperties" -Arguments $payload -TimeoutSeconds $TimeoutSeconds
$toolResult = Get-UnityToolObject -Response $response

[ordered]@{
    success    = $toolResult.success -eq $true
    message    = if ($toolResult.success -eq $true) { "Scene serialized property operation completed." } else { $toolResult.error }
    target     = $Target
    payload    = $payload
    editorIdle = $idleWait
    result     = $toolResult
} | ConvertTo-Json -Depth 30

if ($toolResult.success -eq $true) {
    exit 0
}

exit 1
