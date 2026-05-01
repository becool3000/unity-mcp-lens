#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const result = await common.detectUnityFrozenEditor(projectPath, {
    processId: common.getArgNumber(args, ["ProcessId"], 0),
    includeWindows: common.getArgBool(args, ["IncludeWindows"], true),
    includeBridgeStatus: common.getArgBool(args, ["IncludeBridgeStatus"], true),
    staleReadySeconds: common.getArgNumber(args, ["StaleReadySeconds"], 30),
    maxItems: common.getArgNumber(args, ["MaxItems"], 8),
    timeoutSeconds: common.getArgNumber(args, ["TimeoutSeconds"], 8),
  });

  process.stdout.write(`${JSON.stringify({ projectPath, ...result }, null, 2)}\n`);
  await common.shutdownUnityMcpSessions();
  process.exit(result?.success === false ? 1 : 0);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
