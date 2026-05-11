#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

function getConsoleErrorCount(consoleResult) {
  const data = common.valueOf(consoleResult, "data", "Data") || {};
  const typeCounts = common.valueOf(data, "typeCounts", "TypeCounts") || {};
  const error = Number(common.valueOf(typeCounts, "error", "Error") || 0);
  const exception = Number(common.valueOf(typeCounts, "exception", "Exception") || 0);
  const assert = Number(common.valueOf(typeCounts, "assert", "Assert") || 0);
  return error + exception + assert;
}

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
    let postRefreshConsole = null;
    let postRefreshConsoleCheckSucceeded = true;
    let postRefreshFinalConsoleErrorCount = data.finalConsoleErrorCount ?? data.consoleErrorCount ?? null;
    let postRefreshNewConsoleErrorCount = data.newConsoleErrorCount ?? 0;
    if (toolResult?.success === true && data.refreshScheduledAfterResponse === true) {
      postRefreshIdleWait = await common.waitUnityEditorIdle(projectPath, {
        timeoutSeconds,
        stablePollCount,
        pollIntervalSeconds: pollIntervalMs / 1000,
        postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
      });

      if (postRefreshIdleWait.success === true) {
        try {
          postRefreshConsole = await common.getUnityConsoleEntries(projectPath, {
            types: ["Error"],
            count: 100,
            format: "Summary",
            includeStacktrace: false,
            timeoutSeconds: 30,
          });
          postRefreshConsoleCheckSucceeded = postRefreshConsole?.success === true;
          if (postRefreshConsoleCheckSucceeded) {
            postRefreshFinalConsoleErrorCount = getConsoleErrorCount(postRefreshConsole);
            const initial = Number(data.initialConsoleErrorCount ?? 0);
            postRefreshNewConsoleErrorCount = Math.max(0, Number(postRefreshFinalConsoleErrorCount) - initial);
          }
        } catch (error) {
          postRefreshConsoleCheckSucceeded = false;
          postRefreshConsole = { success: false, error: error.message };
        }
      }
    }
    const postRefreshIdleSucceeded = postRefreshIdleWait ? postRefreshIdleWait.success === true : true;
    const postRefreshConsoleClean = postRefreshConsoleCheckSucceeded && Number(postRefreshNewConsoleErrorCount || 0) === 0;
    const result = {
      success: toolResult?.success === true && postRefreshIdleSucceeded && postRefreshConsoleClean && data.newConsoleErrorsDetected !== true,
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
      finalConsoleErrorCount: postRefreshFinalConsoleErrorCount,
      consoleErrorCount: postRefreshFinalConsoleErrorCount,
      newConsoleErrorCount: postRefreshNewConsoleErrorCount,
      newConsoleErrorsDetected: data.newConsoleErrorsDetected === true || postRefreshNewConsoleErrorCount > 0,
      staleConsoleErrorsPresent: data.staleConsoleErrorsPresent === true,
      durationSeconds: data.elapsedMs != null ? Math.round((Number(data.elapsedMs) / 1000) * 1000) / 1000 : null,
      warningCount: Number(data.warningCount || 0),
      warnings: data.warnings || [],
      warningSummary: Array.isArray(data.warnings) && data.warnings.length > 0
        ? data.warnings.map((warning) => warning.message || warning.kind || String(warning)).join(" ")
        : null,
      postRefreshIdleWait,
      postRefreshConsoleCheckSucceeded,
      postRefreshConsole,
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
