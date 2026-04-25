#!/usr/bin/env node

const path = require("path");
const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

function isLowLevelPackActivationFailure(message) {
  const text = String(message || "").toLowerCase();
  return (
    text.includes("failed to restore required lens packs") ||
    text.includes("failed to restore desired lens packs") ||
    text.includes("failed to restore required unity tool packs") ||
    (text.includes("set_tool_packs") && text.includes("timed out"))
  );
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const includeDiagnostics = common.getArgBool(args, ["IncludeDiagnostics"], false);
  const expectedScenes = common.getArgArray(args, ["ExpectedScenes"], []);
  const monitorBuildMode = common.getArgString(args, ["MonitorBuildMode"], "");
  const editorLogTail = common.getArgNumber(args, ["EditorLogTail"], 400);

  const bridgeCheck = await common.checkUnityMcp(projectPath, {
    includeDiagnostics,
    editorLogTail,
  });

  let editorState = null;
  let wrapperError = null;
  let wrapperHealthy = false;
  let helperDegraded = false;
  let editorIdleSnapshot = null;
  let playReadySnapshot = null;
  const directMcpHealthy = bridgeCheck.result.Classification === "Ready";
  let lowLevelPackActivationFailed = false;

  if (bridgeCheck.result.Classification === "BuildInProgress") {
    wrapperError = "Skipped Lens editor-state probe because Check-UnityMcp classified the session as BuildInProgress.";
  } else if (bridgeCheck.result.Classification === "EditorReloadingExpected") {
    wrapperError = `Skipped Lens editor-state probe because Check-UnityMcp classified the session as EditorReloadingExpected.`;
  } else if (bridgeCheck.result.EditorStatusBeacon?.Fresh && bridgeCheck.result.EditorStatusBeacon?.Classification === "BeaconTransitioning") {
    wrapperError = `Skipped Lens editor-state probe because the editor status beacon reports phase '${bridgeCheck.result.EditorStatusBeacon.Phase}'.`;
  } else if (bridgeCheck.result.Classification === "Ready") {
    try {
      await common.ensureUnityToolPacks(projectPath, ["console"], { timeoutSeconds: 15 });
      const stableWait = await common.waitUnityEditorIdle(projectPath, {
        timeoutSeconds: common.getArgNumber(args, ["EditorStateTimeoutSeconds"], 90),
        stablePollCount: 3,
        pollIntervalSeconds: 0.5,
        postIdleDelaySeconds: 1.0,
      });
      editorState = stableWait.lastState || null;
      wrapperHealthy = stableWait.success === true && editorState && (editorState.success === true || editorState.Success === true);
      if (wrapperHealthy) {
        const readiness = common.getUnityReadinessSnapshot(editorState);
        editorIdleSnapshot = {
          Ready: stableWait.success === true && readiness.IdleReady,
          StablePollRequirement: 3,
          PollIntervalSeconds: 0.5,
          PostIdleDelaySeconds: 1.0,
          Snapshot: readiness,
        };
        playReadySnapshot = {
          Ready:
            readiness.IsPlaying &&
            readiness.RuntimeProbeAvailable &&
            readiness.RuntimeProbeHasAdvancedFrames &&
            readiness.RuntimeProbeUpdateCount >= 10,
          WarmupSeconds: 1.0,
          UpdateCountThreshold: 10,
          Snapshot: readiness,
        };
      } else {
        wrapperError = stableWait.message || stableWait.lastError || "Unity editor did not reach a stable idle state.";
      }
    } catch (error) {
      wrapperError = error.message;
    }

    lowLevelPackActivationFailed = isLowLevelPackActivationFailure(wrapperError);
    helperDegraded = directMcpHealthy && !wrapperHealthy && !lowLevelPackActivationFailed;
  }

  const assistantPackageState = common.getUnityAssistantPackageState(projectPath);
  const buildScenePreflight = expectedScenes.length > 0
    ? common.testUnityBuildSceneList(projectPath, expectedScenes)
    : null;

  const buildMonitor = monitorBuildMode
    ? common.getWebGlBuildProgressState(common.tailFile(common.getUnityEditorLogPath(), editorLogTail))
    : null;

  let recommendedPath;
  if (buildScenePreflight && !buildScenePreflight.exactMatch) {
    recommendedPath = "FixBuildSceneList";
  } else if (bridgeCheck.result.Classification === "BuildInProgress") {
    recommendedPath = "MonitorActiveBuild";
  } else if (bridgeCheck.result.Classification === "EditorReloadingExpected") {
    recommendedPath = "WaitForExpectedReload";
  } else if (bridgeCheck.result.Classification !== "Ready") {
    recommendedPath = "RepairBridge";
  } else if (wrapperHealthy) {
    recommendedPath = "ProceedWithLensHelpers";
  } else if (lowLevelPackActivationFailed) {
    recommendedPath = "InvestigateLensHelperPath";
  } else if (directMcpHealthy) {
    recommendedPath = "ProceedWithDirectLensTools";
  } else {
    recommendedPath = "InvestigateLensHelperPath";
  }

  const compactResult = {
    ProjectPath: projectPath,
    RecommendedPath: recommendedPath,
    Bridge: {
      Classification: bridgeCheck.result.Classification,
      Summary: bridgeCheck.result.Summary,
      RecommendedAction: bridgeCheck.result.RecommendedAction,
      UserActionRequired: bridgeCheck.result.UserActionRequired,
    },
    Editor: {
      DirectMcpHealthy: directMcpHealthy,
      ManualWrapperHealthy: wrapperHealthy,
      LensHelperHealthy: wrapperHealthy,
      HelperDegraded: helperDegraded,
      LowLevelPackActivationFailed: lowLevelPackActivationFailed,
      Error: wrapperError,
      IdleReady: editorIdleSnapshot ? editorIdleSnapshot.Ready : null,
      PlayReady: playReadySnapshot ? playReadySnapshot.Ready : null,
      State:
        editorState && (editorState.success === true || editorState.Success === true)
          ? common.getCompactEditorStateSummary(editorState)
          : null,
    },
    Build: {
      SceneList: buildScenePreflight
        ? {
            ExactMatch: buildScenePreflight.exactMatch,
            Summary: buildScenePreflight.message,
          }
        : null,
      Monitor: buildMonitor
        ? {
            Status: buildMonitor.Status,
            Summary: buildMonitor.Summary,
          }
        : null,
    },
    AssistantPackage: {
      Mode: assistantPackageState.Mode,
      Summary: assistantPackageState.Summary,
      Dependency: assistantPackageState.DependencyValue,
      Path: assistantPackageState.ResolvedFileDependencyPath,
    },
    DiagnosticsHint: "Rerun with --IncludeDiagnostics for the full Unity session payload.",
  };

  const diagnosticResult = {
    ProjectPath: projectPath,
    BridgeCheck: bridgeCheck.result,
    EditorStatusBeacon: bridgeCheck.result.EditorStatusBeacon,
    BeaconWait: bridgeCheck.result.BeaconWait,
    DirectMcpHealthy: directMcpHealthy,
    ManualWrapperHealthy: wrapperHealthy,
    LensHelperHealthy: wrapperHealthy,
    HelperDegraded: helperDegraded,
    LowLevelPackActivationFailed: lowLevelPackActivationFailed,
    ManualWrapperError: wrapperError,
    LensHelperError: wrapperError,
    EditorState: editorState,
    ExpectedReloadState: bridgeCheck.result.ExpectedReloadState,
    BuildScenePreflight: buildScenePreflight,
    BuildMonitor: buildMonitor,
    EditorIdleSnapshot: editorIdleSnapshot,
    PlayReadySnapshot: playReadySnapshot,
    AssistantPackageState: assistantPackageState,
    ReadyForLongBuild:
      bridgeCheck.result.Classification === "Ready" &&
      (!buildScenePreflight || buildScenePreflight.exactMatch),
    RecommendedPath: recommendedPath,
  };

  console.log(JSON.stringify(includeDiagnostics ? diagnosticResult : compactResult, null, 2));
  await common.shutdownUnityMcpSessions();
  process.exit(bridgeCheck.exitCode === 0 || bridgeCheck.exitCode === 14 || bridgeCheck.exitCode === 15 ? 0 : bridgeCheck.exitCode);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
