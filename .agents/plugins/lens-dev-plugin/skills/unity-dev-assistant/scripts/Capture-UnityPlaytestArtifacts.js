#!/usr/bin/env node

const fs = require("fs");
const os = require("os");
const path = require("path");
const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const label = common.getArgString(args, ["Label"], "capture");
  const capturePathMode = common.getArgString(args, ["CapturePathMode"], "Hybrid");
  const safeLabel = label.replace(/[^A-Za-z0-9._-]/g, "-");
  let outputDir = common.getArgString(args, ["OutputDir"], "");
  if (!outputDir) {
    outputDir = common.newUnityArtifactDirectory(projectPath, "unity-artifact");
  }
  common.ensureDir(outputDir);

  const statePath = path.join(outputDir, `state-${safeLabel}.json`);
  const preCapturePath = path.join(outputDir, `precapture-${safeLabel}.json`);
  const probePath = path.join(outputDir, `probe-${safeLabel}.json`);
  const unityImagePath = path.join(outputDir, `game-${safeLabel}.png`);
  const unityStageImagePath = path.join(os.tmpdir(), `codex-unity-capture-${Date.now()}-${process.pid}.png`);
  const desktopImagePath = path.join(outputDir, `desktop-${safeLabel}.png`);

  let idleWait = null;
  if (common.getArgBool(args, ["WaitForEditorIdle"], true)) {
    idleWait = await common.waitUnityEditorIdle(projectPath, {
      timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
      stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
      pollIntervalSeconds: common.getArgNumber(args, ["IdlePollIntervalSeconds"], 0.5),
      postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
    });
  }

  const warmupSeconds = common.getArgNumber(args, ["WarmupSeconds"], 1.0);
  if (warmupSeconds > 0) {
    await common.sleep(warmupSeconds * 1000);
  }

  let editorState;
  try {
    editorState = await common.getUnityEditorState(projectPath, common.getArgNumber(args, ["EditorStateTimeoutSeconds"], 90));
  } catch (error) {
    editorState = { success: false, error: error.message };
  }
  common.writeJsonFile(statePath, editorState);

  let preCaptureCode = common.getArgString(args, ["PreCaptureCode"], "");
  const preCaptureCodePath = common.getArgString(args, ["PreCaptureCodePath"], "");
  if (preCaptureCodePath) {
    preCaptureCode = fs.readFileSync(preCaptureCodePath, "utf8");
  }
  let stateCode = common.getArgString(args, ["StateCode"], "");
  const stateCodePath = common.getArgString(args, ["StateCodePath"], "");
  if (stateCodePath) {
    stateCode = fs.readFileSync(stateCodePath, "utf8");
  }

  const continueOnProbeFailure = common.getArgBool(args, ["ContinueOnProbeFailure"], true);
  let preCaptureResult = null;
  let preCaptureError = null;
  let probeResult = null;
  let probeError = null;
  let fatalError = null;

  if (preCaptureCode.trim()) {
    try {
      preCaptureResult = await common.invokeUnityRunCommandObject(projectPath, {
        code: preCaptureCode,
        title: "Pre-capture setup",
        timeoutSeconds: 45,
      });
      common.writeJsonFile(preCapturePath, preCaptureResult);
    } catch (error) {
      preCaptureError = error.message;
      if (!continueOnProbeFailure) {
        fatalError = preCaptureError;
      }
    }
  }

  if (stateCode.trim()) {
    try {
      probeResult = await common.invokeUnityRunCommandObject(projectPath, {
        code: stateCode,
        title: "Playtest probe",
        timeoutSeconds: 30,
      });
      common.writeJsonFile(probePath, probeResult);
    } catch (error) {
      probeError = error.message;
      if (!continueOnProbeFailure && !fatalError) {
        fatalError = probeError;
      }
    }
  }

  let captureSource = null;
  let captureError = null;
  let fallbackUsed = false;
  let imagePath = null;
  let capturePauseSummary = {
    pauseRequested: false,
    pauseWasApplied: false,
    stepsRequested: Math.max(0, common.getArgNumber(args, ["StepFramesBeforeCapture"], 0)),
    stepsApplied: 0,
    wasPlaying: false,
    wasPaused: false,
    isPausedAfter: false,
    pauseStepOnly: false,
  };

  const pausePlaymodeForCapture = common.getArgBool(args, ["PausePlaymodeForCapture"], true);
  const capturePauseAndStepOnly = common.getArgBool(args, ["CapturePauseAndStepOnly"], false);
  const requestedStepFrames = Math.max(0, common.getArgNumber(args, ["StepFramesBeforeCapture"], 0));
  const unityCaptureTimeoutSeconds = common.getArgNumber(args, ["UnityCaptureTimeoutSeconds"], 45);

  if (capturePathMode !== "DesktopOnly" && !capturePauseAndStepOnly && !fatalError) {
    const editorData = editorState?.data || editorState?.Data || {};
    const editorWasPlaying = editorData.IsPlaying === true || editorData.isPlaying === true;
    const editorWasPaused = editorData.IsPaused === true || editorData.isPaused === true;
    const nativeCaptureDir = "Temp/LensCaptures";
    const copyNativeImage = (sourcePath) => {
      if (!sourcePath || !fs.existsSync(sourcePath)) {
        return false;
      }
      common.ensureDir(path.dirname(unityImagePath));
      fs.copyFileSync(sourcePath, unityImagePath);
      imagePath = unityImagePath;
      fallbackUsed = false;
      return true;
    };

    if (editorWasPlaying) {
      try {
        const gameViewResponse = await common.invokeUnityMcpToolJson(
          projectPath,
          "Unity.UI.CaptureGameView",
          {
            OutputPath: `${nativeCaptureDir}/${safeLabel}-game-view.png`,
            WarmupMs: Math.max(0, Math.round(warmupSeconds * 1000)),
            PausePlayMode: pausePlaymodeForCapture,
            StepFrames: requestedStepFrames,
            WaitForFileTimeoutMs: Math.max(4000, unityCaptureTimeoutSeconds * 1000),
          },
          { timeoutSeconds: Math.max(30, unityCaptureTimeoutSeconds) }
        );
        const gameViewResult = common.getToolObject(gameViewResponse);
        if (gameViewResult?.success === true && copyNativeImage(gameViewResult.data?.absoluteOutputPath || gameViewResult.Data?.absoluteOutputPath)) {
          captureSource = "GameView";
          captureError = null;
          capturePauseSummary = {
            pauseRequested: pausePlaymodeForCapture || requestedStepFrames > 0,
            pauseWasApplied: gameViewResult.data?.pauseApplied === true || gameViewResult.Data?.pauseApplied === true,
            stepsRequested: requestedStepFrames,
            stepsApplied: Number(gameViewResult.data?.stepFrames ?? gameViewResult.Data?.stepFrames ?? 0),
            wasPlaying: gameViewResult.data?.wasPlaying === true || gameViewResult.Data?.wasPlaying === true,
            wasPaused: gameViewResult.data?.wasPaused === true || gameViewResult.Data?.wasPaused === true,
            isPausedAfter: gameViewResult.data?.wasPaused === true || gameViewResult.Data?.wasPaused === true,
            pauseStepOnly: false,
          };
        } else {
          captureError = gameViewResult?.error || gameViewResult?.message || "Game-view capture did not produce an image file.";
        }
      } catch (error) {
        captureError = error.message;
      }
    }

    if (!captureSource) {
      try {
        const cameraResponse = await common.invokeUnityMcpToolJson(
          projectPath,
          "Unity.Scene.CaptureView",
          {
            Mode: "camera",
            OutputPath: `${nativeCaptureDir}/${safeLabel}-camera.png`,
            Width: common.getArgNumber(args, ["CameraCaptureWidth"], 1280),
            Height: common.getArgNumber(args, ["CameraCaptureHeight"], 720),
          },
          { timeoutSeconds: Math.max(30, unityCaptureTimeoutSeconds) }
        );
        const cameraResult = common.getToolObject(cameraResponse);
        const capture = cameraResult?.data?.captures?.[0] || cameraResult?.Data?.captures?.[0];
        const cameraPath = capture?.path ? path.join(projectPath, capture.path) : null;
        if (cameraResult?.success === true && copyNativeImage(cameraPath)) {
          captureSource = "CameraRender";
          captureError = null;
          capturePauseSummary.pauseRequested = false;
          capturePauseSummary.stepsRequested = 0;
          capturePauseSummary.wasPlaying = editorWasPlaying;
          capturePauseSummary.wasPaused = editorWasPaused;
          capturePauseSummary.isPausedAfter = editorWasPaused;
        } else if (!captureError) {
          captureError = cameraResult?.error || cameraResult?.message || "Camera-render capture did not produce an image file.";
        }
      } catch (error) {
        if (!captureError) {
          captureError = error.message;
        }
      }
    }
  }

  if (!captureSource && capturePathMode !== "DesktopOnly" && !fatalError) {
    let unityCapture = null;
    try {
      fs.rmSync(unityStageImagePath, { force: true });
      const escapedImagePath = common.escapeCSharpString(unityStageImagePath);
      const unityCaptureCode = capturePauseAndStepOnly
        ? 'result.Log("Unity pause/step-only prelude complete.");'
        : [
            "int width = Mathf.Max(640, Screen.width > 0 ? Screen.width : 0);",
            "int height = Mathf.Max(360, Screen.height > 0 ? Screen.height : 0);",
            "var target = RenderTexture.GetTemporary(width, height, 24);",
            "ScreenCapture.CaptureScreenshotIntoRenderTexture(target);",
            "RenderTexture.active = null;",
            "RenderTexture.active = target;",
            "var texture = new Texture2D(width, height, TextureFormat.RGB24, false);",
            "texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);",
            "texture.Apply();",
            `File.WriteAllBytes("${escapedImagePath}", texture.EncodeToPNG());`,
            "RenderTexture.active = null;",
            "RenderTexture.ReleaseTemporary(target);",
            "Object.DestroyImmediate(texture);",
            `result.Log("Saved Unity capture to {0}", "${escapedImagePath}");`,
          ].join("\n");

      unityCapture = await common.invokeUnityRunCommandObject(projectPath, {
        code: unityCaptureCode,
        title: "Unity-aware screenshot",
        usings: ["System.IO"],
        timeoutSeconds: unityCaptureTimeoutSeconds,
        pausePlayMode: pausePlaymodeForCapture,
        stepFrames: requestedStepFrames,
        restorePauseState: true,
      });
    } catch (error) {
      captureError = error.message;
    }

    const playModeExecution = common.getUnityRunCommandPlayModeExecution(unityCapture);
    if (unityCapture && unityCapture.success !== true) {
      captureError = unityCapture.error || captureError || "Unity-aware capture did not produce an image file.";
    }
    if (playModeExecution) {
      capturePauseSummary = playModeExecution;
    } else {
      const data = editorState?.data || editorState?.Data || {};
      capturePauseSummary.pauseRequested = pausePlaymodeForCapture || requestedStepFrames > 0;
      capturePauseSummary.stepsRequested = requestedStepFrames;
      capturePauseSummary.wasPlaying = data.IsPlaying === true || data.isPlaying === true;
      capturePauseSummary.wasPaused = data.IsPaused === true || data.isPaused === true;
      capturePauseSummary.pauseWasApplied = (pausePlaymodeForCapture || requestedStepFrames > 0) && capturePauseSummary.wasPlaying;
      capturePauseSummary.isPausedAfter = capturePauseSummary.wasPaused;
    }
    capturePauseSummary.pauseRequested = pausePlaymodeForCapture || requestedStepFrames > 0;
    capturePauseSummary.pauseStepOnly = capturePauseAndStepOnly;

    if (capturePauseAndStepOnly && unityCapture?.success === true) {
      captureSource = "UnityPauseAndStepOnly";
      captureError = null;
    } else {
      const unityCaptureFile = await common.waitForFile(unityStageImagePath, 2000, 200);
      if (unityCaptureFile.Exists) {
        try {
          fs.renameSync(unityStageImagePath, unityImagePath);
        } catch (_error) {
          fs.copyFileSync(unityStageImagePath, unityImagePath);
          fs.rmSync(unityStageImagePath, { force: true });
        }
        captureSource = "RunCommandScreenCapture";
        imagePath = unityImagePath;
        captureError = null;
      } else if (!captureError) {
        captureError = "Unity-aware capture did not produce an image file within the flush window.";
      }
    }
    fs.rmSync(unityStageImagePath, { force: true });
  }

  if (!captureSource && !capturePauseAndStepOnly && capturePathMode !== "UnityOnly" && !fatalError) {
    const desktopCaptured = await common.captureDesktop(desktopImagePath);
    if (desktopCaptured) {
      captureSource = "DesktopFallback";
      imagePath = desktopImagePath;
      fallbackUsed = true;
    } else if (!captureError) {
      captureError = "Desktop fallback did not produce an image file.";
    }
  }

  const manifestPath = path.join(outputDir, `artifact-${safeLabel}.json`);
  const manifest = {
    Label: safeLabel,
    OutputDir: outputDir,
    StatePath: statePath,
    PreCapturePath: preCaptureResult ? preCapturePath : null,
    ProbePath: probeResult ? probePath : null,
    ImagePath: imagePath,
    CaptureSource: captureSource,
    CapturePathMode: capturePathMode,
    PausePlaymodeForCapture: pausePlaymodeForCapture,
    CapturePauseApplied: capturePauseSummary.pauseWasApplied,
    CapturePauseWasRequested: capturePauseSummary.pauseRequested,
    CaptureStepFramesRequested: capturePauseSummary.stepsRequested,
    CaptureStepFramesApplied: capturePauseSummary.stepsApplied,
    CapturePauseAndStepOnly: capturePauseAndStepOnly,
    CaptureWasPausedBefore: capturePauseSummary.wasPaused,
    CapturePlayModeWasRunning: capturePauseSummary.wasPlaying,
    CaptureIsPausedAfter: capturePauseSummary.isPausedAfter,
    CaptureFallbackUsed: fallbackUsed,
    UnityCaptureTimeoutSeconds: unityCaptureTimeoutSeconds,
    IdleWait: idleWait,
    PreCaptureError: preCaptureError,
    ProbeError: probeError,
    CaptureError: captureError,
    FatalError: fatalError,
  };
  common.writeJsonFile(manifestPath, manifest);
  console.log(JSON.stringify(manifest, null, 2));
  await common.shutdownUnityMcpSessions();
  process.exit(fatalError ? 1 : 0);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
