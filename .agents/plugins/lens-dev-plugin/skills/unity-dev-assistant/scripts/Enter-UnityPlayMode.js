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
    console.log(JSON.stringify({ success: false, message: "Unity editor did not become idle before play.", idleWait }, null, 2));
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

  console.log(JSON.stringify(result, null, 2));
  process.exit(playReady.success ? 0 : 1);
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
