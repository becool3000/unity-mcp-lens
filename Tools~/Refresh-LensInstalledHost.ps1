param(
    [string]$PackageRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$LensAppRoot,
    [string]$InstallDirectory,
    [string]$RuntimeIdentifier,
    [switch]$Force,
    [switch]$CheckOnly
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [System.IO.Path]::GetFullPath($Path)
}

function Get-LensRuntimeIdentifier {
    if (-not [string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
        return $RuntimeIdentifier
    }

    $architecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture
    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "win-arm64"
        }

        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::X86) {
            return "win-x86"
        }

        return "win-x64"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::OSX)) {
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "osx-arm64"
        }

        return "osx-x64"
    }

    if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            return "linux-arm64"
        }

        if ($architecture -eq [System.Runtime.InteropServices.Architecture]::X86) {
            return "linux-x86"
        }

        return "linux-x64"
    }

    throw "Unsupported platform for Unity MCP Lens host refresh."
}

function Get-InstalledHostFileName {
    param([Parameter(Mandatory = $true)][string]$RuntimeId)

    if ($RuntimeId.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "unity_mcp_lens_win.exe"
    }

    if ($RuntimeId.StartsWith("osx-arm64", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "unity_mcp_lens_mac_arm64"
    }

    if ($RuntimeId.StartsWith("osx-", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "unity_mcp_lens_mac_x64"
    }

    return "unity_mcp_lens_linux"
}

function Get-PublishedDefaultHostFileName {
    param([Parameter(Mandatory = $true)][string]$RuntimeId)

    if ($RuntimeId.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
        return "UnityMcpLens.exe"
    }

    return "UnityMcpLens"
}

function Read-JsonVersion {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return "not installed"
    }

    try {
        $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        $version = [string]$json.version
        if ([string]::IsNullOrWhiteSpace($version)) {
            return "unknown"
        }

        return $version
    }
    catch {
        return "unknown"
    }
}

function Get-NewestWriteUtc {
    param([Parameter(Mandatory = $true)][string]$Directory)

    if ([string]::IsNullOrWhiteSpace($Directory) -or -not (Test-Path -LiteralPath $Directory -PathType Container)) {
        return [datetime]::MinValue
    }

    $newest = [datetime]::MinValue
    Get-ChildItem -LiteralPath $Directory -Recurse -File -ErrorAction Stop |
        Where-Object {
            $normalized = $_.FullName.Replace('\', '/')
            $normalized.IndexOf("/bin/", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $normalized.IndexOf("/obj/", [System.StringComparison]::OrdinalIgnoreCase) -lt 0
        } |
        ForEach-Object {
            if ($_.LastWriteTimeUtc -gt $newest) {
                $newest = $_.LastWriteTimeUtc
            }
        }

    return $newest
}

function Get-RunningHostProcesses {
    param([Parameter(Mandatory = $true)][string]$InstalledHostPath)

    $resolvedHostPath = Resolve-FullPath -Path $InstalledHostPath
    $rows = @()
    try {
        Get-CimInstance Win32_Process -ErrorAction Stop |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
                [string]::Equals((Resolve-FullPath -Path $_.ExecutablePath), $resolvedHostPath, [System.StringComparison]::OrdinalIgnoreCase)
            } |
            ForEach-Object {
                $rows += [pscustomobject]@{
                    processId = [int]$_.ProcessId
                    parentProcessId = [int]$_.ParentProcessId
                    commandLine = [string]$_.CommandLine
                }
            }
    }
    catch {
        Get-Process -ErrorAction SilentlyContinue |
            ForEach-Object {
                try {
                    if (-not [string]::IsNullOrWhiteSpace($_.Path) -and
                        [string]::Equals((Resolve-FullPath -Path $_.Path), $resolvedHostPath, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $rows += [pscustomobject]@{
                            processId = [int]$_.Id
                            parentProcessId = $null
                            commandLine = [string]$_.Path
                        }
                    }
                }
                catch {
                }
            }
    }

    return @($rows)
}

function Resolve-DotNetExecutable {
    $dotnetRoot = [Environment]::GetEnvironmentVariable("DOTNET_ROOT")
    if (-not [string]::IsNullOrWhiteSpace($dotnetRoot)) {
        $candidate = Join-Path $dotnetRoot "dotnet.exe"
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    return "dotnet.exe"
}

function Publish-LensHost {
    param(
        [Parameter(Mandatory = $true)][string]$AppRoot,
        [Parameter(Mandatory = $true)][string]$RuntimeId,
        [Parameter(Mandatory = $true)][string]$StagingDirectory,
        [Parameter(Mandatory = $true)][string]$HostFileName,
        [Parameter(Mandatory = $true)][bool]$PreferSourcePublish
    )

    $prebuiltDirectory = Join-Path (Join-Path $AppRoot "prebuilt") $RuntimeId
    $prebuiltHost = Join-Path $prebuiltDirectory $HostFileName
    if (-not $PreferSourcePublish -and (Test-Path -LiteralPath $prebuiltHost -PathType Leaf)) {
        New-Item -ItemType Directory -Path $StagingDirectory -Force | Out-Null
        Copy-Item -LiteralPath $prebuiltHost -Destination (Join-Path $StagingDirectory $HostFileName) -Force
        return [pscustomobject]@{
            technique = "prebuilt"
            prebuiltHost = $prebuiltHost
            projectFile = $null
            publishOutputPreview = @()
        }
    }

    $projectFile = Join-Path (Join-Path (Join-Path $AppRoot "src") "UnityMcpLens") "UnityMcpLens.csproj"
    if (-not (Test-Path -LiteralPath $projectFile -PathType Leaf)) {
        throw "Unity MCP Lens host project file was not found at '$projectFile'."
    }

    New-Item -ItemType Directory -Path $StagingDirectory -Force | Out-Null
    $dotnet = Resolve-DotNetExecutable
    $publishArgs = @(
        "publish",
        $projectFile,
        "-c",
        "Release",
        "-r",
        $RuntimeId,
        "--self-contained",
        "true",
        "/p:PublishSingleFile=true",
        "/p:DebugType=None",
        "/p:DebugSymbols=false",
        "-o",
        $StagingDirectory
    )

    Push-Location (Split-Path -Parent $projectFile)
    try {
        $publishOutput = & $dotnet @publishArgs 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        $tail = ($publishOutput | Select-Object -Last 80) -join [Environment]::NewLine
        throw "dotnet publish failed for Unity MCP Lens host with exit $exitCode.`n$tail"
    }

    $publishedDefault = Join-Path $StagingDirectory (Get-PublishedDefaultHostFileName -RuntimeId $RuntimeId)
    $expected = Join-Path $StagingDirectory $HostFileName
    if ((Test-Path -LiteralPath $publishedDefault -PathType Leaf) -and
        -not [string]::Equals($publishedDefault, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        Copy-Item -LiteralPath $publishedDefault -Destination $expected -Force
    }

    if (-not (Test-Path -LiteralPath $expected -PathType Leaf)) {
        throw "Published host binary was not found at '$expected'."
    }

    return [pscustomobject]@{
        technique = "dotnet_publish"
        prebuiltHost = $null
        projectFile = $projectFile
        publishOutputPreview = @($publishOutput | Select-Object -Last 20)
    }
}

function Copy-LensHostInstall {
    param(
        [Parameter(Mandatory = $true)][string]$StagedHostPath,
        [Parameter(Mandatory = $true)][string]$InstalledHostPath,
        [Parameter(Mandatory = $true)][string]$MetadataSourcePath,
        [Parameter(Mandatory = $true)][string]$InstalledMetadataPath
    )

    $installDir = Split-Path -Parent $InstalledHostPath
    New-Item -ItemType Directory -Path $installDir -Force | Out-Null
    [System.IO.File]::Copy($StagedHostPath, $InstalledHostPath, $true)
    [System.IO.File]::Copy($MetadataSourcePath, $InstalledMetadataPath, $true)

    if (-not [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)) {
        & chmod +x $InstalledHostPath 2>$null
    }
}

function Build-RefreshPlan {
    param(
        [Parameter(Mandatory = $true)][string]$AppRoot,
        [Parameter(Mandatory = $true)][string]$RuntimeId,
        [Parameter(Mandatory = $true)][string]$InstalledHostPath,
        [Parameter(Mandatory = $true)][string]$InstalledMetadataPath
    )

    $metadataSourcePath = Join-Path $AppRoot "unity-mcp-lens.json"
    if (-not (Test-Path -LiteralPath $metadataSourcePath -PathType Leaf)) {
        throw "Unity MCP Lens metadata was not found at '$metadataSourcePath'."
    }

    $repoVersion = Read-JsonVersion -Path $metadataSourcePath
    $installedVersion = Read-JsonVersion -Path $InstalledMetadataPath
    $prebuiltDirectory = Join-Path (Join-Path $AppRoot "prebuilt") $RuntimeId
    $sourceDirectory = Join-Path (Join-Path $AppRoot "src") "UnityMcpLens"
    $prebuiltNewest = Get-NewestWriteUtc -Directory $prebuiltDirectory
    $sourceNewest = Get-NewestWriteUtc -Directory $sourceDirectory
    $installedWriteUtc = if (Test-Path -LiteralPath $InstalledHostPath -PathType Leaf) {
        (Get-Item -LiteralPath $InstalledHostPath).LastWriteTimeUtc
    }
    else {
        [datetime]::MinValue
    }

    $sourceNewerThanPrebuilt = $sourceNewest -gt $prebuiltNewest.AddSeconds(1)
    $newestPublishInputUtc = if ($sourceNewerThanPrebuilt -or $prebuiltNewest -eq [datetime]::MinValue) {
        $sourceNewest
    }
    else {
        $prebuiltNewest
    }

    $reasons = New-Object System.Collections.Generic.List[string]
    if ($Force) {
        $reasons.Add("force")
    }

    if (-not (Test-Path -LiteralPath $InstalledHostPath -PathType Leaf)) {
        $reasons.Add("installed_host_missing")
    }

    if (-not [string]::Equals($repoVersion, $installedVersion, [System.StringComparison]::OrdinalIgnoreCase)) {
        $reasons.Add("version_mismatch")
    }

    if ($newestPublishInputUtc -gt $installedWriteUtc.AddSeconds(1)) {
        $reasons.Add("source_or_prebuilt_newer_than_installed_host")
    }

    return [pscustomobject]@{
        shouldRefresh = $reasons.Count -gt 0
        reasons = @($reasons)
        metadataSourcePath = $metadataSourcePath
        repoVersion = $repoVersion
        installedVersion = $installedVersion
        sourceDirectory = $sourceDirectory
        prebuiltDirectory = $prebuiltDirectory
        sourceNewestWriteUtc = $sourceNewest
        prebuiltNewestWriteUtc = $prebuiltNewest
        installedHostWriteUtc = $installedWriteUtc
        sourceNewerThanPrebuilt = $sourceNewerThanPrebuilt
        preferSourcePublish = [bool]($sourceNewerThanPrebuilt -or $prebuiltNewest -eq [datetime]::MinValue)
    }
}

$stagingDirectory = $null
try {
    $resolvedPackageRoot = Resolve-FullPath -Path $PackageRoot
    if ([string]::IsNullOrWhiteSpace($LensAppRoot)) {
        $LensAppRoot = Join-Path $resolvedPackageRoot "UnityMcpLensApp~"
    }

    $resolvedLensAppRoot = Resolve-FullPath -Path $LensAppRoot
    if (-not (Test-Path -LiteralPath $resolvedLensAppRoot -PathType Container)) {
        throw "UnityMcpLensApp~ root was not found at '$resolvedLensAppRoot'."
    }

    $runtimeId = Get-LensRuntimeIdentifier
    $hostFileName = Get-InstalledHostFileName -RuntimeId $runtimeId
    if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
        $InstallDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) ".unity\unity-mcp-lens"
    }

    $resolvedInstallDirectory = Resolve-FullPath -Path $InstallDirectory
    $installedHostPath = Join-Path $resolvedInstallDirectory $hostFileName
    $installedMetadataPath = Join-Path $resolvedInstallDirectory "unity-mcp-lens.json"
    $plan = Build-RefreshPlan -AppRoot $resolvedLensAppRoot -RuntimeId $runtimeId -InstalledHostPath $installedHostPath -InstalledMetadataPath $installedMetadataPath
    $runningHostProcesses = @(Get-RunningHostProcesses -InstalledHostPath $installedHostPath)
    $publish = $null

    if ($CheckOnly -or -not $plan.shouldRefresh) {
        $status = if ($plan.shouldRefresh) { "stale" } else { "current" }
        $result = [pscustomobject]@{
            success = $true
            status = $status
            message = if ($plan.shouldRefresh) { "Installed Lens host is stale; rerun without -CheckOnly to refresh it." } else { "Installed Lens host is current." }
            checkOnly = [bool]$CheckOnly
            packageRoot = $resolvedPackageRoot
            lensAppRoot = $resolvedLensAppRoot
            runtimeIdentifier = $runtimeId
            installDirectory = $resolvedInstallDirectory
            installedHostPath = $installedHostPath
            installedMetadataPath = $installedMetadataPath
            runningHostProcesses = $runningHostProcesses
            plan = $plan
        }
        $result | ConvertTo-Json -Depth 8
        exit 0
    }

    if ($runningHostProcesses.Count -gt 0) {
        $result = [pscustomobject]@{
            success = $false
            status = "blocked_running_host"
            message = "Installed Lens host is running from the stable install path. Stop/reconnect the Codex MCP host or close clients using it, then rerun this helper."
            checkOnly = $false
            packageRoot = $resolvedPackageRoot
            lensAppRoot = $resolvedLensAppRoot
            runtimeIdentifier = $runtimeId
            installDirectory = $resolvedInstallDirectory
            installedHostPath = $installedHostPath
            installedMetadataPath = $installedMetadataPath
            runningHostProcesses = $runningHostProcesses
            plan = $plan
        }
        $result | ConvertTo-Json -Depth 8
        exit 2
    }

    $stagingDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("unity-mcp-lens-installed-host-refresh-" + [Guid]::NewGuid().ToString("N"))
    $publish = Publish-LensHost -AppRoot $resolvedLensAppRoot -RuntimeId $runtimeId -StagingDirectory $stagingDirectory -HostFileName $hostFileName -PreferSourcePublish $plan.preferSourcePublish
    $stagedHostPath = Join-Path $stagingDirectory $hostFileName
    Copy-LensHostInstall -StagedHostPath $stagedHostPath -InstalledHostPath $installedHostPath -MetadataSourcePath $plan.metadataSourcePath -InstalledMetadataPath $installedMetadataPath

    $installedVersionAfter = Read-JsonVersion -Path $installedMetadataPath
    $installedHostAfter = Get-Item -LiteralPath $installedHostPath
    $versionMatches = [string]::Equals($plan.repoVersion, $installedVersionAfter, [System.StringComparison]::OrdinalIgnoreCase)
    $result = [pscustomobject]@{
        success = $versionMatches
        status = if ($versionMatches) { "refreshed" } else { "verification_failed" }
        message = if ($versionMatches) { "Installed Lens host refreshed and metadata matches repo version." } else { "Installed Lens host refresh completed, but installed metadata does not match repo version." }
        checkOnly = $false
        packageRoot = $resolvedPackageRoot
        lensAppRoot = $resolvedLensAppRoot
        runtimeIdentifier = $runtimeId
        installDirectory = $resolvedInstallDirectory
        installedHostPath = $installedHostPath
        installedMetadataPath = $installedMetadataPath
        runningHostProcesses = $runningHostProcesses
        plan = $plan
        publish = $publish
        installedAfter = [pscustomobject]@{
            version = $installedVersionAfter
            length = $installedHostAfter.Length
            lastWriteUtc = $installedHostAfter.LastWriteTimeUtc
        }
    }

    $result | ConvertTo-Json -Depth 8
    if (-not $versionMatches) {
        exit 1
    }
}
catch {
    $result = [pscustomobject]@{
        success = $false
        status = "failed"
        message = $_.Exception.Message
        packageRoot = $PackageRoot
        lensAppRoot = $LensAppRoot
        runtimeIdentifier = $RuntimeIdentifier
        installDirectory = $InstallDirectory
    }
    $result | ConvertTo-Json -Depth 8
    exit 1
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($stagingDirectory) -and (Test-Path -LiteralPath $stagingDirectory -PathType Container)) {
        $fullStaging = Resolve-FullPath -Path $stagingDirectory
        $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        if ($fullStaging.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $fullStaging -Recurse -Force
        }
    }
}
