#!/usr/bin/env node

const fs = require("fs");
const path = require("path");
const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

function resolveArtifactPath(projectPath, targetPath) {
  if (!targetPath) {
    return null;
  }

  return path.isAbsolute(targetPath) ? targetPath : path.join(projectPath, targetPath);
}

function getBuildMonitorState(projectPath, options = {}) {
  const monitorMode = options.monitorBuildMode || "";
  if (!monitorMode) {
    return null;
  }
  if (monitorMode !== "WebGL") {
    throw new Error(`Unsupported --MonitorBuildMode '${monitorMode}'. Supported values: WebGL.`);
  }

  const buildOutputPath = resolveArtifactPath(projectPath, options.buildOutputPath || "");
  const buildReportPath = resolveArtifactPath(projectPath, options.buildReportPath || "");
  const successArtifactPath = resolveArtifactPath(projectPath, options.successArtifactPath || "");
  const editorLogTail = common.getArgNumber(options.args || {}, ["EditorLogTail"], 400);
  const logState = common.getWebGlBuildProgressState(common.tailFile(common.getUnityEditorLogPath(), editorLogTail));

  const outputExists = !!buildOutputPath && common.pathExists(buildOutputPath);
  const successArtifactExists = !!successArtifactPath && common.pathExists(successArtifactPath);
  const buildReportExists = !!buildReportPath && common.pathExists(buildReportPath);

  if (successArtifactExists || logState.Status === "Succeeded") {
    return {
      Status: "Succeeded",
      Summary: successArtifactExists
        ? "Build success artifact exists."
        : logState.Summary,
      BuildOutputPath: buildOutputPath,
      BuildOutputExists: outputExists,
      BuildReportPath: buildReportPath,
      BuildReportExists: buildReportExists,
      SuccessArtifactPath: successArtifactPath,
      SuccessArtifactExists: successArtifactExists,
      LogState: logState,
    };
  }

  if (logState.Status === "Failed") {
    return {
      Status: "Failed",
      Summary: logState.Summary,
      BuildOutputPath: buildOutputPath,
      BuildOutputExists: outputExists,
      BuildReportPath: buildReportPath,
      BuildReportExists: buildReportExists,
      SuccessArtifactPath: successArtifactPath,
      SuccessArtifactExists: successArtifactExists,
      LogState: logState,
    };
  }

  if (outputExists || buildReportExists || logState.Status === "InProgress") {
    return {
      Status: "InProgress",
      Summary: logState.Status === "InProgress"
        ? logState.Summary
        : "Build output exists but no terminal build marker has been observed yet.",
      BuildOutputPath: buildOutputPath,
      BuildOutputExists: outputExists,
      BuildReportPath: buildReportPath,
      BuildReportExists: buildReportExists,
      SuccessArtifactPath: successArtifactPath,
      SuccessArtifactExists: successArtifactExists,
      LogState: logState,
    };
  }

  return {
    Status: "Unknown",
    Summary: "No build monitor signal was observed after the transport failure.",
    BuildOutputPath: buildOutputPath,
    BuildOutputExists: outputExists,
    BuildReportPath: buildReportPath,
    BuildReportExists: buildReportExists,
    SuccessArtifactPath: successArtifactPath,
    SuccessArtifactExists: successArtifactExists,
    LogState: logState,
  };
}

async function waitForBuildMonitor(projectPath, options = {}) {
  const timeoutSeconds = options.timeoutSeconds ?? 1800;
  const pollIntervalSeconds = options.pollIntervalSeconds ?? 5.0;
  const deadline = Date.now() + Math.max(1, timeoutSeconds) * 1000;
  const attempts = [];
  let lastState = null;

  while (Date.now() < deadline) {
    lastState = getBuildMonitorState(projectPath, options);
    attempts.push({
      Timestamp: common.nowIso(),
      Status: lastState.Status,
      Summary: lastState.Summary,
    });

    if (lastState.Status === "Succeeded") {
      return {
        success: true,
        message: lastState.Summary,
        timeoutSeconds,
        pollIntervalSeconds,
        attempts,
        lastState,
      };
    }

    if (lastState.Status === "Failed") {
      return {
        success: false,
        message: lastState.Summary,
        timeoutSeconds,
        pollIntervalSeconds,
        attempts,
        lastState,
      };
    }

    await common.sleep(pollIntervalSeconds * 1000);
  }

  return {
    success: false,
    message: "Build monitor did not reach a terminal state before timeout.",
    timeoutSeconds,
    pollIntervalSeconds,
    attempts,
    lastState,
  };
}

