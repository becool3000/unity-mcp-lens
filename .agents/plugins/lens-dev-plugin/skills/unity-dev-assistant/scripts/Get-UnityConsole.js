#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const result = await common.getUnityConsoleEntries(projectPath, {
    types: common.getArgArray(args, ["Types"], ["Error", "Warning", "Log"]),
    count: common.getArgNumber(args, ["Count"], 100),
    filterText: common.getArgString(args, ["FilterText"], ""),
    sinceTimestamp: common.getArgString(args, ["SinceTimestamp"], ""),
    format: common.getArgString(args, ["Format"], "Detailed"),
    excludeMcpNoise: common.getArgBool(args, ["ExcludeMcpNoise"], true),
    includeStacktrace: common.getArgBool(args, ["IncludeStacktrace"], true),
    timeoutSeconds: common.getArgNumber(args, ["TimeoutSeconds"], 30),
  });
  console.log(JSON.stringify(result, null, 2));
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
