#!/usr/bin/env node
"use strict";

const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const defaultExpectedTools = [
  "Unity_GetLensHealth",
  "Unity_Tools_List",
  "Unity_Tools_Invoke",
  "Unity_Tools_BatchInvoke",
  "Unity_Editor_SyncScripts",
  "Unity_UI_CaptureGameView",
  "Unity_Prefab_AuditSerializedReferences",
  "Unity_UI_VerifyPrefabLayoutMatrix",
  "Unity_Prefab_ExplainOverrides",
  "Unity_Asset_VerifySpriteSlicesAndReferences",
  "Unity_PlayMode_InteractionSmoke",
  "Unity_Project_BlockedLanguageScan",
  "Unity_Tests_Run",
];

function parseArgs(argv) {
  const result = {};
  for (let index = 0; index < argv.length; index++) {
    const arg = argv[index];
    if (!arg.startsWith("--")) continue;
    const key = arg.slice(2).replace(/-([a-z])/g, (_match, letter) => letter.toUpperCase());
    const next = argv[index + 1];
    if (!next || next.startsWith("--")) {
      result[key] = true;
      continue;
    }

    if (Object.prototype.hasOwnProperty.call(result, key)) {
      result[key] = Array.isArray(result[key]) ? [...result[key], next] : [result[key], next];
    } else {
      result[key] = next;
    }
    index++;
  }

  return result;
}

function toArray(value) {
  if (value == null || value === false) return [];
  return Array.isArray(value) ? value : [value];
}

function normalizeToolName(value) {
  return String(value || "").trim().replace(/\./g, "_");
}

function defaultHostPath() {
  if (process.platform === "win32") {
    return path.join(os.homedir(), ".unity", "unity-mcp-lens", "unity_mcp_lens_win.exe");
  }

  if (process.platform === "darwin" && process.arch === "arm64") {
    return path.join(os.homedir(), ".unity", "unity-mcp-lens", "unity_mcp_lens_mac_arm64");
  }

  if (process.platform === "darwin") {
    return path.join(os.homedir(), ".unity", "unity-mcp-lens", "unity_mcp_lens_mac_x64");
  }

  return path.join(os.homedir(), ".unity", "unity-mcp-lens", "unity_mcp_lens_linux");
}

function rpcFrame(message) {
  const body = Buffer.from(JSON.stringify(message), "utf8");
  return Buffer.concat([
    Buffer.from(`Content-Length: ${body.length}\r\n\r\n`, "utf8"),
    body,
  ]);
}

function createFrameParser(onMessage) {
  let buffer = Buffer.alloc(0);
  return (chunk) => {
    buffer = Buffer.concat([buffer, chunk]);
    while (true) {
      const headerEnd = buffer.indexOf("\r\n\r\n");
      if (headerEnd < 0) return;

      const header = buffer.slice(0, headerEnd).toString("utf8");
      const match = /^Content-Length:\s*(\d+)/im.exec(header);
      if (!match) throw new Error(`Missing Content-Length header: ${header}`);

      const length = Number(match[1]);
      const bodyStart = headerEnd + 4;
      const bodyEnd = bodyStart + length;
      if (buffer.length < bodyEnd) return;

      const body = buffer.slice(bodyStart, bodyEnd).toString("utf8");
      buffer = buffer.slice(bodyEnd);
      onMessage(JSON.parse(body));
    }
  };
}

