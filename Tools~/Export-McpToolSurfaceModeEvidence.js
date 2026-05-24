const childProcess = require("child_process");
const fs = require("fs");
const net = require("net");
const os = require("os");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");
const defaultHostPath = path.join(repoRoot, "UnityMcpLensApp~", "src", "UnityMcpLens", "bin", "Debug", "net8.0", "UnityMcpLens.exe");
const defaultOutputDir = path.join(repoRoot, "artifacts");
const pluginManifestPath = path.join(repoRoot, ".agents", "plugins", "lens-dev-plugin", "manifest.json");
const manifestGeneratorPath = path.join(repoRoot, "Tools~", "Export-LensDevPluginManifest.js");
const packOrder = ["foundation", "console", "project", "scripting", "scene", "ui", "runtime", "assets", "debug"];
const facadeToolNames = [
  "Unity_Tools_List",
  "Unity_Tools_Invoke",
  "Unity_Tools_BatchInvoke",
];
const requiredManifestToolNames = [
  ...facadeToolNames,
  "Unity_Tools_Describe",
  "Unity_Tools_Menu",
  "Unity_Project_BlockedLanguageScan",
  "Unity_Tests_Run",
  "Unity_UI_CaptureGameView",
];
const representativeToolNames = [
  ...facadeToolNames,
  "Unity_Project_PackageCompatibility",
  "Unity_Project_BlockedLanguageScan",
  "Unity_Tests_Run",
  "Unity_Editor_SetPlayMode",
  "Unity_Asset_Search",
  "Unity_GameObject_Inspect",
  "Unity_UI_VerifyScreenLayout",
  "Unity_UI_CaptureGameView",
  "Unity_GetLensUsageReport",
];

let nextPipeId = 1;

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

    result[key] = next;
    index++;
  }

  return result;
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
      onMessage(JSON.parse(body), Buffer.byteLength(body, "utf8"));
    }
  };
}

