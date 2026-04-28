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

  return {
    name: common.valueOf(step, "name", "Name") || `step_${index + 1}`,
    tool,
    arguments: common.valueOf(step, "arguments", "Arguments") || {},
    timeoutSeconds: Math.max(1, Number(common.valueOf(step, "timeoutSeconds", "TimeoutSeconds") || defaultTimeoutSeconds)),
    expectReload: common.toBool(common.valueOf(step, "expectReload", "ExpectReload"), false),
    continueOnError: common.toBool(common.valueOf(step, "continueOnError", "ContinueOnError"), false),
  };
}

function compactToolResult(toolResult) {
  if (!toolResult || typeof toolResult !== "object") {
    return { success: false, error: "Tool returned no structured result." };
  }

  const success = common.valueOf(toolResult, "success", "Success");
  if (success !== true && success !== false && isUnwrappedDetailRefResult(toolResult)) {
    const refId = common.valueOf(toolResult, "refId", "RefId");
    const contentType = common.valueOf(toolResult, "contentType", "ContentType") || null;
    const createdUtc = common.valueOf(toolResult, "createdUtc", "CreatedUtc") || null;
    const meta = common.valueOf(toolResult, "meta", "Meta") || null;
    const payload = common.valueOf(toolResult, "payload", "Payload");
    const isError = common.valueOf(toolResult, "isError", "IsError") === true;
    const payloadSummary = compactDetailPayload(payload);

    return {
      success: !isError,
      message: isError ? `Detail ref '${refId}' resolved with an error payload.` : `Resolved detail ref '${refId}'.`,
      code: null,
      error: isError ? common.valueOf(toolResult, "error", "Error") || "Detail ref payload reported isError=true." : null,
      refId,
      contentType,
      data: {
        refId,
        contentType,
        createdUtc,
        meta,
        ...payloadSummary,
      },
    };
  }

  const data = common.valueOf(toolResult, "data", "Data");
  return {
    success: success === true,
    message: common.valueOf(toolResult, "message", "Message") || null,
    code: common.valueOf(toolResult, "code", "Code") || null,
    error: common.valueOf(toolResult, "error", "Error") || null,
    data,
  };
}

function isUnwrappedDetailRefResult(toolResult) {
  const refId = common.valueOf(toolResult, "refId", "RefId");
  if (!refId || typeof refId !== "string") {
    return false;
  }

  if (Object.prototype.hasOwnProperty.call(toolResult, "payload") ||
      Object.prototype.hasOwnProperty.call(toolResult, "Payload")) {
    return true;
  }

  return common.valueOf(toolResult, "isError", "IsError") === false;
}

function compactDetailPayload(payload) {
  const payloadBytes = estimateJsonBytes(payload);
  if (payloadBytes <= 4096) {
    return {
      payloadIncluded: true,
      payloadBytes,
      payload,
    };
  }

  return {
    payloadIncluded: false,
    payloadBytes,
    payloadSummary: summarizeDetailPayload(payload),
  };
}

function estimateJsonBytes(value) {
  try {
    return Buffer.byteLength(JSON.stringify(value ?? null), "utf8");
  } catch (_error) {
    return 0;
  }
}

function summarizeDetailPayload(payload) {
  if (!payload || typeof payload !== "object") {
    return {
      type: payload === null ? "null" : typeof payload,
    };
  }

  const data = common.valueOf(payload, "data", "Data");
  const summary = {
    type: Array.isArray(payload) ? "array" : "object",
    keys: Object.keys(payload).slice(0, 12),
    tool: common.valueOf(payload, "tool", "Tool") || null,
    bytes: common.valueOf(payload, "bytes", "Bytes") || null,
    dataType: data === null ? "null" : typeof data,
    hasData: data !== undefined && data !== null,
  };

  if (typeof data === "string") {
    summary.dataPreview = data.length <= 512 ? data : data.slice(0, 512);
    summary.dataPreviewTruncated = data.length > 512;
  } else if (data && typeof data === "object") {
    summary.dataKeys = Object.keys(data).slice(0, 12);
  }

  return summary;
}

function packsKey(packs) {
  return JSON.stringify(common.inferRequiredPacks("").concat(packs || []));
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
  const startedAt = Date.now();
  const results = [];
  let previousRequiredPacksKey = packsKey([]);
  let predictedPackTransitions = 0;
  let success = true;

  for (let index = 0; index < steps.length; index += 1) {
    const step = steps[index];
    const requiredPacks = common.inferRequiredPacks(step.tool);
    const requiredPacksKey = packsKey(requiredPacks);
    const packTransitionPredicted = requiredPacksKey !== previousRequiredPacksKey;
    if (packTransitionPredicted) {
      predictedPackTransitions += 1;
      previousRequiredPacksKey = requiredPacksKey;
    }

    const stepStartedAt = Date.now();
    try {
      const response = await common.invokeUnityMcpToolJson(projectPath, step.tool, step.arguments, {
        timeoutSeconds: step.timeoutSeconds,
        allowReconnect: step.expectReload,
        exactPacks: true,
      });
      const toolResult = compactToolResult(common.getToolObject(response));
      results.push({
        index,
        name: step.name,
        tool: step.tool,
        requiredPacks,
        packTransitionPredicted,
        durationMs: Date.now() - stepStartedAt,
        result: toolResult,
      });

      if (!toolResult.success) {
        success = false;
        if (!step.continueOnError) {
          break;
        }
      }
    } catch (error) {
      success = false;
      results.push({
        index,
        name: step.name,
        tool: step.tool,
        requiredPacks,
        packTransitionPredicted,
        durationMs: Date.now() - stepStartedAt,
        result: {
          success: false,
          error: error.message,
        },
      });
      if (!step.continueOnError) {
        break;
      }
    }
  }

  const output = {
    success,
    projectPath,
    stepCount: steps.length,
    completedStepCount: results.length,
    durationSeconds: Math.round(((Date.now() - startedAt) / 1000) * 1000) / 1000,
    predictedPackTransitions,
    results,
  };

  process.stdout.write(`${JSON.stringify(output, null, 2)}\n`);
  await common.shutdownUnityMcpSessions();
  process.exit(success ? 0 : 1);
}

main().catch((error) => {
  console.error(error.message);
  common.shutdownUnityMcpSessions().finally(() => process.exit(1));
});
