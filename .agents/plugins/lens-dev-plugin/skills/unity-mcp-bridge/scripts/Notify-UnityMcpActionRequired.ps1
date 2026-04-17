param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$Title = "Codex Action Required",
    [string]$Reason = ""
)

$projectName = Split-Path -Leaf $ProjectPath
if ([string]::IsNullOrWhiteSpace($projectName)) {
    $projectName = "this Unity project"
}

$message = "Unity MCP bridge for $projectName needs approval or reconnection."
if (-not [string]::IsNullOrWhiteSpace($Reason)) {
    $message = "$message Reason: $Reason"
}

try {
    if (Get-Module -ListAvailable -Name BurntToast) {
        Import-Module BurntToast -ErrorAction Stop
        New-BurntToastNotification -Text $Title, $message | Out-Null
        exit 0
    }
} catch {
}

try {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms

    $notifyIcon = New-Object System.Windows.Forms.NotifyIcon
    $notifyIcon.Icon = [System.Drawing.SystemIcons]::Warning
    $notifyIcon.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Warning
    $notifyIcon.BalloonTipTitle = $Title
    $notifyIcon.BalloonTipText = $message
    $notifyIcon.Visible = $true
    $notifyIcon.ShowBalloonTip(10000)
    Start-Sleep -Seconds 6
    $notifyIcon.Dispose()
    exit 0
} catch {
}

try {
    $shell = New-Object -ComObject WScript.Shell
    [void]$shell.Popup($message, 10, $Title, 0x30)
    exit 0
} catch {
}

Write-Warning "${Title}: $message"
