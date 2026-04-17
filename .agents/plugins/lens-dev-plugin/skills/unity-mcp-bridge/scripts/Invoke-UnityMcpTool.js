const { spawn } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const toolName = process.argv[2];
const argsJson = process.argv[3] || process.env.UNITY_MCP_TOOL_ARGS_JSON || "{}";

if (!toolName) {
  console.error("Usage: node Invoke-UnityMcpTool.js <ToolName> [jsonArgs]");
  process.exit(2);
}

let toolArgs;
try {
  toolArgs = JSON.parse(argsJson);
} catch (error) {
  console.error(`Invalid JSON arguments: ${error.message}`);
  process.exit(2);
}

const settingsPath =
  process.env.UNITY_MCP_SETTINGS_PATH ||
  path.join(os.homedir(), ".codex", "unity-mcp-settings.json");

function getDefaultLensPath() {
  const serverDir = path.join(os.homedir(), ".unity", "unity-mcp-lens");
  if (process.platform === "win32") {
    return path.join(serverDir, "unity_mcp_lens_win.exe");
  }

  if (process.platform === "darwin") {
    const arch = os.arch() === "arm64" ? "arm64" : "x64";
    return path.join(serverDir, `unity_mcp_lens_mac_${arch}`);
  }

  return path.join(serverDir, "unity_mcp_lens_linux");
}

const lensPath =
  process.env.UNITY_MCP_LENS_PATH ||
  getDefaultLensPath();

const defaultSettings = Object.freeze({
  directRelayExperimental: false,
});

function pathExists(candidatePath) {
  try {
    return !!candidatePath && fs.existsSync(candidatePath);
  } catch (_error) {
    return false;
  }
}

function parseBoolean(value, fallback) {
  if (typeof value === "boolean") {
    return value;
  }

  if (typeof value !== "string") {
    return fallback;
  }

  const normalized = value.trim().toLowerCase();
  if (["1", "true", "yes", "on"].includes(normalized)) {
    return true;
  }

  if (["0", "false", "no", "off"].includes(normalized)) {
    return false;
  }

  return fallback;
}

function loadSettings() {
  let fileSettings = {};
  try {
    if (pathExists(settingsPath)) {
      const rawSettings = fs.readFileSync(settingsPath, "utf8").replace(/^\uFEFF/, "");
      fileSettings = JSON.parse(rawSettings);
    }
  } catch (_error) {
    fileSettings = {};
  }

  return {
    directRelayExperimental: parseBoolean(
      process.env.UNITY_MCP_DIRECT_RELAY_EXPERIMENTAL ??
        fileSettings.directRelayExperimental,
      defaultSettings.directRelayExperimental
    ),
  };
}

const settings = loadSettings();
if (settings.directRelayExperimental) {
  console.error(
    "Direct relay experimental mode is no longer supported by Invoke-UnityMcpTool.js. Use unity-mcp-lens only."
  );
  process.exit(4);
}

if (!pathExists(lensPath)) {
  console.error(
    `Unity MCP Lens server not found at '${lensPath}'. Reinstall or republish unity-mcp-lens before running helper scripts.`
  );
  process.exit(4);
}

const expectReload = process.env.UNITY_MCP_EXPECT_RELOAD === "1";
const maxAttempts = expectReload ? 2 : 1;
const retryDelayMs = Number(process.env.UNITY_MCP_EXPECT_RELOAD_RETRY_DELAY_MS || 1500);
const perAttemptTimeoutMs = Number(process.env.UNITY_MCP_TOOL_TIMEOUT_MS || 45000);

function normalizeToolName(value) {
  return typeof value === "string" ? value.replace(/\./g, "_") : "";
}

const foundationToolNames = new Set(
  [
    "Unity.ListToolPacks",
    "Unity.SetToolPacks",
    "Unity.ReadDetailRef",
    "Unity.GetLensHealth",
    "Unity.ReadConsole",
    "Unity.ListResources",
    "Unity.ReadResource",
    "Unity.FindInFile",
    "Unity.GetSha",
    "Unity.ValidateScript",
    "Unity.ManageScript_capabilities",
    "Unity.Project.GetInfo",
  ].map(normalizeToolName)
);

