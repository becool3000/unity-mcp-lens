#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function testUnityPlayReadyDegradedFallback(projectPath, options) {
  return common.waitUnityPlayReady(projectPath, {
    timeoutSeconds: options.timeoutSeconds,
    pollIntervalSeconds: options.pollIntervalSeconds,
    warmupSeconds: options.warmupSeconds,
  }).then((result) => ({ ...result, degradedFallback: true }));
}

function summarizeToolResult(toolResult) {
  if (!toolResult) {
    return null;
  }

  const data = toolResult.data || {};
  return {
    success: toolResult.success === true,
    message: toolResult.message || toolResult.error || null,
    transitionState: data.transitionState || data.TransitionState || null,
    reconnectExpected: data.reconnectExpected === true || data.ReconnectExpected === true,
    runtimeAdvanced: data.runtimeAdvanced === true || data.RuntimeAdvanced === true,
    timedOut: data.timedOut === true || data.TimedOut === true,
    consoleErrorCount: data.consoleErrorCount ?? data.ConsoleErrorCount ?? null,
  };
}

function summarizePlayReady(result) {
  if (!result) {
    return null;
  }

  const finalState = common.valueOf(result, "finalState", "FinalState") || {};
  const lastState = common.valueOf(result, "lastState", "LastState") || {};
  const lastStateData = common.valueOf(lastState, "data", "Data") || {};
  const attempts = common.valueOf(result, "attempts", "Attempts") || [];
  const lastAttempt = Array.isArray(attempts) && attempts.length > 0 ? attempts[attempts.length - 1] : {};
  const editorIdle =
    common.valueOf(result, "editorIdle", "EditorIdle", "isEditorIdle", "IsEditorIdle") ??
    common.valueOf(lastAttempt, "IdleReady", "idleReady") ??
    null;
  const isPlaying =
    common.valueOf(result, "isPlaying", "IsPlaying") ??
    common.valueOf(lastStateData, "IsPlaying", "isPlaying") ??
    common.valueOf(lastAttempt, "IsPlaying", "isPlaying") ??
    null;
  const finalIsPlaying =
    common.valueOf(finalState, "isPlaying", "IsPlaying") ??
    common.valueOf(lastStateData, "IsPlaying", "isPlaying") ??
    null;
  const runtimeAdvanced =
    common.valueOf(result, "runtimeAdvanced", "RuntimeAdvanced") ??
    common.valueOf(lastAttempt, "PlayReady", "playReady", "RuntimeProbeHasAdvancedFrames", "runtimeProbeHasAdvancedFrames") ??
    null;

  return {
    success: result.success === true,
    message: result.message || result.error || null,
    degradedFallback: result.degradedFallback === true,
    editorIdle,
    isPlaying,
    finalIsPlaying,
    runtimeAdvanced,
  };
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const timeoutSeconds = common.getArgNumber(args, ["TimeoutSeconds"], 25);
  const pollIntervalSeconds = common.getArgNumber(args, ["PollIntervalSeconds"], 1.0);
  const warmupSeconds = common.getArgNumber(args, ["WarmupSeconds"], 1.0);
  const playRequestTimeoutSeconds = common.getArgNumber(args, ["PlayRequestTimeoutSeconds"], 180);
  const includeDetails = common.getArgBool(args, ["IncludeDetails"], false);
  const sourceIntegrity = common.getUnitySourceFileIntegrity(projectPath);

  if (!sourceIntegrity.success) {
    console.log(JSON.stringify({
      success: false,
      message: "Unity source integrity check failed before play-mode entry.",
      sourceIntegrity,
    }, null, 2));
    await common.shutdownUnityMcpSessions();
    process.exit(1);
    return;
  }

  if (common.getArgBool(args, ["StopFirst"], false)) {
    try {
      await common.invokeUnityMcpToolJson(
        projectPath,
        "Unity_Editor_SetPlayMode",
        {
          mode: "exit",
          timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
          waitForRuntimeAdvance: false,
          unpauseBeforeExit: true,
        },
        { timeoutSeconds: Math.max(15, common.getArgNumber(args, ["IdleTimeoutSeconds"], 60) + 10) }
      );
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
    console.log(JSON.stringify({ success: false, message: "Unity editor did not become idle before play.", sourceIntegrity, idleWait }, null, 2));
    await common.shutdownUnityMcpSessions();
    process.exit(1);
    return;
  }

  let playResponse = null;
  let playError = null;
  try {
    playResponse = await common.invokeUnityMcpToolJson(
      projectPath,
      "Unity_Editor_SetPlayMode",
      {
        mode: "enter",
        stopFirst: common.getArgBool(args, ["StopFirst"], false),
        waitForRuntimeAdvance: true,
        warmupSeconds,
        timeoutSeconds,
        unpauseBeforeExit: true,
      },
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
  const playData = playResponseObject?.data || {};
  const reconnectExpected = playData.reconnectExpected === true || playData.ReconnectExpected === true;
  const transitionState = playData.transitionState || playData.TransitionState || "";
  const playRequestWasReconnectProne =
    playRequestErrorMessage.toLowerCase().includes("connection disconnected") ||
    reconnectExpected ||
    transitionState === "transitioning_to_play" ||
    transitionState === "enter_requested_after_response";
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

  let consoleErrors = null;
  if (!playReady.success) {
    try {
      consoleErrors = await common.getUnityConsoleEntries(projectPath, {
        types: ["Error"],
        count: 10,
        format: "Summary",
        includeStacktrace: false,
        timeoutSeconds: 20,
      });
    } catch (error) {
      consoleErrors = { success: false, error: error.message };
    }
  }

  const result = {
    success: playReady.success,
    message: finalMessage,
    sourceIntegrity,
    idleWait,
    detailMode: includeDetails || !playReady.success ? "full" : "compact",
    degradedPath,
    playRequestTimeoutSeconds,
    playRequestWasReconnectProne,
    playRequestErrorMessage: playRequestErrorMessage || null,
    playResponse: includeDetails || !playReady.success ? playResponseObject : summarizeToolResult(playResponseObject),
    playError,
    playReady: includeDetails || !playReady.success ? playReady : summarizePlayReady(playReady),
    degradedFallback: includeDetails || !playReady.success ? degradedFallback : summarizePlayReady(degradedFallback),
    consoleErrors,
  };

  console.log(JSON.stringify(result, null, 2));
  await common.shutdownUnityMcpSessions();
  process.exit(playReady.success ? 0 : 1);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
