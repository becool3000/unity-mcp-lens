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
  constructor(projectRoot, statusDir) {
    this.nextId = 1;
    this.pending = new Map();
    this.stderr = "";
    this.child = childProcess.spawn(hostPath, [], {
      cwd: projectRoot,
      env: {
        ...process.env,
        UNITY_MCP_STATUS_DIR: statusDir,
        UNITY_MCP_PROJECT_PATH: projectRoot,
      },
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
      toolCount: 999,
    });
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
    editor_pid: process.pid,
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
  assert(names.includes("Unity_RunCommand"), `tools/list should include dynamic mutating tool; got ${names.join(", ")}`);
  assert(response.tools.length >= 4, "tools/list should include dynamic tools plus local bootstrap helpers");
}

async function main() {
  assert(fs.existsSync(hostPath), `Host path does not exist: ${hostPath}`);

  await withScenario("normal", null, async (_context, client) => {
    await assertFullTools(client);
  });

  await withScenario("stale-ignored", null, async (context, client) => {
    context.writeStaleStatus();
    await assertFullTools(client);
  });

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
