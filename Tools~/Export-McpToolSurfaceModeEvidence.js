const childProcess = require("child_process");
const fs = require("fs");
const net = require("net");
const os = require("os");
const path = require("path");

const repoRoot = path.resolve(__dirname, "..");
const defaultHostPath = path.join(repoRoot, "UnityMcpLensApp~", "src", "UnityMcpLens", "bin", "Debug", "net8.0", "UnityMcpLens.exe");
const defaultOutputDir = path.join(repoRoot, "artifacts");
const packOrder = ["foundation", "console", "project", "scripting", "scene", "ui", "runtime", "assets", "debug"];

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
    const type = command.type;
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
    this.toolCatalog = toolCatalog;
    this.bridge = null;
    fs.mkdirSync(this.statusDir, { recursive: true });
    fs.mkdirSync(this.projectRoot, { recursive: true });
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

function resultFor(type, _params, context) {
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
      workflowRecommendations: [
        fullSurface
          ? "Call real native tools directly; no Unity.SetToolPacks step is required in static_all mode."
          : "Use Unity.SetToolPacks before calling pack-gated tools.",
      ],
    },
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
    ["runtime", "Unity_Editor_SetPlayMode", false],
    ["assets", "Unity_Asset_Search", true],
    ["scene", "Unity_GameObject_Inspect", true],
    ["ui", "Unity_UI_VerifyScreenLayout", true],
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
    const setAssetsBefore = client.notificationCount("notifications/tools/list_changed");
    const setAssets = await client.callTool("Unity_SetToolPacks", { Packs: ["assets"] });
    const sawListChanged = await client.waitForNotification("notifications/tools/list_changed", setAssetsBefore, 1500);
    const afterSetList = await client.listTools();
    const menu = await client.callTool("Unity_Tools_Menu", {});

    return {
      toolSurfaceMode,
      initialize: summarizeEnvelope(initialize),
      startupToolsList: summarizeToolsList(startupList),
      setAssets: summarizeToolCall(setAssets),
      sawListChanged,
      afterSetToolsList: summarizeToolsList(afterSetList),
      menu: summarizeToolCall(menu),
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
    representativePresence: representativePresence(names),
    toolNames: names,
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

function representativePresence(names) {
  const wanted = [
    "Unity_Project_PackageCompatibility",
    "Unity_Editor_SetPlayMode",
    "Unity_Asset_Search",
    "Unity_GameObject_Inspect",
    "Unity_UI_VerifyScreenLayout",
    "Unity_GetLensUsageReport",
  ];
  return Object.fromEntries(wanted.map((name) => [name, names.includes(name)]));
}

function buildConclusion(dynamicScenario, staticScenario) {
  const dynamicStartup = dynamicScenario.startupToolsList;
  const staticStartup = staticScenario.startupToolsList;
  const staticHasRepresentatives = Object.values(staticStartup.representativePresence).every(Boolean);
  return {
    protocolLevelStaticAllHandsClientFullToolList:
      staticHasRepresentatives && staticStartup.toolCount > dynamicStartup.toolCount,
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
    canDirectlyObserveCodexPromptInjection: false,
    promptInjectionLimit:
      "This artifact proves what the MCP host sends to a client over tools/list. It cannot prove whether Codex injects every received tool schema into model context without a Codex-side prompt/tool-snapshot trace.",
  };
}

function writeMarkdownReport(report, markdownPath) {
  const c = report.conclusion;
  const dynamic = report.scenarios.dynamic_packs.startupToolsList;
  const stat = report.scenarios.static_all.startupToolsList;
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
    `- Direct Codex prompt/context injection observed: **no**. ${c.promptInjectionLimit}`,
    `- Static \`Unity.SetToolPacks([\"assets\"])\` preserved full surface without list-changed: **${c.setToolPacksNoopKeptStaticToolCount ? "yes" : "no"}**.`,
    "",
    "## Startup tools/list",
    "",
    "| Mode | Tool count | Response bytes | Approx response tokens | Descriptor bytes | Schema approx tokens |",
    "| --- | ---: | ---: | ---: | ---: | ---: |",
    `| dynamic_packs | ${dynamic.toolCount} | ${dynamic.responseBodyBytes} | ${dynamic.responseBodyApproxTokens} | ${dynamic.descriptorBytes} | ${dynamic.schemaApproxTokens} |`,
    `| static_all | ${stat.toolCount} | ${stat.responseBodyBytes} | ${stat.responseBodyApproxTokens} | ${stat.descriptorBytes} | ${stat.schemaApproxTokens} |`,
    "",
    "## Representative static-all tools present at startup",
    "",
    ...Object.entries(stat.representativePresence).map(([name, present]) => `- \`${name}\`: ${present ? "present" : "missing"}`),
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
  const fakeFullToolCount = Math.max(24, Number(args.fakeFullToolCount || 80));

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
  const report = {
    schemaVersion: "tool-surface-mode-evidence.v1",
    capturedAtUtc: new Date().toISOString(),
    hostPath,
    fakeFullToolCount,
    scenarios: {
      dynamic_packs: dynamicScenario,
      static_all: staticScenario,
    },
    conclusion: buildConclusion(dynamicScenario, staticScenario),
  };

  fs.writeFileSync(jsonPath, JSON.stringify(report, null, 2), "utf8");
  writeMarkdownReport(report, markdownPath);
  console.log(JSON.stringify({
    success: true,
    jsonPath,
    markdownPath,
    conclusion: report.conclusion,
  }, null, 2));
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
