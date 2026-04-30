#!/usr/bin/env node

const fs = require("fs");
const common = require("./UnityMcpCommon");

function loadSteps(args) {
  const stepsPath = common.getArgString(args, ["StepsPath"], "");
  const stepsJson = common.getArgString(args, ["StepsJson"], "") || process.env.UNITY_MCP_BATCH_STEPS_JSON || "";
  if (stepsPath) {
    return JSON.parse(fs.readFileSync(stepsPath, "utf8").replace(/^\uFEFF/, ""));
  }
  if (stepsJson) {
    return JSON.parse(stepsJson);
  }
  throw new Error("Provide --StepsPath or --StepsJson.");
}

function normalizeStep(step, index, defaultTimeoutSeconds) {
  const tool = common.valueOf(step, "tool", "Tool", "toolName", "ToolName");
  if (!tool || typeof tool !== "string") {
    throw new Error(`Batch step ${index + 1} requires a string 'tool'.`);
  }

  const requiredPacks = common.valueOf(step, "requiredPacks", "RequiredPacks");
  return {
    name: common.valueOf(step, "name", "Name") || `step_${index + 1}`,
    tool,
    arguments: common.valueOf(step, "arguments", "Arguments") || {},
    requiredPacks: Array.isArray(requiredPacks) ? requiredPacks : undefined,
    continueOnError: common.toBool(common.valueOf(step, "continueOnError", "ContinueOnError"), false),
    expectReload: common.toBool(common.valueOf(step, "expectReload", "ExpectReload"), false),
    readOnlyExpected: common.toBool(common.valueOf(step, "readOnlyExpected", "ReadOnlyExpected"), false),
    timeoutSeconds: Math.max(1, Number(common.valueOf(step, "timeoutSeconds", "TimeoutSeconds") || defaultTimeoutSeconds)),
  };
}

function toPublicWorkflowStep(step) {
  const publicStep = {
    name: step.name,
    tool: step.tool,
    arguments: step.arguments,
    continueOnError: step.continueOnError,
    expectReload: step.expectReload,
    readOnlyExpected: step.readOnlyExpected,
  };
  if (step.requiredPacks && step.requiredPacks.length > 0) {
    publicStep.requiredPacks = step.requiredPacks;
  }
  return publicStep;
}

function buildHelperDiagnostics(steps) {
  const usageReportPacks = common.inferRequiredPacks("Unity_GetLensUsageReport");
  return {
    implementation: "public_tool",
    publicTool: "Unity_Batch_ExecuteWorkflow",
    usageReportPackInference: {
      inferredPacks: usageReportPacks,
      hasDebug: usageReportPacks.includes("debug"),
    },
    stepPackInference: steps.map((step) => ({
      name: step.name,
      tool: step.tool,
      inferredPacks: common.inferRequiredPacks(step.tool),
      requiredPacks: step.requiredPacks || null,
    })),
  };
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const defaultTimeoutSeconds = common.getArgNumber(args, ["TimeoutSeconds"], 45);
  const rawSteps = loadSteps(args);
  if (!Array.isArray(rawSteps) || rawSteps.length === 0) {
    throw new Error("Batch steps must be a non-empty JSON array.");
  }

  const steps = rawSteps.map((step, index) => normalizeStep(step, index, defaultTimeoutSeconds));
  const workflowTimeoutSeconds = Math.max(
    defaultTimeoutSeconds,
    steps.reduce((sum, step) => sum + step.timeoutSeconds, 0) + 5
  );
  const helperDiagnostics = buildHelperDiagnostics(steps);
  const startedAt = Date.now();

  try {
    const response = await common.invokeUnityMcpToolJson(projectPath, "Unity_Batch_ExecuteWorkflow", {
      steps: steps.map(toPublicWorkflowStep),
    }, {
      timeoutSeconds: workflowTimeoutSeconds,
      exactPacks: true,
    });

    const toolResult = common.getToolObject(response) || {
      success: false,
      error: "Unity_Batch_ExecuteWorkflow returned no structured payload.",
    };
    const success = common.valueOf(toolResult, "success", "Success") === true;
    const output = {
      projectPath,
      durationSeconds: Math.round(((Date.now() - startedAt) / 1000) * 1000) / 1000,
      helperDiagnostics,
      ...toolResult,
    };

    process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
    await common.shutdownUnityMcpSessions();
    process.exit(success ? 0 : 1);
  } catch (error) {
    const message = String(error?.message || error);
    let nativeModal = null;
    try {
      nativeModal = await common.detectUnityNativeModals(projectPath, { timeoutSeconds: 6, maxItems: 8 });
    } catch (_modalError) {
    }
    const modalBlocking = nativeModal?.found === true;
    const publicToolHint = message.includes("Unity_Batch_ExecuteWorkflow") || message.includes("Batch_ExecuteWorkflow")
      ? "The active Lens server may be older than this repo-local helper. In Unity, run Tools > Unity MCP Lens > Install/Refresh Lens Server, then retry."
      : null;
    const output = {
      success: false,
      projectPath,
      durationSeconds: Math.round(((Date.now() - startedAt) / 1000) * 1000) / 1000,
      error: message,
      classification: modalBlocking ? "EditorModalBlocking" : null,
      nativeModal,
      helperDiagnostics: {
        ...helperDiagnostics,
        installedCacheOrServerDriftHint: publicToolHint,
      },
    };
    process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
    await common.shutdownUnityMcpSessions();
    process.exit(1);
  }
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
