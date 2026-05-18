#!/usr/bin/env node

const { spawnSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

function readJsonArg(args, names, pathNames, fallback = null) {
  const jsonPath = common.getArgString(args, pathNames, "");
  if (jsonPath) {
    return JSON.parse(fs.readFileSync(path.resolve(jsonPath), "utf8").replace(/^\uFEFF/, ""));
  }

  const json = common.getArgString(args, names, "");
  if (json) {
    return JSON.parse(json);
  }

  return fallback;
}

function asArray(value) {
  if (value == null) return [];
  return Array.isArray(value) ? value : [value];
}

function addPreviewApplySteps(steps, options) {
  steps.push({
    name: options.previewName,
    tool: options.previewTool,
    arguments: options.arguments,
  });

  if (options.includeApply) {
    steps.push({
      name: options.applyName,
      tool: options.applyTool,
      arguments: options.arguments,
    });
  }
}

function buildSteps(args, projectPath) {
  const includeApply = common.getArgBool(args, ["Apply", "IncludeApply"], false);
  const steps = [
    {
      name: "health",
      tool: "Unity_Editor_HealthCheckFast",
      arguments: {
        ProjectPath: projectPath,
      },
    },
  ];

  const hierarchyTarget = common.getArgString(args, ["HierarchyTarget"], "");
  const nodes = asArray(readJsonArg(args, ["NodesJson"], ["NodesPath"], null));
  if (hierarchyTarget && nodes.length > 0) {
    addPreviewApplySteps(steps, {
      includeApply,
      previewName: "preview_ensure_hierarchy",
      previewTool: "Unity_UI_PreviewEnsureHierarchy",
      applyName: "apply_ensure_hierarchy",
      applyTool: "Unity_UI_ApplyEnsureHierarchy",
      arguments: {
        Target: hierarchyTarget,
        SearchMethod: common.getArgString(args, ["HierarchySearchMethod", "SearchMethod"], "by_name"),
        PreviewOnly: !includeApply,
        Nodes: nodes,
      },
    });
  }

  const bindingTarget = common.getArgString(args, ["BindingTarget"], "");
  const bindings = asArray(readJsonArg(args, ["BindingsJson"], ["BindingsPath"], null));
  if (bindingTarget && bindings.length > 0) {
    addPreviewApplySteps(steps, {
      includeApply,
      previewName: "preview_bind_serialized_references",
      previewTool: "Unity_Scene_PreviewBindSerializedReferences",
      applyName: "apply_bind_serialized_references",
      applyTool: "Unity_Scene_ApplyBindSerializedReferences",
      arguments: {
        Target: bindingTarget,
        SearchMethod: common.getArgString(args, ["BindingSearchMethod", "SearchMethod"], "by_name"),
        Bindings: bindings,
      },
    });
  }

  const layoutTarget = common.getArgString(args, ["LayoutTarget"], "");
  const layoutProperties = readJsonArg(args, ["LayoutPropertiesJson", "PropertiesJson"], ["LayoutPropertiesPath", "PropertiesPath"], null);
  if (layoutTarget) {
    addPreviewApplySteps(steps, {
      includeApply,
      previewName: "preview_layout_properties",
      previewTool: "Unity_UI_PreviewLayoutProperties",
      applyName: "apply_layout_properties",
      applyTool: "Unity_UI_ApplyLayoutProperties",
      arguments: {
        Target: layoutTarget,
        SearchMethod: common.getArgString(args, ["LayoutSearchMethod", "SearchMethod"], "by_name"),
        TargetPath: common.getArgString(args, ["LayoutTargetPath"], "."),
        ...(layoutProperties || {}),
      },
    });
  }

  const verifyTargets = asArray(readJsonArg(args, ["VerifyTargetsJson", "TargetsJson"], ["VerifyTargetsPath", "TargetsPath"], null));
  const verifyAssertions = asArray(readJsonArg(args, ["VerifyAssertionsJson", "AssertionsJson"], ["VerifyAssertionsPath", "AssertionsPath"], null));
  if (verifyTargets.length > 0 && verifyAssertions.length > 0) {
    steps.push({
      name: "verify_screen_layout",
      tool: "Unity_UI_VerifyScreenLayout",
      arguments: {
        Targets: verifyTargets,
        Assertions: verifyAssertions,
      },
    });
  }

  if (steps.length === 1) {
    throw new Error(
      "Provide at least one Phase 12 operation: --HierarchyTarget with --NodesJson/--NodesPath, " +
        "--BindingTarget with --BindingsJson/--BindingsPath, --LayoutTarget, or " +
        "--VerifyTargetsJson/--VerifyTargetsPath plus --VerifyAssertionsJson/--VerifyAssertionsPath."
    );
  }

  return steps;
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const timeoutSeconds = common.getArgNumber(args, ["TimeoutSeconds"], 60);

  if (common.getArgBool(args, ["WaitForEditorIdle"], true)) {
    const idleWait = await common.waitUnityEditorIdle(projectPath, {
      timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
      stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
      pollIntervalSeconds: common.getArgNumber(args, ["IdlePollIntervalSeconds"], 0.5),
      postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
    });
    if (!idleWait.success) {
      console.log(JSON.stringify({
        success: false,
        message: idleWait.message || "Unity editor did not become idle before the UI/scene-binding workflow.",
        projectPath,
        editorIdle: idleWait,
      }, null, 2));
      process.exit(1);
    }
  }

  const tempDirectory = path.join(os.tmpdir(), "codex-unity");
  common.ensureDir(tempDirectory);
  const stepsPath = path.join(tempDirectory, `unity-ui-scene-binding-workflow-${Date.now()}-${process.pid}.json`);
  common.writeJsonFile(stepsPath, buildSteps(args, projectPath));
  let exitCode = 1;

  try {
    const batchScript = path.resolve(__dirname, "../../unity-mcp-bridge/scripts/Invoke-UnityMcpBatch.js");
    const result = spawnSync(process.execPath, [
      batchScript,
      "--ProjectPath",
      projectPath,
      "--StepsPath",
      stepsPath,
      "--TimeoutSeconds",
      String(timeoutSeconds),
    ], {
      stdio: "inherit",
      windowsHide: true,
    });

    if (result.error) {
      throw result.error;
    }
    exitCode = typeof result.status === "number" ? result.status : 1;
  } finally {
    try {
      fs.unlinkSync(stepsPath);
    } catch (_error) {
    }
  }

  process.exit(exitCode);
}

main().catch((error) => {
  console.error(error && error.stack ? error.stack : String(error));
  process.exit(1);
});
