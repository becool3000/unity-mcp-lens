param(
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

$mcpRoot = Join-Path $PackageRoot "Editor/Lens"
$asmdef = Join-Path $mcpRoot "Becool.UnityMcpLens.Editor.asmdef"
$failures = New-Object System.Collections.Generic.List[string]

if (-not (Test-Path $mcpRoot)) {
    throw "MCP editor module not found: $mcpRoot"
}

Get-ChildItem -Path $mcpRoot -Recurse -Filter "*.cs" | ForEach-Object {
    $matches = Select-String -Path $_.FullName -Pattern '^\s*using\s+Unity\.AI\.Assistant(?:\.|;)' -ErrorAction Stop
    foreach ($match in $matches) {
        $failures.Add("$($match.Path):$($match.LineNumber): Assistant namespace import is not allowed in Becool.UnityMcpLens.Editor")
    }
}

if (Test-Path $asmdef) {
    $asmdefMatches = Select-String -Path $asmdef -Pattern '"Unity\.AI\.Assistant\.' -ErrorAction Stop
    foreach ($match in $asmdefMatches) {
        $failures.Add("$($match.Path):$($match.LineNumber): Assistant asmdef reference is not allowed in Becool.UnityMcpLens.Editor")
    }
}

$packageJsonPath = Join-Path $PackageRoot "package.json"
if (Test-Path $packageJsonPath) {
    $packageJson = Get-Content -Path $packageJsonPath -Raw | ConvertFrom-Json
    if ($packageJson.name -ne "com.becool3000.unity-mcp-lens") {
        $failures.Add("${packageJsonPath}: package name must be com.becool3000.unity-mcp-lens")
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Host "MCP standalone boundary check passed."
