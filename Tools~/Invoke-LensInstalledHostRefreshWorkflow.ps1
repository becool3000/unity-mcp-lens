param(
    [string]$ProjectPath,
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$HostPath,
    [string]$InstallDirectory,
    [string]$RuntimeIdentifier,
    [string[]]$ExpectedTools = @(),
    [string]$OutputPath,
    [int]$WaitForHostExitSeconds = 0,
    [double]$PollIntervalSeconds = 1.0,
    [int]$ProofTimeoutMs = 20000,
    [switch]$Force,
    [switch]$CheckOnly,
    [switch]$ProofOnly,
    [switch]$SkipProof,
    [switch]$SkipListFacade,
    [switch]$ReportOnly
)

$ErrorActionPreference = "Stop"

function Resolve-WorkflowFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function ConvertFrom-JsonOutput {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $start = $Text.IndexOf("{", [System.StringComparison]::Ordinal)
    $end = $Text.LastIndexOf("}", [System.StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -lt $start) {
        return $null
    }

    $json = $Text.Substring($start, $end - $start + 1)
    try {
        return $json | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Invoke-WorkflowJsonScript {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$Arguments = @()
    )

    $powerShell = (Get-Command powershell -ErrorAction Stop).Source
    $allArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments
    $rawOutput = & $powerShell @allArgs 2>&1
    $exitCode = $LASTEXITCODE
    $text = ($rawOutput | Out-String).Trim()
    $parsed = ConvertFrom-JsonOutput -Text $text

    return [pscustomobject]@{
        scriptPath = $ScriptPath
        exitCode = $exitCode
        parsed = $parsed
        rawOutput = $text
    }
}

function Get-RunningHostProcessCount {
    param([AllowNull()]$RefreshResult)

    if ($null -eq $RefreshResult -or $null -eq $RefreshResult.parsed) {
        return 0
    }

    return @($RefreshResult.parsed.runningHostProcesses).Count
}

function Build-RefreshArgs {
    param([switch]$IncludeCheckOnly)

    $args = @("-PackageRoot", (Resolve-WorkflowFullPath -Path $PackageRoot))
    if (-not [string]::IsNullOrWhiteSpace($InstallDirectory)) {
        $args += @("-InstallDirectory", (Resolve-WorkflowFullPath -Path $InstallDirectory))
    }

    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        $args += @("-RuntimeIdentifier", $RuntimeIdentifier)
    }

    if ($Force) {
        $args += "-Force"
    }

    if ($IncludeCheckOnly) {
        $args += "-CheckOnly"
    }

    return $args
}

function Build-ProofArgs {
    param([AllowNull()]$RefreshResult)

    $resolvedProjectPath = $ProjectPath
    if ([string]::IsNullOrWhiteSpace($resolvedProjectPath)) {
        $resolvedProjectPath = $env:UNITY_MCP_PROJECT_PATH
    }

    if ([string]::IsNullOrWhiteSpace($resolvedProjectPath)) {
        $resolvedProjectPath = (Get-Location).Path
    }

    $args = @(
        "-ProjectPath", (Resolve-WorkflowFullPath -Path $resolvedProjectPath),
        "-PackageRoot", (Resolve-WorkflowFullPath -Path $PackageRoot),
        "-TimeoutMs", [string]$ProofTimeoutMs,
        "-ReportOnly"
    )

    if (-not [string]::IsNullOrWhiteSpace($HostPath)) {
        $args += @("-HostPath", (Resolve-WorkflowFullPath -Path $HostPath))
    }
    elseif ($null -ne $RefreshResult -and $null -ne $RefreshResult.parsed -and
        -not [string]::IsNullOrWhiteSpace([string]$RefreshResult.parsed.installedHostPath)) {
        $args += @("-HostPath", (Resolve-WorkflowFullPath -Path ([string]$RefreshResult.parsed.installedHostPath)))
    }

    foreach ($toolName in $ExpectedTools) {
        if (-not [string]::IsNullOrWhiteSpace($toolName)) {
            $args += @("-ExpectedTools", $toolName)
        }
    }

    if ($SkipListFacade) {
        $args += "-SkipListFacade"
    }

    return $args
}

function Write-WorkflowResult {
    param([Parameter(Mandatory = $true)]$Result)

    $json = $Result | ConvertTo-Json -Depth 12
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutputPath = Resolve-WorkflowFullPath -Path $OutputPath
        $outputDirectory = Split-Path -Parent $resolvedOutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
        }

        Set-Content -LiteralPath $resolvedOutputPath -Value $json -Encoding UTF8
    }

    $json
}

$refreshScript = Join-Path $PSScriptRoot "Refresh-LensInstalledHost.ps1"
$proofScript = Join-Path $PSScriptRoot "Test-LensInstalledHostProof.ps1"
if (-not (Test-Path -LiteralPath $refreshScript -PathType Leaf)) {
    throw "Refresh helper was not found at '$refreshScript'."
}

if (-not (Test-Path -LiteralPath $proofScript -PathType Leaf)) {
    throw "Installed-host proof helper was not found at '$proofScript'."
}

$check = $null
$refresh = $null
$proof = $null
$waitRows = @()
$status = "ready"
$message = "Installed Lens host workflow completed."

