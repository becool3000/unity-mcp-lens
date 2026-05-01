#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  try {
    const result = await common.recoverUnityFrozenEditor(projectPath, {
      processId: common.getArgNumber(args, ["ProcessId"], 0),
      action: common.getArgString(args, ["Action"], "DetectOnly"),
      unityEditorPath: common.getArgString(args, ["UnityEditorPath"], ""),
      waitForBridgeReady: common.getArgBool(args, ["WaitForBridgeReady"], true),
      timeoutSeconds: common.getArgNumber(args, ["TimeoutSeconds"], 90),
      startupPromptAction: common.getArgString(args, ["StartupPromptAction"], "DetectOnly"),
      sceneReloadPromptAction: common.getArgString(args, ["SceneReloadPromptAction"], "DetectOnly"),
      expectedChangedPaths: common.getArgArray(args, ["ExpectedChangedPaths"], []),
    });

    process.stdout.write(`${JSON.stringify({ projectPath, ...result }, null, 2)}\n`);
    await common.shutdownUnityMcpSessions();
    process.exit(result?.success === false ? 1 : 0);
  } catch (error) {
    const message = String(error?.message || error);
    process.stdout.write(`${JSON.stringify({
      projectPath,
      success: false,
      error: message,
      installedCacheOrServerDriftHint: message.includes("RecoverFrozenEditor") || message.includes("DetectFrozenEditor")
        ? "The active Lens server may be older than this repo-local helper. In Unity, run Tools > Unity MCP Lens > Install/Refresh Lens Server, then retry."
        : null,
    }, null, 2)}\n`);
    await common.shutdownUnityMcpSessions();
    process.exit(1);
  }
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
