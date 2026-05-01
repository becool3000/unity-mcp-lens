#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function testUnityPlayReadyDegradedFallback(projectPath, options) {
  return common.waitUnityPlayReady(projectPath, {
    timeoutSeconds: options.timeoutSeconds,
    pollIntervalSeconds: options.pollIntervalSeconds,
    warmupSeconds: options.warmupSeconds,
  }).then((result) => ({ ...result, degradedFallback: true }));
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const timeoutSeconds = common.getArgNumber(args, ["TimeoutSeconds"], 25);
  const pollIntervalSeconds = common.getArgNumber(args, ["PollIntervalSeconds"], 1.0);
  const warmupSeconds = common.getArgNumber(args, ["WarmupSeconds"], 1.0);
  const playRequestTimeoutSeconds = common.getArgNumber(args, ["PlayRequestTimeoutSeconds"], 180);

  if (common.getArgBool(args, ["StopFirst"], false)) {
    try {
      await common.invokeUnityMcpToolJson(projectPath, "Unity_ManageEditor", { Action: "Stop" }, { timeoutSeconds: 15 });
    } catch (_error) {
    }
  }

  const idleWait = await common.waitUnityEditorIdle(projectPath, {
    timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
    stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
    pollIntervalSeconds: common.getArgNumber(args, ["IdlePollIntervalSeconds"], 0.5),
    postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
  });

  if (!idleWait.success) {
    const failureClassification = await common.classifyUnityHelperFailure(projectPath, {
      errorMessage: idleWait.message,
      timeoutSeconds: 6,
      maxItems: 8,
    });
    console.log(JSON.stringify({
      success: false,
      message: failureClassification.classification === "EditorModalBlocking"
        ? "Unity play-mode entry is blocked by an OS-native Unity modal dialog. Resolve the modal before retrying."
        : failureClassification.classification === "EditorFrozen"
          ? "Unity play-mode entry is blocked because Unity.exe is not responding. Run Recover-UnityFrozenEditor.ps1 explicitly before retrying."
          : failureClassification.classification === "UnityNotRunning"
            ? "Unity play-mode entry is blocked because the Unity editor is not running."
        : "Unity editor did not become idle before play.",
      classification: failureClassification.classification,
      recommendedPath: failureClassification.recommendedPath,
      idleWait,
      nativeModal: failureClassification.nativeModal,
      frozenEditor: failureClassification.frozenEditor,
    }, null, 2));
    await common.shutdownUnityMcpSessions();
    process.exit(1);
    return;
  }

  let playResponse = null;
  let playError = null;
  try {
    playResponse = await common.invokeUnityMcpToolJson(
      projectPath,
      "Unity_ManageEditor",
      { Action: "Play" },
      { timeoutSeconds: playRequestTimeoutSeconds }
    );
  } catch (error) {
    playError = error.message;
  }

  let playReady = await common.waitUnityPlayReady(projectPath, {
    timeoutSeconds,
    pollIntervalSeconds,
    warmupSeconds,
  });
  const playResponseObject = playResponse ? common.getToolObject(playResponse) : null;
  const playRequestErrorMessage = playError || playResponseObject?.error || "";
  const playRequestWasReconnectProne =
    playRequestErrorMessage.toLowerCase().includes("connection disconnected") ||
    playResponseObject?.data?.ReconnectExpected === true ||
    playResponseObject?.data?.TransitionState === "transitioning_to_play";
  let degradedPath = false;
  let finalMessage = playReady.message;
  let degradedFallback = null;

  if (!playReady.success && playRequestWasReconnectProne) {
    degradedFallback = await testUnityPlayReadyDegradedFallback(projectPath, {
      timeoutSeconds: Math.max(6, Math.ceil(Math.max(warmupSeconds, 1.0) + 6)),
      pollIntervalSeconds,
      warmupSeconds,
    });
    if (degradedFallback.success) {
      playReady = degradedFallback;
      degradedPath = true;
      finalMessage = degradedFallback.message || "Play mode entered after a reconnect-prone transition.";
    }
  }

  if (playReady.success && !degradedPath && playRequestWasReconnectProne) {
    degradedPath = true;
    finalMessage = "Play mode entered and runtime advanced after an expected reconnect-prone play transition.";
  }

  const result = {
    success: playReady.success,
    message: finalMessage,
    idleWait,
    degradedPath,
    playRequestTimeoutSeconds,
    playRequestWasReconnectProne,
    playRequestErrorMessage: playRequestErrorMessage || null,
    playResponse: playResponseObject,
    playError,
    playReady,
    degradedFallback,
  };

  if (!result.success) {
    const failureClassification = await common.classifyUnityHelperFailure(projectPath, {
      errorMessage: result.playError || result.playRequestErrorMessage || result.message,
      timeoutSeconds: 6,
      maxItems: 8,
    });
    result.nativeModal = failureClassification.nativeModal;
    result.frozenEditor = failureClassification.frozenEditor;
    result.classification = failureClassification.classification;
    result.recommendedPath = failureClassification.recommendedPath;
    if (failureClassification.classification === "EditorModalBlocking") {
      result.message = "Unity play-mode entry is blocked by an OS-native Unity modal dialog. Resolve the modal before retrying.";
    } else if (failureClassification.classification === "EditorFrozen") {
      result.message = "Unity play-mode entry is blocked because Unity.exe is not responding. Run Recover-UnityFrozenEditor.ps1 explicitly before retrying.";
    } else if (failureClassification.classification === "UnityNotRunning") {
      result.message = "Unity play-mode entry is blocked because the Unity editor is not running.";
    }
  }

  console.log(JSON.stringify(result, null, 2));
  await common.shutdownUnityMcpSessions();
  process.exit(playReady.success ? 0 : 1);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
