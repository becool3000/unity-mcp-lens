#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const result = await common.resolveUnitySceneReloadPrompt(projectPath, {
    processId: common.getArgNumber(args, ["ProcessId"], 0),
    action: common.getArgString(args, ["Action"], "DetectOnly"),
    expectedChangedPaths: common.getArgArray(args, ["ExpectedChangedPaths"], []),
    timeoutSeconds: common.getArgNumber(args, ["TimeoutSeconds"], 10),
    waitForBridgeReady: common.getArgBool(args, ["WaitForBridgeReady"], true),
  });

  process.stdout.write(`${JSON.stringify({ projectPath, ...result }, null, 2)}\n`);
  await common.shutdownUnityMcpSessions();
  process.exit(result?.success === false ? 1 : 0);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
