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
    implementation: "hybrid_public_batch_local_recovery",
    publicTool: "Unity_Batch_ExecuteWorkflow",
    localRecoveryRouting: true,
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

function summarizeStepData(toolResult) {
  const data = common.valueOf(toolResult, "data", "Data") || toolResult;
  const text = JSON.stringify(data || {});
  if (text.length <= 2048) {
    return {
      included: true,
      bytes: Buffer.byteLength(text, "utf8"),
      value: data,
    };
  }

  const selected = {};
  for (const key of ["success", "message", "classification", "supported", "found", "modalCount", "frozenCount", "applied", "reason", "detailRef"]) {
    const value = common.valueOf(data, key, key.charAt(0).toUpperCase() + key.slice(1));
    if (value !== undefined) {
      selected[key] = value;
    }
  }
  return {
    included: false,
    bytes: Buffer.byteLength(text, "utf8"),
    keys: data && typeof data === "object" ? Object.keys(data).slice(0, 16) : [],
    selected,
  };
}

function collectDetailRefs(value, path = "$", refs = []) {
  if (!value || typeof value !== "object" || refs.length >= 8) {
    return refs;
  }

  if (Array.isArray(value)) {
    for (let index = 0; index < value.length && refs.length < 8; index += 1) {
      collectDetailRefs(value[index], `${path}[${index}]`, refs);
    }
    return refs;
  }

  for (const [key, child] of Object.entries(value)) {
    const childPath = `${path}.${key}`;
    if (key === "detailRef" || key.endsWith("DetailRef")) {
      refs.push({
        path: childPath,
        refId: child && typeof child === "object" ? (child.refId || child.RefId) : child,
        tool: child && typeof child === "object" ? (child.tool || child.Tool) : null,
        bytes: child && typeof child === "object" ? (child.bytes || child.Bytes) : null,
      });
    }
    collectDetailRefs(child, childPath, refs);
    if (refs.length >= 8) {
      break;
    }
  }
  return refs;
}

async function runPublicBatch(projectPath, bridgeSteps, timeoutSeconds) {
  if (bridgeSteps.length === 0) {
    return {
      success: true,
      completedStepCount: 0,
      failedStepCount: 0,
      packTransitions: 0,
      restoredPacks: true,
      results: [],
    };
  }

  const response = await common.invokeUnityMcpToolJson(projectPath, "Unity_Batch_ExecuteWorkflow", {
    steps: bridgeSteps.map(toPublicWorkflowStep),
  }, {
    timeoutSeconds,
    exactPacks: true,
  });

  return common.getToolObject(response) || {
    success: false,
    error: "Unity_Batch_ExecuteWorkflow returned no structured payload.",
    results: [],
  };
}

async function runLocalRecoveryStep(projectPath, step, index) {
  const started = Date.now();
  const response = await common.invokeUnityMcpToolJson(projectPath, step.tool, step.arguments, {
    timeoutSeconds: step.timeoutSeconds,
  });
  const toolResult = common.getToolObject(response) || {
    success: false,
    error: `${step.tool} returned no structured payload.`,
  };
  const success = common.valueOf(toolResult, "success", "Success") !== false;
  return {
    index,
    name: step.name,
    tool: step.tool,
    requiredPacks: [],
    continueOnError: step.continueOnError,
    expectReload: step.expectReload,
    readOnlyExpected: step.readOnlyExpected,
    localRecoveryTool: true,
    success,
    message: common.valueOf(toolResult, "message", "Message") || null,
    error: common.valueOf(toolResult, "error", "Error") || null,
    durationMs: Date.now() - started,
    data: summarizeStepData(toolResult),
    detailRefs: collectDetailRefs(toolResult),
  };
}

async function runHybridWorkflow(projectPath, steps, defaultTimeoutSeconds) {
  const results = [];
  let completedStepCount = 0;
  let failedStepCount = 0;
  let packTransitions = 0;
  let restoredPacks = true;
  let success = true;
  let bridgeBuffer = [];

  async function flushBridgeBuffer() {
    if (bridgeBuffer.length === 0) {
      return true;
    }

    const timeoutSeconds = Math.max(
      defaultTimeoutSeconds,
      bridgeBuffer.reduce((sum, step) => sum + step.timeoutSeconds, 0) + 5
    );
    const batchResult = await runPublicBatch(projectPath, bridgeBuffer, timeoutSeconds);
    const batchSuccess = common.valueOf(batchResult, "success", "Success") === true;
    const batchRows = common.valueOf(batchResult, "results", "Results") || [];
    for (const row of batchRows) {
      results.push(row);
      completedStepCount += 1;
      if (common.valueOf(row, "success", "Success") === false) {
        failedStepCount += 1;
        success = false;
      }
    }
    packTransitions += Number(common.valueOf(batchResult, "packTransitions", "PackTransitions") || 0);
    restoredPacks = restoredPacks && common.valueOf(batchResult, "restoredPacks", "RestoredPacks") !== false;
    bridgeBuffer = [];
    if (!batchSuccess) {
      success = false;
    }
    return batchSuccess;
  }

  for (let index = 0; index < steps.length; index += 1) {
    const step = steps[index];
    if (!common.isLocalRecoveryTool(step.tool)) {
      bridgeBuffer.push(step);
      continue;
    }

    const bridgeOk = await flushBridgeBuffer();
    if (!bridgeOk && !step.continueOnError) {
      break;
    }

    try {
      const row = await runLocalRecoveryStep(projectPath, step, index);
      results.push(row);
      completedStepCount += 1;
      if (!row.success) {
        failedStepCount += 1;
        success = false;
        if (!step.continueOnError) {
          break;
        }
      }
    } catch (error) {
      failedStepCount += 1;
      success = false;
      results.push({
        index,
        name: step.name,
        tool: step.tool,
        requiredPacks: [],
        localRecoveryTool: true,
        success: false,
        errorKind: error?.name || "Error",
        error: error?.message || String(error),
      });
      if (!step.continueOnError) {
        break;
      }
    }
  }

  await flushBridgeBuffer();
  return {
    success: success && failedStepCount === 0 && restoredPacks,
    message: failedStepCount === 0
      ? `Executed ${completedStepCount} hybrid workflow step(s).`
      : `Executed ${completedStepCount} hybrid workflow step(s) with ${failedStepCount} failure(s).`,
    stepCount: steps.length,
    completedStepCount,
    failedStepCount,
    packTransitions,
    restoredPacks,
    results,
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
  const helperDiagnostics = buildHelperDiagnostics(steps);
  const startedAt = Date.now();

  try {
    const toolResult = await runHybridWorkflow(projectPath, steps, defaultTimeoutSeconds);
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
    const classification = await common.classifyUnityHelperFailure(projectPath, {
      errorMessage: message,
      timeoutSeconds: 6,
      maxItems: 8,
    });
    const publicToolHint = message.includes("Unity_Batch_ExecuteWorkflow") || message.includes("Batch_ExecuteWorkflow")
      ? "The active Lens server may be older than this repo-local helper. In Unity, run Tools > Unity MCP Lens > Install/Refresh Lens Server, then retry."
      : null;
    const output = {
      success: false,
      projectPath,
      durationSeconds: Math.round(((Date.now() - startedAt) / 1000) * 1000) / 1000,
      error: message,
      classification: classification.classification,
      recommendedPath: classification.recommendedPath,
      nativeModal: classification.nativeModal,
      frozenEditor: classification.frozenEditor,
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
