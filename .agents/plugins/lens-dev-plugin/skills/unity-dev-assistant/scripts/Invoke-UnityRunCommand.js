#!/usr/bin/env node

const fs = require("fs");
const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  let code = common.getArgString(args, ["Code"], "");
  const codePath = common.getArgString(args, ["CodePath"], "");
  if (codePath) {
    code = fs.readFileSync(codePath, "utf8");
  }
  if (!code.trim()) {
    throw new Error("Provide --Code or --CodePath.");
  }

  const waitForEditorIdle = common.getArgBool(args, ["WaitForEditorIdle"], true);
  let idleResult = null;
  if (waitForEditorIdle) {
    idleResult = await common.waitUnityEditorIdle(projectPath, {
      timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
      stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
      pollIntervalSeconds: common.getArgNumber(args, ["IdlePollIntervalSeconds"], 0.5),
      postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
    });
    if (!idleResult.success) {
      console.log(JSON.stringify({ success: false, error: idleResult.message, editorIdle: idleResult }, null, 2));
      process.exit(1);
      return;
    }
  }

  const result = await common.invokeUnityRunCommandObject(projectPath, {
    code,
    title: common.getArgString(args, ["Title"], ""),
    usings: common.getArgArray(args, ["Using", "Usings"], []),
    timeoutSeconds: common.getArgNumber(args, ["TimeoutSeconds"], 60),
    pausePlayMode: common.getArgBool(args, ["PausePlayMode"], false),
    stepFrames: common.getArgNumber(args, ["StepFrames"], 0),
    restorePauseState: common.getArgBool(args, ["RestorePauseState"], true),
  });

  if (idleResult) {
    result.editorIdle = idleResult;
  }
  console.log(JSON.stringify(result, null, 2));
  process.exit(result && result.success === false ? 1 : 0);
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
