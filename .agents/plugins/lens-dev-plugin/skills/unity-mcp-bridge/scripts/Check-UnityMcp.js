#!/usr/bin/env node

const common = require("./UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.getArgString(args, ["ProjectPath"], process.cwd());
  const includeDiagnostics = common.getArgBool(args, ["IncludeDiagnostics"], false);
  const serverName = common.getArgString(args, ["ServerName"], "unity-mcp");
  const editorLogTail = common.getArgNumber(args, ["EditorLogTail"], 400);

  const check = await common.checkUnityMcp(projectPath, {
    includeDiagnostics,
    serverName,
    editorLogTail,
  });

  console.log(JSON.stringify(includeDiagnostics ? check.result : check.compactResult, null, 2));
  process.exit(check.exitCode);
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
