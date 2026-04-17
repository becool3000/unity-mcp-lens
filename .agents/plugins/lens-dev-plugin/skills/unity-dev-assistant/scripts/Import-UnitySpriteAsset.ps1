param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$AssetPath,
    [string]$SpriteMode = "",
    [Nullable[bool]]$AlphaIsTransparency = $null,
    [string]$FilterMode = "",
    [string]$Compression = "",
    [Nullable[float]]$PixelsPerUnit = $null,
    [bool]$PreserveExistingSlicing = $true,
    [bool]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$ToolTimeoutSeconds = 90
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$idleWait = $null
if ($WaitForEditorIdle) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
    if (-not $idleWait.success) {
        [ordered]@{
            success    = $false
            message    = $idleWait.message
            assetPath  = $AssetPath
            editorIdle = $idleWait
        } | ConvertTo-Json -Depth 20
        exit 1
    }
}

$payload = [ordered]@{
    AssetPath = $AssetPath
    PreserveExistingSlicing = $PreserveExistingSlicing
}

if (-not [string]::IsNullOrWhiteSpace($SpriteMode)) {
    $payload.SpriteMode = $SpriteMode
}

if ($null -ne $AlphaIsTransparency) {
    $payload.AlphaIsTransparency = [bool]$AlphaIsTransparency
}

if (-not [string]::IsNullOrWhiteSpace($FilterMode)) {
    $payload.FilterMode = $FilterMode
}

if (-not [string]::IsNullOrWhiteSpace($Compression)) {
    $payload.Compression = $Compression
}

if ($null -ne $PixelsPerUnit) {
    $payload.PixelsPerUnit = [float]$PixelsPerUnit
}

$response = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Asset_ConfigureSpriteImport" -Arguments $payload -TimeoutSeconds $ToolTimeoutSeconds
$toolResult = Get-UnityToolObject -Response $response

[ordered]@{
    success    = $toolResult.success -eq $true
    message    = if ($toolResult.success -eq $true) { "Sprite import configured." } else { $toolResult.error }
    assetPath  = $AssetPath
    payload    = $payload
    editorIdle = $idleWait
    result     = $toolResult
} | ConvertTo-Json -Depth 30

if ($toolResult.success -eq $true) {
    exit 0
}

exit 1