const exactPackMap = new Map(
  Object.entries({
    Unity_ManageEditor: ["console"],
    Unity_GetConsoleLogs: ["console"],
    Unity_ManageMenuItem: ["console"],
    Unity_RunCommand: ["scripting"],
    Unity_ManageScript: ["scripting"],
    Unity_ManageShader: ["scripting"],
    Unity_Resource_Write: ["scripting", "assets"],
    Unity_Resource_Delete: ["full"],
    Unity_ManageAsset: ["assets"],
    Unity_ImportExternalModel: ["assets"],
    Unity_Project_ManagePackages: ["full"],
    Unity_Object_ValidateReferences: ["project"],
    Unity_Project_ScanMissingScripts: ["project"],
    Unity_Runtime_GetVisualBoundsSnapshot: ["scene"],
  })
);

const prefixPackMap = [
  { prefix: "Unity_UI_", packs: ["ui"] },
  { prefix: "Unity_Scene_", packs: ["scene"] },
  { prefix: "Unity_ManageGameObject", packs: ["scene"] },
  { prefix: "Unity_ManageScene", packs: ["scene"] },
  { prefix: "Unity_Tilemap_", packs: ["scene"] },
  { prefix: "Unity_Prefab_", packs: ["assets"] },
  { prefix: "Unity_Asset_", packs: ["assets"] },
  { prefix: "Unity_Tile_", packs: ["assets"] },
  { prefix: "Unity_Project_", packs: ["project"] },
  { prefix: "Unity_Profiler_", packs: ["debug"] },
];

function inferRequiredPacks(toolName) {
  const normalizedToolName = normalizeToolName(toolName);
  if (!normalizedToolName || foundationToolNames.has(normalizedToolName)) {
    return [];
  }

  const requiredPacks = new Set();

  const exactPacks = exactPackMap.get(normalizedToolName);
  if (Array.isArray(exactPacks)) {
    for (const pack of exactPacks) {
      requiredPacks.add(pack);
    }
  }

  for (const entry of prefixPackMap) {
    if (normalizedToolName.startsWith(entry.prefix)) {
      for (const pack of entry.packs) {
        requiredPacks.add(pack);
      }
    }
  }

  return Array.from(requiredPacks).slice(0, 2);
}

function getToolCallOutcome(message) {
  const result = message?.result;
  const structured = result?.structuredContent;
  if (structured && typeof structured === "object") {
    const success = structured.success !== false && result?.isError !== true;
    const error =
      structured.error ||
      structured.message ||
      result?.content?.find?.((item) => item?.type === "text")?.text ||
      "Unity MCP tool call failed.";
    return { success, error };
  }

  if (result?.isError === true) {
    const error =
      result?.content?.find?.((item) => item?.type === "text")?.text ||
      "Unity MCP tool call failed.";
    return { success: false, error };
  }

  return { success: true, error: null };
}

function writeFramedMessage(child, payload) {
  const body = Buffer.from(JSON.stringify(payload), "utf8");
  child.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
  child.stdin.write(body);
}

function writeJsonLineMessage(child, payload) {
  child.stdin.write(`${JSON.stringify(payload)}\n`);
}

