param(
    [string]$ProjectPath = (Get-Location).Path,
    [string]$Label = "capture",
    [ValidateSet("Hybrid", "UnityOnly", "DesktopOnly")]
    [string]$CapturePathMode = "Hybrid",
    [string]$OutputDir,
    [string]$StateCode,
    [string]$StateCodePath,
    [string]$PreCaptureCode,
    [string]$PreCaptureCodePath,
    [bool]$WaitForEditorIdle = $true,
    [int]$IdleTimeoutSeconds = 60,
    [int]$IdleStablePollCount = 3,
    [double]$IdlePollIntervalSeconds = 0.5,
    [double]$PostIdleDelaySeconds = 1.0,
    [double]$WarmupSeconds = 1.0,
    [bool]$ContinueOnProbeFailure = $true,
    [object]$PausePlaymodeForCapture = $true,
    [int]$StepFramesBeforeCapture = 0,
    [object]$CapturePauseAndStepOnly = $false,
    [int]$UnityCaptureTimeoutSeconds = 45
)

. "$PSScriptRoot\UnityDevCommon.ps1"

$resolvedProjectPath = Resolve-UnityProjectPath -ProjectPath $ProjectPath

function ConvertTo-BoolFlag {
    param(
        [object]$Value,
        [bool]$Default = $false
    )

    if ($null -eq $Value) {
        return $Default
    }

    if ($Value -is [bool]) {
        return [bool]$Value
    }

    if ($Value -is [int]) {
        return $Value -ne 0
    }

    if ($Value -is [System.Management.Automation.SwitchParameter]) {
        return $Value.IsPresent
    }

    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return $Default
    }

    switch ($text.Trim().ToLowerInvariant()) {
        "1" { return $true }
        "true" { return $true }
        "yes" { return $true }
        "on" { return $true }
        "0" { return $false }
        "false" { return $false }
        "no" { return $false }
        "off" { return $false }
        default { return $Default }
    }
}

function Wait-ForCaptureOutputFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$TimeoutMilliseconds = 2000,
        [int]$PollMilliseconds = 200
    )

    $timeoutMilliseconds = [Math]::Max(0, $TimeoutMilliseconds)
    $pollMilliseconds = [Math]::Max(50, $PollMilliseconds)
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    while ($true) {
        if (Test-Path -LiteralPath $Path) {
            try {
                $item = Get-Item -LiteralPath $Path -ErrorAction Stop
                if ($item.Length -gt 0) {
                    return [pscustomobject]@{
                        Exists             = $true
                        Length             = [int64]$item.Length
                        WaitedMilliseconds = [int][Math]::Min($stopwatch.ElapsedMilliseconds, [int64][int]::MaxValue)
                    }
                }
            }
            catch {
            }
        }

        if ($stopwatch.ElapsedMilliseconds -ge $timeoutMilliseconds) {
            break
        }

        Start-Sleep -Milliseconds ([Math]::Min($pollMilliseconds, [Math]::Max(1, $timeoutMilliseconds - [int]$stopwatch.ElapsedMilliseconds)))
    }

    return [pscustomobject]@{
        Exists             = $false
        Length             = 0
        WaitedMilliseconds = [int][Math]::Min($stopwatch.ElapsedMilliseconds, [int64][int]::MaxValue)
    }
}

$PausePlaymodeForCapture = ConvertTo-BoolFlag -Value $PausePlaymodeForCapture -Default $true
$CapturePauseAndStepOnly = ConvertTo-BoolFlag -Value $CapturePauseAndStepOnly -Default $false
$requestedStepFramesForCapture = [Math]::Max(0, [int]$StepFramesBeforeCapture)
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = New-UnityArtifactDirectory -ProjectPath $resolvedProjectPath -Prefix "unity-artifact"
}
else {
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
}

$safeLabel = ($Label -replace '[^A-Za-z0-9._-]', '-')
$statePath = Join-Path $OutputDir "state-$safeLabel.json"
$preCapturePath = Join-Path $OutputDir "precapture-$safeLabel.json"
$probePath = Join-Path $OutputDir "probe-$safeLabel.json"
$unityImagePath = Join-Path $OutputDir "game-$safeLabel.png"
$unityStageImagePath = Join-Path ([System.IO.Path]::GetTempPath()) ("codex-unity-capture-{0}.png" -f ([guid]::NewGuid().ToString("N")))
$desktopImagePath = Join-Path $OutputDir "desktop-$safeLabel.png"

