#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const changedPaths = common.getArgArray(args, ["ChangedPaths"], []);
  const normalizedChangedPaths = changedPaths
    .map((entry) => common.resolveUnityRelativePath(projectPath, entry))
    .filter(Boolean)
    .filter((entry, index, entries) => entries.indexOf(entry) === index);
  const relevantChangedPaths = changedPaths.length > 0 ? common.getUnityCompileAffectingChanges(projectPath, changedPaths) : [];
  const relevantChangesDetected = changedPaths.length === 0 || relevantChangedPaths.length > 0;
  await common.ensureUnityToolPacks(projectPath, ["console"], { timeoutSeconds: 15 });

  const result = {
    success: false,
    message: null,
    projectPath,
    changedPaths: normalizedChangedPaths,
    relevantChangedPaths,
    relevantChangesDetected,
    compileObserved: false,
    likelyStartedByTransientFailure: false,
    forcedRefresh: false,
    transientMcpFailureObserved: false,
    markerPath: common.getUnityExpectedReloadState(projectPath, true).Path,
    durationSeconds: 0,
    naturalCycle: null,
    forcedCycle: null,
    forceRefreshResult: null,
    forceRefreshError: null,
    editorIdle: null,
    expectedReloadState: null,
    fallbackClassification: null,
    directHealthFallback: null,
  };

  const startedAt = Date.now();
  try {
    if (!relevantChangesDetected) {
      const idleWait = await common.waitUnityEditorIdle(projectPath, {
        timeoutSeconds: common.getArgNumber(args, ["ReloadTimeoutSeconds"], 120),
        stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
        pollIntervalSeconds: common.getArgNumber(args, ["PollIntervalSeconds"], 0.5),
        postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
      });
      result.success = idleWait.success;
      result.message = idleWait.success
        ? "No compile-affecting changes were detected; Unity editor is idle."
        : "No compile-affecting changes were detected, but Unity editor did not reach stable idle before timeout.";
      result.editorIdle = idleWait;
    } else {
      common.setUnityExpectedReloadState(
        projectPath,
        "ExternalScriptChanges",
        relevantChangedPaths,
        common.getArgNumber(args, ["ReloadMarkerTtlSeconds"], 120)
      );
      const naturalCycle = await common.waitUnityCompileReloadCycle(projectPath, {
        startTimeoutSeconds: common.getArgNumber(args, ["NaturalDetectTimeoutSeconds"], 6),
        timeoutSeconds: common.getArgNumber(args, ["ReloadTimeoutSeconds"], 120),
        stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
        pollIntervalSeconds: common.getArgNumber(args, ["PollIntervalSeconds"], 0.5),
        postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
        clearExpectedReloadOnSuccess: true,
      });
      result.naturalCycle = naturalCycle;

      if (naturalCycle.compileObserved || naturalCycle.likelyStartedByTransientFailure) {
        result.compileObserved = naturalCycle.compileObserved;
        result.likelyStartedByTransientFailure = naturalCycle.likelyStartedByTransientFailure;
        result.transientMcpFailureObserved = naturalCycle.transientExpectedReloadFailures > 0;
        result.success = naturalCycle.success;
        result.editorIdle = naturalCycle.idleWait;
        result.message = naturalCycle.success
          ? naturalCycle.compileObserved
            ? "Unity picked up the external script changes and returned to stable idle."
            : "Unity entered an expected reload window after the external script changes and returned to stable idle."
          : "Unity began handling the external script changes, but did not return to stable idle before timeout.";
      } else {
        result.forcedRefresh = true;
        common.setUnityExpectedReloadState(
          projectPath,
          "ForcedScriptRefresh",
          relevantChangedPaths,
          common.getArgNumber(args, ["ReloadMarkerTtlSeconds"], 120)
        );
        const forceRefreshCode = [
          "UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);",
          "UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();",
          'result.Log("SYNC::requested-refresh-and-script-compilation");',
        ].join("\n");

        try {
          result.forceRefreshResult = await common.invokeUnityRunCommandObject(projectPath, {
            code: forceRefreshCode,
            title: "Sync external Unity script changes",
            timeoutSeconds: common.getArgNumber(args, ["ForceRefreshTimeoutSeconds"], 30),
          });
        } catch (error) {
          result.forceRefreshError = error.message;
        }

        const forcedCycle = await common.waitUnityCompileReloadCycle(projectPath, {
          startTimeoutSeconds: common.getArgNumber(args, ["ForcedDetectTimeoutSeconds"], 20),
          timeoutSeconds: common.getArgNumber(args, ["ReloadTimeoutSeconds"], 120),
          stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
          pollIntervalSeconds: common.getArgNumber(args, ["PollIntervalSeconds"], 0.5),
          postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
          clearExpectedReloadOnSuccess: true,
        });
        result.forcedCycle = forcedCycle;
        result.compileObserved = forcedCycle.compileObserved;
        result.likelyStartedByTransientFailure = forcedCycle.likelyStartedByTransientFailure;
        result.transientMcpFailureObserved =
          !!result.forceRefreshError ||
          forcedCycle.transientExpectedReloadFailures > 0 ||
          naturalCycle.transientExpectedReloadFailures > 0;
        result.success = forcedCycle.success;
        result.editorIdle = forcedCycle.idleWait;
        result.message = forcedCycle.success
          ? forcedCycle.compileObserved
            ? "Forced Unity refresh/recompile completed and the editor returned to stable idle."
            : "Forced Unity refresh completed and the editor returned to stable idle, but no compile/update was observed."
          : "Forced Unity refresh did not lead to a settled compile/reload cycle before timeout.";
      }
    }
  } finally {
    result.durationSeconds = Math.round(((Date.now() - startedAt) / 1000) * 1000) / 1000;
    result.expectedReloadState = common.getUnityExpectedReloadState(projectPath, true);

    if (!result.success) {
      try {
        const directHealth = await common.testUnityDirectEditorHealthy(projectPath, {
          timeoutSeconds: 20,
          consecutiveHealthyPolls: 2,
          pollIntervalSeconds: common.getArgNumber(args, ["PollIntervalSeconds"], 0.5),
        });
        result.directHealthFallback = directHealth;

        if (directHealth.success) {
          result.success = true;
          result.fallbackClassification = "LensHelpersRecovered";
          result.message = "Lens helper sync recovered: direct Lens health and compact editor-state probes are healthy and idle.";
          if (!result.editorIdle) {
            result.editorIdle = directHealth;
          }
          common.clearUnityExpectedReloadState(projectPath);
        }
      } catch (_error) {
      }
    }
  }

  console.log(JSON.stringify(result, null, 2));
  await common.shutdownUnityMcpSessions();
  process.exit(result.success ? 0 : 1);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
