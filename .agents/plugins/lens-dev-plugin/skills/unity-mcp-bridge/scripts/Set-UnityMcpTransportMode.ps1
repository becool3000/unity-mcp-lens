param(
    [ValidateSet("LensOnly")]
    [string]$Mode = "LensOnly"
)

$homePath = if ($env:USERPROFILE) { $env:USERPROFILE } else { $HOME }
$configPath = Join-Path (Join-Path $homePath ".codex") "config.toml"
$settingsPath = Join-Path (Join-Path $homePath ".codex") "unity-mcp-settings.json"
$lensDir = Join-Path (Join-Path $homePath ".unity") "unity-mcp-lens"

if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
    $lensBinary = "unity_mcp_lens_win.exe"
}
elseif ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
    $arch = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) { "arm64" } else { "x64" }
    $lensBinary = "unity_mcp_lens_mac_$arch"
}
else {
    $lensBinary = "unity_mcp_lens_linux"
}

$lensPath = Join-Path $lensDir $lensBinary

if (-not (Test-Path -LiteralPath $configPath)) {
    throw "Codex config not found at $configPath"
}

$configText = Get-Content -LiteralPath $configPath -Raw

$section = @"
[mcp_servers."unity-mcp"]
command = '$lensPath'
enabled = true
"@

$settings = [ordered]@{
    allowManualWrapper       = $false
    allowCachedToolsFallback = $false
    directRelayExperimental  = $false
    eagerConnectOnInitialize = $false
    toolsCacheTtlMs          = 300000
    reloadWaitTimeoutMs      = 5000
    reloadPollIntervalMs     = 400
}

$pattern = '(?ms)^\[mcp_servers\."unity-mcp"\]\r?\n(?:.*?(?:\r?\n|$))*?(?=^\[|\z)'
if ([regex]::IsMatch($configText, $pattern)) {
    $updatedConfig = [regex]::Replace($configText, $pattern, ($section.TrimEnd() + "`r`n`r`n"))
}
else {
    $updatedConfig = $configText.TrimEnd() + "`r`n`r`n" + $section.TrimEnd() + "`r`n"
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($configPath, $updatedConfig, $utf8NoBom)
[System.IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json), $utf8NoBom)

[pscustomobject]@{
    Mode = $Mode
    ConfigPath = $configPath
    SettingsPath = $settingsPath
    RestartRequired = $true
} | ConvertTo-Json -Depth 4
