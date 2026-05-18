param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$HierarchyTarget,
    [string]$HierarchySearchMethod = "by_name",
    [string]$NodesJson,
    [string]$NodesPath,
    [string]$BindingTarget,
    [string]$BindingSearchMethod = "by_name",
    [string]$BindingsJson,
    [string]$BindingsPath,
    [string]$LayoutTarget,
    [string]$LayoutSearchMethod = "by_name",
    [string]$LayoutTargetPath = ".",
    [string]$LayoutPropertiesJson,
    [string]$LayoutPropertiesPath,
    [string]$VerifyTargetsJson,
    [string]$VerifyTargetsPath,
    [string]$VerifyAssertionsJson,
    [string]$VerifyAssertionsPath,
    [switch]$Apply,
    [object]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$TimeoutSeconds = 60
)

. "$PSScriptRoot\UnityDevCommon.ps1"

function Read-WorkflowJsonValue {
    param(
        [string]$Json,
        [string]$JsonPath,
        [object]$DefaultValue = $null
    )

    if (-not [string]::IsNullOrWhiteSpace($JsonPath)) {
        return Get-Content -LiteralPath (Resolve-Path -LiteralPath $JsonPath).Path -Raw | ConvertFrom-Json
    }

    if (-not [string]::IsNullOrWhiteSpace($Json)) {
        return $Json | ConvertFrom-Json
    }

    return $DefaultValue
}

function ConvertTo-WorkflowArray {
    param([object]$Value)

    if ($null -eq $Value) {
        return @()
    }

    return @($Value)
}

function Add-PreviewApplySteps {
    param(
        [System.Collections.ArrayList]$Steps,
        [string]$PreviewName,
        [string]$PreviewTool,
        [string]$ApplyName,
        [string]$ApplyTool,
        [object]$Arguments,
        [bool]$IncludeApply
    )

    [void]$Steps.Add([ordered]@{
        name = $PreviewName
        tool = $PreviewTool
        arguments = $Arguments
    })

    if ($IncludeApply) {
        [void]$Steps.Add([ordered]@{
            name = $ApplyName
            tool = $ApplyTool
            arguments = $Arguments
        })
    }
}

$WaitForEditorIdle = ConvertTo-UnityBool -Value $WaitForEditorIdle -Default $true
$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$temporaryStepsPath = $null

try {
    $idleWait = $null
    if ($WaitForEditorIdle) {
        $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
        if (-not $idleWait.success) {
            [ordered]@{
                success    = $false
                message    = $idleWait.message
                projectPath = $resolvedProjectPath
                editorIdle  = $idleWait
            } | ConvertTo-Json -Depth 20
            exit 1
        }
    }

    $steps = [System.Collections.ArrayList]::new()
    [void]$steps.Add([ordered]@{
        name = "health"
        tool = "Unity_Editor_HealthCheckFast"
        arguments = [ordered]@{
            ProjectPath = $resolvedProjectPath
        }
    })

    $nodes = ConvertTo-WorkflowArray -Value (Read-WorkflowJsonValue -Json $NodesJson -JsonPath $NodesPath)
    if (-not [string]::IsNullOrWhiteSpace($HierarchyTarget) -and $nodes.Count -gt 0) {
        Add-PreviewApplySteps -Steps $steps `
            -PreviewName "preview_ensure_hierarchy" `
            -PreviewTool "Unity_UI_PreviewEnsureHierarchy" `
            -ApplyName "apply_ensure_hierarchy" `
            -ApplyTool "Unity_UI_ApplyEnsureHierarchy" `
            -IncludeApply $Apply.IsPresent `
            -Arguments ([ordered]@{
                Target = $HierarchyTarget
                SearchMethod = $HierarchySearchMethod
                PreviewOnly = -not $Apply.IsPresent
                Nodes = @($nodes)
            })
    }

    $bindings = ConvertTo-WorkflowArray -Value (Read-WorkflowJsonValue -Json $BindingsJson -JsonPath $BindingsPath)
    if (-not [string]::IsNullOrWhiteSpace($BindingTarget) -and $bindings.Count -gt 0) {
        Add-PreviewApplySteps -Steps $steps `
            -PreviewName "preview_bind_serialized_references" `
            -PreviewTool "Unity_Scene_PreviewBindSerializedReferences" `
            -ApplyName "apply_bind_serialized_references" `
            -ApplyTool "Unity_Scene_ApplyBindSerializedReferences" `
            -IncludeApply $Apply.IsPresent `
            -Arguments ([ordered]@{
                Target = $BindingTarget
                SearchMethod = $BindingSearchMethod
                Bindings = @($bindings)
            })
    }

    if (-not [string]::IsNullOrWhiteSpace($LayoutTarget)) {
        $layoutProperties = Read-WorkflowJsonValue -Json $LayoutPropertiesJson -JsonPath $LayoutPropertiesPath -DefaultValue ([pscustomobject]@{})
        $layoutArguments = [ordered]@{
            Target = $LayoutTarget
            SearchMethod = $LayoutSearchMethod
            TargetPath = $LayoutTargetPath
        }
        foreach ($property in $layoutProperties.PSObject.Properties) {
            $layoutArguments[$property.Name] = $property.Value
        }

        Add-PreviewApplySteps -Steps $steps `
            -PreviewName "preview_layout_properties" `
            -PreviewTool "Unity_UI_PreviewLayoutProperties" `
            -ApplyName "apply_layout_properties" `
            -ApplyTool "Unity_UI_ApplyLayoutProperties" `
            -IncludeApply $Apply.IsPresent `
            -Arguments $layoutArguments
    }

    $verifyTargets = ConvertTo-WorkflowArray -Value (Read-WorkflowJsonValue -Json $VerifyTargetsJson -JsonPath $VerifyTargetsPath)
    $verifyAssertions = ConvertTo-WorkflowArray -Value (Read-WorkflowJsonValue -Json $VerifyAssertionsJson -JsonPath $VerifyAssertionsPath)
    if ($verifyTargets.Count -gt 0 -and $verifyAssertions.Count -gt 0) {
        [void]$steps.Add([ordered]@{
            name = "verify_screen_layout"
            tool = "Unity_UI_VerifyScreenLayout"
            arguments = [ordered]@{
                Targets = @($verifyTargets)
                Assertions = @($verifyAssertions)
            }
        })
    }

    if ($steps.Count -le 1) {
        throw "Provide at least one Phase 12 operation: -HierarchyTarget with -NodesJson/-NodesPath, -BindingTarget with -BindingsJson/-BindingsPath, -LayoutTarget, or -VerifyTargetsJson/-VerifyTargetsPath plus -VerifyAssertionsJson/-VerifyAssertionsPath."
    }

    $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "codex-unity"
    New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
    $temporaryStepsPath = Join-Path $tempDirectory ("unity-ui-scene-binding-workflow-" + [guid]::NewGuid().ToString("N") + ".json")
    $steps | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $temporaryStepsPath -Encoding UTF8

    & "$PSScriptRoot\Invoke-UnityMcpBatch.ps1" -ProjectPath $resolvedProjectPath -StepsPath $temporaryStepsPath -TimeoutSeconds $TimeoutSeconds
    exit $LASTEXITCODE
}
finally {
    if ($temporaryStepsPath -and (Test-Path -LiteralPath $temporaryStepsPath)) {
        Remove-Item -LiteralPath $temporaryStepsPath -Force -ErrorAction SilentlyContinue
    }
}
