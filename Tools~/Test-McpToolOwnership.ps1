param(
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$mcpRoot = Join-Path $PackageRoot "Modules/Unity.AI.MCP.Editor"
$catalogPath = Join-Path $mcpRoot "Lens/ToolPackCatalog.cs"
$auditPath = Join-Path $PackageRoot "docs/lens-assistant-tool-ownership-audit.md"
$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $catalogPath)) {
    throw "ToolPackCatalog not found: $catalogPath"
}

if (-not (Test-Path $auditPath)) {
    throw "Assistant tool ownership audit not found: $auditPath"
}

$definedTools = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
$constantValues = @{}

Get-ChildItem -Path $mcpRoot -Recurse -Filter "*.cs" | ForEach-Object {
    $text = Get-Content -Path $_.FullName -Raw

    [regex]::Matches($text, 'const\s+string\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*"(Unity\.[^"]+)"') | ForEach-Object {
        $constantValues[$_.Groups[1].Value] = $_.Groups[2].Value
        if ($_.Groups[1].Value -match 'ToolName$') {
            [void]$definedTools.Add($_.Groups[2].Value)
        }
    }

    [regex]::Matches($text, '\[McpTool\(\s*"(Unity\.[^"]+)"') | ForEach-Object {
        [void]$definedTools.Add($_.Groups[1].Value)
    }

    [regex]::Matches($text, '\[McpTool\(\s*(?:[A-Za-z_][A-Za-z0-9_]*\.)*([A-Za-z_][A-Za-z0-9_]*)') | ForEach-Object {
        $constantName = $_.Groups[1].Value
        if ($constantValues.ContainsKey($constantName)) {
            [void]$definedTools.Add($constantValues[$constantName])
        }
    }
}

$catalogText = Get-Content -Path $catalogPath -Raw
$catalogRefs = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

[regex]::Matches($catalogText, 'const\s+string\s+[A-Za-z_][A-Za-z0-9_]*ToolName\s*=\s*"(Unity\.[^"]+)"') | ForEach-Object {
    [void]$catalogRefs.Add($_.Groups[1].Value)
}

[regex]::Matches($catalogText, 'NormalizeToolName\(\s*"(Unity\.[^"]+)"\s*\)') | ForEach-Object {
    [void]$catalogRefs.Add($_.Groups[1].Value)
}

foreach ($toolName in $catalogRefs) {
    if (-not $definedTools.Contains($toolName)) {
        $failures.Add("ToolPackCatalog references missing tool: $toolName")
    }
}

$oldAssistantTools = @(
    "Unity.Camera.Capture",
    "Unity.Camera.GetVisibleObjects",
    "Unity.CodeEdit",
    "Unity.DeleteFile",
    "Unity.EnterPlayMode",
    "Unity.ExitPlayMode",
    "Unity.FindFiles",
    "Unity.FindOrCreateDefaultPanelSettings",
    "Unity.FindPanelSettings",
    "Unity.FindProjectAssets",
    "Unity.FindSceneObjects",
    "Unity.GameObject.AddComponent",
    "Unity.GameObject.CreateGameObject",
    "Unity.GameObject.GetBuiltinAssets",
    "Unity.GameObject.GetComponentProperties",
    "Unity.GameObject.GetGameObjectBounds",
    "Unity.GameObject.GetSelection",
    "Unity.GameObject.ManageLayer",
    "Unity.GameObject.ManagePrefab",
    "Unity.GameObject.ManageTag",
    "Unity.GameObject.ModifyGameObject",
    "Unity.GameObject.RemoveComponent",
    "Unity.GameObject.RemoveGameObject",
    "Unity.GameObject.SetComponentProperty",
    "Unity.GenerateUxmlSchemas",
    "Unity.GetAssetLabels",
    "Unity.GetConsoleLogs",
    "Unity.GetDependency",
    "Unity.GetFileContent",
    "Unity.GetImageAssetContent",
    "Unity.GetObjectData",
    "Unity.GetProjectData",
    "Unity.GetProjectOverview",
    "Unity.GetProjectSettings",
    "Unity.GetSceneInfo",
    "Unity.GetStaticProjectSettingsTool",
    "Unity.GetTextAssetContent",
    "Unity.GetUIAssetPreview",
    "Unity.GetUnityDependenciesTool",
    "Unity.GetUnityVersion",
    "Unity.GetUserGuidelines",
    "Unity.PackageManager.ExecuteAction",
    "Unity.PackageManager.GetData",
    "Unity.RunCommand",
    "Unity.RunCommandValidator",
    "Unity.SaveAndValidateUIAsset",
    "Unity.SaveFile",
    "Unity.SceneView.Capture2DScene",
    "Unity.SceneView.CaptureMultiAngleSceneView",
    "Unity.Skill.ReadSkillBody",
    "Unity.Skill.ReadSkillResource",
    "Unity.ValidateUIAsset",
    "Unity.Web.Fetch"
)

$auditText = Get-Content -Path $auditPath -Raw
foreach ($toolName in $oldAssistantTools) {
    if ($auditText -notmatch [regex]::Escape($toolName)) {
        $failures.Add("Assistant tool audit missing mapping: $toolName")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "MCP tool ownership check passed. Catalog references: $($catalogRefs.Count). Defined tools: $($definedTools.Count). Audited Assistant tools: $($oldAssistantTools.Count)."
