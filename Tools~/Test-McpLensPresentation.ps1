param(
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$failures = New-Object System.Collections.Generic.List[string]
$scanFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message)
}

function Get-RelativePath {
    param([string]$Path)

    try {
        return [System.IO.Path]::GetRelativePath($PackageRoot, $Path)
    }
    catch {
        return $Path
    }
}

function Add-ScanTarget {
    param([string]$Target)

    if (Test-Path $Target -PathType Container) {
        Get-ChildItem -Path $Target -Recurse -File |
            Where-Object { $_.Extension -in @(".cs", ".uxml", ".uss") } |
            ForEach-Object { [void]$scanFiles.Add($_.FullName) }
        return
    }

    if (Test-Path $Target -PathType Leaf) {
        [void]$scanFiles.Add((Resolve-Path $Target).Path)
        return
    }

    Add-Failure "Presentation scan target not found: $Target"
}

$mcpRoot = Join-Path $PackageRoot "Editor/Lens"
$settingsRoot = Join-Path $mcpRoot "Settings"
$oldUsageWindowPath = Join-Path $PackageRoot "Editor/UI/Scripts/PayloadStatsWindow.cs"
$usageWindowPath = Join-Path $PackageRoot "Editor/Lens/Lens/Usage/PayloadStatsWindow.cs"

if (Test-Path $oldUsageWindowPath) {
    Add-Failure "Usage report window must not live under Assistant UI path: Editor/UI/Scripts/PayloadStatsWindow.cs"
}

if (Test-Path (Join-Path $PackageRoot "Editor/Lens/Settings/UI/GatewayConnectionItemControl.cs")) {
    Add-Failure "Legacy relay UI control must use Lens naming: GatewayConnectionItemControl.cs still exists"
}

Add-ScanTarget $settingsRoot
Add-ScanTarget (Join-Path $mcpRoot "Bridge.cs")
Add-ScanTarget (Join-Path $mcpRoot "ToolRegistry/ToolCategories.cs")
Add-ScanTarget (Join-Path $mcpRoot "Tools/ProfilerQueryTools.cs")
Add-ScanTarget (Join-Path $mcpRoot "Tools/UiToolkitTools.cs")
Add-ScanTarget $usageWindowPath

$bannedPatterns = @(
    @{ Pattern = 'Window/AI/Assistant Usage'; Reason = "old Assistant usage menu path" },
    @{ Pattern = '\bAssistant Usage\b'; Reason = "old Assistant usage window/report label" },
    @{ Pattern = '\bAssistant usage\b'; Reason = "old Assistant usage sentence label" },
    @{ Pattern = 'Reset Assistant Usage'; Reason = "old Assistant usage reset dialog title" },
    @{ Pattern = 'Project/AI/Unity MCP'; Reason = "old Project Settings path" },
    @{ Pattern = 'AI > Unity MCP'; Reason = "old Project Settings breadcrumb" },
    @{ Pattern = 'Assistant/Gateway relay'; Reason = "old relay wording" },
    @{ Pattern = 'Assistant profiler session'; Reason = "old profiler wording" },
    @{ Pattern = 'Assistant profiler UI'; Reason = "old profiler UI wording" },
    @{ Pattern = 'Assistant-specific schema'; Reason = "old UI Toolkit wording" },
    @{ Pattern = 'AI Assistant tools exposed via MCP'; Reason = "old category description" },
    @{ Pattern = 'new CategoryInfo\("Assistant"'; Reason = "old category display name" },
    @{ Pattern = 'text="Unity MCP"'; Reason = "old settings panel title" },
    @{ Pattern = 'text="Unity Bridge"'; Reason = "old bridge section title" },
    @{ Pattern = 'text="Integrations"'; Reason = "old integrations foldout title" },
    @{ Pattern = 'text="Locate Server"'; Reason = "old server button label" },
    @{ Pattern = '\(Gateway\)'; Reason = "old gateway display suffix" },
    @{ Pattern = 'AI Gateway connections'; Reason = "old gateway connection UI wording" },
    @{ Pattern = 'namespace Unity\.AI\.Assistant\.UI\.Editor\.Scripts'; Reason = "usage window must not use Assistant UI namespace" },
    @{ Pattern = 'GatewayConnectionItemControl'; Reason = "old legacy relay UI control name" }
)

