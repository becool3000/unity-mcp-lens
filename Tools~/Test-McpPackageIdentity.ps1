param(
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$failures = New-Object System.Collections.Generic.List[string]

function Add-Failure {
    param([string]$Message)
    $failures.Add($Message)
}

$expectedPackageName = "com.becool3000.unity-mcp-lens"
$expectedDisplayName = "Unity MCP Lens"
$editorAsmdef = "Becool.UnityMcpLens.Editor"
$runtimeAsmdef = "Becool.UnityMcpLens.Runtime"

$packageJsonPath = Join-Path $PackageRoot "package.json"
if (-not (Test-Path $packageJsonPath -PathType Leaf)) {
    Add-Failure "package.json not found."
} else {
    $packageJson = Get-Content -Path $packageJsonPath -Raw | ConvertFrom-Json
    if ($packageJson.name -ne $expectedPackageName) {
        Add-Failure "package.json name must be '$expectedPackageName', found '$($packageJson.name)'."
    }
    if ($packageJson.displayName -ne $expectedDisplayName) {
        Add-Failure "package.json displayName must be '$expectedDisplayName', found '$($packageJson.displayName)'."
    }
}

$requiredPaths = @(
    "Editor/Lens/Becool.UnityMcpLens.Editor.asmdef",
    "Runtime/Becool.UnityMcpLens.Runtime.asmdef",
    "UnityMcpLensApp~",
    "Tools~",
    "Documentation~"
)

foreach ($relativePath in $requiredPaths) {
    if (-not (Test-Path (Join-Path $PackageRoot $relativePath))) {
        Add-Failure "Required standalone Lens path missing: $relativePath"
    }
}

$forbiddenPaths = @(
    "Modules",
    "Editor/Assistant",
    "Editor/InternalBridge",
    "Editor/UI",
    "Editor/Unity.AI.Search.Editor",
    "Runtime/Unity.AI.Assistant.Runtime.asmdef",
    "RelayApp~",
    "Tests/Editor/Unity.AI.Assistant.Editor.Tests.asmdef"
)

foreach ($relativePath in $forbiddenPaths) {
    if (Test-Path (Join-Path $PackageRoot $relativePath)) {
        Add-Failure "Forbidden Assistant/legacy package path still exists: $relativePath"
    }
}

$officialAssistantAsmdefs = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
@(
    "Unity.AI.Assistant.Runtime",
    "Unity.AI.Assistant.Editor",
    "Unity.AI.Assistant.UI.Editor",
    "Unity.AI.Assistant.Bridge.Editor",
    "Unity.AI.Assistant.Tools.Editor",
    "Unity.AI.Assistant.API.Editor",
    "Unity.AI.Assistant.AssetGenerators.Editor",
    "Unity.AI.Assistant.Integrations.Profiler.Editor",
    "Unity.AI.Assistant.Integrations.Sample"
) | ForEach-Object { [void]$officialAssistantAsmdefs.Add($_) }

Get-ChildItem -Path $PackageRoot -Recurse -Filter "*.asmdef" |
    Where-Object { $_.FullName -notmatch '\\.codex-temp\\|\\Library\\|\\Temp\\|\\bin\\|\\obj\\' } |
    ForEach-Object {
        $asmdefJson = Get-Content -Path $_.FullName -Raw | ConvertFrom-Json
        $name = [string]$asmdefJson.name
        if ($officialAssistantAsmdefs.Contains($name) -or $name.StartsWith("Unity.AI.Assistant.", [StringComparison]::Ordinal)) {
            Add-Failure "$($_.FullName): ships Assistant asmdef '$name'."
        }
        if ($name.StartsWith("Unity.AI.MCP.", [StringComparison]::Ordinal) -or
            $name.StartsWith("Unity.AI.Toolkit.", [StringComparison]::Ordinal) -or
            $name.StartsWith("Unity.AI.Tracing", [StringComparison]::Ordinal)) {
            Add-Failure "$($_.FullName): ships old Unity AI asmdef '$name' instead of Becool.UnityMcpLens.*."
        }
    }

$activeSourceRoots = @("Editor", "Runtime", "UnityMcpLensApp~") |
    ForEach-Object { Join-Path $PackageRoot $_ } |
    Where-Object { Test-Path $_ }

$patterns = @(
    @{ Pattern = 'Packages/com\.unity\.ai\.assistant'; Reason = "hardcoded old Assistant package path" },
    @{ Pattern = '^\s*using\s+Unity\.AI\.Assistant(?:\.|;)'; Reason = "Assistant namespace import" },
    @{ Pattern = '"Unity\.AI\.Assistant\.'; Reason = "Assistant assembly reference" }
)

foreach ($root in $activeSourceRoots) {
    Get-ChildItem -Path $root -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".asmdef", ".json", ".uxml", ".uss", ".ps1", ".csproj") } |
        ForEach-Object {
            foreach ($entry in $patterns) {
                $matches = Select-String -Path $_.FullName -Pattern $entry.Pattern -CaseSensitive -ErrorAction Stop
                foreach ($match in $matches) {
                    Add-Failure "$($match.Path):$($match.LineNumber): $($entry.Reason): $($match.Line.Trim())"
                }
            }
        }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "MCP package identity check passed. Package: $expectedPackageName; asmdefs: $editorAsmdef, $runtimeAsmdef."
