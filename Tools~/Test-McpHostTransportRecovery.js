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
    this.stderr = "";
    const env = {
      ...process.env,
      UNITY_MCP_STATUS_DIR: statusDir,
    };
    if (!options.omitProjectEnv) env.UNITY_MCP_PROJECT_PATH = projectRoot;

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
      const pending = this.pending.get(message.id);
      if (!pending) return;
      this.pending.delete(message.id);
      if (message.error) pending.reject(new Error(JSON.stringify(message.error)));
      else pending.resolve(message.result);
    }));
  }

  async initialize() {
    await this.request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "transport-recovery-test", version: "1.0.0" },
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
  constructor(context, options = {}) {
    this.context = context;
    this.options = options;
    this.server = null;
    this.connectionPath = makePipePath();
    this.statusPath = path.join(context.statusDir, `bridge-status-${nextPipeId++}.json`);
    this.commandCounts = context.commandCounts;
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
    writeStatus(this.statusPath, this.connectionPath, this.context.projectRoot, {
      status: "ready",
      heartbeat: new Date(),
      toolCount: 3,
    });
  }

  async stop() {
    if (!this.server) return;
    await new Promise((resolve) => this.server.close(resolve));
    this.server = null;
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
    this.commandCounts[type] = (this.commandCounts[type] || 0) + 1;

    if (this.context.shouldFail(type)) {
      await this.context.replaceBridge();
      socket.destroy();
      await this.stop();
      return;
    }

    this.respond(socket, command.requestId, resultFor(type, command.params));
  }

  respond(socket, requestId, result) {
    socket.write(JSON.stringify({ requestId, status: "success", result }) + "\n");
  }
}

class ScenarioContext {
  constructor(name, failOnceOn = null) {
    this.name = name;
    this.root = fs.mkdtempSync(path.join(os.tmpdir(), `lens-transport-${name}-`));
    this.statusDir = path.join(this.root, "connections");
    this.projectRoot = path.join(this.root, "Project");
    this.commandCounts = {};
    this.failOnceOn = failOnceOn;
    this.failed = false;
    this.bridge = null;
    fs.mkdirSync(this.statusDir, { recursive: true });
    fs.mkdirSync(this.projectRoot, { recursive: true });
    fs.mkdirSync(path.join(this.projectRoot, "Assets"), { recursive: true });
    fs.mkdirSync(path.join(this.projectRoot, "ProjectSettings"), { recursive: true });
    fs.mkdirSync(path.join(this.projectRoot, "Packages"), { recursive: true });
    fs.writeFileSync(path.join(this.projectRoot, "Packages", "manifest.json"), "{}");
  }

  shouldFail(type) {
    if (!this.failOnceOn || this.failed || type !== this.failOnceOn) return false;
    this.failed = true;
    return true;
  }

  async startBridge(options = {}) {
    this.bridge = new FakeBridge(this, options);
    await this.bridge.start();
    return this.bridge;
  }

  async replaceBridge() {
    await this.startBridge();
  }

  writeStaleStatus() {
    const stalePipe = makePipePath();
    const staleStatus = path.join(this.statusDir, "bridge-status-stale.json");
    writeStatus(staleStatus, stalePipe, this.projectRoot, {
      status: "ready",
      heartbeat: new Date(Date.now() - 120000),
      healthHeartbeat: new Date(),
      toolCount: 999,
    });
    return staleStatus;
  }

  writeForeignStatus(options = {}) {
    const foreignRoot = options.projectRoot || path.join(this.root, `ForeignProject-${nextPipeId++}`);
    fs.mkdirSync(foreignRoot, { recursive: true });
    const foreignPipe = makePipePath();
    const foreignStatus = path.join(this.statusDir, `bridge-status-foreign-${nextPipeId++}.json`);
    writeStatus(foreignStatus, foreignPipe, foreignRoot, {
      status: "ready",
      heartbeat: options.stale ? new Date(Date.now() - 120000) : new Date(),
      healthHeartbeat: options.healthHeartbeat,
      toolCount: 999,
      editorPid: options.deadPid ? 99999999 : (options.editorPid ?? process.pid),
      processStart: options.processStart,
      writeHealth: options.writeHealth,
    });
    return { foreignRoot, foreignPipe, foreignStatus };
  }

