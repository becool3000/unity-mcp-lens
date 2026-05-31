const assert = require("assert");
const childProcess = require("child_process");
const fs = require("fs");
const net = require("net");
const os = require("os");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");
const hostPath =
  process.env.UNITY_MCP_LENS_HOST ||
  path.join(repoRoot, "UnityMcpLensApp~", "src", "UnityMcpLens", "bin", "Debug", "net8.0", "UnityMcpLens.exe");

const requiredAssetTools = [
  "Unity_Asset_PreviewImportSpriteSheetAndBind",
  "Unity_Asset_ApplyImportSpriteSheetAndBind",
  "Unity_Asset_ImportSpriteSheetAndBind",
  "Unity_Asset_VerifySpriteArrayBinding",
  "Unity_Asset_SpriteSheetVisualDiagnostics",
  "Unity_Asset_VerifySpriteSlicesAndReferences",
  "Unity_Prefab_VerifySerializedProperties",
  "Unity_Prefab_AuditSerializedReferences",
  "Unity_Prefab_ExplainOverrides",
  "Unity_UI_VerifyPrefabLayoutMatrix",
];

const requiredBootstrapWorkflowTools = [
  "Unity_PlayMode_StepVerifier",
  "Unity_Editor_RecoverFromHang",
  "Unity_Workflow_RunGpuSimulationProbe",
  "Unity_Workflow_VerifyRuntimePackSelection",
  "Unity_Workflow_SelectPackThroughMainMenu",
];

const foundationToolNames = [
  "Unity_GetLensHealth",
  "Unity_Editor_HealthCheckFast",
  "Unity_ListToolPacks",
  "Unity_Bridge_ListConnections",
  "Unity_SetToolPacks",
  "Unity_ReadDetailRef",
  "Unity_Tools_Menu",
  "Unity_Tools_Describe",
  "Unity_Tools_List",
  "Unity_Tools_Invoke",
  "Unity_Tools_BatchInvoke",
  "Unity_Tools_ActivateAndVerify",
  "Unity_ReadConsole",
  "Unity_ListResources",
  "Unity_ReadResource",
  "Unity_FindInFile",
  "Unity_ManageEditor",
  "Unity_RunCommand",
  ...requiredBootstrapWorkflowTools,
];

const projectToolNames = [
  "Unity_Project_GetInfo",
  "Unity_Project_PackageCompatibility",
  "Unity_Project_DiagnoseImportSideEffects",
  "Unity_Project_BlockedLanguageScan",
  "Unity_Tests_Run",
];

const runtimeToolNames = [
  "Unity_Editor_SetPlayMode",
  "Unity_PlayMode_PointerInputSmoke",
  "Unity_PlayMode_InteractionSmoke",
  "Unity_Runtime_QueryObjects",
];

const sceneToolNames = [
  "Unity_GameObject_Inspect",
  "Unity_GameObject_ApplyChanges",
  "Unity_Camera_FitComposition",
  "Unity_Scene_PreviewGridBoardLayout",
  "Unity_Scene_ApplyGridBoardLayout",
  "Unity_Scene_PreviewBulkMutation",
  "Unity_Scene_ApplyBulkMutation",
];

const uiToolNames = [
  "Unity_UI_VerifyScreenLayout",
  "Unity_UI_VerifyPrefabLayoutMatrix",
  "Unity_UI_CaptureGameView",
  "Unity_UI_ApplyEnsureHierarchy",
];

const debugToolNames = [
  "Unity_GetLensUsageReport",
  "Unity_PlayMode_InteractionSmoke",
];

const assetToolNames = [
  "Unity_Asset_Search",
  "Unity_Asset_ConfigureSpriteImport",
  "Unity_Asset_SetSerializedProperties",
  "Unity_Asset_ImportSpriteSheetAndBind",
  "Unity_Asset_PreviewImportSpriteSheetAndBind",
  "Unity_Asset_ApplyImportSpriteSheetAndBind",
  "Unity_Asset_VerifySpriteArrayBinding",
  "Unity_Asset_SpriteSheetVisualDiagnostics",
  "Unity_Asset_VerifySpriteSlicesAndReferences",
  "Unity_ManageAsset",
  "Unity_Prefab_SetSerializedProperties",
  "Unity_Prefab_VerifySerializedProperties",
  "Unity_Prefab_AuditSerializedReferences",
  "Unity_Prefab_ExplainOverrides",
  "Unity_UI_VerifyPrefabLayoutMatrix",
  "Unity_Resource_Write",
  "Unity_Tile_BuildSet",
  "Unity_ImportExternalModel",
];

let nextPipeId = 1;
const processStartUtc = () => new Date(Date.now() - process.uptime() * 1000).toISOString();

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

class McpHostClient {
  constructor(projectRoot, statusDir, options = {}) {
    this.nextId = 1;
    this.pending = new Map();
    this.notifications = [];
    this.notificationWaiters = [];
    this.stderr = "";
    const env = {
      ...process.env,
      UNITY_MCP_STATUS_DIR: statusDir,
      UNITY_MCP_PROJECT_PATH: projectRoot,
    };
    const toolSurfaceMode = options.toolSurfaceMode ?? process.env.UNITY_MCP_LENS_TOOL_SURFACE_MODE;
    if (toolSurfaceMode) {
      env.UNITY_MCP_LENS_TOOL_SURFACE_MODE = toolSurfaceMode;
    } else {
      delete env.UNITY_MCP_LENS_TOOL_SURFACE_MODE;
    }

    this.child = childProcess.spawn(hostPath, [], {
      cwd: projectRoot,
      env,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });

    this.child.stderr.on("data", (chunk) => {
      this.stderr += chunk.toString("utf8");
    });

    this.child.stdout.on("data", createFrameParser((message) => {
      if (message.id !== undefined) {
        const pending = this.pending.get(message.id);
        if (!pending) return;
        this.pending.delete(message.id);
        if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
        else pending.resolve(message.result);
        return;
      }

      if (message.method) {
        this.notifications.push(message);
        this.resolveNotificationWaiters();
      }
    }));
  }