try {
    if (-not $ProofOnly) {
        $check = Invoke-WorkflowJsonScript -ScriptPath $refreshScript -Arguments (Build-RefreshArgs -IncludeCheckOnly)
        $runningCount = Get-RunningHostProcessCount -RefreshResult $check
        $checkStatus = [string]$check.parsed.status
        $shouldRefresh = $check.parsed.plan.shouldRefresh -eq $true

        if ($shouldRefresh -and -not $CheckOnly -and $runningCount -gt 0 -and $WaitForHostExitSeconds -gt 0) {
            $deadline = (Get-Date).AddSeconds($WaitForHostExitSeconds)
            while ((Get-Date) -lt $deadline -and $runningCount -gt 0) {
                $waitRows += [pscustomobject]@{
                    atUtc = [DateTime]::UtcNow
                    runningHostProcessCount = $runningCount
                    runningHostProcesses = @($check.parsed.runningHostProcesses)
                }
                Start-Sleep -Milliseconds ([Math]::Max(100, [int]($PollIntervalSeconds * 1000)))
                $check = Invoke-WorkflowJsonScript -ScriptPath $refreshScript -Arguments (Build-RefreshArgs -IncludeCheckOnly)
                $runningCount = Get-RunningHostProcessCount -RefreshResult $check
                $checkStatus = [string]$check.parsed.status
            }
        }

        if ($CheckOnly) {
            if ($shouldRefresh) {
                $status = "check_stale"
                $message = "Installed Lens host is stale; check-only mode did not refresh it."
            }
            elseif ($checkStatus -eq "current") {
                $status = "check_current"
                $message = "Installed Lens host is current in check-only mode."
            }
        }
        elseif ($shouldRefresh) {
            $refresh = Invoke-WorkflowJsonScript -ScriptPath $refreshScript -Arguments (Build-RefreshArgs)
            if ($refresh.parsed.success -ne $true) {
                $status = [string]$refresh.parsed.status
                if ([string]::IsNullOrWhiteSpace($status)) {
                    $status = "refresh_failed"
                }
                $message = [string]$refresh.parsed.message
                if ([string]::IsNullOrWhiteSpace($message)) {
                    $message = "Installed Lens host refresh failed."
                }
            }
            else {
                $status = "refreshed"
                $message = [string]$refresh.parsed.message
            }
        }
        elseif ($checkStatus -eq "current") {
            $status = "current"
            $message = "Installed Lens host is already current."
        }
    }

    $refreshForProof = if ($null -ne $refresh -and $null -ne $refresh.parsed) { $refresh } else { $check }
    $shouldRunProof = -not $SkipProof
    if ($shouldRunProof) {
        $proof = Invoke-WorkflowJsonScript -ScriptPath $proofScript -Arguments (Build-ProofArgs -RefreshResult $refreshForProof)
        if ($proof.parsed.success -ne $true -and ($status -eq "current" -or $status -eq "refreshed" -or $status -eq "ready")) {
            $status = [string]$proof.parsed.status
            if ([string]::IsNullOrWhiteSpace($status)) {
                $status = "proof_failed"
            }
            $message = [string]$proof.parsed.message
        }
    }

    $refreshReady = $ProofOnly -or $CheckOnly -or (
        ($null -ne $refresh -and $refresh.parsed.success -eq $true) -or
        ($null -ne $check -and $check.parsed.status -eq "current" -and $check.parsed.success -eq $true)
    )
    $proofReady = $SkipProof -or ($null -ne $proof -and $proof.parsed.success -eq $true)
    $success = -not $CheckOnly -and $refreshReady -and $proofReady
    if ($ProofOnly) {
        $success = $proofReady
    }

    $result = [pscustomobject]@{
        success = $success
        status = $status
        message = $message
        capturedAtUtc = [DateTime]::UtcNow
        packageRoot = Resolve-WorkflowFullPath -Path $PackageRoot
        projectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) { $env:UNITY_MCP_PROJECT_PATH } else { Resolve-WorkflowFullPath -Path $ProjectPath }
        checkOnly = [bool]$CheckOnly
        proofOnly = [bool]$ProofOnly
        skipProof = [bool]$SkipProof
        waitForHostExitSeconds = $WaitForHostExitSeconds
        waitRows = $waitRows
        check = if ($null -ne $check) { $check.parsed } else { $null }
        refresh = if ($null -ne $refresh) { $refresh.parsed } else { $null }
        proof = if ($null -ne $proof) { $proof.parsed } else { $null }
        readyForCodexReconnect = $success
        recommendedNextAction = if ($success) {
            "Reconnect or restart the Codex MCP server process if the current client tool table is stale, then trust the installed host version and raw registry proof."
        }
        elseif ($status -eq "blocked_running_host") {
            "Stop or reconnect clients using the installed host executable, then rerun this workflow. Do not kill Unity automatically."
        }
        elseif ($status -eq "check_stale") {
            "Rerun without -CheckOnly when installed-host clients are stopped, then rerun the raw proof."
        }
        else {
            "Review check/refresh/proof sections and use only safe Lens actions until installed-host proof succeeds."
        }
    }

    Write-WorkflowResult -Result $result
    if ($success -or $ReportOnly) {
        exit 0
    }

    exit 1
}
catch {
    $result = [pscustomobject]@{
        success = $false
        status = "workflow_failed"
        message = $_.Exception.Message
        capturedAtUtc = [DateTime]::UtcNow
        packageRoot = $PackageRoot
        projectPath = $ProjectPath
        check = if ($null -ne $check) { $check.parsed } else { $null }
        refresh = if ($null -ne $refresh) { $refresh.parsed } else { $null }
        proof = if ($null -ne $proof) { $proof.parsed } else { $null }
        readyForCodexReconnect = $false
        recommendedNextAction = "Review the workflow failure and run lower-level Refresh-LensInstalledHost/Test-LensInstalledHostProof helpers directly."
    }
    Write-WorkflowResult -Result $result
    if ($ReportOnly) {
        exit 0
    }

    exit 1
}
