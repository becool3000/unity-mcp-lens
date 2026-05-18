param(
    [string]$ProjectPath = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$PrefabPath,
    [Parameter()][string]$ChecksJson,
    [Parameter()][string]$ChecksPath,
    [object]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$TimeoutSeconds = 60
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$WaitForEditorIdle = ConvertTo-UnityBool -Value $WaitForEditorIdle -Default $true

if ([string]::IsNullOrWhiteSpace($ChecksJson) -and [string]::IsNullOrWhiteSpace($ChecksPath)) {
    throw "Provide -ChecksJson or -ChecksPath."
}

$checks = if (-not [string]::IsNullOrWhiteSpace($ChecksPath)) {
    Get-Content -LiteralPath (Resolve-Path -LiteralPath $ChecksPath).Path -Raw | ConvertFrom-Json
}
else {
    $ChecksJson | ConvertFrom-Json
}
$checks = @($checks)

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$idleWait = $null
if ($WaitForEditorIdle) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
    if (-not $idleWait.success) {
        [ordered]@{
            success    = $false
            message    = $idleWait.message
            prefabPath = $PrefabPath
            editorIdle = $idleWait
        } | ConvertTo-Json -Depth 20
        exit 1
    }
}

$nativeChecks = @()
$labels = @()
for ($index = 0; $index -lt $checks.Count; $index++) {
    $check = $checks[$index]
    $label = if ($null -ne $check.Label -and -not [string]::IsNullOrWhiteSpace([string]$check.Label)) { [string]$check.Label } else { "check-$index" }
    $targetPath = if ($null -ne $check.TargetPath) { [string]$check.TargetPath } else { "." }
    $componentType = if ($null -ne $check.ComponentType) { [string]$check.ComponentType } else { "" }
    $propertyPath = if ($null -ne $check.PropertyPath) { [string]$check.PropertyPath } else { "" }
    $componentIndex = if ($null -ne $check.ComponentIndex) { [int]$check.ComponentIndex } else { 0 }

    $labels += $label
    $row = [ordered]@{
        Label = $label
        TargetPath = $targetPath
        ComponentType = $componentType
        ComponentIndex = $componentIndex
        PropertyPath = $propertyPath
    }
    if ($null -ne $check.ExpectedValue) {
        $row.ExpectedValue = $check.ExpectedValue
    }
    $nativeChecks += $row
}

$payload = [ordered]@{
    PrefabPath = $PrefabPath
    Checks = $nativeChecks
}

$response = Invoke-UnityMcpToolJson -ProjectPath $resolvedProjectPath -ToolName "Unity_Prefab_VerifySerializedProperties" -Arguments $payload -TimeoutSeconds $TimeoutSeconds
$toolResult = Get-UnityToolObject -Response $response

$entries = @()
if ($toolResult.success -eq $true -and $toolResult.data -and $toolResult.data.checks) {
    $checkResults = @($toolResult.data.checks)
    for ($index = 0; $index -lt $checkResults.Count; $index++) {
        $checkResult = $checkResults[$index]
        $entries += [ordered]@{
            label          = if ($index -lt $labels.Count) { $labels[$index] } else { "check-$index" }
            targetPath     = $checkResult.targetPath
            componentType  = $checkResult.componentType
            propertyPath   = $checkResult.propertyPath
            targetFound    = $checkResult.targetFound -ne $false
            componentFound = $checkResult.componentFound -ne $false
            propertyFound  = $checkResult.propertyFound -eq $true
            propertyType   = $checkResult.propertyType
            propertyValue  = $checkResult.currentValue
            passed         = $checkResult.passed -eq $true
        }
    }
}

[ordered]@{
    success    = $toolResult.success -eq $true
    message    = if ($toolResult.success -eq $true) { "Prefab serialized field verification completed." } else { $toolResult.error }
    prefabPath = $PrefabPath
    editorIdle = $idleWait
    checks     = $checks
    entries    = $entries
    result     = $toolResult
} | ConvertTo-Json -Depth 30

if ($toolResult.success -eq $true) {
    exit 0
}

exit 1
