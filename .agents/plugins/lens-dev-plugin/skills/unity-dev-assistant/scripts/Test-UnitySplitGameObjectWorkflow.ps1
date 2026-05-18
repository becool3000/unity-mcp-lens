param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$ObjectName,
    [object]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [int]$TimeoutSeconds = 60,
    [switch]$KeepObject
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$WaitForEditorIdle = ConvertTo-UnityBool -Value $WaitForEditorIdle -Default $true
$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath
$objectName = if ([string]::IsNullOrWhiteSpace($ObjectName)) {
    "CodexSplitGameObjectSmoke_" + [guid]::NewGuid().ToString("N").Substring(0, 8)
}
else {
    $ObjectName.Trim()
}
$renamedObjectName = "$objectName`_Renamed"
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
                objectName  = $objectName
                editorIdle  = $idleWait
            } | ConvertTo-Json -Depth 20
            exit 1
        }
    }

    $steps = @(
        [ordered]@{
            name = "health"
            tool = "Unity_Editor_HealthCheckFast"
            arguments = [ordered]@{
                ProjectPath = $resolvedProjectPath
            }
        },
        [ordered]@{
            name = "preview_create_empty"
            tool = "Unity_GameObject_PreviewCreate"
            arguments = [ordered]@{
                name = $objectName
                objectKind = "empty"
                position = @(1, 2, 3)
                rotation = @(0, 0, 0)
                scale = @(1, 1, 1)
            }
        },
        [ordered]@{
            name = "create_empty"
            tool = "Unity_GameObject_Create"
            arguments = [ordered]@{
                name = $objectName
                objectKind = "empty"
                position = @(1, 2, 3)
                rotation = @(0, 0, 0)
                scale = @(1, 1, 1)
            }
        },
        [ordered]@{
            name = "inspect_created"
            tool = "Unity_GameObject_Inspect"
            arguments = [ordered]@{
                mode = "find"
                target = $objectName
                searchMethod = "by_name"
                searchInactive = $true
            }
        },
        [ordered]@{
            name = "preview_transform_rename"
            tool = "Unity_GameObject_PreviewChanges"
            arguments = [ordered]@{
                target = $objectName
                searchMethod = "by_name"
                name = $renamedObjectName
                position = @(2, 3, 4)
                rotation = @(0, 45, 0)
                scale = @(1.25, 1.25, 1.25)
            }
        },
        [ordered]@{
            name = "apply_transform_rename"
            tool = "Unity_GameObject_ApplyChanges"
            arguments = [ordered]@{
                target = $objectName
                searchMethod = "by_name"
                name = $renamedObjectName
                position = @(2, 3, 4)
                rotation = @(0, 45, 0)
                scale = @(1.25, 1.25, 1.25)
            }
        },
        [ordered]@{
            name = "preview_add_box_collider"
            tool = "Unity_GameObject_PreviewComponentChanges"
            arguments = [ordered]@{
                operation = "add"
                target = $renamedObjectName
                searchMethod = "by_name"
                componentName = "BoxCollider"
            }
        },
        [ordered]@{
            name = "apply_add_box_collider"
            tool = "Unity_GameObject_ApplyComponentChanges"
            arguments = [ordered]@{
                operation = "add"
                target = $renamedObjectName
                searchMethod = "by_name"
                componentName = "BoxCollider"
            }
        },
        [ordered]@{
            name = "list_components"
            tool = "Unity_GameObject_ListComponents"
            arguments = [ordered]@{
                target = $renamedObjectName
                searchMethod = "by_name"
                searchInactive = $true
            }
        },
        [ordered]@{
            name = "get_transform"
            tool = "Unity_GameObject_GetComponent"
            arguments = [ordered]@{
                target = $renamedObjectName
                searchMethod = "by_name"
                componentName = "Transform"
                componentIndex = 0
            }
        },
        [ordered]@{
            name = "resolve_stable_path"
            tool = "Unity_Object_ResolveStablePath"
            arguments = [ordered]@{
                target = $renamedObjectName
                mode = "scene"
                includeInactive = $true
                maxCandidates = 20
            }
        }
    )

    if (-not $KeepObject) {
        $steps += @(
            [ordered]@{
                name = "preview_delete"
                tool = "Unity_GameObject_PreviewDelete"
                arguments = [ordered]@{
                    target = $renamedObjectName
                    searchMethod = "by_name"
                    searchInactive = $true
                }
            },
            [ordered]@{
                name = "delete"
                tool = "Unity_GameObject_Delete"
                arguments = [ordered]@{
                    target = $renamedObjectName
                    searchMethod = "by_name"
                    searchInactive = $true
                }
            }
        )
    }

    $tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "codex-unity"
    New-Item -ItemType Directory -Force -Path $tempDirectory | Out-Null
    $temporaryStepsPath = Join-Path $tempDirectory ("unity-split-gameobject-workflow-" + [guid]::NewGuid().ToString("N") + ".json")
    $steps | ConvertTo-Json -Depth 40 | Set-Content -LiteralPath $temporaryStepsPath -Encoding UTF8

    & "$PSScriptRoot\Invoke-UnityMcpBatch.ps1" -ProjectPath $resolvedProjectPath -StepsPath $temporaryStepsPath -TimeoutSeconds $TimeoutSeconds
    exit $LASTEXITCODE
}
finally {
    if ($temporaryStepsPath -and (Test-Path -LiteralPath $temporaryStepsPath)) {
        Remove-Item -LiteralPath $temporaryStepsPath -Force -ErrorAction SilentlyContinue
    }
}