foreach ($file in $scanFiles) {
    foreach ($entry in $bannedPatterns) {
        $matches = Select-String -Path $file -Pattern $entry.Pattern -AllMatches -CaseSensitive -ErrorAction Stop
        foreach ($match in $matches) {
            Add-Failure ("{0}:{1}: Found {2}: {3}" -f (Get-RelativePath $match.Path), $match.LineNumber, $entry.Reason, $match.Line.Trim())
        }
    }
}

$requiredText = @(
    @{ Path = "Editor/Lens/Settings/MCPConstants.cs"; Text = 'Project/Tools/Unity MCP Lens'; Reason = "Lens Project Settings path" },
    @{ Path = "Editor/Lens/Settings/UI/MCPSettingsPanel.uxml"; Text = 'text="Unity MCP Lens"'; Reason = "Lens settings panel title" },
    @{ Path = "Editor/Lens/Settings/UI/MCPSettingsPanel.uxml"; Text = 'text="Lens Bridge"'; Reason = "Lens bridge section title" },
    @{ Path = "Editor/Lens/Settings/UI/MCPSettingsPanel.uxml"; Text = 'text="Tool Packs &amp; Registry"'; Reason = "Lens tool surface foldout title" },
    @{ Path = "Editor/Lens/Settings/UI/MCPSettingsPanel.uxml"; Text = 'text="MCP Client Configs"'; Reason = "Lens client config foldout title" },
    @{ Path = "Editor/Lens/Settings/UI/MCPSettingsPanel.uxml"; Text = 'text="Advanced / Legacy Compatibility"'; Reason = "legacy compatibility section" },
    @{ Path = "Editor/Lens/Settings/UI/MCPSettingsPanel.uxml"; Text = 'text="Open Lens Server"'; Reason = "Lens server button label" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Tools/Unity MCP Lens/'; Reason = "Lens menu root" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Open Settings'; Reason = "Open Settings menu command" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Start Bridge'; Reason = "Start Bridge menu command" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Stop Bridge'; Reason = "Stop Bridge menu command" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Install/Refresh Lens Server'; Reason = "Install/Refresh Lens Server menu command" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Open Server Folder'; Reason = "Open Server Folder menu command" },
    @{ Path = "Editor/Lens/Settings/UI/LensMenuItems.cs"; Text = 'Open Status Folder'; Reason = "Open Status Folder menu command" },
    @{ Path = "Editor/Lens/Lens/Usage/PayloadStatsWindow.cs"; Text = 'namespace Becool.UnityMcpLens.Editor.Lens.Usage'; Reason = "Lens-owned usage report namespace" },
    @{ Path = "Editor/Lens/Lens/Usage/PayloadStatsWindow.cs"; Text = 'Tools/Unity MCP Lens/Usage Report'; Reason = "Lens Usage Report menu command" },
    @{ Path = "Editor/Lens/Lens/Usage/PayloadStatsWindow.cs"; Text = 'Unity MCP Lens Usage Report'; Reason = "Lens usage clipboard report heading" },
    @{ Path = "Editor/Lens/Settings/UI/ConnectedClientsControl.cs"; Text = '(Legacy Relay)'; Reason = "legacy relay connected-client label" },
    @{ Path = "Editor/Lens/Settings/UI/LegacyRelayConnectionItemControl.cs"; Text = 'class LegacyRelayConnectionItemControl'; Reason = "legacy relay connection item class name" },
    @{ Path = "Editor/Lens/Settings/UI/LegacyRelayConnectionItemControl.cs"; Text = '(Legacy Relay)'; Reason = "legacy relay connection item label" }
)

foreach ($entry in $requiredText) {
    $path = Join-Path $PackageRoot $entry.Path
    if (-not (Test-Path $path -PathType Leaf)) {
        Add-Failure "Required presentation file not found: $($entry.Path)"
        continue
    }

    $text = Get-Content -Path $path -Raw
    if (-not $text.Contains($entry.Text)) {
        Add-Failure "Missing $($entry.Reason) in $($entry.Path): $($entry.Text)"
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "MCP Lens presentation check passed. Scanned $($scanFiles.Count) files; required labels: $($requiredText.Count)."