  async initialize() {
    await this.request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "dynamic-tool-exposure-test", version: "1.0.0" },
    });
    this.notify("notifications/initialized", {});
  }

  notify(method, params) {
    this.child.stdin.write(rpcFrame({ jsonrpc: "2.0", method, params }));
  }

  request(method, params = {}) {
    const id = this.nextId++;
    const payload = { jsonrpc: "2.0", id, method, params };
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}. stderr=${this.stderr}`));
      }, 15000);
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

  listTools() {
    return this.request("tools/list", { _meta: { progressToken: this.nextId } });
  }

  callTool(name, args = {}) {
    return this.request("tools/call", { name, arguments: args });
  }

  notificationCount(method) {
    return this.notifications.filter((notification) => notification.method === method).length;
  }

  waitForNotification(method, afterCount, timeoutMs = 5000) {
    if (this.notificationCount(method) > afterCount) return Promise.resolve();

    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.notificationWaiters = this.notificationWaiters.filter((waiter) => waiter.reject !== reject);
        reject(new Error(`Timed out waiting for ${method}. stderr=${this.stderr}`));
      }, timeoutMs);

      this.notificationWaiters.push({
        method,
        afterCount,
        resolve: () => {
          clearTimeout(timeout);
          resolve();
        },
        reject,
      });
    });
  }

  resolveNotificationWaiters() {
    for (const waiter of [...this.notificationWaiters]) {
      if (this.notificationCount(waiter.method) <= waiter.afterCount) continue;
      this.notificationWaiters = this.notificationWaiters.filter((candidate) => candidate !== waiter);
      waiter.resolve();
    }
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
      toolCount: fakeTools(this.context.activePacks, false).length,
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
    const type = command.type;
    this.context.commandCounts[type] = (this.context.commandCounts[type] || 0) + 1;
    if (type === "set_tool_packs") {
      this.context.setActivePacks(command.params && command.params.packs);
      this.writeStatus();
      this.respond(socket, command.requestId, manifestResult(this.context.activePacks, false));
      return;
    }

    this.respond(socket, command.requestId, resultFor(type, command.params, this.context));
  }

  respond(socket, requestId, result) {
    socket.write(JSON.stringify({ requestId, status: "success", result }) + "\n");
  }
}

class ScenarioContext {
  constructor() {
    this.root = fs.mkdtempSync(path.join(os.tmpdir(), "lens-dynamic-tools-"));
    this.statusDir = path.join(this.root, "connections");
    this.projectRoot = path.join(this.root, "Project");
    this.activePacks = ["foundation"];
    this.commandCounts = {};
    this.bridge = null;
    fs.mkdirSync(this.statusDir, { recursive: true });
    fs.mkdirSync(this.projectRoot, { recursive: true });
  }

  setActivePacks(packs) {
    const additionalPacks = (Array.isArray(packs) ? packs : [])
      .filter((pack) => typeof pack === "string" && pack.trim())
      .map((pack) => pack.trim())
      .filter((pack) => pack.toLowerCase() !== "foundation");
    const uniqueAdditionalPacks = [...new Set(additionalPacks)].sort((a, b) => a.localeCompare(b));
    this.activePacks = ["foundation", ...uniqueAdditionalPacks];
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
    return `\\\\.\\pipe\\unity-mcp-lens-dynamic-${process.pid}-${nextPipeId++}`;
  }

  return path.join(os.tmpdir(), `unity-mcp-lens-dynamic-${process.pid}-${nextPipeId++}.sock`);
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
    tools_hash: "fake-dynamic",
    tool_discovery_reason: null,
    tool_snapshot_utc: options.heartbeat.toISOString(),
    command_health: "ok",
    last_command_success_utc: options.heartbeat.toISOString(),
    last_command_failure_utc: null,
    last_command_failure_reason: null,
    bridge_session_id: "fake-dynamic-session",
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

  writeHealth(path.join(path.dirname(statusPath), `editor-health-${nextPipeId++}.json`), projectRoot, {
    heartbeat: options.heartbeat,
    editorPid: process.pid,
  });
}

function writeHealth(healthPath, projectRoot, options) {
  const heartbeat = options.heartbeat || new Date();
  fs.writeFileSync(healthPath, JSON.stringify({
    health_schema_version: 1,
    editor_heartbeat_utc: heartbeat.toISOString(),
    state_captured_utc: heartbeat.toISOString(),
    editor_pid: options.editorPid ?? process.pid,
    editor_process_start_utc: processStartUtc(),
    project_path: path.join(projectRoot, "Assets"),
    project_root: projectRoot,
    unity_version: "test-unity",
    lifecycle_state: "active",
    is_compiling: false,
    is_importing: false,
    is_updating: false,
    is_playing: false,
    is_paused: false,
    is_playing_or_will_change_playmode: false,
    is_building_player: false,
    active_scene_name: "TestScene",
    active_scene_path: "Assets/TestScene.unity",
    capture_error: null,
  }, null, 2));
}

function resultFor(type, params, context) {
  switch (type) {
    case "register_client":
      return {
        bridgeSessionId: "fake-dynamic-session",
        manifestVersion: 1,
        profileCatalogVersion: "fake-profile",
        activeToolPacks: context.activePacks,
      };
    case "get_manifest":
      return manifestResult(context.activePacks, false);
    case "get_tool_schema":
      return {
        bridgeSessionId: "fake-dynamic-session",
        manifestVersion: 1,
        activeToolPacks: context.activePacks,
        tools: fakeTools(context.activePacks, true),
      };
    case "Unity_GetLensHealth":
      return {
        success: true,
        message: "Fake Lens health ready.",
        data: {
          activeToolPacks: context.activePacks,
          toolSurfaceMode: context.activePacks.some((pack) => pack.toLowerCase() === "full") ? "static_all" : "dynamic_packs",
          bridgeStatus: { status: "ready", toolDiscoveryMode: "live" },
          internalRegistryToolCount: fakeTools(["foundation", "full"], false).length,
          editorStability: { isStable: true },
          expectedRecovery: { isActive: false },
        },
      };
    case "Unity_ListToolPacks":
      return {
        success: true,
        message: "Fake tool packs listed.",
        data: {
          activeToolPacks: context.activePacks,
          availableToolPacks: ["foundation", "project", "runtime", "assets", "scene", "ui", "debug", "full"],
        },
      };
    case "Unity_Tools_List":
      return toolListResult(context.activePacks, params || {});
    case "Unity_Tools_Describe":
      return describeResult(context.activePacks);
    case "Unity_Tools_Menu":
      return menuResult(context.activePacks);
    default:
      return { success: true, message: `${type} ok`, data: {} };
  }
}

function manifestResult(activeToolPacks, withSchemas) {
  return {
    bridgeSessionId: "fake-dynamic-session",
    manifestVersion: 1,
    profileCatalogVersion: "fake-profile",
    activeToolPacks,
    kind: "full",
    reason: null,
    hashMinimal: `fake-minimal-${activeToolPacks.join("-")}`,
    hashFull: `fake-full-${activeToolPacks.join("-")}`,
    tools: fakeTools(activeToolPacks, withSchemas),
    delta: null,
  };
}

function fakeTools(activeToolPacks, withSchemas) {
  const tools = [];
  for (const name of foundationToolNames) {
    addToolDescriptor(tools, name, ["foundation"], isReadOnlyFoundationTool(name), withSchemas);
  }
  const active = new Set((activeToolPacks || []).map((pack) => String(pack).toLowerCase()));
  const hasFull = active.has("full");
  if (hasFull || active.has("project")) {
    for (const name of projectToolNames) addToolDescriptor(tools, name, ["project"], isReadOnlyTool(name), withSchemas);
  }
  if (hasFull || active.has("runtime")) {
    for (const name of runtimeToolNames) addToolDescriptor(tools, name, ["runtime"], isReadOnlyTool(name), withSchemas);
  }
  if (hasFull || active.has("scene")) {
    for (const name of sceneToolNames) addToolDescriptor(tools, name, ["scene"], isReadOnlyTool(name), withSchemas);
  }
  if (hasFull || active.has("ui")) {
    for (const name of uiToolNames) addToolDescriptor(tools, name, ["ui"], isReadOnlyTool(name), withSchemas);
  }
  if (hasFull || active.has("debug")) {
    for (const name of debugToolNames) addToolDescriptor(tools, name, ["debug"], isReadOnlyTool(name), withSchemas);
  }
  if (hasFull || active.has("assets")) {
    for (const name of assetToolNames) addToolDescriptor(tools, name, ["assets"], isReadOnlyTool(name), withSchemas);
  }
  return tools;
}

function menuResult(activeToolPacks) {
  const fullSurface = activeToolPacks.some((pack) => pack.toLowerCase() === "full");
  const packs = [
    ["foundation", "Foundation"],
    ["project", "Project Diagnostics"],
    ["runtime", "Runtime Verification"],
    ["assets", "Assets"],
    ["scene", "Scene Editing"],
    ["ui", "UI Authoring"],
    ["debug", "Debug"],
  ].map(([packId, title]) => {
    const packTools = fakeTools(["foundation", packId], false)
      .filter((tool) => (tool.packs || []).includes(packId))
      .map((tool) => ({
        name: tool.name,
        title: tool.title,
        readOnlyHint: tool.readOnlyHint,
        mutationHint: tool.readOnlyHint ? "read_only" : "mutating",
      }));
    return {
      packId,
      title,
      description: `${title} tools`,
      alwaysOn: packId === "foundation",
      adminOnly: false,
      isActive: fullSurface || activeToolPacks.some((pack) => pack.toLowerCase() === packId),
      toolCount: packTools.length,
      readOnlyToolCount: packTools.filter((tool) => tool.readOnlyHint).length,
      mutatingToolCount: packTools.filter((tool) => !tool.readOnlyHint).length,
      truncated: false,
      tools: packTools,
    };
  });

  return {
    success: true,
    message: "Fake tool menu.",
    data: {
      toolSurfaceMode: fullSurface ? "static_all" : "dynamic_packs",
      activeToolPacks,
      totalToolCount: fakeTools(["foundation", "full"], false).length,
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

function describeResult(activeToolPacks) {
  const fullSurface = activeToolPacks.some((pack) => pack.toLowerCase() === "full");
  return {
    success: true,
    message: "Described fake Unity MCP Lens tools.",
    data: {
      toolSurfaceMode: fullSurface ? "static_all" : "dynamic_packs",
      activeToolPacks,
      totalToolCount: fakeTools(["foundation", "full"], false).length,
      returnedToolCount: 0,
      clientSurfaceFallback: clientSurfaceFallback(),
      tools: [],
    },
  };
}

function toolListResult(activeToolPacks, params) {
  const fullSurface = activeToolPacks.some((pack) => pack.toLowerCase() === "full");
  const groupBy = params.groupBy || "pack";
  const maxToolsPerGroup = Math.min(500, Math.max(1, Number(params.maxToolsPerGroup || 100)));
  const rows = fakeTools(activeToolPacks, false)
    .map((tool) => ({
      name: tool.name,
      canonicalToolName: tool.name,
      title: tool.title,
      readOnlyHint: tool.readOnlyHint,
      schemaHash: tool.schemaHash,
      packs: tool.packs || [],
      groups: tool.groups || [],
    }))
    .sort((a, b) => a.name.localeCompare(b.name));
  const data = {
    toolSurfaceMode: fullSurface ? "static_all" : "dynamic_packs",
    activeToolPacks,
    exportedToolCount: rows.length,
    groupBy,
    maxToolsPerGroup,
    truncated: false,
    bridgeRefresh: { attempted: true, succeeded: true, skippedReason: null, error: null },
    clientSurfaceFallback: clientSurfaceFallback(),
  };

  if (groupBy === "flat") {
    data.tools = rows;
    data.groups = null;
  } else {
    const grouped = new Map();
    for (const row of rows) {
      const keys = (groupBy === "group" ? row.groups : row.packs.filter((pack) => pack !== "full"));
      for (const key of keys.length ? keys : ["ungrouped"]) {
        if (!grouped.has(key)) grouped.set(key, []);
        grouped.get(key).push(row);
      }
    }
    data.tools = null;
    data.groups = [...grouped.entries()]
      .sort(([left], [right]) => left.localeCompare(right))
      .map(([id, tools]) => ({
        id,
        toolCount: tools.length,
        readOnlyToolCount: tools.filter((tool) => tool.readOnlyHint).length,
        mutatingToolCount: tools.filter((tool) => !tool.readOnlyHint).length,
        truncated: tools.length > maxToolsPerGroup,
        tools: tools.slice(0, maxToolsPerGroup),
      }));
    data.truncated = data.groups.some((group) => group.truncated);
  }

  return {
    success: true,
    message: `Listed ${rows.length} fake tools.`,
    data,
  };
}

function clientSurfaceFallback() {
  return {
    listTool: "Unity.Tools.List",
    invokeTool: "Unity.Tools.Invoke",
    batchInvokeTool: "Unity.Tools.BatchInvoke",
  };
}

function toolDescriptor(name, packs, readOnlyHint, withSchemas) {
  const descriptor = {
    name,
    title: titleFromName(name),
    description: `${name} fake descriptor`,
    schemaHash: `${name}-schema-v1`,
    groups: ["assistant", ...packs],
    packs,
    readOnlyHint,
  };
  if (withSchemas) {
    descriptor.inputSchema = schemaFor(name);
    descriptor.outputSchema = { type: "object", properties: {} };
    descriptor.annotations = { readOnlyHint };
  }
  return descriptor;
}

function addToolDescriptor(tools, name, packs, readOnlyHint, withSchemas) {
  const existing = tools.find((tool) => tool.name === name);
  if (!existing) {
    tools.push(toolDescriptor(name, packs, readOnlyHint, withSchemas));
    return;
  }

  existing.packs = [...new Set([...(existing.packs || []), ...packs])];
  existing.groups = [...new Set([...(existing.groups || []), ...packs])];
  existing.readOnlyHint = existing.readOnlyHint && readOnlyHint;
  if (existing.annotations) existing.annotations.readOnlyHint = existing.readOnlyHint;
}

function isReadOnlyTool(name) {
  return name === "Unity_Project_GetInfo" ||
    name === "Unity_Project_PackageCompatibility" ||
    name === "Unity_Project_DiagnoseImportSideEffects" ||
    name === "Unity_Project_BlockedLanguageScan" ||
    name === "Unity_GameObject_Inspect" ||
    name === "Unity_Scene_PreviewGridBoardLayout" ||
    name === "Unity_Scene_PreviewBulkMutation" ||
    name === "Unity_UI_VerifyScreenLayout" ||
    name === "Unity_GetLensUsageReport" ||
    name === "Unity_Runtime_QueryObjects" ||
    name === "Unity_Asset_Search" ||
    name === "Unity_Asset_PreviewImportSpriteSheetAndBind" ||
    name === "Unity_Asset_VerifySpriteArrayBinding" ||
    name === "Unity_Asset_SpriteSheetVisualDiagnostics" ||
    name === "Unity_Asset_VerifySpriteSlicesAndReferences" ||
    name === "Unity_Prefab_VerifySerializedProperties" ||
    name === "Unity_Prefab_AuditSerializedReferences" ||
    name === "Unity_Prefab_ExplainOverrides" ||
    name === "Unity_UI_VerifyPrefabLayoutMatrix";
}

function isReadOnlyFoundationTool(name) {
  return name === "Unity_Bridge_ListConnections" ||
    name !== "Unity_SetToolPacks" &&
    name !== "Unity_Tools_Invoke" &&
    name !== "Unity_Tools_BatchInvoke" &&
    name !== "Unity_Tools_ActivateAndVerify" &&
    name !== "Unity_ManageEditor" &&
    name !== "Unity_RunCommand" &&
    !requiredBootstrapWorkflowTools.includes(name);
}

function titleFromName(name) {
  return name.replace(/^Unity_/, "Unity ").replace(/_/g, " ");
}

function schemaFor(name) {
  if (name === "Unity_SetToolPacks") {
    return {
      type: "object",
      properties: {
        packs: { type: "array", items: { type: "string" } },
      },
    };
  }
  if (name === "Unity_Tools_Menu") {
    return {
      type: "object",
      properties: {
        maxToolsPerPack: { type: "integer" },
      },
    };
  }
  if (name === "Unity_Tools_List") {
    return {
      type: "object",
      properties: {
        groupBy: { type: "string", enum: ["pack", "group", "flat"] },
        maxToolsPerGroup: { type: "integer" },
      },
    };
  }
  if (name === "Unity_Tools_Invoke") {
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
  if (name === "Unity_Tools_BatchInvoke") {
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
  if (name === "Unity_Bridge_ListConnections") {
    return {
      type: "object",
      properties: {
        projectPath: { type: "string" },
        includeStale: { type: "boolean" },
        maxEntries: { type: "integer" },
      },
    };
  }
  if (name === "Unity_Editor_HealthCheckFast") {
    return {
      type: "object",
      properties: {
        projectPath: { type: "string" },
        includeCandidates: { type: "boolean" },
        maxEntries: { type: "integer" },
        timeoutMs: { type: "integer" },
      },
    };
  }
  if (name === "Unity_Runtime_QueryObjects") {
    return {
      type: "object",
      properties: {
        componentTypes: { type: "array", items: { type: "string" } },
        includeInactive: { type: "boolean" },
        maxSamplesPerType: { type: "integer" },
      },
    };
  }
  if (name === "Unity_PlayMode_InteractionSmoke") return interactionSmokeSchema();
  if (name === "Unity_Asset_SetSerializedProperties") {
    return {
      type: "object",
      properties: {
        assetPath: { type: "string" },
        mode: { type: "string", enum: ["preview", "apply"] },
        assignments: {
          type: "array",
          items: {
            type: "object",
            properties: {
              propertyPath: { type: "string" },
              value: {},
              objectReferencePath: { type: "string" },
            },
          },
        },
      },
    };
  }
  if (name === "Unity_Asset_ImportSpriteSheetAndBind") return importSpriteSheetSchema(true);
  if (name === "Unity_Asset_PreviewImportSpriteSheetAndBind") return importSpriteSheetSchema(false);
  if (name === "Unity_Asset_ApplyImportSpriteSheetAndBind") return importSpriteSheetSchema(false);
  if (name === "Unity_Asset_VerifySpriteArrayBinding") return verifySpriteArrayBindingSchema();
  if (name === "Unity_Asset_SpriteSheetVisualDiagnostics") return spriteSheetVisualDiagnosticsSchema();
  if (name === "Unity_Asset_VerifySpriteSlicesAndReferences") return verifySpriteSlicesAndReferencesSchema();
  if (name === "Unity_Prefab_AuditSerializedReferences") return prefabAuditSerializedReferencesSchema();
  if (name === "Unity_Prefab_ExplainOverrides") return prefabExplainOverridesSchema();
  if (name === "Unity_UI_VerifyPrefabLayoutMatrix") return uiPrefabLayoutMatrixSchema();
  if (name === "Unity_Project_BlockedLanguageScan") return blockedLanguageScanSchema();
  if (name === "Unity_Asset_Search") {
    return {
      type: "object",
      properties: {
        query: { type: "string" },
        labels: { type: "array", items: { type: "string" } },
      },
    };
  }
  if (name === "Unity_Tests_Run") {
    return {
      type: "object",
      properties: {
        mode: { type: "string", enum: ["EditMode", "PlayMode"] },
        assembly: { type: "string" },
        assemblies: { type: "array", items: { type: "string" } },
        filter: { type: "string" },
        testNames: { type: "array", items: { type: "string" } },
        category: { type: "string" },
        categories: { type: "array", items: { type: "string" } },
        timeoutMs: { type: "integer" },
        timeoutSeconds: { type: "integer" },
        maxFailedTests: { type: "integer" },
        maxAssertionMessages: { type: "integer" },
        captureConsoleDelta: { type: "boolean" },
      },
    };
  }
  if (name === "Unity_UI_CaptureGameView") return captureGameViewSchema();
  if (name === "Unity_Camera_FitComposition") {
    return {
      type: "object",
      properties: {
        target: { type: "string" },
        searchMethod: { type: "string" },
        namePrefix: { type: "string" },
        nameExact: { type: "string" },
        componentTypes: { type: "array", items: { type: "string" } },
        componentMatch: { type: "string" },
        root: { type: "string" },
        rootSearchMethod: { type: "string" },
        scene: { type: "string" },
        includeInactive: { type: "boolean" },
        cameraTarget: { type: "string" },
        cameraSearchMethod: { type: "string" },
        desiredCoverageMin: { type: "number" },
        desiredCoverageMax: { type: "number" },
        aspectRatio: { type: "number" },
        viewportWidth: { type: "integer" },
        viewportHeight: { type: "integer" },
        captureScreenshot: { type: "boolean" },
        outputPath: { type: "string" },
        screenshotWidth: { type: "integer" },
        screenshotHeight: { type: "integer" },
        maxRows: { type: "integer" },
      },
    };
  }
  if (name === "Unity_Scene_PreviewGridBoardLayout") return gridBoardLayoutSchema(false);
  if (name === "Unity_Scene_ApplyGridBoardLayout") return gridBoardLayoutSchema(true);
  if (name === "Unity_Scene_PreviewBulkMutation") return bulkMutationSchema(false);
  if (name === "Unity_Scene_ApplyBulkMutation") return bulkMutationSchema(true);

  return { type: "object", properties: {} };
}

function bulkMutationSchema(includeSaveScene) {
  const properties = {
    scene: { type: "string" },
    scenePath: { type: "string" },
    namePrefix: { type: "string" },
    nameExact: { type: "string" },
    componentTypes: { type: "array", items: { type: "string" } },
    componentType: { type: "string" },
    componentMatch: { type: "string" },
    root: { type: "string" },
    rootSearchMethod: { type: "string" },
    includeInactive: { type: "boolean" },
    gridFieldName: { type: "string" },
    gridFieldComponentType: { type: "string" },
    gridFieldComponentIndex: { type: "integer" },
    fieldVariables: { type: "array", items: {} },
    mutations: { type: "array", items: {} },
    maxObjects: { type: "integer" },
    maxRows: { type: "integer" },
    allowPartial: { type: "boolean" },
  };
  if (includeSaveScene) properties.saveScene = { type: "boolean" };
  return {
    type: "object",
    properties,
    required: ["componentTypes", "mutations"],
  };
}

function gridBoardLayoutSchema(includeSaveScene) {
  const properties = {
    scenePath: { type: "string" },
    boardWidth: { type: "integer" },
    boardHeight: { type: "integer" },
    boardSize: {},
    tileComponentType: { type: "string" },
    gridFieldName: { type: "string" },
    gridFieldComponentType: { type: "string" },
    gridFieldComponentIndex: { type: "integer" },
    root: { type: "string" },
    rootSearchMethod: { type: "string" },
    includeInactive: { type: "boolean" },
    projectionType: { type: "string" },
    tileSize: {},
    tileSizeX: { type: "number" },
    tileSizeY: { type: "number" },
    tileSizeZ: { type: "number" },
    origin: {},
    originX: { type: "number" },
    originY: { type: "number" },
    originZ: { type: "number" },
    useLocalPosition: { type: "boolean" },
    classificationFieldName: { type: "string" },
    obstacleFieldName: { type: "string" },
    floorValue: { type: "string" },
    obstacleValue: { type: "string" },
    sortingBase: { type: "integer" },
    floorSortingBase: { type: "integer" },
    obstacleSortingBase: { type: "integer" },
    sortingRowStride: { type: "integer" },
    sortingColumnStride: { type: "integer" },
    sortingZStride: { type: "integer" },
    sortingLayerName: { type: "string" },
    applySorting: { type: "boolean" },
    cameraFitMode: { type: "string" },
    cameraTarget: { type: "string" },
    cameraSearchMethod: { type: "string" },
    desiredCoverageMin: { type: "number" },
    desiredCoverageMax: { type: "number" },
    aspectRatio: { type: "number" },
    viewportWidth: { type: "integer" },
    viewportHeight: { type: "integer" },
    maxRows: { type: "integer" },
    maxOverlapSamples: { type: "integer" },
  };
  if (includeSaveScene) properties.saveScene = { type: "boolean" };
  return {
    type: "object",
    properties,
    required: ["tileComponentType"],
  };
}

function importSpriteSheetSchema(includeMode) {
  const properties = {
    assetPath: { type: "string" },
    frameCount: { type: "integer" },
    frameWidth: { type: "integer" },
    frameHeight: { type: "integer" },
    paddingX: { type: "integer" },
    paddingY: { type: "integer" },
    offsetX: { type: "integer" },
    offsetY: { type: "integer" },
    spriteNamePrefix: { type: "string" },
    pixelsPerUnit: { type: "number" },
    mipmapEnabled: { type: "boolean" },
    alphaIsTransparency: { type: "boolean" },
    compression: { type: "string" },
    filterMode: { type: "string" },
    wrapMode: { type: "string" },
    targetAssetPath: { type: "string" },
    targetFieldName: { type: "string" },
  };

  if (includeMode) {
    properties.mode = { type: "string", enum: ["preview", "apply"] };
    properties.apply = { type: "boolean" };
  }

  return {
    type: "object",
    properties,
    required: ["assetPath", "frameCount", "frameWidth", "frameHeight", "targetAssetPath", "targetFieldName"],
  };
}

function verifySpriteArrayBindingSchema() {
  return {
    type: "object",
    properties: {
      targetAssetPath: { type: "string" },
      targetFieldName: { type: "string" },
      expectedCount: { type: "integer" },
      expectedTextureName: { type: "string" },
      expectedTextureGuid: { type: "string" },
      expectedSpriteNames: { type: "array", items: { type: "string" } },
    },
    required: ["targetAssetPath", "targetFieldName"],
  };
}

function captureGameViewSchema() {
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

function interactionSmokeSchema() {
  return {
    type: "object",
    properties: {
      scenePath: { type: "string" },
      enterPlayMode: { type: "boolean" },
      exitAfter: { type: "boolean" },
      waitMs: { type: "integer" },
      consoleCount: { type: "integer" },
      failFast: { type: "boolean" },
      steps: {
        type: "array",
        items: {
          type: "object",
          properties: {
            type: { type: "string" },
            label: { type: "string" },
            target: { type: "string" },
            targetPath: { type: "string" },
            searchMethod: { type: "string" },
            includeInactive: { type: "boolean" },
            action: { type: "string" },
            value: {},
            screenX: { type: "number" },
            screenY: { type: "number" },
            key: { type: "string" },
            keys: { type: "array", items: { type: "string" } },
            holdFrames: { type: "integer" },
            waitFrames: { type: "integer" },
            waitMs: { type: "integer" },
            expectedActive: { type: "boolean" },
            active: { type: "boolean" },
            componentType: { type: "string" },
            outputPath: { type: "string" },
          },
          required: ["type"],
        },
      },
    },
    required: ["steps"],
  };
}

function spriteSheetVisualDiagnosticsSchema() {
  return {
    type: "object",
    properties: {
      assetPath: { type: "string" },
      frameCount: { type: "integer" },
      frameWidth: { type: "integer" },
      frameHeight: { type: "integer" },
      columns: { type: "integer" },
      rows: { type: "integer" },
      paddingX: { type: "integer" },
      paddingY: { type: "integer" },
      offsetX: { type: "integer" },
      offsetY: { type: "integer" },
      spriteNamePrefix: { type: "string" },
      expectedSpriteNames: { type: "array", items: { type: "string" } },
      alphaThreshold: { type: "number" },
      emptyAlphaCoverageThreshold: { type: "number" },
      oversizedPaddingRatio: { type: "number" },
      minUsableAreaCoverage: { type: "number" },
      textArtifactSensitivity: { type: "number" },
      maxCells: { type: "integer" },
    },
    required: ["assetPath"],
  };
}

function verifySpriteSlicesAndReferencesSchema() {
  return {
    type: "object",
    properties: {
      assetPath: { type: "string" },
      expectedSpriteNames: { type: "array", items: { type: "string" } },
      expectedSprites: {
        type: "array",
        items: {
          type: "object",
          properties: {
            name: { type: "string" },
            x: { type: "number" },
            y: { type: "number" },
            width: { type: "number" },
            height: { type: "number" },
            pixelsPerUnit: { type: "number" },
          },
        },
      },
      expectedSettings: { type: "object" },
      prefabPath: { type: "string" },
      prefabPaths: { type: "array", items: { type: "string" } },
      under: { type: "string" },
      nameFilter: { type: "string" },
      expectedPrefabReferences: {
        type: "array",
        items: {
          type: "object",
          properties: {
            prefabPath: { type: "string" },
            target: { type: "string" },
            targetPath: { type: "string" },
            searchMethod: { type: "string" },
            expectedSpriteName: { type: "string" },
          },
        },
      },
      requireAllScannedImagesUseAtlas: { type: "boolean" },
      includeInactive: { type: "boolean" },
      verifyAlpha: { type: "boolean" },
      alphaThreshold: { type: "number" },
      emptyAlphaCoverageThreshold: { type: "number" },
      maxPrefabs: { type: "integer" },
      maxSprites: { type: "integer" },
      maxFindings: { type: "integer" },
    },
    required: ["assetPath"],
  };
}

function prefabAuditSerializedReferencesSchema() {
  return {
    type: "object",
    properties: {
      prefabPath: { type: "string" },
      prefabPaths: { type: "array", items: { type: "string" } },
      under: { type: "string" },
      nameFilter: { type: "string" },
      maxPrefabs: { type: "integer" },
      maxFindings: { type: "integer" },
      referenceNullPolicy: { type: "string", enum: ["broken_only", "likely_required", "all"] },
      includeNestedPrefabInstances: { type: "boolean" },
      includeRuntimeLoadPatterns: { type: "boolean" },
    },
    required: [],
  };
}

function prefabExplainOverridesSchema() {
  return {
    type: "object",
    properties: {
      target: {},
      searchMethod: { type: "string" },
      includeInactive: { type: "boolean" },
      action: { type: "string", enum: ["apply", "revert", "both"] },
      overrideIds: { type: "array", items: { type: "string" } },
      propertyPaths: { type: "array", items: { type: "string" } },
      targetPaths: { type: "array", items: { type: "string" } },
      includeNested: { type: "boolean" },
      applyAll: { type: "boolean" },
      revertAll: { type: "boolean" },
      maxOverrides: { type: "integer" },
    },
    required: ["target"],
  };
}

function uiPrefabLayoutMatrixSchema() {
  return {
    type: "object",
    properties: {
      prefabPath: { type: "string" },
      resolutions: {
        type: "array",
        items: {
          type: "object",
          properties: {
            key: { type: "string" },
            width: { type: "integer" },
            height: { type: "integer" },
          },
          required: ["width", "height"],
        },
      },
      states: {
        type: "array",
        items: {
          type: "object",
          properties: {
            name: { type: "string" },
            temporaryActivations: temporaryActivationsSchema(),
          },
        },
      },
      temporaryActivations: temporaryActivationsSchema(),
      includeInactive: { type: "boolean" },
      maxElements: { type: "integer" },
      maxFindings: { type: "integer" },
      checks: {
        type: "object",
        properties: {
          boundsWithinCanvas: { type: "boolean" },
          textOverflow: { type: "boolean" },
          zeroOrNegativeSize: { type: "boolean" },
        },
      },
    },
    required: ["prefabPath"],
  };
}

function temporaryActivationsSchema() {
  return {
    type: "array",
    items: {
      type: "object",
      properties: {
        target: { type: "string" },
        targetPath: { type: "string" },
        searchMethod: { type: "string" },
        includeInactive: { type: "boolean" },
        active: { type: "boolean" },
      },
    },
  };
}

function blockedLanguageScanSchema() {
  return {
    type: "object",
    properties: {
      blockedTerms: { type: "array", items: { type: "string" } },
      terms: { type: "array", items: { type: "string" } },
      forbiddenTerms: { type: "array", items: { type: "string" } },
      blockedTermsPath: { type: "string" },
      termFile: { type: "string" },
      termsAssetPath: { type: "string" },
      matchMode: { type: "string" },
      caseSensitive: { type: "boolean" },
      wholeWord: { type: "boolean" },
      under: { type: "string" },
      assetPaths: { type: "array", items: { type: "string" } },
      includeScripts: { type: "boolean" },
      includeScenes: { type: "boolean" },
      includeLoadedScenes: { type: "boolean" },
      includePrefabs: { type: "boolean" },
      includeScriptableObjects: { type: "boolean" },
      includeTextAssets: { type: "boolean" },
      includePackages: { type: "boolean" },
      maxAssets: { type: "integer" },
      maxFindings: { type: "integer" },
      maxTextBytes: { type: "integer" },
      maxSerializedObjectsPerAsset: { type: "integer" },
      contextChars: { type: "integer" },
    },
    required: [],
  };
}

function assertNamesInclude(names, expectedNames, messagePrefix) {
  for (const expectedName of expectedNames) {
    assert(names.includes(expectedName), `${messagePrefix} missing ${expectedName}`);
  }
}

function assertArraySchemasHaveItems(tools) {
  for (const tool of tools) {
    walkSchema(tool.inputSchema, `${tool.name}.inputSchema`);
  }
}

function assertReadOnlyHint(tools, name, expected) {
  const tool = tools.find((item) => item.name === name);
  assert(tool, `tools/list missing ${name}`);
  const actual = tool.annotations && Object.prototype.hasOwnProperty.call(tool.annotations, "readOnlyHint")
    ? tool.annotations.readOnlyHint
    : tool.readOnlyHint;
  assert.strictEqual(actual, expected, `${name} readOnlyHint`);
}

function assertToolSchemaProperties(tools, name, propertyNames, requiredNames = []) {
  const tool = tools.find((item) => item.name === name);
  assert(tool, `tools/list missing ${name}`);
  assert(tool.inputSchema && tool.inputSchema.properties, `${name} should expose inputSchema.properties`);
  for (const propertyName of propertyNames) {
    assert(
      Object.prototype.hasOwnProperty.call(tool.inputSchema.properties, propertyName),
      `${name} schema should include ${propertyName}`,
    );
  }
  for (const requiredName of requiredNames) {
    assert(
      Array.isArray(tool.inputSchema.required) && tool.inputSchema.required.includes(requiredName),
      `${name} schema should require ${requiredName}`,
    );
  }
}

function assertBatchInvokeSchema(tools) {
  assertToolSchemaProperties(tools, "Unity_Tools_BatchInvoke", ["calls", "failFast"], ["calls"]);
  const tool = tools.find((item) => item.name === "Unity_Tools_BatchInvoke");
  const callItemSchema = tool.inputSchema.properties.calls.items;
  assert(callItemSchema && callItemSchema.properties, "Unity_Tools_BatchInvoke calls.items should expose properties");
  for (const propertyName of ["toolName", "arguments", "timeoutMs"]) {
    assert(
      Object.prototype.hasOwnProperty.call(callItemSchema.properties, propertyName),
      `Unity_Tools_BatchInvoke call schema should include ${propertyName}`,
    );
  }
  assert(
    Array.isArray(callItemSchema.required) && callItemSchema.required.includes("toolName"),
    "Unity_Tools_BatchInvoke call schema should require toolName",
  );
}

function assertPrefabLayoutMatrixSchema(tools) {
  assertReadOnlyHint(tools, "Unity_UI_VerifyPrefabLayoutMatrix", true);
  assertToolSchemaProperties(tools, "Unity_UI_VerifyPrefabLayoutMatrix", [
    "prefabPath",
    "resolutions",
    "states",
    "temporaryActivations",
    "includeInactive",
    "maxElements",
    "maxFindings",
    "checks",
  ], ["prefabPath"]);
}

function assertPrefabExplainOverridesSchema(tools) {
  assertReadOnlyHint(tools, "Unity_Prefab_ExplainOverrides", true);
  assertToolSchemaProperties(tools, "Unity_Prefab_ExplainOverrides", [
    "target",
    "searchMethod",
    "includeInactive",
    "action",
    "overrideIds",
    "propertyPaths",
    "targetPaths",
    "includeNested",
    "applyAll",
    "revertAll",
    "maxOverrides",
  ], ["target"]);
}

function assertSpriteSliceReferenceVerifierSchema(tools) {
  assertReadOnlyHint(tools, "Unity_Asset_VerifySpriteSlicesAndReferences", true);
  assertToolSchemaProperties(tools, "Unity_Asset_VerifySpriteSlicesAndReferences", [
    "assetPath",
    "expectedSpriteNames",
    "expectedSprites",
    "expectedSettings",
    "prefabPath",
    "prefabPaths",
    "under",
    "nameFilter",
    "expectedPrefabReferences",
    "requireAllScannedImagesUseAtlas",
    "includeInactive",
    "verifyAlpha",
    "alphaThreshold",
    "emptyAlphaCoverageThreshold",
    "maxPrefabs",
    "maxSprites",
    "maxFindings",
  ], ["assetPath"]);
}

function assertInteractionSmokeSchema(tools) {
  assertReadOnlyHint(tools, "Unity_PlayMode_InteractionSmoke", false);
  assertToolSchemaProperties(tools, "Unity_PlayMode_InteractionSmoke", [
    "scenePath",
    "enterPlayMode",
    "exitAfter",
    "waitMs",
    "consoleCount",
    "failFast",
    "steps",
  ], ["steps"]);
  const tool = tools.find((item) => item.name === "Unity_PlayMode_InteractionSmoke");
  const stepSchema = tool.inputSchema.properties.steps.items;
  assert(stepSchema && stepSchema.properties, "Unity_PlayMode_InteractionSmoke steps.items should expose properties");
  for (const propertyName of ["type", "key", "keys", "holdFrames", "waitFrames"]) {
    assert(
      Object.prototype.hasOwnProperty.call(stepSchema.properties, propertyName),
      `Unity_PlayMode_InteractionSmoke step schema should include ${propertyName}`,
    );
  }
  assert(
    Array.isArray(stepSchema.required) && stepSchema.required.includes("type"),
    "Unity_PlayMode_InteractionSmoke step schema should require type",
  );
}

function assertToolsListFacadePayload(result, expectedNames) {
  assert.strictEqual(result.isError, false, "Unity_Tools_List should not return an MCP error");
  assert.strictEqual(result.structuredContent.success, true, "Unity_Tools_List should report success");
  assert(result.structuredContent.data.clientSurfaceFallback, "Unity_Tools_List should include facade fallback metadata");
  assert.strictEqual(result.structuredContent.data.clientSurfaceFallback.invokeTool, "Unity.Tools.Invoke");

  const rows = result.structuredContent.data.groupBy === "flat"
    ? result.structuredContent.data.tools
    : result.structuredContent.data.groups.flatMap((group) => group.tools);
  assert(rows.length > 0, "Unity_Tools_List should return tool rows");
  for (const row of rows) {
    assert(row.name, "Unity_Tools_List row should include name");
    assert(row.canonicalToolName, "Unity_Tools_List row should include canonicalToolName");
    assert.strictEqual(typeof row.readOnlyHint, "boolean", "Unity_Tools_List row should include readOnlyHint");
    assert(row.schemaHash, "Unity_Tools_List row should include schemaHash");
    assert(Array.isArray(row.packs), "Unity_Tools_List row should include packs");
    assert(Array.isArray(row.groups), "Unity_Tools_List row should include groups");
  }

  assertNamesInclude(rows.map((row) => row.name), expectedNames, "Unity_Tools_List rows");
}

function assertLensDevPluginManifest(pluginConfig) {
  const manifestGenerator = path.join(repoRoot, "Tools~", "Export-LensDevPluginManifest.js");
  childProcess.execFileSync(process.execPath, [manifestGenerator, "--check"], {
    cwd: repoRoot,
    encoding: "utf8",
    stdio: "pipe",
  });

  const manifestPath = path.join(repoRoot, ".agents", "plugins", "lens-dev-plugin", "manifest.json");
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  assert.strictEqual(manifest.manifest_version, "0.3", "Lens plugin manifest version");
  assert.strictEqual(manifest.sourceOfTruth, "discovery_hint_only", "Lens plugin manifest should be a discovery hint only");
  assert.strictEqual(
    manifest.executionSourceOfTruth,
    "Lens host tools/list and Unity bridge manifest",
    "Lens plugin manifest should not be execution source of truth",
  );
  assert.strictEqual(
    manifest.server.mcp_config.env.UNITY_MCP_LENS_TOOL_SURFACE_MODE,
    "static_all",
    "Lens plugin manifest should preserve static_all MCP config hint",
  );
  assert.deepStrictEqual(
    manifest.server.mcp_config,
    pluginConfig.mcpServers.unity_mcp_lens,
    "Lens plugin manifest MCP config should mirror repo-local plugin .mcp.json",
  );

  assert(Array.isArray(manifest.tools), "Lens plugin manifest tools should be an array");
  assert(manifest.tools.length > foundationToolNames.length, "Lens plugin manifest should be broader than foundation-only");
  const toolNames = manifest.tools.map((tool) => tool.name);
  assert.strictEqual(new Set(toolNames).size, toolNames.length, "Lens plugin manifest tool names should be unique");
  assertNamesInclude(toolNames, [
    "Unity_Tools_List",
    "Unity_Tools_Invoke",
    "Unity_Tools_BatchInvoke",
    "Unity_Tools_Describe",
    "Unity_Tools_Menu",
    "Unity_Project_BlockedLanguageScan",
    "Unity_Tests_Run",
    "Unity_UI_CaptureGameView",
    "Unity_PlayMode_InteractionSmoke",
    "Unity_Prefab_AuditSerializedReferences",
    "Unity_Prefab_ExplainOverrides",
    "Unity_Asset_VerifySpriteSlicesAndReferences",
    "Unity_UI_VerifyPrefabLayoutMatrix",
  ], "Lens plugin manifest tools");
  for (const tool of manifest.tools) {
    assert(tool.name && typeof tool.name === "string", "Lens plugin manifest tool should include name");
    assert(tool.description && typeof tool.description === "string", `Lens plugin manifest tool ${tool.name} should include description`);
  }
}

function walkSchema(schema, schemaPath) {
  if (!schema || typeof schema !== "object" || Array.isArray(schema)) return;
  if (schema.type === "array" && !Object.prototype.hasOwnProperty.call(schema, "items")) {
    throw new Error(`${schemaPath} declares type array without items`);
  }

  if (schema.properties && typeof schema.properties === "object") {
    for (const [name, propertySchema] of Object.entries(schema.properties)) {
      walkSchema(propertySchema, `${schemaPath}.properties.${name}`);
    }
  }

  if (schema.items) walkSchema(schema.items, `${schemaPath}.items`);
  for (const keyword of ["oneOf", "anyOf", "allOf", "prefixItems"]) {
    if (!Array.isArray(schema[keyword])) continue;
    schema[keyword].forEach((childSchema, index) => walkSchema(childSchema, `${schemaPath}.${keyword}[${index}]`));
  }
}

async function runDynamicPacksScenario() {
  const context = new ScenarioContext();
  let client = null;
  try {
    await context.startBridge();
    client = new McpHostClient(context.projectRoot, context.statusDir, { toolSurfaceMode: "dynamic_packs" });
    await client.initialize();

    const foundationList = await client.listTools();
    const foundationNames = foundationList.tools.map((tool) => tool.name);
    assert(foundationNames.includes("Unity_SetToolPacks"), "foundation tools/list should expose Unity_SetToolPacks");
    assert(foundationNames.includes("Unity_Tools_Menu"), "foundation tools/list should expose Unity_Tools_Menu");
    assert(foundationNames.includes("Unity_Tools_Describe"), "foundation tools/list should expose Unity_Tools_Describe");
    assert(foundationNames.includes("Unity_Tools_List"), "foundation tools/list should expose Unity_Tools_List");
    assert(foundationNames.includes("Unity_Tools_Invoke"), "foundation tools/list should expose Unity_Tools_Invoke");
    assert(foundationNames.includes("Unity_Tools_BatchInvoke"), "foundation tools/list should expose Unity_Tools_BatchInvoke");
    assert(foundationNames.includes("Unity_Tools_ActivateAndVerify"), "foundation tools/list should expose Unity_Tools_ActivateAndVerify");
    assertNamesInclude(foundationNames, requiredBootstrapWorkflowTools, "foundation bootstrap workflow tools/list");
    assertReadOnlyHint(foundationList.tools, "Unity_Editor_HealthCheckFast", true);
    assertReadOnlyHint(foundationList.tools, "Unity_Tools_List", true);
    assertToolSchemaProperties(foundationList.tools, "Unity_Tools_List", ["groupBy", "maxToolsPerGroup"], []);
    assertReadOnlyHint(foundationList.tools, "Unity_Tools_Invoke", false);
    assertToolSchemaProperties(foundationList.tools, "Unity_Tools_Invoke", ["toolName", "arguments", "timeoutMs"], ["toolName"]);
    assertReadOnlyHint(foundationList.tools, "Unity_Tools_BatchInvoke", false);
    assertBatchInvokeSchema(foundationList.tools);
    assertReadOnlyHint(foundationList.tools, "Unity_PlayMode_StepVerifier", false);
    assertReadOnlyHint(foundationList.tools, "Unity_Editor_RecoverFromHang", false);
    assertReadOnlyHint(foundationList.tools, "Unity_Workflow_RunGpuSimulationProbe", false);
    assertReadOnlyHint(foundationList.tools, "Unity_Workflow_VerifyRuntimePackSelection", false);
    assertReadOnlyHint(foundationList.tools, "Unity_Workflow_SelectPackThroughMainMenu", false);
    for (const assetToolName of requiredAssetTools) {
      assert(!foundationNames.includes(assetToolName), `foundation tools/list should not expose ${assetToolName}`);
    }

    const foundationToolsListResult = await client.callTool("Unity_Tools_List", {});
    assertToolsListFacadePayload(foundationToolsListResult, ["Unity_Tools_List", "Unity_Tools_Invoke", "Unity_Tools_BatchInvoke"]);

    const notificationCount = client.notificationCount("notifications/tools/list_changed");
    const setResult = await client.callTool("Unity_SetToolPacks", { Packs: ["assets"] });
    assert.strictEqual(setResult.structuredContent.success, true, "Unity_SetToolPacks should succeed");
    assert.deepStrictEqual(setResult.structuredContent.data.activeToolPacks, ["foundation", "assets"]);
    await client.waitForNotification("notifications/tools/list_changed", notificationCount);

    const assetsList = await client.listTools();
    const assetNames = assetsList.tools.map((tool) => tool.name);
    assertNamesInclude(assetNames, requiredAssetTools, "foundation+assets tools/list");
    assert(assetNames.includes("Unity_Tools_List"), "foundation+assets tools/list should keep Unity_Tools_List");
    assert(assetNames.includes("Unity_Tools_Invoke"), "foundation+assets tools/list should keep Unity_Tools_Invoke");
    assert(assetNames.includes("Unity_Tools_BatchInvoke"), "foundation+assets tools/list should keep Unity_Tools_BatchInvoke");
    assertReadOnlyHint(assetsList.tools, "Unity_Prefab_AuditSerializedReferences", true);
    assertSpriteSliceReferenceVerifierSchema(assetsList.tools);
    assertPrefabExplainOverridesSchema(assetsList.tools);
    assertToolSchemaProperties(assetsList.tools, "Unity_Prefab_AuditSerializedReferences", [
      "prefabPath",
      "prefabPaths",
      "under",
      "nameFilter",
      "maxPrefabs",
      "maxFindings",
      "referenceNullPolicy",
      "includeNestedPrefabInstances",
      "includeRuntimeLoadPatterns",
    ], []);
    assertPrefabLayoutMatrixSchema(assetsList.tools);
    assertArraySchemasHaveItems(assetsList.tools);

    const verifyResult = await client.callTool("Unity_Tools_ActivateAndVerify", {
      Packs: ["assets"],
      ExpectedTools: ["Unity.Asset.VerifySpriteArrayBinding"],
    });
    assert.strictEqual(verifyResult.structuredContent.success, true, "Unity_Tools_ActivateAndVerify should succeed");
    assert.deepStrictEqual(verifyResult.structuredContent.data.missingFromClient, []);
    assert.strictEqual(verifyResult.structuredContent.data.clientSurface.serverSurfaceVerified, true);

    const uiNotificationCount = client.notificationCount("notifications/tools/list_changed");
    const setUiResult = await client.callTool("Unity_SetToolPacks", { Packs: ["ui"] });
    assert.strictEqual(setUiResult.structuredContent.success, true, "Unity_SetToolPacks ui should succeed");
    assert.deepStrictEqual(setUiResult.structuredContent.data.activeToolPacks, ["foundation", "ui"]);
    await client.waitForNotification("notifications/tools/list_changed", uiNotificationCount);

    const uiList = await client.listTools();
    const uiNames = uiList.tools.map((tool) => tool.name);
    assert(uiNames.includes("Unity_UI_VerifyPrefabLayoutMatrix"), "foundation+ui tools/list should expose Unity_UI_VerifyPrefabLayoutMatrix");
    assertPrefabLayoutMatrixSchema(uiList.tools);

    const runtimeNotificationCount = client.notificationCount("notifications/tools/list_changed");
    const setRuntimeResult = await client.callTool("Unity_SetToolPacks", { Packs: ["runtime"] });
    assert.strictEqual(setRuntimeResult.structuredContent.success, true, "Unity_SetToolPacks runtime should succeed");
    assert.deepStrictEqual(setRuntimeResult.structuredContent.data.activeToolPacks, ["foundation", "runtime"]);
    await client.waitForNotification("notifications/tools/list_changed", runtimeNotificationCount);

    const runtimeList = await client.listTools();
    const runtimeNames = runtimeList.tools.map((tool) => tool.name);
    assert(runtimeNames.includes("Unity_PlayMode_InteractionSmoke"), "foundation+runtime tools/list should expose Unity_PlayMode_InteractionSmoke");
    assertInteractionSmokeSchema(runtimeList.tools);

    const debugNotificationCount = client.notificationCount("notifications/tools/list_changed");
    const setDebugResult = await client.callTool("Unity_SetToolPacks", { Packs: ["debug"] });
    assert.strictEqual(setDebugResult.structuredContent.success, true, "Unity_SetToolPacks debug should succeed");
    assert.deepStrictEqual(setDebugResult.structuredContent.data.activeToolPacks, ["foundation", "debug"]);
    await client.waitForNotification("notifications/tools/list_changed", debugNotificationCount);

    const debugList = await client.listTools();
    const debugNames = debugList.tools.map((tool) => tool.name);
    assert(debugNames.includes("Unity_PlayMode_InteractionSmoke"), "foundation+debug tools/list should expose Unity_PlayMode_InteractionSmoke");
    assertInteractionSmokeSchema(debugList.tools);
  } finally {
    if (client) await client.dispose().catch(() => {});
    await context.dispose();
  }
}

async function runStaticAllScenario() {
  const context = new ScenarioContext();
  let client = null;
  try {
    await context.startBridge();
    client = new McpHostClient(context.projectRoot, context.statusDir, { toolSurfaceMode: "static_all" });
    await client.initialize();

    const staticList = await client.listTools();
    const staticNames = staticList.tools.map((tool) => tool.name);
    assertNamesInclude(staticNames, [
      "Unity_Tools_List",
      "Unity_Tools_Invoke",
      "Unity_Tools_BatchInvoke",
      "Unity_Project_PackageCompatibility",
      "Unity_Project_BlockedLanguageScan",
      "Unity_Tests_Run",
      "Unity_Editor_SetPlayMode",
      "Unity_PlayMode_InteractionSmoke",
      ...requiredBootstrapWorkflowTools,
      "Unity_Asset_Search",
      "Unity_Asset_VerifySpriteSlicesAndReferences",
      "Unity_Prefab_AuditSerializedReferences",
      "Unity_Prefab_ExplainOverrides",
      "Unity_UI_VerifyPrefabLayoutMatrix",
      "Unity_GameObject_Inspect",
      "Unity_Camera_FitComposition",
      "Unity_Scene_PreviewGridBoardLayout",
      "Unity_Scene_ApplyGridBoardLayout",
      "Unity_Scene_PreviewBulkMutation",
      "Unity_Scene_ApplyBulkMutation",
      "Unity_UI_VerifyScreenLayout",
      "Unity_UI_CaptureGameView",
      "Unity_GetLensUsageReport",
    ], "static_all startup tools/list");
    assertArraySchemasHaveItems(staticList.tools);
    assertReadOnlyHint(staticList.tools, "Unity_Tools_List", true);
    assertToolSchemaProperties(staticList.tools, "Unity_Tools_List", ["groupBy", "maxToolsPerGroup"], []);
    assertReadOnlyHint(staticList.tools, "Unity_Tools_Invoke", false);
    assertToolSchemaProperties(staticList.tools, "Unity_Tools_Invoke", ["toolName", "arguments", "timeoutMs"], ["toolName"]);
    assertReadOnlyHint(staticList.tools, "Unity_Tools_BatchInvoke", false);
    assertBatchInvokeSchema(staticList.tools);
    assertReadOnlyHint(staticList.tools, "Unity_PlayMode_StepVerifier", false);
    assertReadOnlyHint(staticList.tools, "Unity_Editor_RecoverFromHang", false);
    assertReadOnlyHint(staticList.tools, "Unity_Workflow_RunGpuSimulationProbe", false);
    assertReadOnlyHint(staticList.tools, "Unity_Workflow_VerifyRuntimePackSelection", false);
    assertReadOnlyHint(staticList.tools, "Unity_Workflow_SelectPackThroughMainMenu", false);
    assertInteractionSmokeSchema(staticList.tools);
    assertReadOnlyHint(staticList.tools, "Unity_Tests_Run", false);
    assertReadOnlyHint(staticList.tools, "Unity_Project_BlockedLanguageScan", true);
    assertReadOnlyHint(staticList.tools, "Unity_Camera_FitComposition", false);
    assertReadOnlyHint(staticList.tools, "Unity_Scene_PreviewGridBoardLayout", true);
    assertReadOnlyHint(staticList.tools, "Unity_Scene_ApplyGridBoardLayout", false);
    assertReadOnlyHint(staticList.tools, "Unity_Scene_PreviewBulkMutation", true);
    assertReadOnlyHint(staticList.tools, "Unity_Scene_ApplyBulkMutation", false);
    assertReadOnlyHint(staticList.tools, "Unity_UI_CaptureGameView", false);
    assertReadOnlyHint(staticList.tools, "Unity_Prefab_AuditSerializedReferences", true);
    assertSpriteSliceReferenceVerifierSchema(staticList.tools);
    assertPrefabExplainOverridesSchema(staticList.tools);
    assertPrefabLayoutMatrixSchema(staticList.tools);
    assertToolSchemaProperties(staticList.tools, "Unity_Prefab_AuditSerializedReferences", [
      "prefabPath",
      "prefabPaths",
      "under",
      "nameFilter",
      "maxPrefabs",
      "maxFindings",
      "referenceNullPolicy",
      "includeNestedPrefabInstances",
      "includeRuntimeLoadPatterns",
    ], []);
    assertToolSchemaProperties(staticList.tools, "Unity_UI_CaptureGameView", [
      "OutputPath",
      "Width",
      "Height",
      "RestoreOriginalResolution",
      "TemporaryActivations",
      "VerifyImageDimensions",
      "WaitForFileTimeoutMs",
    ], ["OutputPath"]);

    const projectResult = await client.callTool("Unity_Project_PackageCompatibility", {});
    assert.strictEqual(projectResult.structuredContent.success, true, "pack-gated project tool should succeed in static_all without pack switching");

    const groupedListResult = await client.callTool("Unity_Tools_List", { groupBy: "pack", maxToolsPerGroup: 100 });
    assertToolsListFacadePayload(groupedListResult, [
      "Unity_Tools_List",
      "Unity_Tools_Invoke",
      "Unity_Tools_BatchInvoke",
      "Unity_Project_BlockedLanguageScan",
      "Unity_Tests_Run",
      "Unity_PlayMode_InteractionSmoke",
      "Unity_Prefab_AuditSerializedReferences",
      "Unity_Prefab_ExplainOverrides",
      "Unity_Asset_VerifySpriteSlicesAndReferences",
      "Unity_UI_VerifyPrefabLayoutMatrix",
    ]);
    assert.strictEqual(groupedListResult.structuredContent.data.groupBy, "pack");
    assert(groupedListResult.structuredContent.data.groups.some((group) => group.id === "foundation"), "Unity_Tools_List should include a foundation group");

    const flatListResult = await client.callTool("Unity_Tools_List", { groupBy: "flat" });
    assertToolsListFacadePayload(flatListResult, ["Unity_Project_BlockedLanguageScan", "Unity_Tests_Run", "Unity_PlayMode_InteractionSmoke", "Unity_Prefab_AuditSerializedReferences", "Unity_Prefab_ExplainOverrides", "Unity_Asset_VerifySpriteSlicesAndReferences", "Unity_UI_VerifyPrefabLayoutMatrix"]);
    assert.strictEqual(flatListResult.structuredContent.data.groupBy, "flat");
    const flatNames = flatListResult.structuredContent.data.tools.map((row) => row.name);
    assert.deepStrictEqual([...flatNames].sort((a, b) => a.localeCompare(b)), flatNames, "Unity_Tools_List flat tools should be sorted");

    const facadePackageResult = await client.callTool("Unity_Tools_Invoke", {
      toolName: "Unity.Project.PackageCompatibility",
      arguments: {},
      timeoutMs: 5000,
    });
    assert.strictEqual(facadePackageResult.isError, false, "Unity_Tools_Invoke should relay package compatibility success");
    assert.strictEqual(facadePackageResult.structuredContent.success, true, "Unity_Tools_Invoke package compatibility success flag");
    assert.strictEqual(facadePackageResult.structuredContent.invokedThroughFacade, true, "Unity_Tools_Invoke should mark facade metadata");
    assert.strictEqual(facadePackageResult.structuredContent.canonicalToolName, "Unity_Project_PackageCompatibility");
    assert.strictEqual(facadePackageResult.structuredContent.timeoutMs, 5000);
    assert.strictEqual(facadePackageResult.structuredContent.result.success, true, "Unity_Tools_Invoke should preserve target structuredContent");

    const facadeBlockedLanguageResult = await client.callTool("Unity_Tools_Invoke", {
      toolName: "Unity_Project_BlockedLanguageScan",
      arguments: {},
    });
    assert.strictEqual(facadeBlockedLanguageResult.isError, false, "Unity_Tools_Invoke should accept underscore target names");
    assert.strictEqual(facadeBlockedLanguageResult.structuredContent.canonicalToolName, "Unity_Project_BlockedLanguageScan");

    const facadeTestsRunResult = await client.callTool("Unity_Tools_Invoke", {
      toolName: "Unity.Tests.Run",
      arguments: {},
    });
    assert.strictEqual(facadeTestsRunResult.isError, false, "Unity_Tools_Invoke should call Unity.Tests.Run by dot name");
    assert.strictEqual(facadeTestsRunResult.structuredContent.canonicalToolName, "Unity_Tests_Run");

    const unknownFacadeResult = await client.callTool("Unity_Tools_Invoke", {
      toolName: "Unity.Project.PackageCompatibilty",
    });
    assert.strictEqual(unknownFacadeResult.isError, true, "Unity_Tools_Invoke should fail unknown target names");
    assert.strictEqual(unknownFacadeResult.structuredContent.code, "UNITY_MCP_TOOL_NOT_FOUND");
    assert(
      unknownFacadeResult.structuredContent.data.suggestions.includes("Unity_Project_PackageCompatibility"),
      "Unity_Tools_Invoke unknown target should suggest similar known tools",
    );

    const recursiveFacadeResult = await client.callTool("Unity_Tools_Invoke", {
      toolName: "Unity.Tools.Invoke",
    });
    assert.strictEqual(recursiveFacadeResult.isError, true, "Unity_Tools_Invoke should block self-recursion");
    assert.strictEqual(recursiveFacadeResult.structuredContent.code, "UNITY_MCP_FACADE_RECURSION_BLOCKED");

    const batchResult = await client.callTool("Unity_Tools_BatchInvoke", {
      calls: [
        { toolName: "Unity.Project.PackageCompatibility", arguments: {}, timeoutMs: 5000 },
        { toolName: "Unity_Project_BlockedLanguageScan", arguments: {} },
        { toolName: "Unity.Tests.Run", arguments: {} },
        { toolName: "Unity.Project.PackageCompatibilty" },
        { toolName: "Unity.Tools.BatchInvoke" },
        { toolName: "Unity_Tools_Invoke" },
      ],
    });
    assert.strictEqual(batchResult.isError, false, "Unity_Tools_BatchInvoke mixed results should not make the batch MCP call an error");
    assert.strictEqual(batchResult.structuredContent.success, false, "Unity_Tools_BatchInvoke mixed results should report success=false");
    assert.strictEqual(batchResult.structuredContent.data.executedCount, 6);
    assert.strictEqual(batchResult.structuredContent.data.failedCount, 3);
    assert.strictEqual(batchResult.structuredContent.data.stoppedEarly, false);
    assert.strictEqual(batchResult.structuredContent.data.results.length, 6);
    assert.deepStrictEqual(
      batchResult.structuredContent.data.results.slice(0, 3).map((row) => row.canonicalToolName),
      ["Unity_Project_PackageCompatibility", "Unity_Project_BlockedLanguageScan", "Unity_Tests_Run"],
      "Unity_Tools_BatchInvoke should accept dot and underscore target names",
    );
    assert.strictEqual(batchResult.structuredContent.data.results[0].success, true);
    assert.strictEqual(batchResult.structuredContent.data.results[0].structuredContent.success, true);
    assert.strictEqual(batchResult.structuredContent.data.results[3].code, "UNITY_MCP_TOOL_NOT_FOUND");
    assert.strictEqual(batchResult.structuredContent.data.results[4].code, "UNITY_MCP_FACADE_RECURSION_BLOCKED");
    assert.strictEqual(batchResult.structuredContent.data.results[5].code, "UNITY_MCP_FACADE_RECURSION_BLOCKED");

    const failFastBatchResult = await client.callTool("Unity_Tools_BatchInvoke", {
      failFast: true,
      calls: [
        { toolName: "Unity.Project.PackageCompatibilty" },
        { toolName: "Unity.Project.PackageCompatibility", arguments: {} },
      ],
    });
    assert.strictEqual(failFastBatchResult.isError, false, "Unity_Tools_BatchInvoke failFast target failure should not make the batch MCP call an error");
    assert.strictEqual(failFastBatchResult.structuredContent.success, false);
    assert.strictEqual(failFastBatchResult.structuredContent.data.executedCount, 1);
    assert.strictEqual(failFastBatchResult.structuredContent.data.failedCount, 1);
    assert.strictEqual(failFastBatchResult.structuredContent.data.stoppedEarly, true);

    const malformedBatchResult = await client.callTool("Unity_Tools_BatchInvoke", {
      calls: [],
    });
    assert.strictEqual(malformedBatchResult.isError, true, "Unity_Tools_BatchInvoke should reject empty calls");
    assert.strictEqual(malformedBatchResult.structuredContent.code, "UNITY_MCP_BATCH_CALLS_REQUIRED");

    const menuResultPayload = await client.callTool("Unity_Tools_Menu", {});
    assert.strictEqual(menuResultPayload.structuredContent.success, true, "Unity_Tools_Menu should succeed");
    assert.strictEqual(menuResultPayload.structuredContent.data.toolSurfaceMode, "static_all");
    assert.strictEqual(menuResultPayload.structuredContent.data.clientSurfaceFallback.invokeTool, "Unity.Tools.Invoke");
    assert(
      menuResultPayload.structuredContent.data.workflowRecommendations.some((line) => line.includes("Unity.Tools.Invoke")),
      "Unity_Tools_Menu should recommend the invoke facade when direct client tools are stale",
    );
    const menuPackIds = menuResultPayload.structuredContent.data.packs.map((pack) => pack.packId);
    assertNamesInclude(menuPackIds, ["project", "runtime", "assets", "scene", "ui", "debug"], "Unity_Tools_Menu packs");

    const describeResultPayload = await client.callTool("Unity_Tools_Describe", { includeExamples: true });
    assert.strictEqual(describeResultPayload.structuredContent.success, true, "Unity_Tools_Describe should succeed");
    assert.strictEqual(describeResultPayload.structuredContent.data.clientSurfaceFallback.batchInvokeTool, "Unity.Tools.BatchInvoke");

    const notificationCount = client.notificationCount("notifications/tools/list_changed");
    const setToolPacksCountBeforeNoop = context.commandCounts.set_tool_packs || 0;
    const setResult = await client.callTool("Unity_SetToolPacks", { Packs: ["assets"] });
    assert.strictEqual(setResult.structuredContent.success, true, "static_all Unity_SetToolPacks compatibility no-op should succeed");
    assert.strictEqual(setResult.structuredContent.data.toolSurfaceMode, "static_all");
    assert.deepStrictEqual(setResult.structuredContent.data.activeToolPacks, ["foundation", "full"]);
    assert.strictEqual(setResult.structuredContent.data.toolsListChangedNotificationSent, false);
    assert.strictEqual(setResult.structuredContent.data.bridgeTouched, false);
    assert.strictEqual(context.commandCounts.set_tool_packs || 0, setToolPacksCountBeforeNoop, "static_all Unity_SetToolPacks no-op should not call bridge set_tool_packs");
    assert.strictEqual(client.notificationCount("notifications/tools/list_changed"), notificationCount);

    const afterNoopList = await client.listTools();
    const afterNoopNames = afterNoopList.tools.map((tool) => tool.name);
    assertNamesInclude(afterNoopNames, [
      "Unity_Tools_List",
      "Unity_Project_PackageCompatibility",
      "Unity_Project_BlockedLanguageScan",
      "Unity_Tests_Run",
      "Unity_Editor_SetPlayMode",
      "Unity_PlayMode_InteractionSmoke",
      ...requiredBootstrapWorkflowTools,
      "Unity_Asset_Search",
      "Unity_Asset_VerifySpriteSlicesAndReferences",
      "Unity_Prefab_AuditSerializedReferences",
      "Unity_Prefab_ExplainOverrides",
      "Unity_UI_VerifyPrefabLayoutMatrix",
      "Unity_GameObject_Inspect",
      "Unity_Camera_FitComposition",
      "Unity_Scene_PreviewGridBoardLayout",
      "Unity_Scene_ApplyGridBoardLayout",
      "Unity_Scene_PreviewBulkMutation",
      "Unity_Scene_ApplyBulkMutation",
      "Unity_UI_VerifyScreenLayout",
      "Unity_UI_CaptureGameView",
      "Unity_GetLensUsageReport",
    ], "static_all tools/list after Unity_SetToolPacks no-op");
  } finally {
    if (client) await client.dispose().catch(() => {});
    await context.dispose();
  }
}

async function runDefaultStaticAllScenario() {
  const context = new ScenarioContext();
  let client = null;
  try {
    await context.startBridge();
    client = new McpHostClient(context.projectRoot, context.statusDir);
    await client.initialize();

    const list = await client.listTools();
    const names = list.tools.map((tool) => tool.name);
    assertNamesInclude(names, [
      "Unity_Tools_List",
      "Unity_Tools_Invoke",
      "Unity_Tools_BatchInvoke",
      "Unity_Project_PackageCompatibility",
      "Unity_Project_BlockedLanguageScan",
      "Unity_Tests_Run",
      "Unity_PlayMode_InteractionSmoke",
      "Unity_Asset_Search",
      "Unity_Asset_VerifySpriteSlicesAndReferences",
      "Unity_Prefab_AuditSerializedReferences",
      "Unity_Prefab_ExplainOverrides",
      "Unity_UI_VerifyPrefabLayoutMatrix",
      "Unity_Camera_FitComposition",
      "Unity_Scene_PreviewGridBoardLayout",
      "Unity_Scene_ApplyGridBoardLayout",
      "Unity_Scene_PreviewBulkMutation",
      "Unity_Scene_ApplyBulkMutation",
      "Unity_UI_VerifyScreenLayout",
      "Unity_UI_CaptureGameView",
    ], "default startup tools/list should be static_all");
    assertPrefabLayoutMatrixSchema(list.tools);
    assertPrefabExplainOverridesSchema(list.tools);
    assertSpriteSliceReferenceVerifierSchema(list.tools);
    assertInteractionSmokeSchema(list.tools);
    assertToolSchemaProperties(list.tools, "Unity_UI_CaptureGameView", [
      "OutputPath",
      "Width",
      "Height",
      "RestoreOriginalResolution",
      "TemporaryActivations",
      "VerifyImageDimensions",
      "WaitForFileTimeoutMs",
    ], ["OutputPath"]);
    assertReadOnlyHint(list.tools, "Unity_Prefab_AuditSerializedReferences", true);
    assertPrefabLayoutMatrixSchema(list.tools);
    assertToolSchemaProperties(list.tools, "Unity_Prefab_AuditSerializedReferences", [
      "prefabPath",
      "prefabPaths",
      "under",
      "nameFilter",
      "maxPrefabs",
      "maxFindings",
      "referenceNullPolicy",
      "includeNestedPrefabInstances",
      "includeRuntimeLoadPatterns",
    ], []);

    const menuResultPayload = await client.callTool("Unity_Tools_Menu", {});
    assert.strictEqual(menuResultPayload.structuredContent.success, true, "Unity_Tools_Menu should succeed in default mode");
    assert.strictEqual(menuResultPayload.structuredContent.data.toolSurfaceMode, "static_all");
  } finally {
    if (client) await client.dispose().catch(() => {});
    await context.dispose();
  }
}

async function main() {
  assert(fs.existsSync(hostPath), `Host path does not exist: ${hostPath}`);
  const pluginConfig = JSON.parse(fs.readFileSync(path.join(repoRoot, ".agents", "plugins", "lens-dev-plugin", ".mcp.json"), "utf8"));
  assert.strictEqual(
    pluginConfig.mcpServers.unity_mcp_lens.env.UNITY_MCP_LENS_TOOL_SURFACE_MODE,
    "static_all",
    "Codex plugin config should default Lens to static_all",
  );
  assertLensDevPluginManifest(pluginConfig);

  await runDefaultStaticAllScenario();
  await runDynamicPacksScenario();
  await runStaticAllScenario();

  console.log("MCP dynamic/static tool exposure tests passed.");
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
