param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$Reason = "transport_closed",
    [int]$TtlSeconds = 1800
)

$resolvedProjectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    (Get-Location).Path
}
else {
    (Resolve-Path -Path $ProjectPath).Path
}

$stateDir = Join-Path $resolvedProjectPath "Temp\CodexUnity"
$statePath = Join-Path $stateDir "observed-direct-mcp-failure.json"
$createdAt = [DateTimeOffset]::UtcNow
$ttlSecondsValue = [Math]::Max(30, $TtlSeconds)
$expiresAt = $createdAt.AddSeconds($ttlSecondsValue)

$payload = [ordered]@{
    ProjectPath   = $resolvedProjectPath
    Reason        = $Reason
    CreatedAtUtc  = $createdAt.ToString("O")
    ExpiresAtUtc  = $expiresAt.ToString("O")
    TtlSeconds    = $ttlSecondsValue
}

New-Item -ItemType Directory -Path $stateDir -Force | Out-Null
$payload | ConvertTo-Json -Depth 5 | Set-Content -Path $statePath -Encoding utf8
$payload | Add-Member -NotePropertyName Path -NotePropertyValue $statePath
$payload | ConvertTo-Json -Depth 5