  writeMalformedHealth() {
    const healthPath = path.join(this.statusDir, `editor-health-malformed-${nextPipeId++}.json`);
    fs.writeFileSync(healthPath, "{ not json");
    return healthPath;
  }

  async dispose() {
    if (this.bridge) await this.bridge.stop().catch(() => {});
    fs.rmSync(this.root, { recursive: true, force: true });
  }
}

function makePipePath() {
  if (process.platform === "win32") {
    return `\\\\.\\pipe\\unity-mcp-lens-test-${process.pid}-${nextPipeId++}`;
  }

  return path.join(os.tmpdir(), `unity-mcp-lens-test-${process.pid}-${nextPipeId++}.sock`);
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
    tools_hash: "fake",
    tool_discovery_reason: null,
    tool_snapshot_utc: options.heartbeat.toISOString(),
    command_health: "ok",
    last_command_success_utc: options.heartbeat.toISOString(),
    last_command_failure_utc: null,
    last_command_failure_reason: null,
    bridge_session_id: "fake-session",
    manifest_version: 1,
    profile_catalog_version: "fake-profile",
    supports_tool_sync_lens: true,
    last_tools_changed_utc: options.heartbeat.toISOString(),
    project_path: path.join(projectRoot, "Assets"),
    project_root: projectRoot,
    last_heartbeat: options.heartbeat.toISOString(),
    protocol_version: "2.0",
    editor_pid: options.editorPid ?? process.pid,
  }, null, 2));

  let healthPath = null;
  if (options.writeHealth !== false) {
    healthPath = path.join(path.dirname(statusPath), `editor-health-${nextPipeId++}.json`);
    writeHealth(healthPath, projectRoot, {
      heartbeat: options.healthHeartbeat || options.heartbeat,
      editorPid: options.editorPid ?? process.pid,
      processStart: options.processStart,
    });
  }

  return { statusPath, healthPath };
}

