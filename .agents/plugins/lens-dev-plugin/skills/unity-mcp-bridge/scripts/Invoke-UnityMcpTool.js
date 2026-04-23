#!/usr/bin/env node

const common = require("./UnityMcpCommon");

async function main() {
  const toolName = process.argv[2];
  const argsJson = process.argv[3] || process.env.UNITY_MCP_TOOL_ARGS_JSON || "{}";

  if (!toolName) {
    console.error("Usage: node Invoke-UnityMcpTool.js <ToolName> [jsonArgs]");
    process.exit(2);
    return;
  }

  let toolArgs;
  try {
    toolArgs = JSON.parse(argsJson);
  } catch (error) {
    console.error(`Invalid JSON arguments: ${error.message}`);
    process.exit(2);
    return;
  }

  const timeoutSeconds = Math.max(
    1,
    Math.ceil(Number(process.env.UNITY_MCP_TOOL_TIMEOUT_MS || 45000) / 1000)
  );
  const expectReload = process.env.UNITY_MCP_EXPECT_RELOAD === "1";

  const response = await common.invokeUnityMcpToolJson(process.cwd(), toolName, toolArgs, {
    timeoutSeconds,
    allowReconnect: expectReload,
    retryDelayMs: Number(process.env.UNITY_MCP_EXPECT_RELOAD_RETRY_DELAY_MS || 1500),
  });

  process.stdout.write(`${JSON.stringify(response, null, 2)}\n`);
  await common.shutdownUnityMcpSessions();
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
