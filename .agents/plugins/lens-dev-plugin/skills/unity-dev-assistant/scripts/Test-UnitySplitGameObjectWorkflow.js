#!/usr/bin/env node

const { spawnSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");
const common = require("../../unity-mcp-bridge/scripts/UnityMcpCommon");

function buildSteps(projectPath, objectName, keepObject) {
  const renamedObjectName = `${objectName}_Renamed`;
  const steps = [
    {
      name: "health",
      tool: "Unity_Editor_HealthCheckFast",
      arguments: {
        ProjectPath: projectPath,
      },
    },
    {
      name: "preview_create_empty",
      tool: "Unity_GameObject_PreviewCreate",
      arguments: {
        name: objectName,
        objectKind: "empty",
        position: [1, 2, 3],
        rotation: [0, 0, 0],
        scale: [1, 1, 1],
      },
    },
    {
      name: "create_empty",
      tool: "Unity_GameObject_Create",
      arguments: {
        name: objectName,
        objectKind: "empty",
        position: [1, 2, 3],
        rotation: [0, 0, 0],
        scale: [1, 1, 1],
      },
    },
    {
      name: "inspect_created",
      tool: "Unity_GameObject_Inspect",
      arguments: {
        mode: "find",
        target: objectName,
        searchMethod: "by_name",
        searchInactive: true,
      },
    },
    {
      name: "preview_transform_rename",
      tool: "Unity_GameObject_PreviewChanges",
      arguments: {
        target: objectName,
        searchMethod: "by_name",
        name: renamedObjectName,
        position: [2, 3, 4],
        rotation: [0, 45, 0],
        scale: [1.25, 1.25, 1.25],
      },
    },
    {
      name: "apply_transform_rename",
      tool: "Unity_GameObject_ApplyChanges",
      arguments: {
        target: objectName,
        searchMethod: "by_name",
        name: renamedObjectName,
        position: [2, 3, 4],
        rotation: [0, 45, 0],
        scale: [1.25, 1.25, 1.25],
      },
    },
    {
      name: "preview_add_box_collider",
      tool: "Unity_GameObject_PreviewComponentChanges",
      arguments: {
        operation: "add",
        target: renamedObjectName,
        searchMethod: "by_name",
        componentName: "BoxCollider",
      },
    },
    {
      name: "apply_add_box_collider",
      tool: "Unity_GameObject_ApplyComponentChanges",
      arguments: {
        operation: "add",
        target: renamedObjectName,
        searchMethod: "by_name",
        componentName: "BoxCollider",
      },
    },
    {
      name: "list_components",
      tool: "Unity_GameObject_ListComponents",
      arguments: {
        target: renamedObjectName,
        searchMethod: "by_name",
        searchInactive: true,
      },
    },
    {
      name: "get_transform",
      tool: "Unity_GameObject_GetComponent",
      arguments: {
        target: renamedObjectName,
        searchMethod: "by_name",
        componentName: "Transform",
        componentIndex: 0,
      },
    },
    {
      name: "resolve_stable_path",
      tool: "Unity_Object_ResolveStablePath",
      arguments: {
        target: renamedObjectName,
        mode: "scene",
        includeInactive: true,
        maxCandidates: 20,
      },
    },
  ];

  if (!keepObject) {
    steps.push(
      {
        name: "preview_delete",
        tool: "Unity_GameObject_PreviewDelete",
        arguments: {
          target: renamedObjectName,
          searchMethod: "by_name",
          searchInactive: true,
        },
      },
      {
        name: "delete",
        tool: "Unity_GameObject_Delete",
        arguments: {
          target: renamedObjectName,
          searchMethod: "by_name",
          searchInactive: true,
        },
      }
    );
  }

  return steps;
}

async function main() {
  const args = common.parseCliArgs(process.argv.slice(2));
  const projectPath = common.resolveProjectPath(common.getArgString(args, ["ProjectPath"], process.cwd()));
  const objectName =
    common.getArgString(args, ["ObjectName"], "") ||
    `CodexSplitGameObjectSmoke_${Date.now().toString(36)}_${process.pid}`;
  const timeoutSeconds = common.getArgNumber(args, ["TimeoutSeconds"], 60);
  const keepObject = common.getArgBool(args, ["KeepObject"], false);
  const waitForEditorIdle = common.getArgBool(args, ["WaitForEditorIdle"], true);
  let idleWait = null;

  if (waitForEditorIdle) {
    idleWait = await common.waitUnityEditorIdle(projectPath, {
      timeoutSeconds: common.getArgNumber(args, ["IdleTimeoutSeconds"], 60),
      stablePollCount: common.getArgNumber(args, ["IdleStablePollCount"], 3),
      pollIntervalSeconds: common.getArgNumber(args, ["IdlePollIntervalSeconds"], 0.5),
      postIdleDelaySeconds: common.getArgNumber(args, ["PostIdleDelaySeconds"], 1.0),
    });
    if (!idleWait.success) {
      console.log(JSON.stringify({
        success: false,
        message: idleWait.message || "Unity editor did not become idle before the split GameObject workflow.",
        projectPath,
        objectName,
        editorIdle: idleWait,
      }, null, 2));
      process.exit(1);
    }
  }

  const tempDirectory = path.join(os.tmpdir(), "codex-unity");
  common.ensureDir(tempDirectory);
  const stepsPath = path.join(tempDirectory, `unity-split-gameobject-workflow-${Date.now()}-${process.pid}.json`);
  common.writeJsonFile(stepsPath, buildSteps(projectPath, objectName, keepObject));
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
