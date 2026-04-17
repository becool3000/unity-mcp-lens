param(
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$mcpRoot = Join-Path $PackageRoot "Modules/Unity.AI.MCP.Editor"
$asmdef = Join-Path $mcpRoot "Unity.AI.MCP.Editor.asmdef"
$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $mcpRoot)) {
    throw "MCP editor module not found: $mcpRoot"
}

Get-ChildItem -Path $mcpRoot -Recurse -Filter "*.cs" | ForEach-Object {
    $matches = Select-String -Path $_.FullName -Pattern '^\s*using\s+Unity\.AI\.Assistant(?:\.|;)' -ErrorAction Stop
    foreach ($match in $matches) {
        $failures.Add("$($match.Path):$($match.LineNumber): Assistant namespace import is not allowed in Unity.AI.MCP.Editor")
    }
}

if (Test-Path $asmdef) {
    $asmdefMatches = Select-String -Path $asmdef -Pattern '"Unity\.AI\.Assistant\.' -ErrorAction Stop
    foreach ($match in $asmdefMatches) {
        $failures.Add("$($match.Path):$($match.LineNumber): Assistant asmdef reference is not allowed in Unity.AI.MCP.Editor")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "MCP standalone boundary check passed."