function writeHealth(healthPath, projectRoot, options) {
  const heartbeat = options.heartbeat || new Date();
  fs.writeFileSync(healthPath, JSON.stringify({
    health_schema_version: 1,
    editor_heartbeat_utc: heartbeat.toISOString(),
    state_captured_utc: heartbeat.toISOString(),
    editor_pid: options.editorPid ?? process.pid,
    editor_process_start_utc: options.processStart || processStartUtc(),
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

function resultFor(type, params) {
  switch (type) {
    case "register_client":
      return {
        bridgeSessionId: "fake-session",
        manifestVersion: 1,
        profileCatalogVersion: "fake-profile",
        activeToolPacks: ["foundation"],
      };
    case "get_manifest":
    case "set_tool_packs":
      return manifestResult(params && params.packs ? ["foundation", ...params.packs] : ["foundation"]);
    case "get_tool_schema":
      return {
        bridgeSessionId: "fake-session",
        manifestVersion: 1,
        activeToolPacks: ["foundation"],
        tools: fakeTools(true),
      };
    case "Unity_GetLensHealth":
      return {
        success: true,
        message: "Fake Lens health ready.",
        data: {
          bridgeStatus: { status: "ready", toolDiscoveryMode: "live" },
          internalRegistryToolCount: 3,
          editorStability: { isStable: true },
          expectedRecovery: { isActive: false },
        },
      };
    case "Unity_ListToolPacks":
      return {
        success: true,
        message: "Fake tool packs listed.",
        data: { activeToolPacks: ["foundation"], availableToolPacks: ["foundation"] },
      };
    case "Unity_RunCommand":
      return {
        success: true,
        message: "Fake mutating command ran.",
        data: {},
      };
    default:
      return { success: true, message: `${type} ok`, data: {} };
  }
}

function manifestResult(activeToolPacks) {
  return {
    bridgeSessionId: "fake-session",
    manifestVersion: 1,
    profileCatalogVersion: "fake-profile",
    activeToolPacks,
    kind: "full",
    reason: null,
    hashMinimal: "fake-minimal",
    hashFull: "fake-full",
    tools: fakeTools(false),
    delta: null,
  };
}

function fakeTools(withSchemas) {
  return [
    toolDescriptor("Unity_GetLensHealth", true, withSchemas),
    toolDescriptor("Unity_ListToolPacks", true, withSchemas),
    toolDescriptor("Unity_RunCommand", false, withSchemas),
  ];
}

function toolDescriptor(name, readOnlyHint, withSchemas) {
  const descriptor = {
    name,
    title: name,
    description: `${name} fake descriptor`,
    schemaHash: `${name}-schema`,
    groups: ["assistant"],
    packs: ["foundation"],
    readOnlyHint,
  };
  if (withSchemas) {
    descriptor.inputSchema = { type: "object", properties: {} };
    descriptor.outputSchema = { type: "object", properties: {} };
    descriptor.annotations = { readOnlyHint };
  }
  return descriptor;
}

async function withScenario(name, failOnceOn, body) {
  const context = new ScenarioContext(name, failOnceOn);
  let client = null;
  try {
    await context.startBridge();
    client = new McpHostClient(context.projectRoot, context.statusDir);
    await client.initialize();
    await body(context, client);
  } finally {
    if (client) await client.dispose().catch(() => {});
    await context.dispose();
  }
}

async function assertFullTools(client) {
  const response = await client.listTools();
  const names = response.tools.map((tool) => tool.name);
  assert(names.includes("Unity_GetLensHealth"), "tools/list should include dynamic health tool");
  assert(names.includes("Unity_ListToolPacks"), "tools/list should include dynamic pack tool");
  assert(names.includes("Unity_RunCommand"), `tools/list should include dynamic mutating tool; got ${names.join(", ")}; stderr=${client.stderr}`);
  assert(response.tools.length >= 4, "tools/list should include dynamic tools plus local bootstrap helpers");
}

async function main() {
  assert(fs.existsSync(hostPath), `Host path does not exist: ${hostPath}`);

  await withScenario("normal", null, async (_context, client) => {
    await assertFullTools(client);
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    assert.strictEqual(diagnostic.structuredContent.data.selected.basicHealth, "fresh");
    assert.strictEqual(diagnostic.structuredContent.data.selected.editorHealth.basicHealth, "fresh");
    assert.strictEqual(diagnostic.structuredContent.data.selected.editorHealth.pidStartMatches, true);
  });

  await withScenario("stale-ignored", null, async (context, client) => {
    const staleStatus = context.writeStaleStatus();
    await assertFullTools(client);
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    const staleCandidate = diagnostic.structuredContent.data.candidates.find((candidate) => candidate.statusPath === staleStatus);
    assert(staleCandidate, "stale bridge candidate should be listed");
    assert.strictEqual(staleCandidate.basicHealth, "bridge_stale_unity_alive");
  });

  await withScenario("matching-project-beats-foreign", null, async (context, client) => {
    const foreign = context.writeForeignStatus();
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    assert.strictEqual(diagnostic.structuredContent.success, true, "bridge diagnostics should succeed");
    assert.strictEqual(diagnostic.structuredContent.data.selected.projectRoot, context.projectRoot);
    const foreignCandidate = diagnostic.structuredContent.data.candidates.find((candidate) => candidate.statusPath === foreign.foreignStatus);
    assert(foreignCandidate, "foreign bridge candidate should be listed");
    assert.strictEqual(foreignCandidate.projectRootMatch, false);
    assert.strictEqual(foreignCandidate.basicHealth, "fresh");
    assert(foreignCandidate.exclusionReasons.includes("project_mismatch"));
    await assertFullTools(client);
  });

  await withScenario("stale-dead-foreign-never-selected", null, async (context, client) => {
    const foreign = context.writeForeignStatus({ stale: true, deadPid: true });
    await assertFullTools(client);
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    assert.strictEqual(diagnostic.structuredContent.data.selected.projectRoot, context.projectRoot);
    const foreignCandidate = diagnostic.structuredContent.data.candidates.find((candidate) => candidate.statusPath === foreign.foreignStatus);
    assert.strictEqual(foreignCandidate.basicHealth, "process_missing");
  });

  await withScenario("health-edge-diagnostics", null, async (context, client) => {
    const missingHealth = context.writeForeignStatus({ writeHealth: false, editorPid: 0 });
    const staleHealth = context.writeForeignStatus({ healthHeartbeat: new Date(Date.now() - 120000) });
    const pidReused = context.writeForeignStatus({ processStart: new Date(0).toISOString() });
    const malformedHealth = context.writeMalformedHealth();
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    const candidates = diagnostic.structuredContent.data.candidates;

    const missingHealthCandidate = candidates.find((candidate) => candidate.statusPath === missingHealth.foreignStatus);
    assert(missingHealthCandidate, "missing-health bridge candidate should be listed");
    assert.strictEqual(missingHealthCandidate.editorHealth ?? null, null);
    assert.strictEqual(missingHealthCandidate.basicHealth, "fresh");

    const staleHealthCandidate = candidates.find((candidate) => candidate.statusPath === staleHealth.foreignStatus);
    assert(staleHealthCandidate, "stale-health bridge candidate should be listed");
    assert.strictEqual(staleHealthCandidate.basicHealth, "unity_silent");

    const pidReusedCandidate = candidates.find((candidate) => candidate.statusPath === pidReused.foreignStatus);
    assert(pidReusedCandidate, "pid-reused bridge candidate should be listed");
    assert.strictEqual(pidReusedCandidate.basicHealth, "pid_reused");

    const malformedCandidate = diagnostic.structuredContent.data.unmatchedEditorHealthCandidates
      .find((candidate) => candidate.healthPath === malformedHealth);
    assert(malformedCandidate, "malformed editor health candidate should be listed");
    assert.strictEqual(malformedCandidate.basicHealth, "malformed_status");
  });

  {
    const context = new ScenarioContext("no-matching-bridge");
    let client = null;
    try {
      context.writeForeignStatus();
      client = new McpHostClient(context.projectRoot, context.statusDir, { omitProjectEnv: true });
      await client.initialize();
      const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
      assert.strictEqual(diagnostic.structuredContent.success, true, "bridge diagnostics should work without a selected bridge");
      assert.strictEqual(diagnostic.structuredContent.data.selected ?? null, null);

      const result = await client.callTool("Unity_ListToolPacks", {});
      assert.strictEqual(result.isError, true, "mismatched bridge must not be selected when project root is known");
      assert.strictEqual(result.structuredContent.code, "UNITY_MCP_NO_MATCHING_BRIDGE");
      assert(result.structuredContent.data.discovery.candidates.some((candidate) => candidate.exclusionReasons.includes("project_mismatch")));
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  await withScenario("register-recovery", "register_client", async (_context, client) => {
    await assertFullTools(client);
  });

  await withScenario("manifest-recovery", "get_manifest", async (_context, client) => {
    await assertFullTools(client);
  });

  await withScenario("schema-recovery", "get_tool_schema", async (_context, client) => {
    await assertFullTools(client);
  });

  await withScenario("readonly-call-recovery", "Unity_ListToolPacks", async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_ListToolPacks", {});
    assert.strictEqual(result.structuredContent.success, true, "read-only call should recover and succeed");
    assert.strictEqual(context.commandCounts.Unity_ListToolPacks, 2, "read-only tool should be retried once");
  });

  await withScenario("mutating-no-retry", "Unity_RunCommand", async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_RunCommand", {});
    assert.strictEqual(result.isError, true, "mutating transport failure should be returned as a tool error");
    assert.strictEqual(result.structuredContent.code, "UNITY_MCP_TRANSPORT_ERROR");
    assert.strictEqual(result.structuredContent.data.retrySafe, false);
    assert.strictEqual(result.structuredContent.data.maybeApplied, true);
    assert.strictEqual(context.commandCounts.Unity_RunCommand, 1, "mutating tool must not be retried");
  });

  console.log("MCP host transport recovery tests passed.");
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