class McpHostClient {
  constructor(projectRoot, statusDir, toolSurfaceMode, hostPath) {
    this.nextId = 1;
    this.pending = new Map();
    this.notifications = [];
    this.stderr = "";
    this.child = childProcess.spawn(hostPath, [], {
      cwd: projectRoot,
      env: {
        ...process.env,
        UNITY_MCP_STATUS_DIR: statusDir,
        UNITY_MCP_PROJECT_PATH: projectRoot,
        UNITY_MCP_LENS_TOOL_SURFACE_MODE: toolSurfaceMode,
      },
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    this.child.stderr.on("data", (chunk) => {
      this.stderr += chunk.toString("utf8");
    });

    this.child.stdout.on("data", createFrameParser((message, responseBodyBytes) => {
      if (message.id !== undefined) {
        const pending = this.pending.get(message.id);
        if (!pending) return;
        this.pending.delete(message.id);
        const envelope = {
          responseBodyBytes,
          responseBodyApproxTokens: approxTokens(responseBodyBytes),
          message,
          result: message.result,
        };
        if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
        else pending.resolve(envelope);
        return;
      }

      if (message.method) {
        this.notifications.push({
          method: message.method,
          responseBodyBytes,
          message,
        });
      }
    }));
  }

  async initialize() {
    const envelope = await this.request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "tool-surface-mode-evidence", version: "1.0.0" },
    });
    this.notify("notifications/initialized", {});
    return envelope;
  }

  notify(method, params = {}) {
    this.child.stdin.write(rpcFrame({ jsonrpc: "2.0", method, params }));
  }

  request(method, params = {}) {
    const id = this.nextId++;
    const payload = { jsonrpc: "2.0", id, method, params };
    const requestBodyBytes = Buffer.byteLength(JSON.stringify(payload), "utf8");
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}. stderr=${this.stderr}`));
      }, 15000);
      this.pending.set(id, {
        resolve: (value) => {
          clearTimeout(timeout);
          resolve({ ...value, requestBodyBytes, method, params });
        },
        reject: (error) => {
          clearTimeout(timeout);
          reject(error);
        },
      });
      this.child.stdin.write(rpcFrame(payload));
    });
  }

  listTools() {
    return this.request("tools/list", {});
  }

  callTool(name, args = {}) {
    return this.request("tools/call", { name, arguments: args });
  }

  notificationCount(method) {
    return this.notifications.filter((notification) => notification.method === method).length;
  }

  async waitForNotification(method, afterCount, timeoutMs = 5000) {
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
      if (this.notificationCount(method) > afterCount) return true;
      await delay(50);
    }

    return false;
  }

  async dispose() {
    for (const pending of this.pending.values()) {
      pending.reject(new Error("Host disposed."));
    }
    this.pending.clear();
    this.child.stdin.end();
    this.child.kill();
    await new Promise((resolve) => this.child.once("exit", resolve));
  }
}

class FakeBridge {
  constructor(context) {
    this.context = context;
    this.server = null;
    this.connectionPath = makePipePath();
    this.statusPath = path.join(context.statusDir, `bridge-status-${nextPipeId++}.json`);
  }

  async start() {
    this.server = net.createServer((socket) => this.handleSocket(socket));
    await new Promise((resolve, reject) => {
      this.server.once("error", reject);
      this.server.listen(this.connectionPath, () => {
        this.server.off("error", reject);
        resolve();
      });
    });
    this.writeStatus();
  }

  async stop() {
    if (!this.server) return;
    await new Promise((resolve) => this.server.close(resolve));
    this.server = null;
  }

  writeStatus() {
    writeStatus(this.statusPath, this.connectionPath, this.context.projectRoot, {
      status: "ready",
      heartbeat: new Date(),
      toolCount: fakeTools(this.context.toolCatalog, this.context.activePacks, false).length,
    });
  }

  handleSocket(socket) {
    socket.setEncoding("utf8");
    socket.write(JSON.stringify({ type: "handshake" }) + "\n");
    let buffer = "";
    socket.on("data", async (chunk) => {
      buffer += chunk;
      while (true) {
        const newline = buffer.indexOf("\n");
        if (newline < 0) return;

        const line = buffer.slice(0, newline).trim();
        buffer = buffer.slice(newline + 1);
        if (!line) continue;

        const command = JSON.parse(line);
        await this.handleCommand(socket, command);
      }
    });
  }

  async handleCommand(socket, command) {
    this.context.recordCommand(command);
    const type = command.type;
    if (type === "register_client") {
      const requestedToolPacks = command.params && command.params.requestedToolPacks;
      if (Array.isArray(requestedToolPacks)) {
        this.context.setActivePacks(requestedToolPacks);
        this.writeStatus();
      }
    }

    if (type === "set_tool_packs") {
      this.context.setActivePacks(command.params && command.params.packs);
      this.writeStatus();
      this.respond(socket, command.requestId, manifestResult(this.context, false));
      return;
    }

    this.respond(socket, command.requestId, resultFor(type, command.params, this.context));
  }

  respond(socket, requestId, result) {
    socket.write(JSON.stringify({ requestId, status: "success", result }) + "\n");
  }
}

class ScenarioContext {
  constructor(toolCatalog) {
    this.root = fs.mkdtempSync(path.join(os.tmpdir(), "lens-tool-surface-evidence-"));
    this.statusDir = path.join(this.root, "connections");
    this.projectRoot = path.join(this.root, "Project");
    this.activePacks = ["foundation"];
    this.commands = [];
    this.toolCatalog = toolCatalog;
    this.bridge = null;
    fs.mkdirSync(this.statusDir, { recursive: true });
    fs.mkdirSync(this.projectRoot, { recursive: true });
  }

  recordCommand(command) {
    this.commands.push({
      type: command && command.type,
      params: command && command.params ? JSON.parse(JSON.stringify(command.params)) : null,
    });
  }

  setActivePacks(packs) {
    const additionalPacks = (Array.isArray(packs) ? packs : [])
      .filter((pack) => typeof pack === "string" && pack.trim())
      .map((pack) => pack.trim().toLowerCase())
      .filter((pack) => pack !== "foundation");
    const uniqueAdditionalPacks = [...new Set(additionalPacks)].sort((a, b) => a.localeCompare(b));
    this.activePacks = uniqueAdditionalPacks.includes("full")
      ? ["foundation", "full"]
      : ["foundation", ...uniqueAdditionalPacks];
  }

  async startBridge() {
    this.bridge = new FakeBridge(this);
    await this.bridge.start();
  }

  async dispose() {
    if (this.bridge) await this.bridge.stop().catch(() => {});
    fs.rmSync(this.root, { recursive: true, force: true });
  }
}

function makePipePath() {
  if (process.platform === "win32") {
    return `\\\\.\\pipe\\unity-mcp-lens-surface-${process.pid}-${nextPipeId++}`;
  }

  return path.join(os.tmpdir(), `unity-mcp-lens-surface-${process.pid}-${nextPipeId++}.sock`);
}

function writeStatus(statusPath, connectionPath, projectRoot, options) {
  fs.writeFileSync(statusPath, JSON.stringify({
    connection_type: process.platform === "win32" ? "named_pipe" : "unix_socket",
    connection_path: connectionPath,
    status: options.status,
    reason: null,
    expected_recovery: false,
    expected_recovery_expires_utc: null,
    tool_discovery_mode: "live",
    tool_count: options.toolCount,
    tools_hash: "fake-surface-mode",
    tool_discovery_reason: null,
    tool_snapshot_utc: options.heartbeat.toISOString(),
    command_health: "ok",
    last_command_success_utc: options.heartbeat.toISOString(),
    last_command_failure_utc: null,
    last_command_failure_reason: null,
    bridge_session_id: "fake-surface-mode-session",
    manifest_version: 1,
    profile_catalog_version: "fake-profile",
    supports_tool_sync_lens: true,
    last_tools_changed_utc: options.heartbeat.toISOString(),
    project_path: path.join(projectRoot, "Assets"),
    project_root: projectRoot,
    last_heartbeat: options.heartbeat.toISOString(),
    protocol_version: "2.0",
    editor_pid: process.pid,
  }, null, 2));
}

function resultFor(type, params, context) {
  switch (type) {
    case "register_client":
      return {
        bridgeSessionId: "fake-surface-mode-session",
        manifestVersion: 1,
        profileCatalogVersion: "fake-profile",
        activeToolPacks: context.activePacks,
      };
    case "get_manifest":
      return manifestResult(context, false);
    case "get_tool_schema":
      return {
        bridgeSessionId: "fake-surface-mode-session",
        manifestVersion: 1,
        activeToolPacks: context.activePacks,
        tools: fakeTools(context.toolCatalog, context.activePacks, true),
      };
    case "Unity_GetLensHealth":
      return {
        success: true,
        message: "Fake Lens health ready.",
        data: {
          activeToolPacks: context.activePacks,
          toolSurfaceMode: context.activePacks.includes("full") ? "static_all" : "dynamic_packs",
          bridgeStatus: { status: "ready", toolDiscoveryMode: "live" },
          internalRegistryToolCount: fakeTools(context.toolCatalog, ["foundation", "full"], false).length,
          editorStability: { isStable: true },
          expectedRecovery: { isActive: false },
        },
      };
    case "Unity_Tools_Menu":
      return menuResult(context);
    case "Unity_Tools_List":
      return toolListResult(context, params);
    case "Unity_Tools_Describe":
      return describeResult(context, params);
    default:
      return { success: true, message: `${type} ok`, data: { activeToolPacks: context.activePacks } };
  }
}

function manifestResult(context, withSchemas) {
  return {
    bridgeSessionId: "fake-surface-mode-session",
    manifestVersion: 1,
    profileCatalogVersion: "fake-profile",
    activeToolPacks: context.activePacks,
    kind: "full",
    reason: null,
    hashMinimal: `fake-minimal-${context.activePacks.join("-")}`,
    hashFull: `fake-full-${context.activePacks.join("-")}`,
    tools: fakeTools(context.toolCatalog, context.activePacks, withSchemas),
    delta: null,
  };
}

function fakeTools(toolCatalog, activeToolPacks, withSchemas) {
  const active = new Set((activeToolPacks || []).map((pack) => String(pack).toLowerCase()));
  const hasFull = active.has("full");
  return toolCatalog
    .filter((tool) => tool.pack === "foundation" || hasFull || active.has(tool.pack))
    .map((tool) => toolDescriptor(tool, withSchemas));
}

function toolDescriptor(tool, withSchemas) {
  const descriptor = {
    name: tool.name,
    title: tool.title,
    description: tool.description,
    schemaHash: `${tool.name}-schema-v1`,
    groups: ["assistant", tool.pack],
    packs: tool.pack === "foundation" ? ["foundation", "full"] : [tool.pack, "full"],
    readOnlyHint: tool.readOnlyHint,
  };
  if (withSchemas) {
    descriptor.inputSchema = schemaFor(tool);
    descriptor.outputSchema = {
      type: "object",
      properties: {
        success: { type: "boolean" },
        message: { type: "string" },
        data: { type: "object", additionalProperties: true },
      },
    };
    descriptor.annotations = { readOnlyHint: tool.readOnlyHint };
  }
  return descriptor;
}

function schemaFor(tool) {
  if (tool.name === "Unity_SetToolPacks") {
    return {
      type: "object",
      properties: {
        packs: { type: "array", items: { type: "string" } },
      },
    };
  }

  if (tool.name === "Unity_Tools_Menu") {
    return {
      type: "object",
      properties: {
        maxToolsPerPack: { type: "integer" },
      },
    };
  }

  if (tool.name === "Unity_Tools_List") {
    return {
      type: "object",
      properties: {
        groupBy: { type: "string", enum: ["pack", "group", "flat"] },
        maxToolsPerGroup: { type: "integer" },
      },
    };
  }

  if (tool.name === "Unity_Tools_Invoke") {
    return {
      type: "object",
      properties: {
        toolName: { type: "string" },
        arguments: { type: "object" },
        timeoutMs: { type: "integer" },
      },
      required: ["toolName"],
    };
  }

  if (tool.name === "Unity_Tools_BatchInvoke") {
    return {
      type: "object",
      properties: {
        calls: {
          type: "array",
          items: {
            type: "object",
            properties: {
              toolName: { type: "string" },
              arguments: { type: "object" },
              timeoutMs: { type: "integer" },
            },
            required: ["toolName"],
          },
        },
        failFast: { type: "boolean" },
      },
      required: ["calls"],
    };
  }

  if (tool.name === "Unity_UI_CaptureGameView") {
    return {
      type: "object",
      properties: {
        SceneName: { type: "string" },
        OutputPath: { type: "string" },
        Width: { type: "integer" },
        Height: { type: "integer" },
        RestoreOriginalResolution: { type: "boolean" },
        WarmupMs: { type: "integer" },
        WarmupFrames: { type: "integer" },
        PausePlayMode: { type: "boolean" },
        StepFrames: { type: "integer" },
        RestorePauseState: { type: "boolean" },
        RequirePlaying: { type: "boolean" },
        CaptureConsoleDelta: { type: "boolean" },
        FallbackSceneView: { type: "boolean" },
        TemporaryActivations: {
          type: "array",
          items: {
            type: "object",
            properties: {
              Target: { type: "string" },
              SearchMethod: { type: "string" },
              IncludeInactive: { type: "boolean" },
              Active: { type: "boolean" },
            },
            required: ["Target"],
          },
        },
        VerifyImageDimensions: { type: "boolean" },
        WaitForFileTimeoutMs: { type: "integer" },
      },
      required: ["OutputPath"],
    };
  }

  return {
    type: "object",
    properties: {
      target: { type: "string", description: "Optional object, asset, or subsystem target." },
      includeInactive: { type: "boolean", description: "Include inactive objects when applicable." },
      names: { type: "array", items: { type: "string" }, description: "Optional name filters." },
      options: {
        type: "object",
        additionalProperties: { type: "string" },
        description: "Optional tool-specific string options.",
      },
      apply: { type: "boolean", description: "Persist changes when this is a mutating tool." },
    },
  };
}

function menuResult(context) {
  const fullSurface = context.activePacks.includes("full");
  const packs = packOrder.map((packId) => {
    const tools = context.toolCatalog
      .filter((tool) => tool.pack === packId)
      .map((tool) => ({
        name: tool.name,
        title: tool.title,
        readOnlyHint: tool.readOnlyHint,
        mutationHint: tool.readOnlyHint ? "read_only" : "mutating",
      }));
    return {
      packId,
      title: titleFromPack(packId),
      description: `${titleFromPack(packId)} tools`,
      alwaysOn: packId === "foundation",
      adminOnly: false,
      isActive: fullSurface || context.activePacks.includes(packId),
      toolCount: tools.length,
      readOnlyToolCount: tools.filter((tool) => tool.readOnlyHint).length,
      mutatingToolCount: tools.filter((tool) => !tool.readOnlyHint).length,
      truncated: false,
      tools,
    };
  });

  return {
    success: true,
    message: "Fake tool menu.",
    data: {
      toolSurfaceMode: fullSurface ? "static_all" : "dynamic_packs",
      activeToolPacks: context.activePacks,
      totalToolCount: context.toolCatalog.length,
      packs,
      clientSurfaceFallback: clientSurfaceFallback(),
      workflowRecommendations: [
        fullSurface
          ? "Call real native tools directly; no Unity.SetToolPacks step is required in static_all mode."
          : "Use Unity.SetToolPacks before calling pack-gated tools.",
        "If a direct native tool is unavailable in the MCP client, use Unity.Tools.List and call it through Unity.Tools.Invoke or Unity.Tools.BatchInvoke.",
      ],
    },
  };
}

function toolListResult(context, params) {
  const fullSurface = context.activePacks.includes("full");
  const groupBy = ["pack", "group", "flat"].includes(params?.groupBy) ? params.groupBy : "pack";
  const rawMaxToolsPerGroup = Number(params?.maxToolsPerGroup || 100);
  const maxToolsPerGroup = Math.min(500, Math.max(1, Number.isFinite(rawMaxToolsPerGroup) ? rawMaxToolsPerGroup : 100));
  const rows = fakeTools(context.toolCatalog, context.activePacks, false)
    .map((tool) => ({
      name: tool.name,
      canonicalToolName: tool.name,
      title: tool.title,
      readOnlyHint: tool.readOnlyHint,
      schemaHash: tool.schemaHash,
      packs: tool.packs,
      groups: tool.groups,
    }))
    .sort((left, right) => left.name.localeCompare(right.name));
  const data = {
    toolSurfaceMode: fullSurface ? "static_all" : "dynamic_packs",
    activeToolPacks: context.activePacks,
    exportedToolCount: rows.length,
    groupBy,
    maxToolsPerGroup,
    truncated: false,
    bridgeRefresh: { attempted: true, success: true, warning: null },
    clientSurfaceFallback: clientSurfaceFallback(),
  };

  if (groupBy === "flat") {
    data.tools = rows;
  } else {
    const key = groupBy === "group" ? "groups" : "packs";
    data.groups = groupRows(rows, key, maxToolsPerGroup);
    data.truncated = data.groups.some((group) => group.truncated);
  }

  return {
    success: true,
    message: "Fake tool list.",
    data,
  };
}

function groupRows(rows, key, maxToolsPerGroup) {
  const groups = new Map();
  for (const row of rows) {
    const ids = Array.isArray(row[key]) && row[key].length > 0 ? row[key] : ["ungrouped"];
    for (const id of ids) {
      if (!groups.has(id)) groups.set(id, []);
      groups.get(id).push(row);
    }
  }

  return [...groups.entries()]
    .sort(([left], [right]) => left.localeCompare(right))
    .map(([id, tools]) => ({
      id,
      toolCount: tools.length,
      truncated: tools.length > maxToolsPerGroup,
      tools: tools.slice(0, maxToolsPerGroup),
    }));
}

function describeResult(context, params) {
  const fullSurface = context.activePacks.includes("full");
  const includeExamples = Boolean(params?.includeExamples);
  const tools = fakeTools(context.toolCatalog, context.activePacks, false).map((tool) => ({
    name: tool.name,
    title: tool.title,
    description: tool.description,
    readOnlyHint: tool.readOnlyHint,
    packs: tool.packs,
    groups: tool.groups,
    examples: includeExamples
      ? [{ tool: "Unity.Tools.Invoke", arguments: { toolName: tool.name, arguments: {} } }]
      : undefined,
  }));
  return {
    success: true,
    message: "Fake tool descriptions.",
    data: {
      toolSurfaceMode: fullSurface ? "static_all" : "dynamic_packs",
      activeToolPacks: context.activePacks,
      exportedToolCount: tools.length,
      clientSurfaceFallback: clientSurfaceFallback(),
      fallbackGuidance: "If a native tool is missing from the client callable surface, call it through Unity.Tools.Invoke or Unity.Tools.BatchInvoke.",
      tools,
    },
  };
}

function clientSurfaceFallback() {
  return {
    listTool: "Unity.Tools.List",
    invokeTool: "Unity.Tools.Invoke",
    batchInvokeTool: "Unity.Tools.BatchInvoke",
  };
}

function buildToolCatalog(targetToolCount) {
  const baseTools = [
    ["foundation", "Unity_GetLensHealth", true],
    ["foundation", "Unity_ListToolPacks", true],
    ["foundation", "Unity_SetToolPacks", false],
    ["foundation", "Unity_ReadDetailRef", true],
    ["foundation", "Unity_Tools_Menu", true],
    ["foundation", "Unity_Tools_Describe", true],
    ["foundation", "Unity_Tools_List", true],
    ["foundation", "Unity_Tools_Invoke", false],
    ["foundation", "Unity_Tools_BatchInvoke", false],
    ["foundation", "Unity_Tools_ActivateAndVerify", false],
    ["foundation", "Unity_ReadConsole", true],
    ["foundation", "Unity_ListResources", true],
    ["foundation", "Unity_ReadResource", true],
    ["foundation", "Unity_FindInFile", true],
    ["foundation", "Unity_GetSha", true],
    ["foundation", "Unity_ValidateScript", true],
    ["foundation", "Unity_ManageScript_capabilities", true],
    ["foundation", "Unity_Project_GetInfo", true],
    ["foundation", "Unity_Editor_ScriptUpdatingConsentModal", false],
    ["project", "Unity_Project_PackageCompatibility", true],
    ["project", "Unity_Project_BlockedLanguageScan", true],
    ["project", "Unity_Tests_Run", false],
    ["runtime", "Unity_Editor_SetPlayMode", false],
    ["assets", "Unity_Asset_Search", true],
    ["scene", "Unity_GameObject_Inspect", true],
    ["ui", "Unity_UI_VerifyScreenLayout", true],
    ["ui", "Unity_UI_CaptureGameView", false],
    ["debug", "Unity_GetLensUsageReport", true],
    ["scripting", "Unity_Editor_SyncScripts", false],
    ["console", "Unity_ManageEditor", false],
  ];
  const tools = baseTools.map(([pack, name, readOnlyHint]) => createTool(pack, name, readOnlyHint));
  let index = 1;
  while (tools.length < targetToolCount) {
    const pack = packOrder[(index % (packOrder.length - 1)) + 1];
    const readOnlyHint = index % 3 !== 0;
    const verb = readOnlyHint ? "Inspect" : "Apply";
    const name = `Unity_${titleFromPack(pack).replace(/\s+/g, "")}_${verb}${String(index).padStart(3, "0")}`;
    tools.push(createTool(pack, name, readOnlyHint));
    index++;
  }

  return tools.slice(0, targetToolCount);
}

function createTool(pack, name, readOnlyHint) {
  return {
    pack,
    name,
    title: name.replace(/^Unity_/, "Unity ").replace(/_/g, " "),
    description: `${name} representative ${pack} tool descriptor used for tool-surface payload evidence.`,
    readOnlyHint,
  };
}

function titleFromPack(packId) {
  return {
    foundation: "Foundation",
    console: "Console Diagnostics",
    project: "Project Diagnostics",
    scripting: "Scripting",
    scene: "Scene Editing",
    ui: "UI Authoring",
    runtime: "Runtime Verification",
    assets: "Assets",
    debug: "Debug",
  }[packId] || packId;
}

async function runScenario(toolSurfaceMode, toolCatalog, hostPath) {
  const context = new ScenarioContext(toolCatalog);
  let client = null;
  try {
    await context.startBridge();
    client = new McpHostClient(context.projectRoot, context.statusDir, toolSurfaceMode, hostPath);
    const initialize = await client.initialize();
    const startupList = await client.listTools();
    const startupBridgeCommands = context.commands.slice();
    const setAssetsBefore = client.notificationCount("notifications/tools/list_changed");
    const setAssets = await client.callTool("Unity_SetToolPacks", { Packs: ["assets"] });
    const sawListChanged = await client.waitForNotification("notifications/tools/list_changed", setAssetsBefore, 1500);
    const afterSetList = await client.listTools();
    const menu = await client.callTool("Unity_Tools_Menu", {});
    const listFacade = await client.callTool("Unity_Tools_List", { groupBy: "flat", maxToolsPerGroup: 500 });
    const describe = await client.callTool("Unity_Tools_Describe", { includeExamples: true });
    const menuSummary = summarizeToolCall(menu);
    const listFacadeSummary = summarizeToolCall(listFacade);
    const describeSummary = summarizeToolCall(describe);

    return {
      toolSurfaceMode,
      initialize: summarizeEnvelope(initialize),
      startupToolsList: summarizeToolsList(startupList),
      startupBridgeCommands: summarizeBridgeCommands(startupBridgeCommands),
      setAssets: summarizeToolCall(setAssets),
      sawListChanged,
      afterSetToolsList: summarizeToolsList(afterSetList),
      menu: menuSummary,
      listFacade: listFacadeSummary,
      describe: describeSummary,
      fallbackGuidance: summarizeFallbackGuidance({
        menu: menuSummary,
        listFacade: listFacadeSummary,
        describe: describeSummary,
      }),
      stderr: client.stderr.trim(),
    };
  } finally {
    if (client) await client.dispose().catch(() => {});
    await context.dispose();
  }
}

function summarizeEnvelope(envelope) {
  return {
    method: envelope.method,
    requestBodyBytes: envelope.requestBodyBytes,
    responseBodyBytes: envelope.responseBodyBytes,
    responseBodyApproxTokens: envelope.responseBodyApproxTokens,
  };
}

function summarizeToolsList(envelope) {
  const tools = envelope.result?.tools || [];
  const descriptorBytes = byteLength(tools);
  const schemaBytes = tools.reduce((sum, tool) => sum + byteLength(tool.inputSchema) + byteLength(tool.outputSchema) + byteLength(tool.annotations), 0);
  const names = tools.map((tool) => tool.name).filter(Boolean).sort((a, b) => a.localeCompare(b));
  return {
    ...summarizeEnvelope(envelope),
    toolCount: tools.length,
    descriptorBytes,
    descriptorApproxTokens: approxTokens(descriptorBytes),
    schemaBytes,
    schemaApproxTokens: approxTokens(schemaBytes),
    facadePresence: facadePresence(names),
    representativePresence: representativePresence(names),
    toolNames: names,
  };
}

function summarizeBridgeCommands(commands) {
  const registerClient = commands.find((command) => command.type === "register_client") || null;
  const setToolPacks = commands.filter((command) => command.type === "set_tool_packs");
  return {
    count: commands.length,
    types: commands.map((command) => command.type),
    registerRequestedToolPacks: registerClient?.params?.requestedToolPacks ?? null,
    registerToolSurfaceMode: registerClient?.params?.toolSurfaceMode ?? null,
    setToolPacksCount: setToolPacks.length,
    setToolPacksReasons: setToolPacks.map((command) => command.params?.reason ?? null),
  };
}

function summarizeToolCall(envelope) {
  const structuredContent = envelope.result?.structuredContent || null;
  return {
    ...summarizeEnvelope(envelope),
    success: structuredContent?.success ?? null,
    message: structuredContent?.message ?? structuredContent?.error ?? null,
    data: structuredContent?.data ?? null,
  };
}

function summarizeFallbackGuidance(calls) {
  return Object.fromEntries(Object.entries(calls).map(([key, call]) => {
    const data = call?.data || {};
    const recommendations = Array.isArray(data.workflowRecommendations) ? data.workflowRecommendations : [];
    const fallback = data.clientSurfaceFallback || null;
    const serializedData = JSON.stringify(data);
    return [key, {
      hasClientSurfaceFallback:
        fallback?.listTool === "Unity.Tools.List" &&
        fallback?.invokeTool === "Unity.Tools.Invoke" &&
        fallback?.batchInvokeTool === "Unity.Tools.BatchInvoke",
      mentionsInvoke:
        serializedData.includes("Unity.Tools.Invoke") ||
        recommendations.some((line) => String(line).includes("Unity.Tools.Invoke")),
      mentionsBatchInvoke:
        serializedData.includes("Unity.Tools.BatchInvoke") ||
        recommendations.some((line) => String(line).includes("Unity.Tools.BatchInvoke")),
    }];
  }));
}

function facadePresence(names) {
  const presence = Object.fromEntries(facadeToolNames.map((name) => [name, names.includes(name)]));
  return {
    ...presence,
    allPresent: Object.values(presence).every(Boolean),
  };
}

function representativePresence(names) {
  return Object.fromEntries(representativeToolNames.map((name) => [name, names.includes(name)]));
}

function loadPluginManifestStatus() {
  const status = {
    path: path.relative(repoRoot, pluginManifestPath).replace(/\\/g, "/"),
    generatorPath: path.relative(repoRoot, manifestGeneratorPath).replace(/\\/g, "/"),
    exists: fs.existsSync(pluginManifestPath),
    checkPassed: false,
    checkOutput: null,
    checkError: null,
    manifestVersion: null,
    toolCount: null,
    sourceOfTruth: null,
    executionSourceOfTruth: null,
    staticAllConfigured: null,
    requiredToolPresence: Object.fromEntries(requiredManifestToolNames.map((name) => [name, false])),
    requiredToolsPresent: false,
  };

  try {
    status.checkOutput = childProcess.execFileSync(process.execPath, [manifestGeneratorPath, "--check"], {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    }).trim();
    status.checkPassed = true;
  } catch (error) {
    status.checkError = [
      error.stdout ? String(error.stdout).trim() : "",
      error.stderr ? String(error.stderr).trim() : "",
      error.message ? String(error.message).trim() : "",
    ].filter(Boolean).join("\n");
  }

  if (!status.exists) return status;

  try {
    const manifest = JSON.parse(fs.readFileSync(pluginManifestPath, "utf8"));
    const toolNames = Array.isArray(manifest.tools) ? manifest.tools.map((tool) => tool.name).filter(Boolean) : [];
    status.manifestVersion = manifest.manifest_version ?? null;
    status.toolCount = toolNames.length;
    status.sourceOfTruth = manifest.sourceOfTruth ?? null;
    status.executionSourceOfTruth = manifest.executionSourceOfTruth ?? null;
    status.staticAllConfigured =
      manifest.server?.mcp_config?.env?.UNITY_MCP_LENS_TOOL_SURFACE_MODE === "static_all";
    status.requiredToolPresence = Object.fromEntries(requiredManifestToolNames.map((name) => [name, toolNames.includes(name)]));
    status.requiredToolsPresent = Object.values(status.requiredToolPresence).every(Boolean);
  } catch (error) {
    status.checkError = [status.checkError, `Could not read manifest: ${error.message}`].filter(Boolean).join("\n");
  }

  return status;
}

function buildFacadeResilience(dynamicScenario, staticScenario, pluginManifest) {
  const dynamicStartup = dynamicScenario.startupToolsList;
  const staticStartup = staticScenario.startupToolsList;
  const staticGuidance = staticScenario.fallbackGuidance || {};
  const fallbackGuidancePresent = Object.values(staticGuidance).every((entry) =>
    entry.hasClientSurfaceFallback && entry.mentionsInvoke && entry.mentionsBatchInvoke);

  return {
    foundationFacadesPresent: Boolean(dynamicStartup.facadePresence?.allPresent),
    staticAllFacadesPresent: Boolean(staticStartup.facadePresence?.allPresent),
    staticAllNativeMissedToolRepresentativesPresent:
      Boolean(staticStartup.representativePresence.Unity_Project_BlockedLanguageScan) &&
      Boolean(staticStartup.representativePresence.Unity_Tests_Run),
    fallbackGuidancePresent,
    pluginManifestFresh: Boolean(pluginManifest.checkPassed),
    pluginManifestHasRequiredTools: Boolean(pluginManifest.requiredToolsPresent),
    escapeHatchReady:
      Boolean(dynamicStartup.facadePresence?.allPresent) &&
      Boolean(staticStartup.facadePresence?.allPresent) &&
      fallbackGuidancePresent &&
      Boolean(pluginManifest.checkPassed) &&
      Boolean(pluginManifest.requiredToolsPresent),
  };
}

function buildConclusion(dynamicScenario, staticScenario, pluginManifest) {
  const dynamicStartup = dynamicScenario.startupToolsList;
  const staticStartup = staticScenario.startupToolsList;
  const staticHasRepresentatives = Object.values(staticStartup.representativePresence).every(Boolean);
  const facadeResilience = buildFacadeResilience(dynamicScenario, staticScenario, pluginManifest);
  return {
    protocolLevelStaticAllHandsClientFullToolList:
      staticHasRepresentatives && staticStartup.toolCount > dynamicStartup.toolCount,
    clientResilientFacadeEscapeHatchReady: facadeResilience.escapeHatchReady,
    foundationFacadesPresent: facadeResilience.foundationFacadesPresent,
    staticAllFacadesPresent: facadeResilience.staticAllFacadesPresent,
    staticAllNativeMissedToolRepresentativesPresent: facadeResilience.staticAllNativeMissedToolRepresentativesPresent,
    fallbackGuidancePresent: facadeResilience.fallbackGuidancePresent,
    pluginManifestFresh: facadeResilience.pluginManifestFresh,
    pluginManifestHasRequiredTools: facadeResilience.pluginManifestHasRequiredTools,
    staticAllStartupToolCount: staticStartup.toolCount,
    dynamicStartupToolCount: dynamicStartup.toolCount,
    staticAllStartupResponseBytes: staticStartup.responseBodyBytes,
    dynamicStartupResponseBytes: dynamicStartup.responseBodyBytes,
    staticAllApproxResponseTokens: staticStartup.responseBodyApproxTokens,
    dynamicApproxResponseTokens: dynamicStartup.responseBodyApproxTokens,
    staticAllSchemaApproxTokens: staticStartup.schemaApproxTokens,
    setToolPacksNoopKeptStaticToolCount:
      staticScenario.afterSetToolsList.toolCount === staticStartup.toolCount &&
      staticScenario.setAssets.data?.toolSurfaceMode === "static_all" &&
      staticScenario.setAssets.data?.toolsListChangedNotificationSent === false,
    staticAllRegisterRequestedFull:
      Array.isArray(staticScenario.startupBridgeCommands?.registerRequestedToolPacks) &&
      staticScenario.startupBridgeCommands.registerRequestedToolPacks.includes("full") &&
      staticScenario.startupBridgeCommands?.registerToolSurfaceMode === "static_all",
    staticAllAvoidedStartupPackRestore:
      staticScenario.startupBridgeCommands?.setToolPacksCount === 0,
    canDirectlyObserveCodexPromptInjection: false,
    promptInjectionLimit:
      "This artifact proves what the MCP host sends to a client over tools/list. It cannot prove whether Codex injects every received tool schema into model context without a Codex-side prompt/tool-snapshot trace.",
  };
}

function writeMarkdownReport(report, markdownPath) {
  const c = report.conclusion;
  const dynamic = report.scenarios.dynamic_packs.startupToolsList;
  const stat = report.scenarios.static_all.startupToolsList;
  const manifest = report.pluginManifest;
  const facade = report.facadeResilience;
  const lines = [
    "# MCP Tool Surface Mode Evidence",
    "",
    `Captured: ${report.capturedAtUtc}`,
    `Host: \`${report.hostPath}\``,
    `Fake full tool count: ${report.fakeFullToolCount}`,
    "",
    "## Verdict",
    "",
    `- Protocol-level static-all hands the client the full tool list at startup: **${c.protocolLevelStaticAllHandsClientFullToolList ? "yes" : "no"}**.`,
    `- Client-resilient facade escape hatch ready: **${c.clientResilientFacadeEscapeHatchReady ? "yes" : "no"}**.`,
    `- Foundation exposes List/Invoke/BatchInvoke facades: **${c.foundationFacadesPresent ? "yes" : "no"}**.`,
    `- Static-all exposes representative missed native tools: **${c.staticAllNativeMissedToolRepresentativesPresent ? "yes" : "no"}**.`,
    `- Menu/List/Describe fallback guidance present: **${c.fallbackGuidancePresent ? "yes" : "no"}**.`,
    `- Repo-local plugin manifest fresh: **${c.pluginManifestFresh ? "yes" : "no"}**.`,
    `- Direct Codex prompt/context injection observed: **no**. ${c.promptInjectionLimit}`,
    `- Static-all startup register requested the full surface: **${c.staticAllRegisterRequestedFull ? "yes" : "no"}**.`,
    `- Static-all avoided startup pack-restore before first tools/list: **${c.staticAllAvoidedStartupPackRestore ? "yes" : "no"}**.`,
    `- Static \`Unity.SetToolPacks([\"assets\"])\` preserved full surface without list-changed: **${c.setToolPacksNoopKeptStaticToolCount ? "yes" : "no"}**.`,
    "",
    "## Startup tools/list",
    "",
    "| Mode | Tool count | Response bytes | Approx response tokens | Descriptor bytes | Schema approx tokens |",
    "| --- | ---: | ---: | ---: | ---: | ---: |",
    `| dynamic_packs | ${dynamic.toolCount} | ${dynamic.responseBodyBytes} | ${dynamic.responseBodyApproxTokens} | ${dynamic.descriptorBytes} | ${dynamic.schemaApproxTokens} |`,
    `| static_all | ${stat.toolCount} | ${stat.responseBodyBytes} | ${stat.responseBodyApproxTokens} | ${stat.descriptorBytes} | ${stat.schemaApproxTokens} |`,
    "",
    "## Facade presence",
    "",
    "| Mode | Unity_Tools_List | Unity_Tools_Invoke | Unity_Tools_BatchInvoke |",
    "| --- | --- | --- | --- |",
    `| dynamic_packs startup | ${dynamic.facadePresence.Unity_Tools_List ? "present" : "missing"} | ${dynamic.facadePresence.Unity_Tools_Invoke ? "present" : "missing"} | ${dynamic.facadePresence.Unity_Tools_BatchInvoke ? "present" : "missing"} |`,
    `| static_all startup | ${stat.facadePresence.Unity_Tools_List ? "present" : "missing"} | ${stat.facadePresence.Unity_Tools_Invoke ? "present" : "missing"} | ${stat.facadePresence.Unity_Tools_BatchInvoke ? "present" : "missing"} |`,
    "",
    "## Representative static-all tools present at startup",
    "",
    ...Object.entries(stat.representativePresence).map(([name, present]) => `- \`${name}\`: ${present ? "present" : "missing"}`),
    "",
    "## Plugin manifest discovery hint",
    "",
    `- Path: \`${manifest.path}\``,
    `- Fresh against generator: **${manifest.checkPassed ? "yes" : "no"}**`,
    `- Tool count: ${manifest.toolCount ?? "unknown"}`,
    `- Source of truth: \`${manifest.sourceOfTruth ?? "unknown"}\``,
    `- Execution source of truth: \`${manifest.executionSourceOfTruth ?? "unknown"}\``,
    `- Static-all configured: **${manifest.staticAllConfigured ? "yes" : "no"}**`,
    "",
    "Required manifest tools:",
    "",
    ...Object.entries(manifest.requiredToolPresence).map(([name, present]) => `- \`${name}\`: ${present ? "present" : "missing"}`),
    "",
    "## Facade fallback guidance",
    "",
    ...Object.entries(report.scenarios.static_all.fallbackGuidance).map(([name, value]) =>
      `- \`${name}\`: fallback=${value.hasClientSurfaceFallback ? "yes" : "no"}, invoke=${value.mentionsInvoke ? "yes" : "no"}, batch=${value.mentionsBatchInvoke ? "yes" : "no"}`),
    "",
    "## Resilience interpretation",
    "",
    `- Escape hatch ready: **${facade.escapeHatchReady ? "yes" : "no"}**.`,
    "- `Unity_Tools_List` gives clients a compact live index when direct tool tables are stale.",
    "- `Unity_Tools_Invoke` and `Unity_Tools_BatchInvoke` keep missed native tools callable through one stable surface.",
    "",
    "## Interpretation",
    "",
    "If a client inserts every `tools/list` descriptor into model-visible context, `static_all` has the startup cost shown above. If the client keeps tool schemas outside prompt context and exposes them through a native tool table, the protocol payload still grows but model context may not grow by the same amount. This script can prove the first hop; it cannot inspect Codex Desktop's hidden prompt assembly.",
    "",
  ];
  fs.writeFileSync(markdownPath, lines.join("\n"), "utf8");
}