class RawMcpClient {
  constructor(hostPath, projectPath, timeoutMs) {
    this.hostPath = hostPath;
    this.projectPath = projectPath;
    this.timeoutMs = timeoutMs;
    this.nextId = 1;
    this.pending = new Map();
    this.notifications = [];
    this.stderr = "";
    this.child = childProcess.spawn(hostPath, [], {
      cwd: projectPath,
      env: {
        ...process.env,
        UNITY_MCP_PROJECT_PATH: projectPath,
      },
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    this.child.stderr.on("data", (chunk) => {
      this.stderr += chunk.toString("utf8");
    });
    this.child.stdout.on("data", createFrameParser((message) => this.handleMessage(message)));
  }

  handleMessage(message) {
    if (message.id !== undefined) {
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
      else pending.resolve(message.result);
      return;
    }

    if (message.method) {
      this.notifications.push({
        method: message.method,
        params: message.params ?? null,
      });
    }
  }

  request(method, params = {}) {
    const id = this.nextId++;
    const payload = { jsonrpc: "2.0", id, method, params };
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}. stderr=${this.stderr}`));
      }, this.timeoutMs);
      this.pending.set(id, {
        resolve: (value) => {
          clearTimeout(timeout);
          resolve(value);
        },
        reject: (error) => {
          clearTimeout(timeout);
          reject(error);
        },
      });
      this.child.stdin.write(rpcFrame(payload));
    });
  }

  notify(method, params = {}) {
    this.child.stdin.write(rpcFrame({ jsonrpc: "2.0", method, params }));
  }

  async initialize() {
    const result = await this.request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "lens-installed-host-proof", version: "1.0.0" },
    });
    this.notify("notifications/initialized", {});
    return result;
  }

  listTools() {
    return this.request("tools/list", {});
  }

  callTool(name, args = {}) {
    return this.request("tools/call", { name, arguments: args });
  }

  async dispose() {
    for (const pending of this.pending.values()) {
      pending.reject(new Error("MCP host disposed."));
    }
    this.pending.clear();
    this.child.stdin.end();
    this.child.kill();
    await new Promise((resolve) => this.child.once("exit", resolve));
  }
}

function listToolNames(toolsListResult) {
  return Array.isArray(toolsListResult?.tools)
    ? toolsListResult.tools.map((tool) => tool.name).filter(Boolean).sort((a, b) => a.localeCompare(b))
    : [];
}

function extractListFacadeNames(result) {
  const data = result?.structuredContent?.data;
  if (!data || typeof data !== "object") return [];

  const flatTools = Array.isArray(data.tools) ? data.tools : null;
  if (flatTools) {
    return flatTools.map((tool) => tool.name).filter(Boolean);
  }

  const groups = Array.isArray(data.groups) ? data.groups : [];
  return groups
    .flatMap((group) => Array.isArray(group.tools) ? group.tools : [])
    .map((tool) => tool.name)
    .filter(Boolean);
}

function statusFor(versionMatches, missingTools, listFacade) {
  if (!versionMatches) return "version_mismatch";
  if (missingTools.length > 0) return "missing_expected_tools";
  if (listFacade.attempted && listFacade.success !== true) return "list_facade_failed";
  if (listFacade.attempted && listFacade.missingExpectedTools.length > 0) return "list_facade_missing_expected_tools";
  return "ready";
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const hostPath = path.resolve(args.hostPath || process.env.UNITY_MCP_LENS_HOST || defaultHostPath());
  const projectPath = path.resolve(args.projectPath || process.env.UNITY_MCP_PROJECT_PATH || process.cwd());
  const outputPath = args.outputPath ? path.resolve(args.outputPath) : null;
  const timeoutMs = Math.min(120000, Math.max(1000, Number(args.timeoutMs || 20000)));
  const expectedVersion = String(args.expectedVersion || "").trim();
  const reportOnly = args.reportOnly === true || String(args.reportOnly).toLowerCase() === "true";
  const callListFacade = args.callListFacade !== false && String(args.callListFacade).toLowerCase() !== "false";
  const explicitExpectedTools = toArray(args.expectedTools).map(normalizeToolName).filter(Boolean);
  const expectedTools = explicitExpectedTools.length > 0
    ? [...new Set(explicitExpectedTools)]
    : [...defaultExpectedTools];

  if (!fs.existsSync(hostPath)) {
    throw new Error(`Unity MCP Lens installed host was not found at '${hostPath}'.`);
  }
  if (!fs.existsSync(projectPath)) {
    throw new Error(`Unity project path was not found at '${projectPath}'.`);
  }

  function writeEvidence(evidence) {
    const json = `${JSON.stringify(evidence, null, 2)}\n`;
    if (outputPath) {
      fs.mkdirSync(path.dirname(outputPath), { recursive: true });
      fs.writeFileSync(outputPath, json, "utf8");
    }

    process.stdout.write(json);
    process.exitCode = evidence.success === true || reportOnly ? 0 : 1;
  }

  const client = new RawMcpClient(hostPath, projectPath, timeoutMs);
  try {
    let initializeResult = null;
    try {
      initializeResult = await client.initialize();
    } catch (error) {
      writeEvidence({
        success: false,
        status: "initialize_failed",
        message: "Installed Lens host proof could not initialize the raw MCP host.",
        capturedAtUtc: new Date().toISOString(),
        hostPath,
        projectPath,
        hostProcessId: client.child.pid,
        expectedVersion: expectedVersion || null,
        actualVersion: null,
        versionMatches: false,
        expectedTools,
        missingExpectedTools: expectedTools,
        rawToolsList: {
          count: 0,
          names: [],
          error: error.message,
        },
        listFacade: {
          attempted: false,
          success: null,
          isError: null,
          message: null,
          toolCount: null,
          missingExpectedTools: [],
          error: null,
        },
        initializeServerInfo: null,
        notifications: client.notifications,
        recommendedNextAction: "Check the installed host path and rerun the proof helper after the MCP host can initialize.",
      });
      return;
    }

    const actualVersion = String(initializeResult?.serverInfo?.version || "");
    let toolsList = null;
    try {
      toolsList = await client.listTools();
    } catch (error) {
      const versionMatches = !expectedVersion ||
        actualVersion.localeCompare(expectedVersion, undefined, { sensitivity: "accent" }) === 0;
      writeEvidence({
        success: false,
        status: "tools_list_failed",
        message: "Installed Lens host initialized, but raw tools/list did not complete.",
        capturedAtUtc: new Date().toISOString(),
        hostPath,
        projectPath,
        hostProcessId: client.child.pid,
        expectedVersion: expectedVersion || null,
        actualVersion: actualVersion || null,
        versionMatches,
        expectedTools,
        missingExpectedTools: expectedTools,
        rawToolsList: {
          count: 0,
          names: [],
          error: error.message,
        },
        listFacade: {
          attempted: false,
          success: null,
          isError: null,
          message: null,
          toolCount: null,
          missingExpectedTools: [],
          error: "Skipped because tools/list failed.",
        },
        initializeServerInfo: initializeResult?.serverInfo ?? null,
        notifications: client.notifications,
        recommendedNextAction: "Run the proof helper with the target Unity project path, or use HealthCheckFast/Bridge.ListConnections if the bridge is currently unavailable.",
      });
      return;
    }

    const names = listToolNames(toolsList);
    const missingTools = expectedTools.filter((toolName) => !names.includes(toolName));
    const versionMatches = !expectedVersion ||
      actualVersion.localeCompare(expectedVersion, undefined, { sensitivity: "accent" }) === 0;

    const listFacade = {
      attempted: false,
      success: null,
      isError: null,
      message: null,
      toolCount: null,
      missingExpectedTools: [],
      error: null,
    };

    if (callListFacade && names.includes("Unity_Tools_List")) {
      listFacade.attempted = true;
      try {
        const listResult = await client.callTool("Unity_Tools_List", {
          groupBy: "flat",
          maxToolsPerGroup: 500,
        });
        const listNames = extractListFacadeNames(listResult).map(normalizeToolName);
        listFacade.success = listResult?.structuredContent?.success === true ||
          listResult?.structuredContent?.data?.success === true ||
          listResult?.isError === false;
        listFacade.isError = listResult?.isError === true;
        listFacade.message = listResult?.structuredContent?.message || listResult?.structuredContent?.data?.message || null;
        listFacade.toolCount = listNames.length;
        listFacade.missingExpectedTools = expectedTools.filter((toolName) => !listNames.includes(toolName));
      } catch (error) {
        listFacade.success = false;
        listFacade.error = error.message;
      }
    } else if (callListFacade) {
      listFacade.attempted = false;
      listFacade.error = "Unity_Tools_List is not present in tools/list.";
    }

    const status = statusFor(versionMatches, missingTools, listFacade);
    const success = status === "ready";
    const evidence = {
      success,
      status,
      message: success
        ? "Installed Lens host metadata and raw tool registry proof passed."
        : "Installed Lens host proof failed; refresh/reconnect before trusting this host.",
      capturedAtUtc: new Date().toISOString(),
      hostPath,
      projectPath,
      hostProcessId: client.child.pid,
      expectedVersion: expectedVersion || null,
      actualVersion: actualVersion || null,
      versionMatches,
      expectedTools,
      missingExpectedTools: missingTools,
      rawToolsList: {
        count: names.length,
        names,
      },
      listFacade,
      initializeServerInfo: initializeResult?.serverInfo ?? null,
      notifications: client.notifications,
      recommendedNextAction: success
        ? "Reconnect Codex MCP to the installed host if the current client surface is stale."
        : "Run Tools~/Refresh-LensInstalledHost.ps1 after stopping installed-host clients, then rerun this proof helper from a fresh installed host.",
    };

    writeEvidence(evidence);
  } finally {
    await client.dispose().catch(() => {});
  }
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
