#!/usr/bin/env node

const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const expectedScenes = common.getArgArray(args, ["ExpectedScenes"], []);
  if (expectedScenes.length === 0) {
    throw new Error("Provide --ExpectedScenes.");
  }

  const result = common.testUnityBuildSceneList(projectPath, expectedScenes);
  console.log(JSON.stringify(result, null, 2));
  process.exit(result.exactMatch ? 0 : 1);
}

try {
  main();
} catch (error) {
  console.error(error.message);
  process.exit(1);
}