function runLensAttempt() {
  return new Promise((resolve, reject) => {
    const requiredPacks = inferRequiredPacks(toolName);
    const normalizedToolName = normalizeToolName(toolName);
    const shouldPrimePacks =
      requiredPacks.length > 0 && normalizedToolName !== normalizeToolName("Unity.SetToolPacks");

    const child = spawn(lensPath, [], {
      cwd: process.cwd(),
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    let buffer = Buffer.alloc(0);
    let initialized = false;
    let completed = false;
    let awaitingPackSetup = shouldPrimePacks;
    const stderrChunks = [];

    function sendToolCall(id, name, args) {
      writeFramedMessage(child, {
        jsonrpc: "2.0",
        id,
        method: "tools/call",
        params: {
          name,
          arguments: args,
        },
      });
    }

    const timeoutHandle = setTimeout(() => {
      if (completed) {
        return;
      }

      completed = true;
      try {
        child.kill();
      } catch {
      }

      const error = new Error("Timed out waiting for Unity MCP tool response");
      error.exitCode = 3;
      reject(error);
    }, perAttemptTimeoutMs);

    function finishError(message, exitCode = 1) {
      if (completed) {
        return;
      }

      completed = true;
      clearTimeout(timeoutHandle);
      try {
        child.kill();
      } catch {
      }

      const error = new Error(message);
      error.exitCode = exitCode;
      reject(error);
    }

    function parseFrames() {
      while (true) {
        const separator = buffer.indexOf(Buffer.from("\r\n\r\n"));
        if (separator === -1) {
          return;
        }

        const header = buffer.subarray(0, separator).toString("ascii");
        const match = /Content-Length:\s*(\d+)/i.exec(header);
        if (!match) {
          finishError("Missing Content-Length header", 2);
          return;
        }

        const contentLength = Number(match[1]);
        const messageEnd = separator + 4 + contentLength;
        if (buffer.length < messageEnd) {
          return;
        }

        const body = buffer.subarray(separator + 4, messageEnd).toString("utf8");
        buffer = buffer.subarray(messageEnd);

        let message;
        try {
          message = JSON.parse(body);
        } catch (error) {
          finishError(`Invalid JSON message from unity-mcp-lens: ${error.message}`, 2);
          return;
        }

        if (message.id === 1 && !initialized) {
          initialized = true;
          writeFramedMessage(child, {
            jsonrpc: "2.0",
            method: "notifications/initialized",
            params: {},
          });
          if (awaitingPackSetup) {
            sendToolCall(2, "Unity_SetToolPacks", { packs: requiredPacks });
          } else {
            sendToolCall(2, toolName, toolArgs);
          }
          continue;
        }

        if (message.id === 2 && awaitingPackSetup && !completed) {
          const setupOutcome = getToolCallOutcome(message);
          if (!setupOutcome.success) {
            finishError(
              `Failed to activate required Lens packs [${requiredPacks.join(", ")}] for '${toolName}': ${setupOutcome.error}`,
              5
            );
            return;
          }

          awaitingPackSetup = false;
          sendToolCall(3, toolName, toolArgs);
          continue;
        }

        if ((message.id === 2 || message.id === 3) && !completed) {
          completed = true;
          clearTimeout(timeoutHandle);
          resolve(`${JSON.stringify(message, null, 2)}\n`);
          return;
        }
      }
    }

    child.stdout.on("data", (chunk) => {
      buffer = Buffer.concat([buffer, chunk]);
      parseFrames();
    });

    child.stderr.on("data", (chunk) => {
      stderrChunks.push(chunk);
    });

    child.on("error", (error) => {
      finishError(`Failed to start unity-mcp-lens: ${error.message}`, 1);
    });

    child.on("exit", (code) => {
      if (completed) {
        return;
      }

      clearTimeout(timeoutHandle);
      const stderrText = Buffer.concat(stderrChunks).toString("utf8").trim();
      if (stderrText) {
        finishError(stderrText, code || 1);
        return;
      }

      finishError(`unity-mcp-lens exited before tool response (code ${code || 1}).`, code || 1);
    });

    writeFramedMessage(child, {
      jsonrpc: "2.0",
      id: 1,
      method: "initialize",
      params: {
        protocolVersion: "2025-06-18",
        capabilities: {},
        clientInfo: {
          name: "codex-unity-tool-lens",
          version: "1.0.0",
        },
      },
    });
  });
}

async function runAttempt() {
  return await runLensAttempt();
}

async function main() {
  let lastError = null;

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      const output = await runAttempt();
      process.stdout.write(output);
      process.exit(0);
      return;
    } catch (error) {
      lastError = error;
      if (attempt >= maxAttempts) {
        console.error(error.message);
        process.exit(error.exitCode || 1);
        return;
      }

      await new Promise((resolve) => setTimeout(resolve, retryDelayMs));
    }
  }

  console.error(lastError ? lastError.message : "Unknown Unity MCP transport failure");
  process.exit(lastError?.exitCode || 1);
}

main().catch((error) => {
  console.error(error.message);
  process.exit(1);
});