$idleWait = $null
if ($WaitForEditorIdle) {
    $idleWait = Wait-UnityEditorIdle -ProjectPath $resolvedProjectPath -TimeoutSeconds $IdleTimeoutSeconds -StablePollCount $IdleStablePollCount -PollIntervalSeconds $IdlePollIntervalSeconds -PostIdleDelaySeconds $PostIdleDelaySeconds
}

if ($WarmupSeconds -gt 0) {
    Start-Sleep -Seconds $WarmupSeconds
}

$editorState = $null
try {
    $editorState = Get-UnityEditorState -ProjectPath $resolvedProjectPath -TimeoutSeconds 20
}
catch {
    $editorState = [ordered]@{
        success = $false
        error   = $_.Exception.Message
    }
}
Save-JsonFile -Path $statePath -Data $editorState

$preCaptureResult = $null
$preCaptureError = $null
$probeResult = $null
$probeError = $null
$fatalError = $null

if (-not [string]::IsNullOrWhiteSpace($PreCaptureCodePath)) {
    $PreCaptureCode = Get-Content -Path $PreCaptureCodePath -Raw
}

if (-not [string]::IsNullOrWhiteSpace($PreCaptureCode)) {
    try {
        $preCaptureResult = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $PreCaptureCode -Title "Pre-capture setup" -TimeoutSeconds 45
        Save-JsonFile -Path $preCapturePath -Data $preCaptureResult
    }
    catch {
        $preCaptureError = $_.Exception.Message
        if (-not $ContinueOnProbeFailure) {
            $fatalError = $preCaptureError
        }
    }
}

if (-not [string]::IsNullOrWhiteSpace($StateCodePath)) {
    $StateCode = Get-Content -Path $StateCodePath -Raw
}

if (-not [string]::IsNullOrWhiteSpace($StateCode)) {
    try {
        $probeResult = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $StateCode -Title "Playtest probe" -TimeoutSeconds 30
        Save-JsonFile -Path $probePath -Data $probeResult
    }
    catch {
        $probeError = $_.Exception.Message
        if (-not $ContinueOnProbeFailure -and [string]::IsNullOrWhiteSpace($fatalError)) {
            $fatalError = $probeError
        }
    }
}

$captureSource = $null
$captureError = $null
$fallbackUsed = $false
$imagePath = $null
$capturePauseApplied = $false
$captureStepFramesApplied = 0
$unityCaptureFileFlushTimeoutMilliseconds = 2000
$unityCaptureFileFlushPollMilliseconds = 200
$capturePauseSummary = @{
    pauseRequested = $false
    pauseWasApplied = $false
    stepsRequested = $requestedStepFramesForCapture
    stepsApplied = 0
    wasPlaying = $false
    wasPaused = $false
    isPausedAfter = $false
    pauseStepOnly = $false
}

