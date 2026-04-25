param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$TargetsJson,
    [Parameter(Mandatory = $true)][string]$AssertionsJson,
    [bool]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$TimeoutSeconds = 60
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$targets = @($TargetsJson | ConvertFrom-Json)
$assertions = @($AssertionsJson | ConvertFrom-Json)

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$idleWait = $null
if ($WaitForEditorIdle) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
    if (-not $idleWait.success) {
        [ordered]@{
            success    = $false
            message    = $idleWait.message
            editorIdle = $idleWait
        } | ConvertTo-Json -Depth 20
        exit 1
    }
}

$payload = [ordered]@{
    Targets = @($targets)
    Assertions = @($assertions)
}

$response = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_UI_VerifyScreenLayout" -Arguments $payload -TimeoutSeconds $TimeoutSeconds
$toolResult = Get-UnityToolObject -Response $response

[ordered]@{
    success    = $toolResult.success -eq $true
    message    = if ($toolResult.success -eq $true) { "UI screen layout verification completed." } else { $toolResult.error }
    payload    = $payload
    editorIdle = $idleWait
    result     = $toolResult
} | ConvertTo-Json -Depth 30

if ($toolResult.success -eq $true) {
    exit 0
}

exit 1