function byteLength(value) {
  if (value === undefined || value === null) return 0;
  return Buffer.byteLength(JSON.stringify(value), "utf8");
}

function approxTokens(bytes) {
  return Math.ceil(bytes / 4);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const hostPath = path.resolve(args.hostPath || process.env.UNITY_MCP_LENS_HOST || defaultHostPath);
  const outputDir = path.resolve(args.outputDir || defaultOutputDir);
  const fakeFullToolCount = Math.max(40, Number(args.fakeFullToolCount || 80));

  if (!fs.existsSync(hostPath)) {
    throw new Error(`Host path does not exist: ${hostPath}`);
  }

  fs.mkdirSync(outputDir, { recursive: true });
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  const jsonPath = path.join(outputDir, `tool-surface-mode-evidence-${stamp}.json`);
  const markdownPath = path.join(outputDir, `tool-surface-mode-evidence-${stamp}.md`);
  const toolCatalog = buildToolCatalog(fakeFullToolCount);

  const dynamicScenario = await runScenario("dynamic_packs", toolCatalog, hostPath);
  const staticScenario = await runScenario("static_all", toolCatalog, hostPath);
  const pluginManifest = loadPluginManifestStatus();
  const facadeResilience = buildFacadeResilience(dynamicScenario, staticScenario, pluginManifest);
  const report = {
    schemaVersion: "tool-surface-mode-evidence.v2",
    capturedAtUtc: new Date().toISOString(),
    hostPath,
    fakeFullToolCount,
    pluginManifest,
    facadeResilience,
    scenarios: {
      dynamic_packs: dynamicScenario,
      static_all: staticScenario,
    },
    conclusion: buildConclusion(dynamicScenario, staticScenario, pluginManifest),
  };

  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2), "utf8");
  writeMarkdownReport(report, markdownPath);
  console.log(JSON.stringify({
    success: true,
    jsonPath,
    markdownPath,
    facadeResilience: report.facadeResilience,
    pluginManifest: report.pluginManifest,
    conclusion: report.conclusion,
  }, null, 2));
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
