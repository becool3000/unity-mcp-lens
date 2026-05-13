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
];

const foundationToolNames = [
  "Unity_GetLensHealth",
  "Unity_ListToolPacks",
  "Unity_Bridge_ListConnections",
  "Unity_SetToolPacks",
  "Unity_ReadDetailRef",
  "Unity_Tools_Menu",
  "Unity_Tools_Describe",
  "Unity_Tools_ActivateAndVerify",
  "Unity_ReadConsole",
  "Unity_ListResources",
  "Unity_ReadResource",
  "Unity_FindInFile",
  "Unity_ManageEditor",
  "Unity_RunCommand",
];

const projectToolNames = [
  "Unity_Project_GetInfo",
  "Unity_Project_PackageCompatibility",
];

const runtimeToolNames = [
  "Unity_Editor_SetPlayMode",
  "Unity_PlayMode_PointerInputSmoke",
  "Unity_Runtime_QueryObjects",
];

const sceneToolNames = [
  "Unity_GameObject_Inspect",
  "Unity_GameObject_ApplyChanges",
];

const uiToolNames = [
  "Unity_UI_VerifyScreenLayout",
  "Unity_UI_ApplyEnsureHierarchy",
];

const debugToolNames = [
  "Unity_GetLensUsageReport",
];

const assetToolNames = [
  "Unity_Asset_Search",
  "Unity_Asset_ConfigureSpriteImport",
  "Unity_Asset_SetSerializedProperties",
  "Unity_Asset_ImportSpriteSheetAndBind",
  "Unity_Asset_PreviewImportSpriteSheetAndBind",
  "Unity_Asset_ApplyImportSpriteSheetAndBind",
  "Unity_Asset_VerifySpriteArrayBinding",
  "Unity_ManageAsset",
  "Unity_Prefab_SetSerializedProperties",
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
    this.child = childProcess.spawn(hostPath, [], {
      cwd: projectRoot,
      env: {
        ...process.env,
        UNITY_MCP_STATUS_DIR: statusDir,
        UNITY_MCP_PROJECT_PATH: projectRoot,
        UNITY_MCP_LENS_TOOL_SURFACE_MODE: options.toolSurfaceMode || process.env.UNITY_MCP_LENS_TOOL_SURFACE_MODE || "dynamic_packs",
      },
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

function resultFor(type, _params, context) {
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
  const tools = foundationToolNames.map((name) => toolDescriptor(name, ["foundation"], isReadOnlyFoundationTool(name), withSchemas));
  const active = new Set((activeToolPacks || []).map((pack) => String(pack).toLowerCase()));
  const hasFull = active.has("full");
  if (hasFull || active.has("project")) {
    tools.push(...projectToolNames.map((name) => toolDescriptor(name, ["project"], isReadOnlyTool(name), withSchemas)));
  }
  if (hasFull || active.has("runtime")) {
    tools.push(...runtimeToolNames.map((name) => toolDescriptor(name, ["runtime"], isReadOnlyTool(name), withSchemas)));
  }
  if (hasFull || active.has("scene")) {
    tools.push(...sceneToolNames.map((name) => toolDescriptor(name, ["scene"], isReadOnlyTool(name), withSchemas)));
  }
  if (hasFull || active.has("ui")) {
    tools.push(...uiToolNames.map((name) => toolDescriptor(name, ["ui"], isReadOnlyTool(name), withSchemas)));
  }
  if (hasFull || active.has("debug")) {
    tools.push(...debugToolNames.map((name) => toolDescriptor(name, ["debug"], isReadOnlyTool(name), withSchemas)));
  }
  if (hasFull || active.has("assets")) {
    tools.push(...assetToolNames.map((name) => toolDescriptor(name, ["assets"], isReadOnlyTool(name), withSchemas)));
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
      workflowRecommendations: [
        fullSurface
          ? "Call real native tools directly; no Unity.SetToolPacks step is required in static_all mode."
          : "Use Unity.SetToolPacks before calling pack-gated tools.",
      ],
    },
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

function isReadOnlyTool(name) {
  return name === "Unity_Project_GetInfo" ||
    name === "Unity_Project_PackageCompatibility" ||
    name === "Unity_GameObject_Inspect" ||
    name === "Unity_UI_VerifyScreenLayout" ||
    name === "Unity_GetLensUsageReport" ||
    name === "Unity_Runtime_QueryObjects" ||
    name === "Unity_Asset_Search" ||
    name === "Unity_Asset_PreviewImportSpriteSheetAndBind" ||
    name === "Unity_Asset_VerifySpriteArrayBinding";
}

function isReadOnlyFoundationTool(name) {
  return name === "Unity_Bridge_ListConnections" ||
    name !== "Unity_SetToolPacks" &&
    name !== "Unity_Tools_ActivateAndVerify" &&
    name !== "Unity_ManageEditor" &&
    name !== "Unity_RunCommand";
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
  if (name === "Unity_Asset_Search") {
    return {
      type: "object",
      properties: {
        query: { type: "string" },
        labels: { type: "array", items: { type: "string" } },
      },
    };
  }

  return { type: "object", properties: {} };
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
    assert(foundationNames.includes("Unity_Tools_ActivateAndVerify"), "foundation tools/list should expose Unity_Tools_ActivateAndVerify");
    for (const assetToolName of requiredAssetTools) {
      assert(!foundationNames.includes(assetToolName), `foundation tools/list should not expose ${assetToolName}`);
    }

    const notificationCount = client.notificationCount("notifications/tools/list_changed");
    const setResult = await client.callTool("Unity_SetToolPacks", { Packs: ["assets"] });
    assert.strictEqual(setResult.structuredContent.success, true, "Unity_SetToolPacks should succeed");
    assert.deepStrictEqual(setResult.structuredContent.data.activeToolPacks, ["foundation", "assets"]);
    await client.waitForNotification("notifications/tools/list_changed", notificationCount);

    const assetsList = await client.listTools();
    const assetNames = assetsList.tools.map((tool) => tool.name);
    assertNamesInclude(assetNames, requiredAssetTools, "foundation+assets tools/list");
    assertArraySchemasHaveItems(assetsList.tools);

    const verifyResult = await client.callTool("Unity_Tools_ActivateAndVerify", {
      Packs: ["assets"],
      ExpectedTools: ["Unity.Asset.VerifySpriteArrayBinding"],
    });
    assert.strictEqual(verifyResult.structuredContent.success, true, "Unity_Tools_ActivateAndVerify should succeed");
    assert.deepStrictEqual(verifyResult.structuredContent.data.missingFromClient, []);
    assert.strictEqual(verifyResult.structuredContent.data.clientSurface.serverSurfaceVerified, true);
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
      "Unity_Project_PackageCompatibility",
      "Unity_Editor_SetPlayMode",
      "Unity_Asset_Search",
      "Unity_GameObject_Inspect",
      "Unity_UI_VerifyScreenLayout",
      "Unity_GetLensUsageReport",
    ], "static_all startup tools/list");
    assertArraySchemasHaveItems(staticList.tools);

    const projectResult = await client.callTool("Unity_Project_PackageCompatibility", {});
    assert.strictEqual(projectResult.structuredContent.success, true, "pack-gated project tool should succeed in static_all without pack switching");

    const menuResultPayload = await client.callTool("Unity_Tools_Menu", {});
    assert.strictEqual(menuResultPayload.structuredContent.success, true, "Unity_Tools_Menu should succeed");
    assert.strictEqual(menuResultPayload.structuredContent.data.toolSurfaceMode, "static_all");
    const menuPackIds = menuResultPayload.structuredContent.data.packs.map((pack) => pack.packId);
    assertNamesInclude(menuPackIds, ["project", "runtime", "assets", "scene", "ui", "debug"], "Unity_Tools_Menu packs");

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
      "Unity_Project_PackageCompatibility",
      "Unity_Editor_SetPlayMode",
      "Unity_Asset_Search",
      "Unity_GameObject_Inspect",
      "Unity_UI_VerifyScreenLayout",
      "Unity_GetLensUsageReport",
    ], "static_all tools/list after Unity_SetToolPacks no-op");
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

  await runDynamicPacksScenario();
  await runStaticAllScenario();

  console.log("MCP dynamic/static tool exposure tests passed.");
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
