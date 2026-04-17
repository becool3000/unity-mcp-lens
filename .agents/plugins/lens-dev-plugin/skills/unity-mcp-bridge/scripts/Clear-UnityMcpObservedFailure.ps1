param(
    [string]$ProjectPath = (Get-Location).Path
)

$resolvedProjectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    (Get-Location).Path
}
else {
    (Resolve-Path -Path $ProjectPath).Path
}

$statePath = Join-Path $resolvedProjectPath "Temp\CodexUnity\observed-direct-mcp-failure.json"
$removed = $false
if (Test-Path -LiteralPath $statePath) {
    Remove-Item -LiteralPath $statePath -Force
    $removed = $true
}

[pscustomobject]@{
    success = $true
    removed = $removed
    path = $statePath
} | ConvertTo-Json -Depth 5
