const assert = require("assert");
const childProcess = require("child_process");
const crypto = require("crypto");
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
    this.sockets = new Set();
    this.connectionPath = makePipePath();
    this.statusPath = path.join(context.statusDir, `bridge-status-${nextPipeId++}.json`);
    this.healthPath = null;
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
    const written = writeStatus(this.statusPath, this.connectionPath, this.context.projectRoot, {
      status: "ready",
      heartbeat: new Date(),
      healthHeartbeat: this.options.healthHeartbeat,
      toolCount: 3,
      writeHealth: this.options.writeHealth,
      healthFlags: this.options.healthFlags,
    });
    this.healthPath = written.healthPath;
  }

  async stop() {
    if (!this.server) return;
    for (const socket of this.sockets) {
      socket.destroy();
    }
    await new Promise((resolve) => this.server.close(resolve));
    this.server = null;
  }

  handleSocket(socket) {
    this.sockets.add(socket);
    socket.on("close", () => this.sockets.delete(socket));
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
    this.context.commands.push({ type, params: command.params || {} });

    if (this.context.shouldFail(type)) {
      await this.context.replaceBridge();
      socket.destroy();
      await this.stop();
      return;
    }

    if (this.options.hangOn === type) {
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
    this.commands = [];
    this.failOnceOn = failOnceOn;
    this.failed = false;
    this.bridge = null;
    this.bridges = [];
    this.replacementBridgeOptions = {};
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
    this.bridges.push(this.bridge);
    await this.bridge.start();
    return this.bridge;
  }

  async replaceBridge() {
    await this.startBridge(this.replacementBridgeOptions);
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

  writeMalformedHealth(options = {}) {
    const hash = options.foreign ? "ffffffff" : projectHashForStatusFile(this.projectRoot);
    const healthPath = path.join(this.statusDir, `editor-health-${hash}-${nextPipeId++}.json`);
    fs.writeFileSync(healthPath, "{ not json");
    if (options.stale) setStaleFileTime(healthPath);
    return healthPath;
  }

  writeMalformedStatus(options = {}) {
    const hash = options.foreign ? "ffffffff" : projectHashForStatusFile(this.projectRoot);
    const statusPath = path.join(this.statusDir, `bridge-status-${hash}-${nextPipeId++}.json`);
    fs.writeFileSync(statusPath, "{ not json");
    if (options.stale) setStaleFileTime(statusPath);
    return statusPath;
  }

  writeHealthOnly(options = {}) {
    const healthPath = path.join(this.statusDir, `editor-health-only-${nextPipeId++}.json`);
    writeHealth(healthPath, this.projectRoot, {
      heartbeat: options.heartbeat || new Date(),
      editorPid: options.deadPid ? 99999999 : (options.editorPid ?? process.pid),
      processStart: options.processStart,
      flags: options.flags,
    });
    return healthPath;
  }

  async dispose() {
    for (const bridge of this.bridges.reverse()) {
      await bridge.stop().catch(() => {});
    }
    fs.rmSync(this.root, { recursive: true, force: true });
  }
}

function makePipePath() {
  if (process.platform === "win32") {
    return `\\\\.\\pipe\\unity-mcp-lens-test-${process.pid}-${nextPipeId++}`;
  }

  return path.join(os.tmpdir(), `unity-mcp-lens-test-${process.pid}-${nextPipeId++}.sock`);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function writeStatus(statusPath, connectionPath, projectRoot, options) {
  fs.writeFileSync(statusPath, JSON.stringify({
    connection_type: process.platform === "win32" ? "named_pipe" : "unix_socket",
    connection_path: connectionPath,
    status: options.status,
    reason: options.reason || null,
    expected_recovery: !!options.expectedRecovery,
    expected_recovery_expires_utc: options.expectedRecoveryExpiresUtc || null,
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
      flags: options.healthFlags,
    });
  }

  return { statusPath, healthPath };
}

function projectHashForStatusFile(projectRoot) {
  const assetsPath = path.join(projectRoot, "Assets").replace(/\\/g, "/");
  return crypto.createHash("sha1").update(assetsPath, "utf8").digest("hex").slice(0, 8);
}

function setStaleFileTime(filePath) {
  const stale = new Date(Date.now() - 120000);
  fs.utimesSync(filePath, stale, stale);
}

function markStatusStaleAndDead(statusPath) {
  const status = JSON.parse(fs.readFileSync(statusPath, "utf8"));
  const stale = new Date(Date.now() - 120000).toISOString();
  status.last_heartbeat = stale;
  status.tool_snapshot_utc = stale;
  status.last_command_success_utc = stale;
  status.last_tools_changed_utc = stale;
  status.editor_pid = 99999999;
  fs.writeFileSync(statusPath, JSON.stringify(status, null, 2));
  setStaleFileTime(statusPath);
}

function markHealthStaleAndDead(healthPath) {
  const health = JSON.parse(fs.readFileSync(healthPath, "utf8"));
  const stale = new Date(Date.now() - 120000).toISOString();
  health.editor_heartbeat_utc = stale;
  health.state_captured_utc = stale;
  health.editor_pid = 99999999;
  fs.writeFileSync(healthPath, JSON.stringify(health, null, 2));
  setStaleFileTime(healthPath);
}

function writeHealth(healthPath, projectRoot, options) {
  const heartbeat = options.heartbeat || new Date();
  const flags = options.flags || {};
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
    is_compiling: !!flags.isCompiling,
    is_importing: !!flags.isImporting,
    is_updating: !!flags.isUpdating,
    is_playing: !!flags.isPlaying,
    is_paused: !!flags.isPaused,
    is_playing_or_will_change_playmode: !!flags.isPlayingOrWillChangePlaymode,
    is_building_player: !!flags.isBuildingPlayer,
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
    case "Unity.ReadConsole":
      return readConsoleResult(params);
    case "Unity.ManageEditor":
      return manageEditorResult(params);
    case "Unity.Editor.SyncScripts":
      return syncScriptsResult(params);
    case "Unity_RunCommand":
      return {
        success: true,
        message: "Fake mutating command ran.",
        data: {},
      };
    case "Unity.Editor.SetPlayMode":
      return playModeSetResult(params);
    case "Unity.PlayMode.StepVerifier":
      return stepVerifierResult(params);
    case "Unity.UI.QueryRuntimeLayout":
      return uiRuntimeLayoutResult(params);
    case "Unity.UI.InvokeControl":
      return uiInvokeControlResult(params);
    case "Unity.Workflow.RunGpuSimulationProbe":
      return gpuSimulationProbeResult(params);
    case "Unity.Workflow.VerifyRuntimePackSelection":
      return runtimePackSelectionResult(params);
    default:
      return { success: true, message: `${type} ok`, data: {} };
  }
}

function readConsoleResult(params = {}) {
  const cursorSupplied = params.cursor !== undefined && params.cursor !== null;
  const cursor = cursorSupplied ? Number(params.cursor) + 1 : 1;
  return {
    success: true,
    message: "Fake console summary.",
    data: {
      cursor,
      scannedFrom: cursorSupplied ? Number(params.cursor) : 0,
      cursorSupplied,
      entryCount: 0,
      newErrors: 0,
      newWarnings: 0,
      staleErrorsPresent: null,
      staleWarningsPresent: null,
      typeCounts: {
        error: 0,
        warning: 0,
        log: 0,
        exception: 0,
        assert: 0,
        unknown: 0,
      },
      entries: [],
    },
  };
}

function manageEditorResult(params = {}) {
  const action = params.action || params.Action || "GetState";
  if (action === "GetCompactState") {
    return {
      success: true,
      message: "Fake compact editor state.",
      data: {
        isPlaying: true,
        isPaused: false,
        isCompiling: false,
        isUpdating: false,
        isPlayingOrWillChangePlaymode: true,
        isBuildingPlayer: false,
        isEditorIdle: false,
        runtimeAdvanced: true,
        runtimeProbe: {
          isAvailable: true,
          hasAdvancedFrames: true,
          updateCount: 12,
          fixedUpdateCount: 6,
          unscaledTime: 1.25,
          activeSceneName: "TestScene",
        },
      },
    };
  }

  return {
    success: true,
    message: `Fake ManageEditor ${action}.`,
    data: {},
  };
}

function syncScriptsResult(params = {}) {
  const changedPaths = Array.isArray(params.changedPaths)
    ? params.changedPaths
    : Array.isArray(params.ChangedPaths)
      ? params.ChangedPaths
      : [];
  const relevantChangedPaths = changedPaths.filter((changedPath) =>
    /\.(cs|asmdef|asmref|rsp)$/i.test(String(changedPath || "")) ||
    /(^|\/)package\.json$/i.test(String(changedPath || "")) ||
    String(changedPath || "").replace(/\\/g, "/").toLowerCase() === "packages/manifest.json" ||
    String(changedPath || "").replace(/\\/g, "/").toLowerCase() === "packages/packages-lock.json");
  const force = params.force === true || params.Force === true;
  const noChangesDetected = relevantChangedPaths.length === 0 && !force;
  const refreshScheduledAfterResponse = !noChangesDetected;
  const packageResolvePaths = relevantChangedPaths.filter((changedPath) =>
    String(changedPath || "").replace(/\\/g, "/").toLowerCase().startsWith("packages/"));
  const packageResolveRequested =
    params.resolvePackages !== false &&
    params.ResolvePackages !== false &&
    (params.localPackageRefreshRequested === true ||
      params.LocalPackageRefreshRequested === true ||
      packageResolvePaths.length > 0);

  return {
    success: true,
    message: refreshScheduledAfterResponse
      ? "Fake script refresh scheduled."
      : "Fake script sync ready.",
    data: {
      status: refreshScheduledAfterResponse ? "pending_refresh" : "ready",
      readyForFollowUp: !refreshScheduledAfterResponse,
      noChangesDetected,
      changedPaths,
      relevantChangedPaths,
      packageResolveRequested,
      packageResolvePaths,
      force,
      waitForCompile: params.waitForCompile !== false && params.WaitForCompile !== false,
      refreshRequested: refreshScheduledAfterResponse,
      refreshScheduledAfterResponse,
      compileStarted: false,
      compileObserved: false,
      editorIdle: !refreshScheduledAfterResponse,
      timedOut: false,
      initialConsoleErrorCount: 0,
      finalConsoleErrorCount: 0,
      consoleErrorCount: 0,
      newConsoleErrorCount: 0,
      newConsoleErrorsDetected: false,
      staleConsoleErrorsPresent: false,
      consoleErrorsDetected: false,
      consoleDelta: readConsoleResult().data,
      warningCount: refreshScheduledAfterResponse ? 1 : 0,
      warnings: refreshScheduledAfterResponse
        ? [{
            kind: "refresh_scheduled_after_response",
            message: "Fake refresh scheduled after response.",
          }]
        : [],
      finalState: {},
    },
  };
}

function playModeSetResult(params = {}) {
  const mode = params.mode || params.Mode || "enter";
  const entering = mode !== "exit";
  return {
    success: true,
    message: entering ? "Fake play mode entered." : "Fake play mode exited.",
    data: {
      requested: entering,
      transitionState: entering ? "entered_play_mode" : "exited_play_mode",
      runtimeAdvanced: entering,
      readyForRuntimeTools: entering,
      transitionPending: false,
      consoleErrorCount: 0,
      finalState: {
        isPlaying: entering,
        isPaused: false,
        isCompiling: false,
        isUpdating: false,
        isBuildingPlayer: false,
        isPlayingOrWillChangePlaymode: false,
        runtimeProbe: {
          isAvailable: entering,
          hasAdvancedFrames: entering,
          updateCount: entering ? 12 : 0,
          fixedUpdateCount: entering ? 2 : 0,
          unscaledTime: entering ? 0.25 : 0,
          activeSceneName: "FakeScene",
        },
      },
    },
  };
}

function stepVerifierResult(params = {}) {
  const steps = Number(params.steps ?? params.Steps ?? 1);
  const warmupSteps = Number(params.warmupSteps ?? params.WarmupSteps ?? 0);
  const forceNewError = steps === 99;
  return {
    success: !forceNewError,
    message: forceNewError ? "Fake paused stepping saw a new console error." : "Fake paused stepping completed.",
    data: {
      enteredPlayMode: true,
      paused: true,
      stepsRequested: steps,
      stepsCompleted: forceNewError ? Math.max(0, steps - 1) : steps,
      warmupSteps,
      warmupCompleted: warmupSteps,
      runtimeAdvanced: steps + warmupSteps > 0,
      timedOut: false,
      editorResponsiveAfter: true,
      exitAfter: params.exitAfter ?? params.ExitAfter ?? true,
      allowRealtimeRun: params.allowRealtimeRun ?? params.AllowRealtimeRun ?? false,
      consoleDelta: {
        newErrors: forceNewError ? 1 : 0,
        newWarnings: 0,
        staleErrorsPresent: true,
        staleWarningsPresent: true,
      },
    },
  };
}

function uiRuntimeLayoutResult(params = {}) {
  const target = params.target || params.Target || "";
  const found = target && !String(target).includes("Missing");
  return {
    success: true,
    message: `Fake runtime layout found ${found ? 1 : 0} element(s).`,
    data: {
      rootCount: 1,
      totalElementCount: found ? 1 : 0,
      returnedElementCount: found ? 1 : 0,
      warningCount: 0,
      elements: found ? [{
        name: target,
        path: `Canvas/${target}`,
        elementTypes: ["button", "selectable"],
      }] : [],
    },
  };
}

function uiInvokeControlResult(params = {}) {
  const target = params.target || params.Target || "";
  const invoked = target && !String(target).includes("Missing");
  return {
    success: !!invoked,
    message: invoked ? "Fake UI control invoked." : "Fake UI control target missing.",
    data: {
      target: { name: target, path: `Canvas/${target}` },
      action: params.action || params.Action || "click",
      actionResult: { succeeded: !!invoked },
      consoleDelta: {
        newErrors: 0,
        newWarnings: 0,
        staleErrorsPresent: true,
        staleWarningsPresent: true,
      },
    },
  };
}

function gpuSimulationProbeResult(params = {}) {
  const steps = Number(params.steps ?? params.Steps ?? 240);
  const packId = params.packId || params.PackId || "garden";
  return {
    success: true,
    message: "Fake FallingSands GPU simulation probe completed.",
    data: {
      activePack: packId,
      gridSize: { width: 8, height: 8 },
      stepsCompleted: steps,
      dispatchTiming: { elapsedMs: 1.5, timedOut: false },
      readbackTiming: { elapsedMs: 0.75, timedOut: false },
      counts: {
        seed: 1,
        sprout: 1,
        plant: 1,
        flower: 1,
        water: 2,
        steam: 0,
        bee: 1,
        nectarBee: 0,
        hive: 1,
      },
      capsPassed: true,
      failedCaps: [],
      consoleDelta: {
        newErrors: 0,
        newWarnings: 0,
        staleErrorsPresent: true,
      },
      editorResponsiveAfter: true,
    },
  };
}

function runtimePackSelectionResult(params = {}) {
  const packId = params.selectedPackId || params.SelectedPackId || params.packId || params.PackId || "garden";
  return {
    success: true,
    message: "Fake runtime pack selection verified.",
    data: {
      selectedPackId: packId,
      activeRuntimePackName: packId,
      selectPack: params.selectPack ?? params.SelectPack ?? true,
      elementCount: 9,
      sceneLoaded: !!(params.scenePath || params.ScenePath),
      domainReloadObserved: false,
      passed: true,
    },
  };
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
    toolDescriptor("Unity.Editor.SyncScripts", false, withSchemas),
    toolDescriptor("Unity.Editor.SetPlayMode", false, withSchemas),
    toolDescriptor("Unity.PlayMode.StepVerifier", false, withSchemas),
    toolDescriptor("Unity.Workflow.RunGpuSimulationProbe", false, withSchemas),
    toolDescriptor("Unity.Workflow.VerifyRuntimePackSelection", false, withSchemas),
    toolDescriptor("Unity.Workflow.SelectPackThroughMainMenu", false, withSchemas),
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
  assert(names.includes("Unity_Editor_HealthCheckFast"), "tools/list should include file-backed fast health tool");
  assert(names.includes("Unity_PlayMode_StepVerifier"), "tools/list should include bootstrap StepVerifier tool");
  assert(names.includes("Unity_Editor_RecoverFromHang"), "tools/list should include bootstrap recovery tool");
  assert(names.includes("Unity_Workflow_RunGpuSimulationProbe"), "tools/list should include bootstrap GPU probe tool");
  assert(names.includes("Unity_Workflow_VerifyRuntimePackSelection"), "tools/list should include bootstrap pack handoff verifier");
  assert(names.includes("Unity_Workflow_SelectPackThroughMainMenu"), "tools/list should include bootstrap Main Menu pack selection workflow");
  assert(names.includes("Unity_ListToolPacks"), "tools/list should include dynamic pack tool");
  assert(names.includes("Unity_RunCommand"), `tools/list should include dynamic mutating tool; got ${names.join(", ")}; stderr=${client.stderr}`);
  assert(response.tools.length >= 4, "tools/list should include dynamic tools plus local bootstrap helpers");
}

function commandTotal(commandCounts) {
  return Object.values(commandCounts).reduce((sum, value) => sum + value, 0);
}

function assertIncludesAll(actual, expected, message) {
  for (const value of expected) {
    assert(actual.includes(value), `${message} missing ${value}; got ${actual.join(", ")}`);
  }
}

function writeBeeStyleLocalFilePackage(context) {
  const packageName = "com.becool3000.unity-mcp-lens";
  const packageRoot = path.join(context.root, "LocalLensPackage");
  const sourcePath = path.join(packageRoot, "Editor", "Lens", "Tools", "RuntimeInvokeComponentMethodTools.cs");
  const assemblyPath = path.join(context.projectRoot, "Library", "ScriptAssemblies", "Becool.UnityMcpLens.Editor.dll");
  fs.mkdirSync(path.dirname(sourcePath), { recursive: true });
  fs.mkdirSync(path.dirname(assemblyPath), { recursive: true });
  fs.writeFileSync(path.join(packageRoot, "package.json"), JSON.stringify({ name: packageName }, null, 2));
  fs.writeFileSync(sourcePath, "namespace FakeLens { public static class RuntimeInvokeComponentMethodTools {} }\n");
  fs.writeFileSync(assemblyPath, "old fake assembly");

  const oldTime = new Date(Date.now() - 120000);
  const newTime = new Date(Date.now() - 1000);
  fs.utimesSync(assemblyPath, oldTime, oldTime);
  fs.utimesSync(sourcePath, newTime, newTime);

  fs.writeFileSync(path.join(context.projectRoot, "Packages", "manifest.json"), JSON.stringify({
    dependencies: {
      [packageName]: `file:${packageRoot.replace(/\\/g, "/")}`,
    },
  }, null, 2));

  return {
    packageName,
    packageRoot,
    sourcePath,
    assemblyPath,
    assetPath: `Packages/${packageName}/Editor/Lens/Tools/RuntimeInvokeComponentMethodTools.cs`,
  };
}

async function main() {
  assert(fs.existsSync(hostPath), `Host path does not exist: ${hostPath}`);

  await withScenario("normal", null, async (context, client) => {
    await assertFullTools(client);
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    assert.strictEqual(diagnostic.structuredContent.data.selected.basicHealth, "fresh");
    assert.strictEqual(diagnostic.structuredContent.data.selected.editorHealth.basicHealth, "fresh");
    assert.strictEqual(diagnostic.structuredContent.data.selected.editorHealth.pidStartMatches, true);

    const beforeFastHealthCount = commandTotal(context.commandCounts);
    const fastHealth = await client.callTool("Unity_Editor_HealthCheckFast", {});
    assert.strictEqual(fastHealth.structuredContent.success, true, "fast health should succeed");
    assert.strictEqual(fastHealth.structuredContent.state, "unity_alive_fresh");
    assert.strictEqual(fastHealth.structuredContent.safeToContinue, true);
    assert.strictEqual(fastHealth.structuredContent.agent_should_stop, false);
    assert.strictEqual(commandTotal(context.commandCounts), beforeFastHealthCount, "fast health must not call the bridge");
  });

  {
    const context = new ScenarioContext("health-paused-playmode-stable");
    let client = null;
    try {
      await context.startBridge({
        healthFlags: {
          isPlaying: true,
          isPaused: true,
          isPlayingOrWillChangePlaymode: true,
        },
      });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.success, true, "paused play mode health should succeed");
      assert.strictEqual(result.structuredContent.state, "unity_alive_fresh", "paused play mode must not be classified as a play transition");
      assert.strictEqual(result.structuredContent.safeToContinue, true);
      assert.strictEqual(result.structuredContent.data.editorBusy, false);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  await withScenario("syncscripts-local-file-package-refresh", null, async (context, client) => {
    await assertFullTools(client);
    const localPackage = writeBeeStyleLocalFilePackage(context);
    const result = await client.callTool("Unity_Editor_SyncScripts", {
      waitForCompile: false,
      timeoutSeconds: 2,
    });

    assert.strictEqual(context.commandCounts["Unity.Editor.SyncScripts"], 1, "host should call native SyncScripts once");
    const syncCommand = context.commands.find((command) => command.type === "Unity.Editor.SyncScripts");
    assert(syncCommand, "native SyncScripts command should be recorded");
    assert(
      Array.isArray(syncCommand.params.changedPaths) &&
        syncCommand.params.changedPaths.includes(localPackage.assetPath),
      `native SyncScripts should receive local file-package asset path ${localPackage.assetPath}; got ${JSON.stringify(syncCommand.params.changedPaths)}`
    );
    assert.strictEqual(syncCommand.params.localPackageRefreshRequested, true, "host should pass local file-package refresh hint");
    assert.strictEqual(syncCommand.params.resolvePackages, true, "host should ask native SyncScripts to resolve packages");
    assert(
      Array.isArray(syncCommand.params.localPackageRefreshPaths) &&
        syncCommand.params.localPackageRefreshPaths.includes(localPackage.assetPath),
      `native SyncScripts should receive local package refresh paths; got ${JSON.stringify(syncCommand.params.localPackageRefreshPaths)}`
    );
    assert.strictEqual(result.structuredContent.success, false, "stale file-package assembly should fail follow-up readiness");
    assert.strictEqual(result.structuredContent.data.localPackageRefreshRequested, true);
    assert.strictEqual(result.structuredContent.data.packageResolveRequested, true);
    assert(result.structuredContent.data.packageResolvePaths.includes(localPackage.assetPath));
    assert.strictEqual(result.structuredContent.data.localPackageSourceNewerThanAssembly, true);
    assert.strictEqual(result.structuredContent.data.proofStatus, "local_package_source_newer_than_assembly");
    assert.strictEqual(result.structuredContent.data.status, "local_package_source_newer_than_assembly");
    assert(result.structuredContent.data.localPackageRefreshPaths.includes(localPackage.assetPath));
    const warningKinds = result.structuredContent.data.warnings.map((warning) => warning.kind);
    assert(
      warningKinds.includes("local_package_source_newer_than_assembly"),
      `expected local package warning; got ${warningKinds.join(", ")}`
    );
  });

  await withScenario("syncscripts-focus-nudge-skips-busy-editor", null, async (context, client) => {
    await assertFullTools(client);
    writeBeeStyleLocalFilePackage(context);
    const result = await client.callTool("Unity_Editor_SyncScripts", {
      waitForCompile: false,
      timeoutSeconds: 2,
      focusNudgeOnStaleRefresh: true,
      safeClickNudge: true,
    });

    const syncCommand = context.commands.find((command) => command.type === "Unity.Editor.SyncScripts");
    assert(syncCommand, "native SyncScripts command should be recorded");
    assert.strictEqual(syncCommand.params.focusNudgeOnStaleRefresh, true, "host should forward focus nudge hint to native SyncScripts schema");
    assert.strictEqual(syncCommand.params.safeClickNudge, true, "host should forward safe click nudge hint");
    assert.strictEqual(result.structuredContent.success, false, "busy-editor focus nudge skip should not mask stale proof");
    assert.strictEqual(result.structuredContent.data.scriptRefreshOutcome, "focus_nudge_skipped");
    assert.strictEqual(result.structuredContent.data.focusNudgeOnStaleRefresh, true);
    assert.strictEqual(result.structuredContent.data.safeClickNudge, true);
    assert(result.structuredContent.data.focusNudge, "focus nudge diagnostics should be present");
    assert.strictEqual(result.structuredContent.data.focusNudge.requested, true);
    assert.strictEqual(result.structuredContent.data.focusNudge.attempted, false);
    assert.strictEqual(result.structuredContent.data.focusNudge.skipped, true);
    assert.strictEqual(result.structuredContent.data.focusNudge.outcome, "skipped_editor_not_idle");
    assert.strictEqual(result.structuredContent.data.focusNudge.reason, "play_mode_transition");
    const warningKinds = result.structuredContent.data.warnings.map((warning) => warning.kind);
    assert(warningKinds.includes("script_refresh_focus_nudge"), "focus nudge diagnostics should also be summarized in warnings");
  });

  await withScenario("step-verifier-wrapper", null, async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_PlayMode_StepVerifier", {
      scenePath: "Assets/Scenes/MainMenu.unity",
      warmupSteps: 1,
      steps: 2,
      exitAfter: true,
      captureConsoleDelta: true,
      timeoutMs: 10000,
    });

    assert.strictEqual(result.structuredContent.success, true, "StepVerifier wrapper should succeed");
    assert.strictEqual(result.structuredContent.data.enteredPlayMode, true);
    assert.strictEqual(result.structuredContent.data.timedOut, false);
    assert.strictEqual(result.structuredContent.data.editorResponsiveAfter, true);
    assert.strictEqual(result.structuredContent.data.verifier.success, true);
    assert.strictEqual(result.structuredContent.data.verifier.data.paused, true);
    assert.strictEqual(result.structuredContent.data.verifier.data.stepsRequested, 2);
    assert.strictEqual(result.structuredContent.data.verifier.data.stepsCompleted, 2);
    assert.strictEqual(result.structuredContent.data.verifier.data.warmupSteps, 1);
    assert.strictEqual(result.structuredContent.data.verifier.data.allowRealtimeRun, false);
    assert.strictEqual(result.structuredContent.data.verifier.data.consoleDelta.staleErrorsPresent, true);
    assert.strictEqual(result.structuredContent.data.verifier.data.consoleDelta.newErrors, 0);
    assert.strictEqual(result.structuredContent.data.timeoutMs, 10000);
    assert.strictEqual(result.structuredContent.data.entryTimeoutMs, 10000);
    assert.strictEqual(context.commandCounts["Unity.PlayMode.StepVerifier"], 1, "native StepVerifier should be called once");
    assert((context.commandCounts["Unity.Editor.SetPlayMode"] || 0) >= 3, "StepVerifier should enter through SetPlayMode readiness");
  });

  await withScenario("step-verifier-default-timeout", null, async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_PlayMode_StepVerifier", {
      scenePath: "Assets/Scenes/MainMenu.unity",
      steps: 1,
    });

    assert.strictEqual(result.structuredContent.success, true, "StepVerifier default-timeout wrapper should succeed");
    assert.strictEqual(result.structuredContent.data.timeoutMs, 60000);
    assert.strictEqual(result.structuredContent.data.entryTimeoutMs, 60000);
    assert(result.structuredContent.data.stepTimeoutMs > 0, "StepVerifier should report a positive step timeout");
  });

  await withScenario("recover-diagnose-only", null, async (context, client) => {
    await assertFullTools(client);
    const beforeCount = commandTotal(context.commandCounts);
    const result = await client.callTool("Unity_Editor_RecoverFromHang", {
      diagnoseOnly: true,
    });

    assert.strictEqual(result.structuredContent.success, true, "diagnose-only recovery should return a successful bounded result");
    assert.strictEqual(result.structuredContent.state, "recovered");
    assert.strictEqual(result.structuredContent.safeToContinue, true);
    assert.strictEqual(result.structuredContent.data.diagnoseOnly, true);
    assert.strictEqual(result.structuredContent.data.allowKillUnity, false);
    assert.strictEqual(result.structuredContent.data.allowRestartUnity, false);
    assert.strictEqual(result.structuredContent.data.allowScratchCleanup, false);
    assert.strictEqual(result.structuredContent.data.killedPid ?? null, null);
    assert.strictEqual(result.structuredContent.data.restart ?? null, null);
    assert.strictEqual(result.structuredContent.data.scratchCleanup ?? null, null);
    assert.strictEqual(result.structuredContent.data.actionCount, 0);
    assert.strictEqual(result.structuredContent.data.modalHandling.knownDialogsOnly, true);
    assert.strictEqual(commandTotal(context.commandCounts), beforeCount, "diagnose-only recovery must not call the bridge");
  });

  await withScenario("runcommand-preflight-labels", null, async (context, client) => {
    await assertFullTools(client);
    const cases = [
      {
        title: "scene-load",
        code: "UnityEditor.SceneManagement.EditorSceneManager.OpenScene(\"Assets/Scenes/Main.unity\");",
        labels: ["loads_scene"],
      },
      {
        title: "domain-reload",
        code: "UnityEditor.AssetDatabase.Refresh(); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();",
        labels: ["may_trigger_domain_reload", "touches_assets"],
      },
      {
        title: "sync-gpu-readback",
        code: "GridState.IdRead.GetData(ids); AsyncGPUReadback.Request(buffer);",
        labels: ["does_sync_gpu_readback", "uses_full_grid_getdata", "may_block_main_thread"],
      },
      {
        title: "play-and-block",
        code: "UnityEditor.EditorApplication.isPlaying = true; while (true) { System.Threading.Thread.Sleep(1); }",
        labels: ["requires_play_mode", "may_block_main_thread"],
      },
    ];

    const beforeCount = commandTotal(context.commandCounts);
    for (const item of cases) {
      const result = await client.callTool("Unity_RunCommand", {
        mode: "preflight",
        title: item.title,
        code: item.code,
      });
      assert.strictEqual(result.structuredContent.success, true, `${item.title} preflight should succeed`);
      assert.strictEqual(result.structuredContent.data.mode, "preflight");
      assert.strictEqual(result.structuredContent.data.bridgeTouched, false);
      assert.strictEqual(result.structuredContent.data.unityTouched, false);
      assertIncludesAll(result.structuredContent.data.riskLabels, item.labels, `${item.title} risk labels`);
    }
    assert.strictEqual(commandTotal(context.commandCounts), beforeCount, "RunCommand preflight must not call the bridge");
  });

  await withScenario("gpu-probe-wrapper", null, async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_Workflow_RunGpuSimulationProbe", {
      scenePath: "Assets/Scenes/Main.unity",
      packId: "garden",
      fixture: "sparse_nectar_bee",
      steps: 10,
      maxWallMs: 5000,
      caps: {
        beeCountMax: 500,
        steamCountMax: 10000,
        dispatchMsMax: 50,
        readbackMsMax: 50,
      },
    });

    assert.strictEqual(result.structuredContent.success, true, "GPU probe wrapper should succeed");
    assert.strictEqual(result.structuredContent.data.entry.success, true, "GPU probe should enter paused play first");
    assert.strictEqual(result.structuredContent.data.probe.success, true, "native GPU probe should run");
    assert.strictEqual(result.structuredContent.data.probe.data.activePack, "garden");
    assert.strictEqual(result.structuredContent.data.probe.data.stepsCompleted, 10);
    assert.strictEqual(result.structuredContent.data.probe.data.capsPassed, true);
    assert.strictEqual(result.structuredContent.data.exit.success, true, "GPU probe should exit play mode by default");
    assert.strictEqual(result.structuredContent.data.timeoutMs, 65000, "GPU probe default timeout should be maxWallMs + 60000");
    assert.strictEqual(result.structuredContent.data.entryTimeoutMs, 60000, "GPU probe entry cap should default to 60000");
    assert.strictEqual(context.commandCounts["Unity.Workflow.RunGpuSimulationProbe"], 1, "native GPU probe should be called once");
    assert.strictEqual(context.commandCounts["Unity.PlayMode.StepVerifier"], 2, "GPU probe should use StepVerifier for entry and exit");
  });

  await withScenario("pack-verify-default-timeout", null, async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_Workflow_VerifyRuntimePackSelection", {
      selectedPackId: "garden",
      scenePath: "Assets/Scenes/Main.unity",
    });

    assert.strictEqual(result.structuredContent.success, true, "pack handoff verifier should succeed");
    assert.strictEqual(result.structuredContent.data.timeoutMs, 60000);
    assert.strictEqual(result.structuredContent.data.entryTimeoutMs, 60000);
    assert.strictEqual(result.structuredContent.data.verify.success, true);
  });

  await withScenario("select-pack-through-main-menu", null, async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_Workflow_SelectPackThroughMainMenu", {
      packId: "garden",
      mainMenuScenePath: "Assets/Scenes/MainMenu.unity",
      exitAfter: true,
      captureConsoleDelta: true,
    });

    assert.strictEqual(result.structuredContent.success, true, "Main Menu pack workflow should succeed");
    assert.strictEqual(result.structuredContent.data.enteredPlayMode, true);
    assert.strictEqual(result.structuredContent.data.paused, true);
    assert.strictEqual(result.structuredContent.data.buttonFound, true);
    assert.strictEqual(result.structuredContent.data.buttonInvoked, true);
    assert.strictEqual(result.structuredContent.data.stepsAfterClick, 10);
    assert.strictEqual(result.structuredContent.data.activeRuntimePackName, "garden");
    assert.strictEqual(result.structuredContent.data.passed, true);
    assert.strictEqual(result.structuredContent.data.timedOut, false);
    assert.strictEqual(result.structuredContent.data.editorResponsiveAfter, true);
    assert.strictEqual(result.structuredContent.data.consoleDelta.staleErrorsPresent, true);
    assert.strictEqual(result.structuredContent.data.consoleDelta.newErrors, 0);
    assert.strictEqual(result.structuredContent.data.verify.data.selectPack, false, "runtime verifier must not directly select the pack");

    const interesting = context.commands
      .map((command) => command.type)
      .filter((type) => [
        "Unity.PlayMode.StepVerifier",
        "Unity.UI.QueryRuntimeLayout",
        "Unity.UI.InvokeControl",
        "Unity.Workflow.VerifyRuntimePackSelection",
      ].includes(type));
    assert.deepStrictEqual(interesting, [
      "Unity.PlayMode.StepVerifier",
      "Unity.UI.QueryRuntimeLayout",
      "Unity.UI.InvokeControl",
      "Unity.PlayMode.StepVerifier",
      "Unity.Workflow.VerifyRuntimePackSelection",
      "Unity.PlayMode.StepVerifier",
    ], "Main Menu pack workflow should call runtime-safe tools in order");
  });

  await withScenario("select-pack-through-main-menu-new-console-error", null, async (context, client) => {
    await assertFullTools(client);
    const result = await client.callTool("Unity_Workflow_SelectPackThroughMainMenu", {
      packId: "garden",
      stepsAfterClick: 99,
      exitAfter: true,
      captureConsoleDelta: true,
      failOnNewConsoleErrors: true,
    });

    assert.strictEqual(result.isError, true, "new console errors should fail Main Menu pack workflow");
    assert.strictEqual(result.structuredContent.code, "UNITY_MCP_SELECT_PACK_MAIN_MENU_STEP_FAILED");
    assert.strictEqual(result.structuredContent.data.step.data.consoleDelta.newErrors, 1);
    assert.strictEqual(context.commandCounts["Unity.UI.InvokeControl"], 1, "button should be invoked before the new console error is detected");
  });

  {
    const context = new ScenarioContext("select-pack-through-main-menu-entry-failure");
    let client = null;
    try {
      await context.startBridge({ healthHeartbeat: new Date(Date.now() - 120000) });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Workflow_SelectPackThroughMainMenu", {
        packId: "garden",
        timeoutMs: 2000,
      });
      assert.strictEqual(result.isError, true, "unsafe health should block Main Menu pack workflow entry");
      assert.strictEqual(result.structuredContent.code, "UNITY_MCP_SELECT_PACK_MAIN_MENU_ENTER_FAILED");
      assert.strictEqual(context.commandCounts["Unity.UI.InvokeControl"] || 0, 0, "UI invoke must not run after entry failure");
      assert.strictEqual(context.commandCounts["Unity.Workflow.VerifyRuntimePackSelection"] || 0, 0, "pack verification must not run after entry failure");
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("gpu-probe-entry-failure");
    let client = null;
    try {
      await context.startBridge({ healthHeartbeat: new Date(Date.now() - 120000) });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Workflow_RunGpuSimulationProbe", {
        scenePath: "Assets/Scenes/Main.unity",
        packId: "garden",
        steps: 10,
        timeoutMs: 2000,
      });
      assert.strictEqual(result.isError, true, "unsafe health should block GPU probe entry");
      assert.strictEqual(result.structuredContent.code, "UNITY_MCP_GPU_PROBE_ENTER_FAILED");
      assert.strictEqual(context.commandCounts["Unity.Workflow.RunGpuSimulationProbe"] || 0, 0, "native GPU probe must not run after entry failure");
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-no-status");
    let client = null;
    try {
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.state, "no_status_file");
      assert.strictEqual(result.structuredContent.agent_should_stop, true);
      assert.strictEqual(result.structuredContent.user_action_required, true);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("cold-start-status-appears-after-host-init");
    let client = null;
    try {
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();

      const before = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(before.structuredContent.state, "no_status_file");
      assert.strictEqual(before.structuredContent.safeToContinue, false);

      await context.startBridge();
      const after = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
      assert.strictEqual(after.structuredContent.state, "unity_alive_fresh");
      assert.strictEqual(after.structuredContent.safeToContinue, true);
      assert.strictEqual(after.structuredContent.data.selected.editorPid, process.pid);
      assert.strictEqual(after.structuredContent.data.selected.editorHealthBridgePidMatch, true);
      assert.strictEqual(after.structuredContent.data.selected.editorHealthMatchQuality, "fresh_pid_project_match_command_line_unavailable");
      await assertFullTools(client);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-only-fresh");
    let client = null;
    try {
      context.writeHealthOnly();
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.state, "bridge_unavailable");
      assert.strictEqual(result.structuredContent.safeToContinue, false);
      assert.strictEqual(result.structuredContent.agent_should_stop, false);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("reload-gap-stale-bridge-fresh-health");
    let client = null;
    try {
      const staleDeadPipe = makePipePath();
      const staleDeadStatus = path.join(context.statusDir, "bridge-status-reload-gap-dead-pid.json");
      writeStatus(staleDeadStatus, staleDeadPipe, context.projectRoot, {
        status: "disconnected",
        heartbeat: new Date(Date.now() - 120000),
        toolCount: 999,
        editorPid: 99999999,
      });
      const staleUnknownPidPipe = makePipePath();
      const staleUnknownPidStatus = path.join(context.statusDir, "bridge-status-reload-gap-zero-pid.json");
      writeStatus(staleUnknownPidStatus, staleUnknownPidPipe, context.projectRoot, {
        status: "disconnected",
        heartbeat: new Date(Date.now() - 120000),
        toolCount: 999,
        editorPid: 0,
      });
      const freshHealth = context.writeHealthOnly();

      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
      assert.strictEqual(diagnostic.structuredContent.data.selected ?? null, null, "dead stale bridge must not be selected during reload gap");
      const staleDeadCandidate = diagnostic.structuredContent.data.candidates.find((candidate) => candidate.statusPath === staleDeadStatus);
      assert(staleDeadCandidate, "stale dead-PID bridge candidate should remain diagnostic");
      assert.strictEqual(staleDeadCandidate.selectable, false);
      assert.strictEqual(staleDeadCandidate.basicHealth, "process_missing");
      assert(
        staleDeadCandidate.exclusionReasons.includes("fresh_project_editor_health_without_matching_bridge_pid"),
        `stale dead-PID bridge should be excluded by fresh editor health; got ${staleDeadCandidate.exclusionReasons.join(", ")}`
      );
      const staleUnknownPidCandidate = diagnostic.structuredContent.data.candidates.find((candidate) => candidate.statusPath === staleUnknownPidStatus);
      assert(staleUnknownPidCandidate, "stale zero-PID bridge candidate should remain diagnostic");
      assert.strictEqual(staleUnknownPidCandidate.selectable, false);
      assert.strictEqual(staleUnknownPidCandidate.basicHealth, "bridge_stale_unity_alive");
      assert(
        staleUnknownPidCandidate.exclusionReasons.includes("fresh_project_editor_health_without_matching_bridge_pid"),
        `stale zero-PID bridge should be excluded by fresh editor health; got ${staleUnknownPidCandidate.exclusionReasons.join(", ")}`
      );
      const unmatchedHealth = diagnostic.structuredContent.data.unmatchedEditorHealthCandidates
        .find((candidate) => candidate.healthPath === freshHealth);
      const candidateHealth = diagnostic.structuredContent.data.candidates
        .map((candidate) => candidate.editorHealth)
        .find((candidate) => candidate && candidate.healthPath === freshHealth);
      const visibleFreshHealth = unmatchedHealth || candidateHealth;
      assert(visibleFreshHealth, "fresh editor health should remain visible while bridge reloads");
      assert.strictEqual(visibleFreshHealth.basicHealth, "fresh");

      const health = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
      assert.strictEqual(health.structuredContent.state, "bridge_unavailable");
      assert.strictEqual(health.structuredContent.safeToContinue, false);
      assert.strictEqual(health.structuredContent.agent_should_stop, false);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-reload-wait-recovers");
    let client = null;
    try {
      const reloadingStatus = path.join(context.statusDir, "bridge-status-editor-reloading.json");
      writeStatus(reloadingStatus, makePipePath(), context.projectRoot, {
        status: "editor_reloading",
        reason: "compile_reload",
        expectedRecovery: true,
        expectedRecoveryExpiresUtc: new Date(Date.now() + 5000).toISOString(),
        heartbeat: new Date(),
        toolCount: 999,
        healthFlags: { isCompiling: true },
      });

      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const replacementPromise = delay(150).then(() => context.startBridge());
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {
        includeCandidates: true,
        timeoutMs: 3000,
      });
      const replacement = await replacementPromise;

      assert.strictEqual(result.structuredContent.state, "unity_alive_fresh");
      assert.strictEqual(result.structuredContent.safeToContinue, true);
      assert.strictEqual(result.structuredContent.data.selected.statusPath, replacement.statusPath);
      assert.strictEqual(result.structuredContent.data.reloadRecovery.waited, true);
      assert.strictEqual(result.structuredContent.data.reloadRecovery.recovered, true);
      assert.strictEqual(result.structuredContent.data.reloadRecovery.initialState, "editor_busy_healthy");
      assert(result.structuredContent.data.reloadRecovery.attemptCount >= 2);

      const reloadingCandidate = result.structuredContent.data.candidates.bridge
        .find((candidate) => candidate.statusPath === reloadingStatus);
      assert(reloadingCandidate, "editor_reloading bridge candidate should remain diagnostic");
      assert.strictEqual(reloadingCandidate.selectable, false);
      assert.strictEqual(reloadingCandidate.basicHealth, "editor_reloading");
      assert.strictEqual(reloadingCandidate.recoveryActive, true);
      assert(reloadingCandidate.exclusionReasons.includes("expected_recovery_active"));
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-stale-live");
    let client = null;
    try {
      context.writeHealthOnly({ heartbeat: new Date(Date.now() - 120000) });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.state, "unity_alive_stale_unresponsive");
      assert.strictEqual(result.structuredContent.agent_should_stop, true);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-dead-pid");
    let client = null;
    try {
      context.writeHealthOnly({ deadPid: true });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.state, "unity_missing");
      assert.strictEqual(result.structuredContent.user_action_required, true);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-pid-reused");
    let client = null;
    try {
      context.writeHealthOnly({ processStart: "2000-01-01T00:00:00.000Z" });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.state, "unity_missing");
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  {
    const context = new ScenarioContext("health-malformed");
    let client = null;
    try {
      context.writeMalformedHealth();
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(result.structuredContent.state, "malformed_status");
      assert.strictEqual(result.structuredContent.agent_should_stop, true);
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  await withScenario("stale-malformed-same-project-ignored", null, async (context, client) => {
    const malformed = context.writeMalformedStatus({ stale: true });
    const result = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
    assert.strictEqual(result.structuredContent.state, "unity_alive_fresh");
    assert.strictEqual(result.structuredContent.data.freshMalformedStatusCount, 0);
    assert.strictEqual(result.structuredContent.data.ignoredMalformedStatusCount, 1);
    assert(result.structuredContent.data.ignoredMalformedStatusFiles.includes(malformed), "ignored malformed file should be reported");
    const candidate = result.structuredContent.data.candidates.bridge.find((item) => item.statusPath === malformed);
    assert(candidate, "ignored stale malformed bridge candidate should be listed");
    assert.strictEqual(candidate.ignoredMalformed, true);
    assert.strictEqual(candidate.malformedIgnoreReason, "stale_malformed_status");
  });

  await withScenario("fresh-malformed-same-project-blocks", null, async (context, client) => {
    const malformed = context.writeMalformedStatus();
    const result = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
    assert.strictEqual(result.structuredContent.state, "malformed_status");
    assert.strictEqual(result.structuredContent.safeToContinue, false);
    assert.strictEqual(result.structuredContent.data.freshMalformedStatusCount, 1);
    const candidate = result.structuredContent.data.candidates.bridge.find((item) => item.statusPath === malformed);
    assert(candidate, "fresh malformed bridge candidate should be listed");
    assert.strictEqual(candidate.ignoredMalformed, false);
  });

  await withScenario("stale-malformed-foreign-ignored", null, async (context, client) => {
    const malformed = context.writeMalformedStatus({ stale: true, foreign: true });
    const result = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
    assert.strictEqual(result.structuredContent.state, "unity_alive_fresh");
    assert.strictEqual(result.structuredContent.data.freshMalformedStatusCount, 0);
    assert.strictEqual(result.structuredContent.data.ignoredMalformedStatusCount, 1);
    const candidate = result.structuredContent.data.candidates.bridge.find((item) => item.statusPath === malformed);
    assert(candidate, "ignored foreign malformed bridge candidate should be listed");
    assert.strictEqual(candidate.ignoredMalformed, true);
    assert(["stale_malformed_status", "foreign_malformed_status"].includes(candidate.malformedIgnoreReason));
  });

  await withScenario("fresh-bridge-missing-health", null, async (context, client) => {
    await context.bridge.stop();
    fs.rmSync(context.statusDir, { recursive: true, force: true });
    fs.mkdirSync(context.statusDir, { recursive: true });
    await context.startBridge({ writeHealth: false });
    const result = await client.callTool("Unity_Editor_HealthCheckFast", {});
    assert.strictEqual(result.structuredContent.state, "bridge_alive_no_editor_heartbeat");
    assert.strictEqual(result.structuredContent.safeToContinue, false);
  });

  await withScenario("fresh-bridge-ignores-dead-same-project-health", null, async (context, client) => {
    const deadHealth = context.writeHealthOnly({ deadPid: true });
    const result = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
    assert.strictEqual(result.structuredContent.state, "unity_alive_fresh");
    assert.strictEqual(result.structuredContent.safeToContinue, true);
    assert.strictEqual(result.structuredContent.data.selected.editorHealthBridgePidMatch, true);
    const ignoredHealth = result.structuredContent.data.candidates.unmatchedEditorHealth
      .find((candidate) => candidate.healthPath === deadHealth);
    assert(ignoredHealth, "dead same-project health should remain diagnostic");
    assert.strictEqual(ignoredHealth.basicHealth, "process_missing");
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

  await withScenario("stale-dead-bridge-after-domain-reload-ignored", null, async (context, client) => {
    const oldBridge = context.bridge;
    assert(oldBridge, "scenario should start with a bridge");
    await assertFullTools(client);

    await oldBridge.stop();
    markStatusStaleAndDead(oldBridge.statusPath);
    if (oldBridge.healthPath) markHealthStaleAndDead(oldBridge.healthPath);

    const replacement = await context.startBridge();
    const diagnostic = await client.callTool("Unity_Bridge_ListConnections", {});
    assert.strictEqual(diagnostic.structuredContent.success, true, "bridge diagnostics should succeed after replacement bridge appears");
    assert.strictEqual(diagnostic.structuredContent.data.selected.statusPath, replacement.statusPath);

    const staleCandidate = diagnostic.structuredContent.data.candidates.find((candidate) => candidate.statusPath === oldBridge.statusPath);
    assert(staleCandidate, "stale stopped bridge should remain visible as diagnostic evidence");
    assert.strictEqual(staleCandidate.selectable, false);
    assert(
      staleCandidate.exclusionReasons.includes("stale_heartbeat") ||
        staleCandidate.exclusionReasons.includes("fresh_project_editor_health_without_matching_bridge_pid"),
      `stale stopped bridge should be excluded; got ${staleCandidate.exclusionReasons.join(", ")}`
    );

    const health = await client.callTool("Unity_Editor_HealthCheckFast", { includeCandidates: true });
    assert.strictEqual(health.structuredContent.state, "unity_alive_fresh");
    assert.strictEqual(health.structuredContent.data.selected.statusPath, replacement.statusPath);
    assert.strictEqual(health.structuredContent.data.selected.editorHealthBridgePidMatch, true);
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
    assert.strictEqual(staleHealthCandidate.basicHealth, "fresh");
    assert.strictEqual(staleHealthCandidate.editorHealth ?? null, null);
    assert.strictEqual(staleHealthCandidate.editorHealthMatchQuality, "pid_health_present_but_not_fresh_or_not_project_matched");

    const pidReusedCandidate = candidates.find((candidate) => candidate.statusPath === pidReused.foreignStatus);
    assert(pidReusedCandidate, "pid-reused bridge candidate should be listed");
    assert.strictEqual(pidReusedCandidate.basicHealth, "fresh");
    assert.strictEqual(pidReusedCandidate.editorHealth ?? null, null);
    assert.strictEqual(pidReusedCandidate.editorHealthMatchQuality, "pid_health_present_but_not_fresh_or_not_project_matched");

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

  {
    const context = new ScenarioContext("runcommand-watchdog");
    let client = null;
    try {
      await context.startBridge({ hangOn: "Unity_RunCommand" });
      client = new McpHostClient(context.projectRoot, context.statusDir);
      await client.initialize();
      await assertFullTools(client);

      const result = await client.callTool("Unity_RunCommand", {
        code: "return;",
        title: "hung fake command",
        timeoutMs: 1000,
      });
      assert.strictEqual(result.isError, true, "hung RunCommand should return a tool error");
      assert.strictEqual(result.structuredContent.code, "editor_hung_during_command");
      assert.strictEqual(result.structuredContent.agent_should_stop, true);
      assert.strictEqual(result.structuredContent.safeToContinue, false);
      assert.strictEqual(result.structuredContent.data.maybeApplied, true);

      const unsafePreflightCount = commandTotal(context.commandCounts);
      const unsafePreflight = await client.callTool("Unity_RunCommand", {
        mode: "preflight",
        title: "unsafe preflight is file-backed",
        code: "UnityEditor.AssetDatabase.Refresh(); GridState.IdRead.GetData(ids); while(true) {}",
      });
      assert.strictEqual(unsafePreflight.structuredContent.success, true, "preflight should remain available while session is unsafe");
      assert.strictEqual(unsafePreflight.structuredContent.data.bridgeTouched, false);
      assertIncludesAll(
        unsafePreflight.structuredContent.data.riskLabels,
        ["may_trigger_domain_reload", "touches_assets", "does_sync_gpu_readback", "uses_full_grid_getdata", "may_block_main_thread"],
        "unsafe preflight risk labels");
      assert.strictEqual(commandTotal(context.commandCounts), unsafePreflightCount, "unsafe preflight must not call the bridge");

      const blocked = await client.callTool("Unity_ListToolPacks", {});
      assert.strictEqual(blocked.isError, true, "unsafe session should block bridge-backed tools");
      assert.strictEqual(blocked.structuredContent.code, "UNITY_MCP_SESSION_UNSAFE");

      const stablePlayFlags = { isPlaying: true, isPlayingOrWillChangePlaymode: true };
      context.failOnceOn = "Unity.ReadConsole";
      context.failed = false;
      context.replacementBridgeOptions = { healthFlags: stablePlayFlags };
      await context.startBridge({ healthFlags: stablePlayFlags });
      const bypassed = await client.callTool("Unity_RunCommand", {
        code: "return;",
        title: "stable play mode bypass",
        timeoutMs: 5000,
      });
      assert.strictEqual(bypassed.structuredContent.success, true, "stable Play Mode should allow RunCommand despite stale unsafe latch");
      assert.strictEqual(context.commandCounts["Unity.ReadConsole"], 3, "ReadConsole safety probe should reconnect, retry once, then perform the delta check");
      assert(client.stderr.includes("Allowing Unity.RunCommand while unsafe latch is set"), "host should log scoped RunCommand unsafe-latch bypass");

      const afterBypass = await client.callTool("Unity_ListToolPacks", {});
      assert.strictEqual(afterBypass.structuredContent.success, true, "successful stable Play Mode RunCommand should clear unsafe latch");

      await context.startBridge();
      const recovered = await client.callTool("Unity_Editor_HealthCheckFast", {});
      assert.strictEqual(recovered.structuredContent.state, "unity_alive_fresh");
      assert.strictEqual(recovered.structuredContent.safeToContinue, true);

      const afterRecovery = await client.callTool("Unity_ListToolPacks", {});
      assert.strictEqual(afterRecovery.structuredContent.success, true, "fresh health should clear unsafe session state");
    } finally {
      if (client) await client.dispose().catch(() => {});
      await context.dispose();
    }
  }

  console.log("MCP host transport recovery tests passed.");
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