async function getRunCommandPreflight(projectPath, args) {
  const lensHealthResponse = await common.invokeUnityMcpToolJson(projectPath, "Unity_GetLensHealth", {}, {
    timeoutSeconds: common.getArgNumber(args, ["HealthTimeoutSeconds"], 10),
  });
  const lensHealth = common.getToolObject(lensHealthResponse);
  const lensData = common.valueOf(lensHealth, "data", "Data") || {};
  const bridgeStatus = common.valueOf(
    common.valueOf(lensData, "bridgeStatus", "BridgeStatus") || {},
    "status",
    "Status"
  ) || null;
  const expectedRecoveryActive = common.valueOf(
    common.valueOf(lensData, "expectedRecovery", "ExpectedRecovery") || {},
    "isActive",
    "IsActive"
  ) === true;

  const result = {
    lensHealth: {
      success: common.valueOf(lensHealth, "success", "Success") === true,
      bridgeStatus,
      expectedRecoveryActive,
      activeToolPacks: common.valueOf(lensData, "activeToolPacks", "ActiveToolPacks") || [],
      recommendedNextAction: common.valueOf(lensData, "recommendedNextAction", "RecommendedNextAction") || null,
    },
    directMcpHealthy:
      common.valueOf(lensHealth, "success", "Success") === true &&
      bridgeStatus === "ready" &&
      !expectedRecoveryActive,
    editorState: null,
    readiness: null,
    canBypassIdleWait: false,
    reason: "fallback_to_idle_wait",
  };

  if (!result.directMcpHealthy) {
    return result;
  }

  const editorState = await common.getUnityCompactEditorState(
    projectPath,
    common.getArgNumber(args, ["EditorStateTimeoutSeconds"], 15)
  );
  const readiness = common.getUnityReadinessSnapshot(editorState);

  result.editorState = editorState;
  result.readiness = readiness;
  result.canBypassIdleWait =
    result.directMcpHealthy &&
    readiness.Success === true &&
    readiness.IsPlaying === true &&
    readiness.IsCompiling !== true &&
    readiness.IsUpdating !== true;
  result.reason = result.canBypassIdleWait
    ? "healthy_play_mode"
    : readiness.Success === true && readiness.IsPlaying !== true
      ? "edit_mode"
      : "fallback_to_idle_wait";

  return result;
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  let code = common.getArgString(args, ["Code"], "");
  const codePath = common.getArgString(args, ["CodePath"], "");
  if (codePath) {
    code = fs.readFileSync(codePath, "utf8");
  }
  if (!code.trim()) {
    throw new Error("Provide --Code or --CodePath.");
  }

  const monitorBuildMode = common.getArgString(args, ["MonitorBuildMode"], "");
  if (monitorBuildMode && monitorBuildMode !== "WebGL") {
    throw new Error(`Unsupported --MonitorBuildMode '${monitorBuildMode}'. Supported values: WebGL.`);
  }

  const waitForEditorIdle = common.getArgBool(args, ["WaitForEditorIdle"], true);
  let playModeBypass = {
    applied: false,
    reason: waitForEditorIdle ? "fallback_to_idle_wait" : null,
  };
  let runCommandPreflight = null;

  if (waitForEditorIdle) {
    try {
      runCommandPreflight = await getRunCommandPreflight(projectPath, args);
      playModeBypass = {
        applied: runCommandPreflight.canBypassIdleWait,
        reason: runCommandPreflight.reason,
      };
    } catch (error) {
      runCommandPreflight = {
        error: error.message,
      };
      playModeBypass = {
        applied: false,
        reason: "fallback_to_idle_wait",
      };
    }
  }

  await common.ensureUnityToolPacks(
    projectPath,
    waitForEditorIdle && !playModeBypass.applied ? ["console", "scripting"] : ["scripting"],
    { timeoutSeconds: 20 }
  );

  let idleResult = null;
  if (waitForEditorIdle && !playModeBypass.applied) {
    idleResult = await common.waitUnityEditorIdle(projectPath, {
      timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
      stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
      pollIntervalSeconds: common.getArgNumber(args, ["IdlePollIntervalSeconds"], 0.5),
      postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
    });
    if (!idleResult.success) {
      console.log(JSON.stringify({
        success: false,
        error: idleResult.message,
        editorIdle: idleResult,
        playModeBypass,
        preflight: runCommandPreflight,
      }, null, 2));
      await common.shutdownUnityMcpSessions();
      process.exit(1);
      return;
    }
  }

  let result;
  let exitCode = 0;
  try {
    result = await common.invokeUnityRunCommandObject(projectPath, {
      code,
      title: common.getArgString(args, ["Title"], ""),
      usings: common.getArgArray(args, ["Using", "Usings"], []),
      timeoutSeconds: common.getArgNumber(args, ["TimeoutSeconds"], 60),
      pausePlayMode: common.getArgBool(args, ["PausePlayMode"], false),
      stepFrames: common.getArgNumber(args, ["StepFrames"], 0),
      restorePauseState: common.getArgBool(args, ["RestorePauseState"], true),
    });
  } catch (error) {
    if (!monitorBuildMode) {
      throw error;
    }

    const monitorOptions = {
      args,
      monitorBuildMode,
      buildOutputPath: common.getArgString(args, ["BuildOutputPath"], ""),
      buildReportPath: common.getArgString(args, ["BuildReportPath"], ""),
      successArtifactPath: common.getArgString(args, ["SuccessArtifactPath"], ""),
      timeoutSeconds: common.getArgNumber(args, ["BuildTimeoutSeconds"], 1800),
      pollIntervalSeconds: common.getArgNumber(args, ["BuildPollIntervalSeconds"], 5.0),
    };

    const initialMonitorState = getBuildMonitorState(projectPath, monitorOptions);
    if (initialMonitorState.Status === "Succeeded") {
      result = {
        success: true,
        monitorFallbackUsed: true,
        message: "Unity build completed after the MCP response path became unavailable.",
        runCommandError: error.message,
        buildMonitor: initialMonitorState,
      };
    } else if (initialMonitorState.Status === "Failed") {
      result = {
        success: false,
        monitorFallbackUsed: true,
        error: initialMonitorState.Summary,
        runCommandError: error.message,
        buildMonitor: initialMonitorState,
      };
      exitCode = 1;
    } else if (initialMonitorState.Status === "InProgress") {
      const monitorWait = await waitForBuildMonitor(projectPath, monitorOptions);
      result = {
        success: monitorWait.success,
        monitorFallbackUsed: true,
        message: monitorWait.message,
        runCommandError: error.message,
        buildMonitor: monitorWait,
      };
      if (!monitorWait.success) {
        result.error = monitorWait.message;
        exitCode = 1;
      }
    } else {
      throw error;
    }
  }

  if (idleResult) {
    result.editorIdle = idleResult;
  }
  result.playModeBypass = playModeBypass;
  if (runCommandPreflight) {
    result.preflight = runCommandPreflight.error
      ? runCommandPreflight
      : {
          lensHealth: runCommandPreflight.lensHealth,
          directMcpHealthy: runCommandPreflight.directMcpHealthy,
          editorState: runCommandPreflight.editorState
            ? common.getCompactEditorStateSummary(runCommandPreflight.editorState)
            : null,
          readiness: runCommandPreflight.readiness,
          canBypassIdleWait: runCommandPreflight.canBypassIdleWait,
          reason: runCommandPreflight.reason,
        };
  }
  console.log(JSON.stringify(result, null, 2));
  await common.shutdownUnityMcpSessions();
  process.exit(exitCode || (result && result.success === false ? 1 : 0));
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
