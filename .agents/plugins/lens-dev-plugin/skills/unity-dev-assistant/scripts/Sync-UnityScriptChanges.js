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
  const force = common.getArgBool(args, ["Force"], false);
  const timeoutSeconds = common.getArgNumber(args, ["ReloadTimeoutSeconds"], 120);
  const pollIntervalMs = Math.max(50, Math.round(common.getArgNumber(args, ["PollIntervalSeconds"], 0.5) * 1000));
  const stablePollCount = Math.max(1, Math.round(common.getArgNumber(args, ["IdleStablePollCount"], 3)));

  const payload = {
    changedPaths: normalizedChangedPaths,
    force,
    waitForCompile: true,
    timeoutSeconds,
    pollIntervalMs,
    stablePollCount,
  };

  try {
    const response = await common.invokeUnityMcpToolJson(
      projectPath,
      "Unity_Editor_SyncScripts",
      payload,
      {
        timeoutSeconds: Math.max(15, timeoutSeconds + 10),
        requiredPacks: ["scripting"],
        allowReconnect: true,
      }
    );
    const toolResult = common.getToolObject(response);
    const data = toolResult?.data || {};
    let postRefreshIdleWait = null;
    if (toolResult?.success === true && data.refreshScheduledAfterResponse === true) {
      postRefreshIdleWait = await common.waitUnityEditorIdle(projectPath, {
        timeoutSeconds,
        stablePollCount,
        pollIntervalSeconds: pollIntervalMs / 1000,
        postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
      });
    }
    const postRefreshIdleSucceeded = postRefreshIdleWait ? postRefreshIdleWait.success === true : true;
    const result = {
      success: toolResult?.success === true && postRefreshIdleSucceeded,
      message: toolResult?.message || toolResult?.error || "Unity.Editor.SyncScripts returned no message.",
      projectPath,
      changedPaths: data.changedPaths || normalizedChangedPaths,
      relevantChangedPaths: data.relevantChangedPaths || [],
      relevantChangesDetected: force || (data.relevantChangedPaths || []).length > 0,
      noChangesDetected: data.noChangesDetected === true,
      compileObserved: data.compileObserved === true,
      forcedRefresh: data.refreshRequested === true && force === true,
      refreshRequested: data.refreshRequested === true,
      refreshScheduledAfterResponse: data.refreshScheduledAfterResponse === true,
      editorIdle: data.editorIdle === true || postRefreshIdleWait?.success === true,
      initialConsoleErrorCount: data.initialConsoleErrorCount,
      finalConsoleErrorCount: data.finalConsoleErrorCount,
      consoleErrorCount: data.consoleErrorCount,
      newConsoleErrorCount: data.newConsoleErrorCount,
      newConsoleErrorsDetected: data.newConsoleErrorsDetected === true,
      staleConsoleErrorsPresent: data.staleConsoleErrorsPresent === true,
      durationSeconds: data.elapsedMs != null ? Math.round((Number(data.elapsedMs) / 1000) * 1000) / 1000 : null,
      warningCount: Number(data.warningCount || 0),
      warnings: data.warnings || [],
      warningSummary: Array.isArray(data.warnings) && data.warnings.length > 0
        ? data.warnings.map((warning) => warning.message || warning.kind || String(warning)).join(" ")
        : null,
      postRefreshIdleWait,
      toolResult,
    };

    console.log(JSON.stringify(result, null, 2));
    await common.shutdownUnityMcpSessions();
    process.exit(result.success ? 0 : 1);
  } catch (error) {
    const result = {
      success: false,
      message: `Unity.Editor.SyncScripts failed: ${error.message}`,
      projectPath,
      changedPaths: normalizedChangedPaths,
      relevantChangedPaths: common.getUnityCompileAffectingChanges(projectPath, normalizedChangedPaths),
      relevantChangesDetected: force || common.getUnityCompileAffectingChanges(projectPath, normalizedChangedPaths).length > 0,
      noChangesDetected: false,
      compileObserved: false,
      forcedRefresh: force,
      refreshRequested: false,
      durationSeconds: null,
      warningCount: 1,
      warnings: [{ kind: "sync_scripts_tool_failure", message: error.message }],
      warningSummary: error.message,
    };
    console.log(JSON.stringify(result, null, 2));
    await common.shutdownUnityMcpSessions();
    process.exit(1);
  }
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