if ($CapturePathMode -ne "DesktopOnly") {
    $unityCapture = $null
    $playModeExecution = $null
    $unityCaptureFile = $null
    $unityCaptureMoveError = $null

    try {
        if (Test-Path -LiteralPath $unityStageImagePath) {
            Remove-Item -LiteralPath $unityStageImagePath -Force -ErrorAction SilentlyContinue
        }

        $escapedImagePath = Escape-CSharpString -Value $unityStageImagePath
        if ($CapturePauseAndStepOnly) {
            $unityCaptureCode = @"
result.Log("Unity pause/step-only prelude complete.");
"@
        }
        else {
            $unityCaptureCode = @"
bool pauseStepOnly = false;

if (pauseStepOnly)
{
    result.Log("Unity pause/step-only prelude complete.");
    return;
}

int width = Mathf.Max(640, Screen.width > 0 ? Screen.width : 0);
int height = Mathf.Max(360, Screen.height > 0 ? Screen.height : 0);
var target = RenderTexture.GetTemporary(width, height, 24);
ScreenCapture.CaptureScreenshotIntoRenderTexture(target);
RenderTexture.active = null;
RenderTexture.active = target;

var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
texture.Apply();
File.WriteAllBytes("$escapedImagePath", texture.EncodeToPNG());

RenderTexture.active = null;
RenderTexture.ReleaseTemporary(target);
Object.DestroyImmediate(texture);

result.Log("Saved Unity capture to {0}", "$escapedImagePath");
"@
        }
        $unityCapture = Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $unityCaptureCode -Title "Unity-aware screenshot" -Usings @("System.IO") -TimeoutSeconds $UnityCaptureTimeoutSeconds -PausePlayMode $PausePlaymodeForCapture -StepFrames $requestedStepFramesForCapture -RestorePauseState $true
    }
    catch {
        $captureError = $_.Exception.Message
    }

    if ($null -ne $unityCapture) {
        $playModeExecution = Get-UnityRunCommandPlayModeExecution -RunCommandResult $unityCapture
        if ($unityCapture.success -ne $true) {
            if ($unityCapture.error) {
                $captureError = $unityCapture.error
            }
            elseif ([string]::IsNullOrWhiteSpace($captureError)) {
                $captureError = "Unity-aware capture did not produce an image file."
            }
        }
    }

    if ($playModeExecution) {
        $capturePauseSummary = $playModeExecution
    }
    else {
        $capturePauseSummary.pauseRequested = ($PausePlaymodeForCapture -or $requestedStepFramesForCapture -gt 0)
        $capturePauseSummary.stepsRequested = $requestedStepFramesForCapture
        $capturePauseSummary.wasPlaying = if ($editorState -and $editorState.success -eq $true) { $editorState.data.IsPlaying -eq $true } else { $false }
        $capturePauseSummary.wasPaused = if ($editorState -and $editorState.success -eq $true) { $editorState.data.IsPaused -eq $true } else { $false }
        $capturePauseSummary.pauseWasApplied = (($PausePlaymodeForCapture -or $requestedStepFramesForCapture -gt 0) -and $capturePauseSummary.wasPlaying)
        $capturePauseSummary.isPausedAfter = $capturePauseSummary.wasPaused
    }

    $capturePauseSummary.pauseRequested = ($PausePlaymodeForCapture -or $requestedStepFramesForCapture -gt 0)
    $capturePauseSummary.pauseStepOnly = $CapturePauseAndStepOnly

    if ($CapturePauseAndStepOnly) {
        if ($unityCapture -and $unityCapture.success -eq $true) {
            $captureSource = "UnityPauseAndStepOnly"
            $capturePauseApplied = $capturePauseSummary.pauseWasApplied
            $captureStepFramesApplied = $capturePauseSummary.stepsApplied
            $captureError = $null
        }
    }
    else {
        $unityCaptureFile = Wait-ForCaptureOutputFile -Path $unityStageImagePath -TimeoutMilliseconds $unityCaptureFileFlushTimeoutMilliseconds -PollMilliseconds $unityCaptureFileFlushPollMilliseconds
        if ($unityCaptureFile.Exists) {
            try {
                Move-Item -LiteralPath $unityStageImagePath -Destination $unityImagePath -Force
                $captureSource = "UnityAware"
                $imagePath = $unityImagePath
                $capturePauseApplied = $capturePauseSummary.pauseWasApplied
                $captureStepFramesApplied = $capturePauseSummary.stepsApplied
                $captureError = $null
            }
            catch {
                $unityCaptureMoveError = $_.Exception.Message
                try {
                    Copy-Item -LiteralPath $unityStageImagePath -Destination $unityImagePath -Force
                    Remove-Item -LiteralPath $unityStageImagePath -Force -ErrorAction SilentlyContinue
                    $captureSource = "UnityAware"
                    $imagePath = $unityImagePath
                    $capturePauseApplied = $capturePauseSummary.pauseWasApplied
                    $captureStepFramesApplied = $capturePauseSummary.stepsApplied
                    $captureError = $null
                }
                catch {
                    $captureError = "Unity-aware capture created the staged image but failed to move it into the artifact directory: $unityCaptureMoveError"
                }
            }
        }
        elseif ([string]::IsNullOrWhiteSpace($captureError)) {
            $captureError = "Unity-aware capture did not produce an image file within the flush window."
        }
    }

    if (Test-Path -LiteralPath $unityStageImagePath) {
        Remove-Item -LiteralPath $unityStageImagePath -Force -ErrorAction SilentlyContinue
    }
}

if (-not $captureSource -and -not $CapturePauseAndStepOnly -and $CapturePathMode -ne "UnityOnly") {
    if ($capturePauseSummary.pauseWasApplied -or (($PausePlaymodeForCapture -or $requestedStepFramesForCapture -gt 0) -and $capturePauseSummary.wasPlaying)) {
        try {
            $postCaptureEditorState = Get-UnityEditorState -ProjectPath $resolvedProjectPath -TimeoutSeconds 20
            if ($postCaptureEditorState.success -eq $true -and $postCaptureEditorState.data.IsPlaying -eq $true -and $postCaptureEditorState.data.IsPaused -eq $true) {
                $resumeCode = @"
EditorApplication.isPaused = false;
result.Log("Unity-aware capture cleanup ensured play mode is not left paused.");
"@
                Invoke-UnityRunCommandObject -ProjectPath $resolvedProjectPath -Code $resumeCode -Title "Unity-aware capture pause cleanup" -TimeoutSeconds 20 | Out-Null
                $capturePauseSummary.isPausedAfter = $false
            }
        }
        catch {
        }
    }

    $screenshotScript = Join-Path (Get-ScreenshotSkillPath) "scripts\take_screenshot.ps1"
    & powershell -ExecutionPolicy Bypass -File $screenshotScript -Path $desktopImagePath -ActiveWindow | Out-Null
    if (Test-Path -Path $desktopImagePath) {
        $captureSource = "DesktopFallback"
        $imagePath = $desktopImagePath
        $fallbackUsed = $true
    }
    elseif (-not $captureError) {
        $captureError = "Desktop fallback did not produce an image file."
    }
}

$manifestPath = Join-Path $OutputDir "artifact-$safeLabel.json"
$manifest = [ordered]@{
    Label                = $safeLabel
    OutputDir            = $OutputDir
    StatePath            = $statePath
    PreCapturePath       = if ($preCaptureResult) { $preCapturePath } else { $null }
    ProbePath            = if ($probeResult) { $probePath } else { $null }
    ImagePath            = $imagePath
    CaptureSource        = $captureSource
    CapturePathMode      = $CapturePathMode
    PausePlaymodeForCapture = $PausePlaymodeForCapture
    CapturePauseApplied = $capturePauseSummary.pauseWasApplied
    CapturePauseWasRequested = $capturePauseSummary.pauseRequested
    CaptureStepFramesRequested = $capturePauseSummary.stepsRequested
    CaptureStepFramesApplied = $capturePauseSummary.stepsApplied
    CapturePauseAndStepOnly = $CapturePauseAndStepOnly
    CaptureWasPausedBefore = $capturePauseSummary.wasPaused
    CapturePlayModeWasRunning = $capturePauseSummary.wasPlaying
    CaptureIsPausedAfter = $capturePauseSummary.isPausedAfter
    CaptureFallbackUsed   = $fallbackUsed
    UnityCaptureTimeoutSeconds = $UnityCaptureTimeoutSeconds
    CaptureError         = $captureError
    ProbeError           = $probeError
    PreCaptureError      = $preCaptureError
    FatalError           = $fatalError
    ContinueOnProbeError = $ContinueOnProbeFailure
    WaitForEditorIdle    = $WaitForEditorIdle
    WarmupSeconds        = $WarmupSeconds
    EditorIdle           = $idleWait
    Timestamp            = (Get-Date).ToString("o")
}

Save-JsonFile -Path $manifestPath -Data $manifest
$manifest | ConvertTo-Json -Depth 30

if (($imagePath -or $captureSource -or $CapturePauseAndStepOnly) -and [string]::IsNullOrWhiteSpace($fatalError)) {
    exit 0
}

exit 1
