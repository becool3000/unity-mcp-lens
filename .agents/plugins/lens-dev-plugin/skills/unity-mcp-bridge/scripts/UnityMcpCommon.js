const { spawn, spawnSync } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const bridgeScriptsDir = __dirname;

function sleep(ms) {
  return new Promise((resolve) => setTimeout(resolve, Math.max(0, ms)));
}

function nowIso() {
  return new Date().toISOString();
}

function parseCliArgs(argv) {
  const result = { _: [] };
  let index = 0;
  while (index < argv.length) {
    const token = argv[index];
    if (!token.startsWith("-")) {
      result._.push(token);
      index += 1;
      continue;
    }

    const key = token.replace(/^-+/, "").toLowerCase();
    const values = [];
    index += 1;
    while (index < argv.length && !argv[index].startsWith("-")) {
      values.push(argv[index]);
      index += 1;
    }

    const value = values.length === 0 ? true : values.length === 1 ? values[0] : values;
    if (Object.prototype.hasOwnProperty.call(result, key)) {
      result[key] = Array.isArray(result[key]) ? result[key].concat(value) : [result[key]].concat(value);
    } else {
      result[key] = value;
    }
  }

  return result;
}

function getArg(args, names, fallback) {
  for (const name of names) {
    const key = name.toLowerCase();
    if (Object.prototype.hasOwnProperty.call(args, key)) {
      return args[key];
    }
  }
  return fallback;
}

function getArgString(args, names, fallback = "") {
  const value = getArg(args, names, fallback);
  if (Array.isArray(value)) {
    return value.join(" ");
  }
  if (value === true || value === false || value == null) {
    return value === true ? "true" : fallback;
  }
  return String(value);
}

function getArgNumber(args, names, fallback) {
  const value = Number(getArg(args, names, fallback));
  return Number.isFinite(value) ? value : fallback;
}

function toBool(value, fallback = false) {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  if (value == null) {
    return fallback;
  }

  const normalized = String(value).trim().toLowerCase();
  if (["1", "true", "yes", "on"].includes(normalized)) {
    return true;
  }
  if (["0", "false", "no", "off"].includes(normalized)) {
    return false;
  }
  return fallback;
}

function getArgBool(args, names, fallback = false) {
  return toBool(getArg(args, names, fallback), fallback);
}

function getArgArray(args, names, fallback = []) {
  const value = getArg(args, names, fallback);
  const rawValues = Array.isArray(value) ? value : value == null || value === true ? fallback : [value];
  return rawValues
    .flatMap((entry) => String(entry).split(","))
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function resolveProjectPath(projectPath = process.cwd()) {
  let candidate = projectPath && String(projectPath).trim() ? String(projectPath) : process.cwd();
  candidate = candidate.replace(/^~(?=$|[\\/])/, os.homedir());
  const resolved = fs.existsSync(candidate) ? fs.realpathSync(candidate) : path.resolve(candidate);
  if (path.basename(resolved).toLowerCase() === "assets") {
    return path.dirname(resolved);
  }
  return resolved;
}

function normalizeForCompare(value) {
  return String(value || "")
    .replace(/\\/g, "/")
    .replace(/\/+$/, "")
    .toLowerCase();
}

function normalizeProjectRootForCompare(value) {
  let normalized = normalizeForCompare(value);
  if (normalized.endsWith("/assets")) {
    normalized = normalized.slice(0, -"/assets".length);
  }
  return normalized;
}

function resolveUnityRelativePath(projectPath, pathValue) {
  if (!pathValue) {
    return null;
  }

  const projectRoot = resolveProjectPath(projectPath);
  let candidate = String(pathValue);
  if (!path.isAbsolute(candidate)) {
    candidate = path.join(projectRoot, candidate);
  }
  if (fs.existsSync(candidate)) {
    candidate = fs.realpathSync(candidate);
  } else {
    candidate = path.resolve(candidate);
  }

  const projectNormalized = normalizeForCompare(projectRoot);
  const candidateNormalized = candidate.replace(/\\/g, "/");
  const candidateCompare = normalizeForCompare(candidateNormalized);
  if (candidateCompare === projectNormalized) {
    return ".";
  }
  if (candidateCompare.startsWith(`${projectNormalized}/`)) {
    return candidateNormalized.slice(projectNormalized.length + 1);
  }
  return String(pathValue).replace(/\\/g, "/");
}

function ensureDir(directory) {
  fs.mkdirSync(directory, { recursive: true });
}

function readJsonFile(filePath, fallback = null) {
  try {
    return JSON.parse(fs.readFileSync(filePath, "utf8").replace(/^\uFEFF/, ""));
  } catch (_error) {
    return fallback;
  }
}

function writeJsonFile(filePath, data) {
  ensureDir(path.dirname(filePath));
  fs.writeFileSync(filePath, `${JSON.stringify(data, null, 2)}\n`, "utf8");
}

function pathExists(filePath) {
  try {
    return !!filePath && fs.existsSync(filePath);
  } catch (_error) {
    return false;
  }
}

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

function getLensBinaryState() {
  const lensPath = process.env.UNITY_MCP_LENS_PATH || getDefaultLensPath();
  let executable = false;
  if (pathExists(lensPath)) {
    if (process.platform === "win32") {
      executable = true;
    } else {
      try {
        fs.accessSync(lensPath, fs.constants.X_OK);
        executable = true;
      } catch (_error) {
        executable = false;
      }
    }
  }

  return {
    path: lensPath,
    exists: pathExists(lensPath),
    executable,
  };
}

function runProcess(command, args, options = {}) {
  return new Promise((resolve) => {
    const child = spawn(command, args, {
      cwd: options.cwd || process.cwd(),
      env: options.env || process.env,
      stdio: ["ignore", "pipe", "pipe"],
      windowsHide: true,
    });

    const stdout = [];
    const stderr = [];
    let timedOut = false;
    const timeoutMs = Math.max(1000, Number(options.timeoutMs || 30000));
    const timeout = setTimeout(() => {
      timedOut = true;
      try {
        child.kill();
      } catch (_error) {
      }
    }, timeoutMs);

    child.stdout.on("data", (chunk) => stdout.push(chunk));
    child.stderr.on("data", (chunk) => stderr.push(chunk));
    child.on("error", (error) => {
      clearTimeout(timeout);
      resolve({ code: 1, stdout: "", stderr: error.message, timedOut: false });
    });
    child.on("exit", (code) => {
      clearTimeout(timeout);
      resolve({
        code: timedOut ? 124 : code ?? 0,
        stdout: Buffer.concat(stdout).toString("utf8"),
        stderr: Buffer.concat(stderr).toString("utf8"),
        timedOut,
      });
    });
  });
}

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
    Unity_InputSystem_Diagnostics: ["project"],
    Unity_Project_PackageCompatibility: ["project"],
    Unity_InputActions_InspectAsset: ["project"],
    Unity_ProjectSettings_PreviewActiveInputHandler: ["project"],
    Unity_ProjectSettings_SetActiveInputHandler: ["project"],
    Unity_Object_ValidateReferences: ["project"],
    Unity_Project_ScanMissingScripts: ["project"],
    Unity_Runtime_GetVisualBoundsSnapshot: ["scene"],
    Unity_GetLensUsageReport: ["debug"],
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

function normalizeAdditionalPacks(packs = []) {
  const normalized = [];
  let fullRequested = false;
  for (const pack of packs) {
    const value = String(pack || "").trim();
    if (!value || value === "foundation") {
      continue;
    }
    if (value === "full") {
      fullRequested = true;
      continue;
    }
    if (!normalized.includes(value)) {
      normalized.push(value);
    }
  }

  if (fullRequested) {
    return ["full"];
  }

  return normalized.slice(0, 2);
}

function stringArraysEqual(left = [], right = []) {
  if (left.length !== right.length) {
    return false;
  }
  for (let index = 0; index < left.length; index += 1) {
    if (left[index] !== right[index]) {
      return false;
    }
  }
  return true;
}

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

  return normalizeAdditionalPacks(Array.from(requiredPacks));
}

function isReconnectableUnityMcpError(error) {
  const message = String(error?.message || "").toLowerCase();
  return (
    message.includes("transport closed") ||
    message.includes("connection closed") ||
    message.includes("disconnected") ||
    message.includes("disposed object") ||
    message.includes("timed out waiting for unity mcp response") ||
    message.includes("timed out waiting for response") ||
    message.includes("exited before tool response") ||
    message.includes("produced no stdout output") ||
    message.includes("broken pipe")
  );
}

function buildUnityMcpTimeoutError(toolName, timeoutSeconds) {
  if (toolName === "Unity_RunCommand") {
    return new Error(
      `Unity MCP tool '${toolName}' timed out after ${timeoutSeconds} seconds. Verify on-disk or scene state before retrying because Unity may have applied part of the command before the transport died.`
    );
  }

  return new Error(`Unity MCP tool '${toolName}' timed out after ${timeoutSeconds} seconds.`);
}

function writeFramedMessage(child, payload) {
  const body = Buffer.from(JSON.stringify(payload), "utf8");
  child.stdin.write(`Content-Length: ${body.length}\r\n\r\n`);
  child.stdin.write(body);
}

class UnityMcpLensSession {
  constructor(projectPath) {
    this.projectRoot = resolveProjectPath(projectPath);
    this.child = null;
    this.buffer = Buffer.alloc(0);
    this.stderrChunks = [];
    this.pending = new Map();
    this.nextRequestId = 1;
    this.initializePromise = null;
    this.currentAdditionalPacks = new Set();
    this.desiredAdditionalPacks = new Set();
    this.packSetupPromise = null;
  }

  async ensureStarted(timeoutMs = 30000) {
    if (this.child && !this.initializePromise) {
      return;
    }
    if (this.initializePromise) {
      return await this.initializePromise;
    }

    const lensBinary = getLensBinaryState();
    if (!lensBinary.exists) {
      throw new Error(
        `Unity MCP Lens server not found at '${lensBinary.path}'. Reinstall or republish unity-mcp-lens before running helper scripts.`
      );
    }
    if (!lensBinary.executable) {
      throw new Error(`Unity MCP Lens server exists but is not executable at '${lensBinary.path}'.`);
    }

    this.child = spawn(lensBinary.path, [], {
      cwd: this.projectRoot,
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });
    this.buffer = Buffer.alloc(0);
    this.stderrChunks = [];

    this.child.stdout.on("data", (chunk) => {
      this.buffer = Buffer.concat([this.buffer, chunk]);
      this.parseFrames();
    });

    this.child.stderr.on("data", (chunk) => {
      this.stderrChunks.push(chunk);
    });

    this.child.on("error", (error) => {
      this.handleTransportClosed(`Failed to start unity-mcp-lens: ${error.message}`);
    });

    this.child.on("exit", (code) => {
      const stderrText = Buffer.concat(this.stderrChunks).toString("utf8").trim();
      const message = stderrText || `unity-mcp-lens exited before tool response (code ${code || 1}).`;
      this.handleTransportClosed(message);
    });

    this.initializePromise = (async () => {
      try {
        const initResponse = await this.sendRequest(
          "initialize",
          {
            protocolVersion: "2025-06-18",
            capabilities: {},
            clientInfo: {
              name: "codex-unity-tool-lens",
              version: "1.0.0",
            },
          },
          timeoutMs
        );

        if (initResponse?.error) {
          throw new Error(initResponse.error.message || "Failed to initialize unity-mcp-lens.");
        }

        this.sendNotification("notifications/initialized", {});

        const desiredPacks = normalizeAdditionalPacks(Array.from(this.desiredAdditionalPacks));
        if (desiredPacks.length > 0) {
          const response = await this.sendToolCallRequest("Unity_SetToolPacks", { packs: desiredPacks }, timeoutMs);
          const outcome = getToolObject(response);
          if (outcome?.success === false) {
            throw new Error(
              `Failed to restore required Lens packs [${desiredPacks.join(", ")}]: ${outcome.error || outcome.message || "Unknown error."}`
            );
          }
          this.currentAdditionalPacks = new Set(desiredPacks);
        }
      } finally {
        this.initializePromise = null;
      }
    })();

    return await this.initializePromise;
  }

  sendNotification(method, params) {
    if (!this.child) {
      return;
    }
    writeFramedMessage(this.child, {
      jsonrpc: "2.0",
      method,
      params,
    });
  }

  sendRequest(method, params, timeoutMs = 30000) {
    if (!this.child) {
      return Promise.reject(new Error("unity-mcp-lens session is not connected."));
    }

    const id = this.nextRequestId++;
    return new Promise((resolve, reject) => {
      const timeoutHandle = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for Unity MCP response to '${method}'.`));
      }, Math.max(1000, Number(timeoutMs || 30000)));

      this.pending.set(id, {
        resolve,
        reject,
        timeoutHandle,
      });

      writeFramedMessage(this.child, {
        jsonrpc: "2.0",
        id,
        method,
        params,
      });
    });
  }

  parseFrames() {
    while (true) {
      const separator = this.buffer.indexOf(Buffer.from("\r\n\r\n"));
      if (separator === -1) {
        return;
      }

      const header = this.buffer.subarray(0, separator).toString("ascii");
      const match = /Content-Length:\s*(\d+)/i.exec(header);
      if (!match) {
        this.handleTransportClosed("Missing Content-Length header from unity-mcp-lens.");
        return;
      }

      const contentLength = Number(match[1]);
      const messageEnd = separator + 4 + contentLength;
      if (this.buffer.length < messageEnd) {
        return;
      }

      const body = this.buffer.subarray(separator + 4, messageEnd).toString("utf8");
      this.buffer = this.buffer.subarray(messageEnd);

      let message;
      try {
        message = JSON.parse(body);
      } catch (error) {
        this.handleTransportClosed(`Invalid JSON message from unity-mcp-lens: ${error.message}`);
        return;
      }

      if (message?.id != null && this.pending.has(message.id)) {
        const pending = this.pending.get(message.id);
        this.pending.delete(message.id);
        clearTimeout(pending.timeoutHandle);
        pending.resolve(message);
      }
    }
  }

  handleTransportClosed(message) {
    if (this.child) {
      try {
        this.child.kill();
      } catch (_error) {
      }
    }
    this.child = null;
    this.buffer = Buffer.alloc(0);
    this.initializePromise = null;
    this.packSetupPromise = null;

    const error = new Error(message);
    for (const [id, pending] of this.pending.entries()) {
      clearTimeout(pending.timeoutHandle);
      pending.reject(error);
      this.pending.delete(id);
    }
  }

  async restart() {
    this.handleTransportClosed("unity-mcp-lens session restart requested.");
    await sleep(1500);
  }

  async dispose() {
    this.desiredAdditionalPacks.clear();
    this.currentAdditionalPacks.clear();
    this.handleTransportClosed("unity-mcp-lens session closed.");
  }

  async callToolRaw(toolName, toolArguments = {}, timeoutMs = 30000) {
    await this.ensureStarted(timeoutMs);
    return await this.sendToolCallRequest(toolName, toolArguments, timeoutMs);
  }

  async sendToolCallRequest(toolName, toolArguments = {}, timeoutMs = 30000) {
    return await this.sendRequest(
      "tools/call",
      {
        name: toolName,
        arguments: toolArguments,
      },
      timeoutMs
    );
  }

  async setAdditionalPacksRaw(packs, timeoutMs = 30000) {
    const normalizedPacks = normalizeAdditionalPacks(packs);
    const response = await this.sendToolCallRequest("Unity_SetToolPacks", { packs: normalizedPacks }, timeoutMs);
    const outcome = getToolObject(response);
    if (outcome?.success === false) {
      throw new Error(
        `Failed to activate required Lens packs [${normalizedPacks.join(", ")}]: ${outcome.error || outcome.message || "Unknown error."}`
      );
    }
    this.currentAdditionalPacks = new Set(normalizedPacks);
    return response;
  }

  async ensureAdditionalPacks(packs, timeoutMs = 30000) {
    const normalizedPacks = normalizeAdditionalPacks(packs);
    if (normalizedPacks.length === 0) {
      return;
    }

    const desiredPacks = normalizeAdditionalPacks([
      ...Array.from(this.desiredAdditionalPacks),
      ...normalizedPacks,
    ]);
    this.desiredAdditionalPacks = new Set(desiredPacks);

    const currentPacks = normalizeAdditionalPacks(Array.from(this.currentAdditionalPacks));
    if (JSON.stringify(currentPacks) === JSON.stringify(desiredPacks)) {
      return;
    }

    if (!this.packSetupPromise) {
      this.packSetupPromise = this.ensureStarted(timeoutMs)
        .then(() => {
          const activePacks = normalizeAdditionalPacks(Array.from(this.currentAdditionalPacks));
          if (JSON.stringify(activePacks) === JSON.stringify(desiredPacks)) {
            return null;
          }
          return this.setAdditionalPacksRaw(desiredPacks, timeoutMs);
        })
        .finally(() => {
          this.packSetupPromise = null;
        });
    }

    await this.packSetupPromise;
  }

  async setExactAdditionalPacks(packs, timeoutMs = 30000) {
    const normalizedPacks = normalizeAdditionalPacks(packs);
    this.desiredAdditionalPacks = new Set(normalizedPacks);

    const currentPacks = normalizeAdditionalPacks(Array.from(this.currentAdditionalPacks));
    if (stringArraysEqual(currentPacks, normalizedPacks)) {
      return {
        skipped: true,
        packs: normalizedPacks,
      };
    }

    await this.ensureStarted(timeoutMs);
    const activePacks = normalizeAdditionalPacks(Array.from(this.currentAdditionalPacks));
    if (stringArraysEqual(activePacks, normalizedPacks)) {
      return {
        skipped: true,
        packs: normalizedPacks,
      };
    }

    const response = await this.setAdditionalPacksRaw(normalizedPacks, timeoutMs);
    return {
      skipped: false,
      packs: normalizedPacks,
      response,
    };
  }
}

const unityMcpSessions = new Map();
let unityMcpSessionCleanupRegistered = false;

function ensureUnityMcpSessionCleanup() {
  if (unityMcpSessionCleanupRegistered) {
    return;
  }

  unityMcpSessionCleanupRegistered = true;
  process.once("exit", () => {
    for (const session of unityMcpSessions.values()) {
      session.dispose().catch(() => {});
    }
    unityMcpSessions.clear();
  });
}

function getUnityMcpSession(projectPath) {
  ensureUnityMcpSessionCleanup();
  const projectRoot = resolveProjectPath(projectPath);
  const key = normalizeProjectRootForCompare(projectRoot);
  if (!unityMcpSessions.has(key)) {
    unityMcpSessions.set(key, new UnityMcpLensSession(projectRoot));
  }
  return unityMcpSessions.get(key);
}

async function ensureUnityToolPacks(projectPath, packs = [], options = {}) {
  const session = getUnityMcpSession(projectPath);
  await session.ensureAdditionalPacks(packs, Math.max(5000, Number(options.timeoutSeconds || 30) * 1000));
}

async function setUnityToolPacksExact(projectPath, packs = [], options = {}) {
  const session = getUnityMcpSession(projectPath);
  return await session.setExactAdditionalPacks(packs, Math.max(5000, Number(options.timeoutSeconds || 30) * 1000));
}

async function resetUnityMcpSession(projectPath) {
  const projectRoot = resolveProjectPath(projectPath);
  const key = normalizeProjectRootForCompare(projectRoot);
  const session = unityMcpSessions.get(key);
  if (!session) {
    return;
  }
  await session.dispose();
  unityMcpSessions.delete(key);
}

async function shutdownUnityMcpSessions() {
  const sessions = Array.from(unityMcpSessions.values());
  unityMcpSessions.clear();
  for (const session of sessions) {
    try {
      await session.dispose();
    } catch (_error) {
    }
  }
}

async function invokeUnityMcpToolJson(projectPath, toolName, toolArguments = {}, options = {}) {
  const projectRoot = resolveProjectPath(projectPath);
  const timeoutSeconds = Math.max(1, Number(options.timeoutSeconds || 45));
  const expectedReloadState = getUnityExpectedReloadState(projectRoot);
  const allowReconnect = options.allowReconnect === true || expectedReloadState.IsActive;
  const maxAttempts = allowReconnect ? 2 : 1;
  const retryDelayMs = Math.max(250, Number(options.retryDelayMs || 1500));
  const requiredPacks = normalizeAdditionalPacks(options.requiredPacks || inferRequiredPacks(toolName));

  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const session = getUnityMcpSession(projectRoot);
    try {
      if (normalizeToolName(toolName) !== normalizeToolName("Unity.SetToolPacks")) {
        if (options.exactPacks === true) {
          await session.setExactAdditionalPacks(requiredPacks, Math.max(15000, timeoutSeconds * 1000));
        } else if (requiredPacks.length > 0) {
          await session.ensureAdditionalPacks(requiredPacks, Math.max(15000, timeoutSeconds * 1000));
        }
      }

      return await session.callToolRaw(toolName, toolArguments, Math.max(1000, timeoutSeconds * 1000));
    } catch (error) {
      const normalizedError = error instanceof Error ? error : new Error(String(error));
      const lowerMessage = normalizedError.message.toLowerCase();
      const timedOut =
        lowerMessage.includes("timed out waiting for unity mcp response") ||
        lowerMessage.includes("timed out waiting for response");
      if (timedOut) {
        throw buildUnityMcpTimeoutError(toolName, timeoutSeconds);
      }

      if (attempt >= maxAttempts || !isReconnectableUnityMcpError(normalizedError)) {
        throw normalizedError;
      }

      await session.restart();
      await sleep(retryDelayMs);
    }
  }

  throw new Error(`Unity MCP tool '${toolName}' failed without a response.`);
}

function getToolObject(response) {
  if (response?.result?.structuredContent != null) {
    return response.result.structuredContent;
  }

  const text = response?.result?.content?.find?.((item) => item?.type === "text")?.text;
  if (text) {
    try {
      return JSON.parse(text);
    } catch (_error) {
      return { rawText: text };
    }
  }

  if (response?.error) {
    return {
      success: false,
      error: response.error.message,
    };
  }

  return null;
}

function valueOf(object, ...names) {
  for (const name of names) {
    if (object && Object.prototype.hasOwnProperty.call(object, name)) {
      return object[name];
    }
  }
  return undefined;
}

function boolOf(object, ...names) {
  return valueOf(object, ...names) === true;
}

async function getUnityEditorState(projectPath, timeoutSeconds = 30) {
  const response = await invokeUnityMcpToolJson(
    projectPath,
    "Unity_ManageEditor",
    { Action: "GetState" },
    { timeoutSeconds }
  );
  return getToolObject(response);
}

async function getUnityCompactEditorState(projectPath, timeoutSeconds = 30) {
  const response = await invokeUnityMcpToolJson(
    projectPath,
    "Unity_ManageEditor",
    { Action: "GetCompactState" },
    { timeoutSeconds }
  );
  return getToolObject(response);
}

async function getUnityConsoleEntries(projectPath, options = {}) {
  const args = {
    Action: "Get",
    Types: options.types || ["Error", "Warning", "Log"],
    Count: options.count ?? 100,
    FilterText: options.filterText || "",
    SinceTimestamp: options.sinceTimestamp || "",
    Format: options.format || "Detailed",
    ExcludeMcpNoise: options.excludeMcpNoise !== false,
    IncludeStacktrace: options.includeStacktrace !== false,
  };
  const response = await invokeUnityMcpToolJson(projectPath, "Unity_ReadConsole", args, {
    timeoutSeconds: options.timeoutSeconds || 30,
  });
  return getToolObject(response);
}

function normalizeUnityUsingList(usings = []) {
  const normalized = [];
  for (const entry of usings) {
    for (const segment of String(entry || "").split(",")) {
      let value = segment.trim();
      if (!value) {
        continue;
      }
      if (value.startsWith("using ")) {
        value = value.slice(6).trim();
      }
      value = value.replace(/;+\s*$/, "").trim();
      if (!value) {
        continue;
      }
      const line = `using ${value};`;
      if (!normalized.includes(line)) {
        normalized.push(line);
      }
    }
  }
  return normalized;
}

function convertToUnityRunCommandScript(code, usings = []) {
  if (code.includes("IRunCommand") && code.includes("CommandScript")) {
    return code;
  }

  const usingLines = [];
  for (const line of normalizeUnityUsingList([
    "UnityEngine",
    "UnityEditor",
    "Becool.UnityMcpLens.Editor.Tools.RunCommandSupport",
    ...usings,
  ])) {
    if (!usingLines.includes(line)) {
      usingLines.push(line);
    }
  }

  const bodyLines = String(code).split(/\r?\n/);
  const normalizedBodyLines = [];
  let parsingLeadingUsings = true;
  for (const line of bodyLines) {
    const trimmed = line.trim();
    if (parsingLeadingUsings && !trimmed) {
      continue;
    }
    if (parsingLeadingUsings && /^using\s+[^;]+;\s*$/.test(trimmed)) {
      for (const usingLine of normalizeUnityUsingList([trimmed])) {
        if (!usingLines.includes(usingLine)) {
          usingLines.push(usingLine);
        }
      }
      continue;
    }
    parsingLeadingUsings = false;
    normalizedBodyLines.push(line);
  }

  return [
    ...usingLines,
    "",
    "namespace Becool.UnityMcpLens.Agent.Dynamic.Extension.Editor",
    "{",
    "    internal class CommandScript : IRunCommand",
    "    {",
    "        public void Execute(ExecutionResult result)",
    "        {",
    ...normalizedBodyLines.map((line) => `            ${line}`),
    "        }",
    "    }",
    "}",
  ].join("\n");
}

async function invokeUnityRunCommandObject(projectPath, options) {
  const payload = {
    code: convertToUnityRunCommandScript(options.code, options.usings || []),
  };
  if (options.title) {
    payload.title = options.title;
  }
  if (options.pausePlayMode) {
    payload.pausePlayMode = true;
  }
  if (options.stepFrames > 0) {
    payload.stepFrames = Math.max(0, Number(options.stepFrames));
  }
  if (options.restorePauseState === false) {
    payload.restorePauseState = false;
  }

  const response = await invokeUnityMcpToolJson(projectPath, "Unity_RunCommand", payload, {
    timeoutSeconds: options.timeoutSeconds || 60,
  });
  return getToolObject(response);
}

function getUnityRunCommandPlayModeExecution(runCommandResult) {
  const data = valueOf(runCommandResult, "data", "Data");
  const execution = valueOf(data, "playModeExecution", "PlayModeExecution");
  if (!execution) {
    return null;
  }
  const stepsRequested = Number(valueOf(execution, "stepsRequested", "StepsRequested") || 0);
  const pauseApplied = boolOf(execution, "pauseApplied", "PauseApplied");
  return {
    pauseRequested: pauseApplied || stepsRequested > 0,
    pauseWasApplied: pauseApplied,
    stepsRequested,
    stepsApplied: Number(valueOf(execution, "stepsApplied", "StepsApplied") || 0),
    wasPlaying: boolOf(execution, "wasPlaying", "WasPlaying"),
    wasPaused: boolOf(execution, "wasPaused", "WasPaused"),
    isPausedAfter: boolOf(execution, "isPausedAfter", "IsPausedAfter"),
    pauseStepOnly: false,
  };
}

function getUnitySkillStateDirectory(projectPath) {
  return path.join(resolveProjectPath(projectPath), "Temp", "CodexUnity");
}

function getUnityExpectedReloadMarkerPath(projectPath) {
  return path.join(getUnitySkillStateDirectory(projectPath), "expected-reload.json");
}

function getUnityExpectedReloadState(projectPath, includeExpired = false) {
  const markerPath = getUnityExpectedReloadMarkerPath(projectPath);
  const state = {
    Path: markerPath,
    Exists: false,
    IsActive: false,
    IsExpired: false,
    Reason: null,
    ChangedPaths: [],
    CreatedAtUtc: null,
    ExpiresAtUtc: null,
    TtlSeconds: null,
    Error: null,
  };

  const raw = readJsonFile(markerPath);
  if (!raw) {
    return state;
  }

  const createdAt = raw.CreatedAtUtc || raw.createdAtUtc || null;
  let expiresAt = raw.ExpiresAtUtc || raw.expiresAtUtc || null;
  const ttl = raw.TtlSeconds ?? raw.ttlSeconds ?? null;
  if (!expiresAt && createdAt && ttl != null) {
    expiresAt = new Date(new Date(createdAt).getTime() + Number(ttl) * 1000).toISOString();
  }
  const expired = expiresAt ? new Date(expiresAt).getTime() <= Date.now() : false;
  return {
    ...state,
    Exists: true,
    IsExpired: expired,
    IsActive: !expired || includeExpired,
    Reason: raw.Reason || raw.reason || null,
    ChangedPaths: raw.ChangedPaths || raw.changedPaths || [],
    CreatedAtUtc: createdAt,
    ExpiresAtUtc: expiresAt,
    TtlSeconds: ttl,
  };
}

function setUnityExpectedReloadState(projectPath, reason, changedPaths = [], ttlSeconds = 120) {
  const projectRoot = resolveProjectPath(projectPath);
  const createdAt = new Date();
  const data = {
    Reason: reason,
    ChangedPaths: changedPaths
      .map((entry) => resolveUnityRelativePath(projectRoot, entry))
      .filter(Boolean)
      .filter((entry, index, entries) => entries.indexOf(entry) === index),
    CreatedAtUtc: createdAt.toISOString(),
    ExpiresAtUtc: new Date(createdAt.getTime() + ttlSeconds * 1000).toISOString(),
    TtlSeconds: ttlSeconds,
  };
  writeJsonFile(getUnityExpectedReloadMarkerPath(projectRoot), data);
  return getUnityExpectedReloadState(projectRoot);
}

function clearUnityExpectedReloadState(projectPath) {
  const markerPath = getUnityExpectedReloadMarkerPath(projectPath);
  try {
    fs.rmSync(markerPath, { force: true });
  } catch (_error) {
  }
}

function testUnityCompileAffectingPath(projectPath, pathValue) {
  const relative = resolveUnityRelativePath(projectPath, pathValue);
  if (!relative) {
    return false;
  }
  const normalized = relative.toLowerCase();
  return (
    /\.(cs|asmdef|asmref|rsp)$/.test(normalized) ||
    normalized === "packages/manifest.json" ||
    normalized === "packages/packages-lock.json" ||
    /^packages\/[^/]+\/package\.json$/.test(normalized)
  );
}

function getUnityCompileAffectingChanges(projectPath, changedPaths = []) {
  return changedPaths
    .filter((entry) => testUnityCompileAffectingPath(projectPath, entry))
    .map((entry) => resolveUnityRelativePath(projectPath, entry))
    .filter(Boolean)
    .filter((entry, index, entries) => entries.indexOf(entry) === index);
}

function getUnityEditorBuildSettingsPath(projectPath) {
  return path.join(resolveProjectPath(projectPath), "ProjectSettings", "EditorBuildSettings.asset");
}

function unquoteUnityYamlValue(value) {
  const trimmed = String(value || "").trim();
  if (trimmed.length >= 2) {
    const first = trimmed[0];
    const last = trimmed[trimmed.length - 1];
    if ((first === '"' && last === '"') || (first === "'" && last === "'")) {
      return trimmed.slice(1, -1);
    }
  }
  return trimmed;
}

function getUnityEditorBuildSettingsScenes(projectPath) {
  const settingsPath = getUnityEditorBuildSettingsPath(projectPath);
  if (!pathExists(settingsPath)) {
    return {
      Path: settingsPath,
      Exists: false,
      Scenes: [],
      EnabledScenes: [],
      Error: null,
    };
  }

  let lines;
  try {
    lines = fs.readFileSync(settingsPath, "utf8").split(/\r?\n/);
  } catch (error) {
    return {
      Path: settingsPath,
      Exists: true,
      Scenes: [],
      EnabledScenes: [],
      Error: error.message,
    };
  }

  let inScenes = false;
  let currentScene = null;
  const scenes = [];
  for (const line of lines) {
    if (!inScenes) {
      if (/^\s*m_Scenes:\s*$/.test(line)) {
        inScenes = true;
      }
      continue;
    }

    if (/^[A-Za-z]/.test(line) && !/^\s*m_Scenes:\s*$/.test(line)) {
      break;
    }

    const dashEnabledMatch = /^\s*-\s*enabled:\s*([01])\s*$/.exec(line);
    if (dashEnabledMatch) {
      if (currentScene?.Path) {
        scenes.push(currentScene);
      }
      currentScene = {
        Enabled: dashEnabledMatch[1] === "1",
        Path: null,
      };
      continue;
    }

    const enabledMatch = /^\s*enabled:\s*([01])\s*$/.exec(line);
    if (enabledMatch) {
      if (!currentScene) {
        currentScene = { Enabled: false, Path: null };
      }
      currentScene.Enabled = enabledMatch[1] === "1";
      continue;
    }

    const pathMatch = /^\s*path:\s*(.*?)\s*$/.exec(line);
    if (pathMatch) {
      if (!currentScene) {
        currentScene = { Enabled: false, Path: null };
      }
      currentScene.Path = unquoteUnityYamlValue(pathMatch[1]).replace(/\\/g, "/");
    }
  }

  if (currentScene?.Path) {
    scenes.push(currentScene);
  }

  return {
    Path: settingsPath,
    Exists: true,
    Scenes: scenes,
    EnabledScenes: scenes.filter((scene) => scene.Enabled).map((scene) => scene.Path),
    Error: null,
  };
}

function testUnityBuildSceneList(projectPath, expectedScenes = []) {
  const projectRoot = resolveProjectPath(projectPath);
  const settings = getUnityEditorBuildSettingsScenes(projectRoot);
  const expectedOrdered = expectedScenes
    .map((scene) => String(scene || "").replace(/\\/g, "/").trim())
    .filter(Boolean);
  const enabledScenes = settings.EnabledScenes.map((scene) => String(scene || "").replace(/\\/g, "/").trim());
  const missingScenes = expectedOrdered.filter((scene) => !enabledScenes.includes(scene));
  const unexpectedEnabledScenes = enabledScenes.filter((scene) => !expectedOrdered.includes(scene));
  const sameMembership =
    missingScenes.length === 0 &&
    unexpectedEnabledScenes.length === 0 &&
    expectedOrdered.length === enabledScenes.length;
  const orderDifferences = [];
  if (sameMembership) {
    for (let index = 0; index < expectedOrdered.length; index += 1) {
      if (expectedOrdered[index] !== enabledScenes[index]) {
        orderDifferences.push({
          Index: index,
          Expected: expectedOrdered[index],
          Actual: enabledScenes[index],
        });
      }
    }
  }
  const orderMismatch = orderDifferences.length > 0;
  const exactMatch = sameMembership && !orderMismatch;

  return {
    success: true,
    projectPath: projectRoot,
    editorBuildSettingsPath: settings.Path,
    expectedScenes: expectedOrdered,
    enabledScenes,
    missingScenes,
    unexpectedEnabledScenes,
    orderMismatch,
    orderDifferences,
    exactMatch,
    buildSettingsReadError: settings.Error,
    message: exactMatch
      ? "Enabled build scenes exactly match the expected list."
      : "Enabled build scenes do not exactly match the expected list.",
  };
}

function testUnityExpectedReloadError(message) {
  if (!message) {
    return false;
  }
  const normalized = String(message).toLowerCase();
  return (
    normalized.includes("transport closed") ||
    normalized.includes("connection closed") ||
    normalized.includes("session reset") ||
    normalized.includes("disconnected") ||
    normalized.includes("timeout")
  );
}

function getUnityEditorStatusBeaconPath(projectPath) {
  return path.join(resolveProjectPath(projectPath), "Temp", "UnityEditorStatus", "status.json");
}

function getUnityEditorStatusBeacon(projectPath, includeRaw = false) {
  const beaconPath = getUnityEditorStatusBeaconPath(projectPath);
  const state = {
    Path: beaconPath,
    Exists: false,
    Fresh: false,
    Stale: false,
    Classification: "BeaconMissing",
    Phase: null,
    Substate: null,
    UpdatedAtUtc: null,
    LastTransitionAtUtc: null,
    LastTransitionReason: null,
    AgeSeconds: null,
    FreshnessWindowSeconds: null,
    IsStablePhase: false,
    Error: null,
    Raw: null,
  };

  if (!pathExists(beaconPath)) {
    return state;
  }

  const raw = readJsonFile(beaconPath);
  if (!raw) {
    return {
      ...state,
      Exists: true,
      Stale: true,
      Classification: "BeaconStale",
      Error: "Failed to read editor status beacon.",
    };
  }

  const phase = String(raw.phase || raw.Phase || "");
  const updatedAtUtc = raw.updatedAtUtc || raw.UpdatedAtUtc || null;
  const stablePhases = new Set(["Idle", "Playing", "Paused"]);
  const freshnessWindowSeconds = stablePhases.has(phase) ? 4 : 2;
  const updatedAtMs = updatedAtUtc ? new Date(updatedAtUtc).getTime() : NaN;
  const ageSeconds = Number.isFinite(updatedAtMs) ? Math.round(((Date.now() - updatedAtMs) / 1000) * 1000) / 1000 : null;

  const next = {
    ...state,
    Exists: true,
    Phase: phase || null,
    Substate: raw.substate || raw.Substate || null,
    UpdatedAtUtc: updatedAtUtc,
    LastTransitionAtUtc: raw.lastTransitionAtUtc || raw.LastTransitionAtUtc || null,
    LastTransitionReason: raw.lastTransitionReason || raw.LastTransitionReason || null,
    IsStablePhase: stablePhases.has(phase),
    FreshnessWindowSeconds: freshnessWindowSeconds,
    Raw: includeRaw ? raw : null,
  };

  if (!Number.isFinite(updatedAtMs)) {
    return { ...next, Stale: true, Classification: "BeaconStale", Error: "updatedAtUtc is missing or invalid." };
  }
  if (ageSeconds > freshnessWindowSeconds) {
    return { ...next, AgeSeconds: ageSeconds, Stale: true, Classification: "BeaconStale" };
  }

  let classification = "BeaconTransitioning";
  if (phase === "Idle") {
    classification = "BeaconIdle";
  } else if (phase === "Playing" || phase === "Paused") {
    classification = "BeaconPlaying";
  } else if (phase === "Building") {
    classification = "BeaconBuilding";
  }

  return { ...next, AgeSeconds: ageSeconds, Fresh: true, Classification: classification };
}

function testUnityEditorBeaconTransitionSnapshot(snapshot) {
  if (!snapshot) {
    return false;
  }
  const phases = new Set(["Starting", "Importing", "Compiling", "ReloadingAssemblies", "EnteringPlayMode", "ExitingPlayMode"]);
  return phases.has(snapshot.Phase) || (snapshot.Fresh && snapshot.Classification === "BeaconTransitioning");
}

function testUnityEditorBeaconBuildSnapshot(snapshot) {
  return !!snapshot && (snapshot.Phase === "Building" || (snapshot.Fresh && snapshot.Classification === "BeaconBuilding"));
}

async function waitUnityEditorBeaconStable(projectPath, timeoutSeconds = 6, pollIntervalSeconds = 0.25) {
  const deadline = Date.now() + Math.max(0, timeoutSeconds) * 1000;
  const attempts = [];
  const stableClassifications = new Set(["BeaconIdle", "BeaconPlaying"]);
  const transitionPhases = new Set(["Starting", "Importing", "Compiling", "ReloadingAssemblies", "EnteringPlayMode", "ExitingPlayMode", "Building"]);
  let lastSnapshot = null;

  do {
    const snapshot = getUnityEditorStatusBeacon(projectPath, true);
    lastSnapshot = snapshot;
    attempts.push({
      Timestamp: nowIso(),
      Classification: snapshot.Classification,
      Phase: snapshot.Phase,
      Fresh: snapshot.Fresh,
      Stale: snapshot.Stale,
      Exists: snapshot.Exists,
      Error: snapshot.Error,
    });

    if (snapshot.Fresh && stableClassifications.has(snapshot.Classification)) {
      return {
        success: true,
        timedOut: false,
        classification: snapshot.Classification,
        phase: snapshot.Phase,
        message: "Editor status beacon reached a stable phase.",
        attempts,
        lastSnapshot,
        source: "EditorStatusBeacon.WaitStable",
      };
    }

    const staleTransition = snapshot.Classification === "BeaconStale" && transitionPhases.has(snapshot.Phase);
    if (!staleTransition && ["BeaconMissing", "BeaconStale"].includes(snapshot.Classification)) {
      return {
        success: false,
        timedOut: false,
        classification: snapshot.Classification,
        phase: snapshot.Phase,
        message: "Editor status beacon is unavailable or stale.",
        attempts,
        lastSnapshot,
        source: "EditorStatusBeacon.WaitStable",
      };
    }

    if (Date.now() >= deadline) {
      break;
    }
    await sleep(Math.min(1000, Math.max(10, pollIntervalSeconds * 1000)));
  } while (Date.now() < deadline);

  return {
    success: false,
    timedOut: true,
    classification: lastSnapshot?.Classification || null,
    phase: lastSnapshot?.Phase || null,
    message: "Editor status beacon did not reach a stable phase before timeout.",
    attempts,
    lastSnapshot,
    source: "EditorStatusBeacon.WaitStable",
  };
}

async function waitUnityEditorBeaconTransition(projectPath, timeoutSeconds = 6, pollIntervalSeconds = 0.25) {
  const deadline = Date.now() + Math.max(0, timeoutSeconds) * 1000;
  const attempts = [];
  const transitionClassifications = new Set(["BeaconTransitioning", "BeaconBuilding"]);
  const transitionPhases = new Set(["Starting", "Importing", "Compiling", "ReloadingAssemblies", "EnteringPlayMode", "ExitingPlayMode", "Building"]);
  let lastSnapshot = null;

  do {
    const snapshot = getUnityEditorStatusBeacon(projectPath, true);
    lastSnapshot = snapshot;
    attempts.push({
      Timestamp: nowIso(),
      Classification: snapshot.Classification,
      Phase: snapshot.Phase,
      Fresh: snapshot.Fresh,
      Stale: snapshot.Stale,
      Exists: snapshot.Exists,
      Error: snapshot.Error,
    });

    if (snapshot.Fresh && transitionClassifications.has(snapshot.Classification)) {
      return {
        success: true,
        timedOut: false,
        classification: snapshot.Classification,
        phase: snapshot.Phase,
        message: "Editor status beacon observed an active transition.",
        attempts,
        lastSnapshot,
        source: "EditorStatusBeacon.WaitTransition",
      };
    }
    if (snapshot.Classification === "BeaconStale" && transitionPhases.has(snapshot.Phase)) {
      return {
        success: true,
        timedOut: false,
        classification: snapshot.Classification,
        phase: snapshot.Phase,
        message: "Editor status beacon last-known phase still indicates an active transition.",
        attempts,
        lastSnapshot,
        source: "EditorStatusBeacon.WaitTransition",
      };
    }
    if (["BeaconMissing", "BeaconStale"].includes(snapshot.Classification)) {
      return {
        success: false,
        timedOut: false,
        classification: snapshot.Classification,
        phase: snapshot.Phase,
        message: "Editor status beacon is unavailable or stale.",
        attempts,
        lastSnapshot,
        source: "EditorStatusBeacon.WaitTransition",
      };
    }

    if (Date.now() >= deadline) {
      break;
    }
    await sleep(Math.min(1000, Math.max(10, pollIntervalSeconds * 1000)));
  } while (Date.now() < deadline);

  return {
    success: false,
    timedOut: true,
    classification: lastSnapshot?.Classification || null,
    phase: lastSnapshot?.Phase || null,
    message: "Editor status beacon did not report a transition before timeout.",
    attempts,
    lastSnapshot,
    source: "EditorStatusBeacon.WaitTransition",
  };
}

function getUnityReadinessSnapshot(editorState) {
  const data = valueOf(editorState, "data", "Data") || {};
  const probe = valueOf(data, "RuntimeProbe", "runtimeProbe") || null;
  const success = valueOf(editorState, "success", "Success") === true;
  const isCompiling = success && boolOf(data, "IsCompiling", "isCompiling");
  const isUpdating = success && boolOf(data, "IsUpdating", "isUpdating");
  const isPlaying = success && boolOf(data, "IsPlaying", "isPlaying");
  const probeAvailable = !!probe && boolOf(probe, "IsAvailable", "isAvailable");
  const probeAdvanced = !!probe && boolOf(probe, "HasAdvancedFrames", "hasAdvancedFrames");
  const updateCount = Number(valueOf(probe, "UpdateCount", "updateCount") || 0);
  const fixedUpdateCount = Number(valueOf(probe, "FixedUpdateCount", "fixedUpdateCount") || 0);
  const unscaledTime = Number(valueOf(probe, "UnscaledTime", "unscaledTime") || 0);

  return {
    Timestamp: nowIso(),
    Success: success,
    IsCompiling: isCompiling,
    IsUpdating: isUpdating,
    IsPlaying: isPlaying,
    RuntimeProbeAvailable: probeAvailable,
    RuntimeProbeHasAdvancedFrames: probeAdvanced,
    RuntimeProbeUpdateCount: updateCount,
    RuntimeProbeFixedUpdateCount: fixedUpdateCount,
    RuntimeProbeUnscaledTime: unscaledTime,
    IdleReady: success && !isCompiling && !isUpdating,
    PlayReadyByCount: success && isPlaying && probeAvailable && probeAdvanced && updateCount >= 10,
    RuntimeProbe: probe,
  };
}

async function waitUnityEditorIdle(projectPath, options = {}) {
  const timeoutSeconds = options.timeoutSeconds ?? 60;
  const stablePollCount = options.stablePollCount ?? 3;
  const pollIntervalSeconds = options.pollIntervalSeconds ?? 0.5;
  const postIdleDelaySeconds = options.postIdleDelaySeconds ?? 1.0;
  const clearExpectedReloadOnSuccess = options.clearExpectedReloadOnSuccess === true;
  const startedAt = Date.now();
  let beaconWait = null;
  const initialBeacon = getUnityEditorStatusBeacon(projectPath, true);

  if (testUnityEditorBeaconTransitionSnapshot(initialBeacon) || testUnityEditorBeaconBuildSnapshot(initialBeacon)) {
    beaconWait = await waitUnityEditorBeaconStable(projectPath, timeoutSeconds, Math.min(pollIntervalSeconds, 0.25));
    if (beaconWait.timedOut) {
      return {
        success: false,
        message: beaconWait.message,
        timeoutSeconds,
        pollIntervalSeconds,
        stablePollCountRequired: stablePollCount,
        stablePollCountReached: 0,
        postIdleDelaySeconds,
        expectedReloadFailureCount: 0,
        attempts: beaconWait.attempts,
        lastState: null,
        lastError: null,
        source: beaconWait.source,
        beaconWait,
      };
    }
  }

  try {
    const elapsedSeconds = Math.max(0, (Date.now() - startedAt) / 1000);
    const remainingTimeoutSeconds = Math.max(1, Math.ceil(timeoutSeconds - elapsedSeconds));
    const response = await invokeUnityMcpToolJson(
      projectPath,
      "Unity_ManageEditor",
      {
        Action: "WaitForStableEditor",
        WaitForCompletion: true,
        TimeoutMs: Math.max(1000, remainingTimeoutSeconds * 1000),
        PollIntervalMs: Math.max(100, Math.round(pollIntervalSeconds * 1000)),
        StablePollCount: stablePollCount,
        PostStableDelayMs: Math.max(0, Math.round(postIdleDelaySeconds * 1000)),
      },
      { timeoutSeconds: Math.max(15, remainingTimeoutSeconds + 10) }
    );
    const toolResult = getToolObject(response);
    const errorText = `${toolResult?.code || ""} ${toolResult?.error || ""} ${toolResult?.message || ""}`;
    const unsupported =
      toolResult?.success === false &&
      (errorText.toLowerCase().includes("unknown action") || errorText.toLowerCase().includes("supported actions include"));
    if (!unsupported && toolResult) {
      if (toolResult.success === true && clearExpectedReloadOnSuccess) {
        clearUnityExpectedReloadState(projectPath);
      }
      const toolData = valueOf(toolResult, "data", "Data") || {};
      return {
        success: toolResult.success === true,
        message: toolResult.message || toolResult.error || "Unity editor wait completed.",
        timeoutSeconds: remainingTimeoutSeconds,
        pollIntervalSeconds,
        stablePollCountRequired: stablePollCount,
        stablePollCountReached: valueOf(toolData, "StablePollCountReached", "stablePollCountReached") ?? null,
        postIdleDelaySeconds,
        expectedReloadFailureCount: 0,
        attempts: valueOf(toolData, "Attempts", "attempts") || [],
        lastState: valueOf(toolData, "EditorState", "editorState")
          ? { success: toolResult.success === true, data: valueOf(toolData, "EditorState", "editorState") }
          : null,
        lastError: toolResult.success === true ? null : toolResult.error,
        source: beaconWait ? "EditorStatusBeacon.WaitStable+Unity_ManageEditor.WaitForStableEditor" : "Unity_ManageEditor.WaitForStableEditor",
        toolResult,
        beaconWait,
      };
    }
  } catch (_error) {
  }

  const deadline = Date.now() + Math.max(1, timeoutSeconds) * 1000;
  const attempts = [];
  let stablePolls = 0;
  let lastState = null;
  let lastError = null;
  let expectedReloadFailureCount = 0;

  while (Date.now() < deadline) {
    try {
      const state = await getUnityCompactEditorState(projectPath, 20);
      lastState = state;
      const snapshot = getUnityReadinessSnapshot(state);
      const reloadState = getUnityExpectedReloadState(projectPath);
      snapshot.ExpectedReloadActive = reloadState.IsActive;
      snapshot.ExpectedReloadReason = reloadState.Reason;
      if (snapshot.Success !== true && reloadState.IsActive) {
        expectedReloadFailureCount += 1;
      }
      attempts.push(snapshot);
      if (snapshot.IdleReady) {
        stablePolls += 1;
        if (stablePolls >= stablePollCount) {
          if (postIdleDelaySeconds > 0) {
            await sleep(postIdleDelaySeconds * 1000);
          }
          if (clearExpectedReloadOnSuccess) {
            clearUnityExpectedReloadState(projectPath);
          }
          return {
            success: true,
            message: "Unity editor reached a stable idle state.",
            timeoutSeconds,
            pollIntervalSeconds,
            stablePollCountRequired: stablePollCount,
            stablePollCountReached: stablePolls,
            postIdleDelaySeconds,
            expectedReloadFailureCount,
            attempts,
            lastState,
            beaconWait,
            source: beaconWait ? "EditorStatusBeacon.WaitStable+Unity_ManageEditor.GetCompactState" : "Unity_ManageEditor.GetCompactState",
          };
        }
      } else {
        stablePolls = 0;
      }
    } catch (error) {
      lastError = error.message;
      stablePolls = 0;
      const reloadState = getUnityExpectedReloadState(projectPath);
      const expectedReloadError = reloadState.IsActive && testUnityExpectedReloadError(lastError);
      if (expectedReloadError) {
        expectedReloadFailureCount += 1;
      }
      attempts.push({
        Timestamp: nowIso(),
        Success: false,
        Error: lastError,
        ExpectedReloadActive: reloadState.IsActive,
        ExpectedReloadReason: reloadState.Reason,
        ExpectedReloadError: expectedReloadError,
      });
    }

    await sleep(pollIntervalSeconds * 1000);
  }

  return {
    success: false,
    message: "Unity editor did not reach a stable idle state before timeout.",
    timeoutSeconds,
    pollIntervalSeconds,
    stablePollCountRequired: stablePollCount,
    stablePollCountReached: stablePolls,
    postIdleDelaySeconds,
    expectedReloadFailureCount,
    attempts,
    lastState,
    lastError,
    beaconWait,
    source: beaconWait ? "EditorStatusBeacon.WaitStable+Unity_ManageEditor.GetCompactState" : "Unity_ManageEditor.GetCompactState",
  };
}

async function waitUnityCompileOrUpdateStart(projectPath, options = {}) {
  const timeoutSeconds = options.timeoutSeconds ?? 6;
  const pollIntervalSeconds = options.pollIntervalSeconds ?? 0.5;
  const deadline = Date.now() + timeoutSeconds * 1000;
  const attempts = [];
  let lastState = null;
  let lastError = null;
  let transientExpectedReloadFailures = 0;

  while (Date.now() < deadline) {
    try {
      const state = await getUnityCompactEditorState(projectPath, 15);
      lastState = state;
      const snapshot = getUnityReadinessSnapshot(state);
      const reloadState = getUnityExpectedReloadState(projectPath);
      snapshot.ExpectedReloadActive = reloadState.IsActive;
      snapshot.ExpectedReloadReason = reloadState.Reason;
      if (snapshot.Success !== true && reloadState.IsActive) {
        transientExpectedReloadFailures += 1;
      }
      attempts.push(snapshot);
      if (snapshot.IsCompiling || snapshot.IsUpdating) {
        return {
          success: true,
          started: true,
          likelyStartedByTransientFailure: false,
          transientExpectedReloadFailures,
          timeoutSeconds,
          pollIntervalSeconds,
          attempts,
          lastState,
          lastError,
          message: "Unity compile or update started.",
        };
      }
    } catch (error) {
      lastError = error.message;
      const reloadState = getUnityExpectedReloadState(projectPath);
      const expectedReloadError = reloadState.IsActive && testUnityExpectedReloadError(lastError);
      if (expectedReloadError) {
        transientExpectedReloadFailures += 1;
      }
      attempts.push({
        Timestamp: nowIso(),
        Success: false,
        Error: lastError,
        ExpectedReloadActive: reloadState.IsActive,
        ExpectedReloadReason: reloadState.Reason,
        ExpectedReloadError: expectedReloadError,
      });
    }

    await sleep(pollIntervalSeconds * 1000);
  }

  return {
    success: false,
    started: false,
    likelyStartedByTransientFailure: transientExpectedReloadFailures > 0,
    transientExpectedReloadFailures,
    timeoutSeconds,
    pollIntervalSeconds,
    attempts,
    lastState,
    lastError,
    message: "Unity compile or update did not start before timeout.",
  };
}

async function waitUnityCompileReloadCycle(projectPath, options = {}) {
  const startTimeoutSeconds = options.startTimeoutSeconds ?? 6;
  const timeoutSeconds = options.timeoutSeconds ?? 120;
  const stablePollCount = options.stablePollCount ?? 3;
  const pollIntervalSeconds = options.pollIntervalSeconds ?? 0.5;
  const postIdleDelaySeconds = options.postIdleDelaySeconds ?? 1.0;
  const beaconStartWaitTimeoutSeconds = Math.min(startTimeoutSeconds, 2);
  const beaconStartWait = await waitUnityEditorBeaconTransition(projectPath, beaconStartWaitTimeoutSeconds, Math.min(pollIntervalSeconds, 0.25));

  let startWait;
  let shouldWaitForIdle;
  if (beaconStartWait.success) {
    shouldWaitForIdle = true;
    startWait = {
      success: true,
      started: true,
      likelyStartedByTransientFailure: false,
      transientExpectedReloadFailures: 0,
      timeoutSeconds: beaconStartWaitTimeoutSeconds,
      pollIntervalSeconds,
      attempts: beaconStartWait.attempts,
      lastState: null,
      lastError: null,
      message: "Unity transition observed from the editor status beacon.",
      source: beaconStartWait.source,
    };
  } else {
    startWait = await waitUnityCompileOrUpdateStart(projectPath, {
      timeoutSeconds: startTimeoutSeconds,
      pollIntervalSeconds,
    });
    shouldWaitForIdle = startWait.started || startWait.likelyStartedByTransientFailure;
  }

  const idleWait = shouldWaitForIdle
    ? await waitUnityEditorIdle(projectPath, {
        timeoutSeconds,
        stablePollCount,
        pollIntervalSeconds,
        postIdleDelaySeconds,
        clearExpectedReloadOnSuccess: options.clearExpectedReloadOnSuccess === true,
      })
    : null;

  let transientFailureCount = Number(startWait.transientExpectedReloadFailures || 0);
  if (idleWait?.expectedReloadFailureCount != null) {
    transientFailureCount += Number(idleWait.expectedReloadFailureCount);
  }

  return {
    success: !!(shouldWaitForIdle && idleWait?.success),
    message: shouldWaitForIdle ? (idleWait?.success ? "Unity compile/reload cycle settled back to idle." : idleWait?.message) : startWait.message,
    compileObserved: startWait.started,
    likelyStartedByTransientFailure: startWait.likelyStartedByTransientFailure,
    transientExpectedReloadFailures: transientFailureCount,
    beaconStartWait,
    startWait,
    idleWait,
  };
}

async function waitUnityPlayReady(projectPath, options = {}) {
  const timeoutSeconds = options.timeoutSeconds ?? 25;
  const pollIntervalSeconds = options.pollIntervalSeconds ?? 1.0;
  const warmupSeconds = options.warmupSeconds ?? 1.0;
  const deadline = Date.now() + timeoutSeconds * 1000;
  const attempts = [];
  let lastState = null;
  let lastError = null;
  let previousUnscaledTime = null;

  while (Date.now() < deadline) {
    try {
      const beaconSnapshot = getUnityEditorStatusBeacon(projectPath, true);
      if (testUnityEditorBeaconTransitionSnapshot(beaconSnapshot) || testUnityEditorBeaconBuildSnapshot(beaconSnapshot)) {
        const remaining = Math.max(1, Math.ceil((deadline - Date.now()) / 1000));
        const beaconWait = await waitUnityEditorBeaconStable(projectPath, Math.min(remaining, 2), Math.min(pollIntervalSeconds, 0.25));
        attempts.push({
          Timestamp: nowIso(),
          Success: beaconWait.success,
          SkippedDirectProbe: true,
          BeaconWait: beaconWait,
        });
        if (beaconWait.timedOut && Date.now() >= deadline) {
          break;
        }
        continue;
      }

      const state = await getUnityCompactEditorState(projectPath, 15);
      lastState = state;
      const snapshot = getUnityReadinessSnapshot(state);
      const timeAdvanced = previousUnscaledTime != null && snapshot.RuntimeProbeUnscaledTime > previousUnscaledTime;
      snapshot.PlayReady =
        snapshot.Success &&
        snapshot.IsPlaying &&
        snapshot.RuntimeProbeAvailable &&
        snapshot.RuntimeProbeHasAdvancedFrames &&
        (snapshot.RuntimeProbeUpdateCount >= 10 || timeAdvanced);
      snapshot.RuntimeAdvancedByTime = timeAdvanced;
      attempts.push(snapshot);

      if (snapshot.PlayReady) {
        if (warmupSeconds > 0) {
          await sleep(warmupSeconds * 1000);
        }
        return {
          success: true,
          message: "Play mode entered and runtime reached a settled advancing state.",
          timeoutSeconds,
          pollIntervalSeconds,
          warmupSeconds,
          attempts,
          lastState,
        };
      }

      previousUnscaledTime = snapshot.RuntimeProbeUnscaledTime;
    } catch (error) {
      lastError = error.message;
      attempts.push({
        Timestamp: nowIso(),
        Success: false,
        Error: lastError,
      });
    }

    await sleep(pollIntervalSeconds * 1000);
  }

  return {
    success: false,
    message: "Play mode did not reach a settled advancing runtime state before timeout.",
    timeoutSeconds,
    pollIntervalSeconds,
    warmupSeconds,
    attempts,
    lastState,
    lastError,
  };
}

function getUnityEditorLogPath() {
  if (process.platform === "darwin") {
    return path.join(os.homedir(), "Library", "Logs", "Unity", "Editor.log");
  }
  if (process.platform === "win32") {
    return path.join(process.env.LOCALAPPDATA || path.join(os.homedir(), "AppData", "Local"), "Unity", "Editor", "Editor.log");
  }
  return path.join(os.homedir(), ".config", "unity3d", "Editor.log");
}

function tailFile(filePath, lineCount = 400) {
  if (!pathExists(filePath)) {
    return [];
  }
  try {
    return fs.readFileSync(filePath, "utf8").split(/\r?\n/).slice(-lineCount);
  } catch (_error) {
    return [];
  }
}

function findSignalMatch(lines, patterns) {
  let lastMatch = null;
  lines.forEach((line, lineIndex) => {
    for (const pattern of patterns) {
      if (line.includes(pattern)) {
        lastMatch = { Pattern: pattern, LineIndex: lineIndex };
      }
    }
  });
  return lastMatch;
}

function findRegexSignalMatch(lines, patterns) {
  let lastMatch = null;
  lines.forEach((line, lineIndex) => {
    for (const pattern of patterns) {
      if (new RegExp(pattern).test(line)) {
        lastMatch = { Pattern: pattern, LineIndex: lineIndex, Line: line };
      }
    }
  });
  return lastMatch;
}

function getWebGlBuildProgressState(lines) {
  const activeMatch = findRegexSignalMatch(lines, ["Link_WebGL_wasm", "C_WebGL_wasm", "\\[\\d+/\\d+\\].*(WebGL|wasm)"]);
  const successMatch = findRegexSignalMatch(lines, ["Build completed with a result of 'Succeeded'", "^\\s*Result\\s*:\\s*Succeeded\\s*$"]);
  const failureMatch = findRegexSignalMatch(lines, [
    "Build completed with a result of 'Failed'",
    "Build completed with a result of 'Cancelled'",
    "BuildPlayerWindow\\+BuildMethodException",
    "^\\s*Result\\s*:\\s*(Failed|Cancelled)\\s*$",
  ]);

  if (successMatch && (!failureMatch || successMatch.LineIndex >= failureMatch.LineIndex)) {
    return {
      Status: "Succeeded",
      Summary: "Editor.log reports a completed successful WebGL build.",
      ActiveSignal: activeMatch,
      TerminalSignal: successMatch,
    };
  }
  if (failureMatch && (!successMatch || failureMatch.LineIndex > successMatch.LineIndex)) {
    return {
      Status: "Failed",
      Summary: "Editor.log reports a failed WebGL build.",
      ActiveSignal: activeMatch,
      TerminalSignal: failureMatch,
    };
  }
  if (activeMatch) {
    return {
      Status: "InProgress",
      Summary: "Editor.log still shows active WebGL Bee/wasm progress with no later terminal build marker.",
      ActiveSignal: activeMatch,
      TerminalSignal: null,
    };
  }
  return {
    Status: "Idle",
    Summary: "No active WebGL build markers were detected in the latest Unity log tail.",
    ActiveSignal: null,
    TerminalSignal: null,
  };
}

function getCodexUnityMcpConfig() {
  const configPath = path.join(os.homedir(), ".codex", "config.toml");
  if (!pathExists(configPath)) {
    return null;
  }

  const lines = fs.readFileSync(configPath, "utf8").split(/\r?\n/);
  let inUnitySection = false;
  let command = null;
  let argsLine = null;
  for (const rawLine of lines) {
    const trimmed = rawLine.trim();
    if (/^\[mcp_servers\."unity-mcp"\]$/.test(trimmed)) {
      inUnitySection = true;
      continue;
    }
    if (inUnitySection && trimmed.startsWith("[")) {
      break;
    }
    if (!inUnitySection) {
      continue;
    }
    const commandMatch = /^command\s*=\s*["'](.+?)["']$/.exec(trimmed);
    if (commandMatch) {
      command = commandMatch[1];
      continue;
    }
    const argsMatch = /^args\s*=\s*(\[.+\])$/.exec(trimmed);
    if (argsMatch) {
      argsLine = argsMatch[1];
    }
  }
  if (!inUnitySection) {
    return null;
  }

  const commandLeaf = command ? path.basename(command).toLowerCase() : null;
  const usesWrapper = (commandLeaf === "node" || commandLeaf === "node.exe") && !!argsLine?.includes("unity-mcp-stdio-wrapper.js");
  const usesRawRelay = /relay(_win)?\.exe/.test(command || "") || /--mcp/.test(argsLine || "");
  const usesLensBinary = /unity_mcp_lens_/.test(command || "") || /Launch-UnityMcpLens\.js/.test(argsLine || "");
  return {
    Path: configPath,
    Command: command,
    Args: argsLine,
    UsesWrapper: usesWrapper,
    UsesRawRelay: usesRawRelay,
    UsesLensBinary: usesLensBinary,
  };
}

function getUnityMcpLocalSettings() {
  const settingsPath = path.join(os.homedir(), ".codex", "unity-mcp-settings.json");
  const defaults = {
    Path: settingsPath,
    WrapperMode: "thin",
    AllowManualWrapper: false,
    AllowCachedToolsFallback: false,
    DirectRelayExperimental: false,
    EagerConnectOnInitialize: false,
    ToolsCacheTtlMs: 300000,
    ReloadWaitTimeoutMs: 5000,
    ReloadPollIntervalMs: 400,
  };
  const parsed = readJsonFile(settingsPath);
  return parsed ? { ...defaults, ...parsed } : defaults;
}

function getUnityAssistantPackageState(projectPath) {
  const projectRoot = resolveProjectPath(projectPath);
  const manifestPath = path.join(projectRoot, "Packages", "manifest.json");
  const embeddedPackagePath = path.join(projectRoot, "Packages", "com.unity.ai.assistant", "package.json");
  const manifest = readJsonFile(manifestPath);
  const dependencyValue = manifest?.dependencies?.["com.unity.ai.assistant"] || null;
  let resolvedFileDependencyPath = null;
  if (dependencyValue && String(dependencyValue).toLowerCase().startsWith("file:")) {
    const raw = String(dependencyValue).slice(5);
    const candidate = path.isAbsolute(raw) ? raw : path.join(projectRoot, "Packages", raw);
    resolvedFileDependencyPath = pathExists(candidate) ? fs.realpathSync(candidate) : path.resolve(candidate);
  }

  const embeddedPackageExists = pathExists(embeddedPackagePath);
  const projectNormalized = normalizeForCompare(projectRoot);
  const resolvedNormalized = resolvedFileDependencyPath ? normalizeForCompare(resolvedFileDependencyPath) : null;
  const isEmbeddedPath = !!resolvedNormalized && (resolvedNormalized === projectNormalized || resolvedNormalized.startsWith(`${projectNormalized}/`));
  let mode = "Missing";
  let summary = "Assistant dependency not found.";
  if (embeddedPackageExists) {
    mode = "LocalFolderDependency";
    summary = "Assistant package is embedded in the project Packages folder.";
  } else if (resolvedFileDependencyPath) {
    mode = isEmbeddedPath ? "LocalFolderDependency" : "ExternalPatchSource";
    summary = isEmbeddedPath
      ? "Assistant dependency uses a project-local file path."
      : "Assistant dependency points to an external patch source.";
  } else if (dependencyValue) {
    mode = "RegistryDependency";
    summary = "Assistant dependency comes from the Unity package registry.";
  }

  return {
    ManifestPath: manifestPath,
    ManifestError: manifest ? null : pathExists(manifestPath) ? "Could not parse manifest.json." : null,
    DependencyValue: dependencyValue,
    EmbeddedPackagePath: embeddedPackagePath,
    EmbeddedPackageExists: embeddedPackageExists,
    ResolvedFileDependencyPath: resolvedFileDependencyPath,
    Mode: mode,
    Summary: summary,
  };
}

function getUnityProcesses() {
  if (process.platform === "win32") {
    const result = spawnSync("tasklist", ["/FI", "IMAGENAME eq Unity.exe"], { encoding: "utf8" });
    return result.stdout && result.stdout.includes("Unity.exe") ? [{ name: "Unity" }] : [];
  }

  const result = spawnSync("pgrep", ["-x", "Unity"], { encoding: "utf8" });
  return result.status === 0
    ? result.stdout
        .split(/\r?\n/)
        .filter(Boolean)
        .map((pid) => ({ pid: Number(pid), name: "Unity" }))
    : [];
}

function getBridgeStatusCandidates(projectPath) {
  const normalizedProjectPath = normalizeProjectRootForCompare(projectPath);
  const bridgeDirectory = path.join(os.homedir(), ".unity", "mcp", "connections");
  if (!pathExists(bridgeDirectory)) {
    return [];
  }
  return fs
    .readdirSync(bridgeDirectory)
    .filter((file) => /^bridge-status-.*\.json$/.test(file))
    .map((file) => {
      const filePath = path.join(bridgeDirectory, file);
      const raw = readJsonFile(filePath);
      if (!raw) {
        return null;
      }
      const stat = fs.statSync(filePath);
      const projectRoot = raw.project_root || raw.projectPath || raw.ProjectRoot || raw.project_path || raw.ProjectPath;
      let expectedRecoveryActive = raw.expected_recovery === true;
      let expectedRecoveryExpired = false;
      const expiresRaw = raw.expected_recovery_expires_utc || null;
      if (expectedRecoveryActive && expiresRaw && new Date(expiresRaw).getTime() <= Date.now()) {
        expectedRecoveryActive = false;
        expectedRecoveryExpired = true;
      }
      return {
        FilePath: filePath,
        LastWriteTime: stat.mtime.toISOString(),
        LastWriteTimeMs: stat.mtimeMs,
        Status: raw.status,
        Reason: raw.reason,
        ExpectedRecovery: expectedRecoveryActive,
        ExpectedRecoveryExpiresUtc: expiresRaw,
        ExpectedRecoveryExpired: expectedRecoveryExpired,
        ToolDiscoveryMode: raw.tool_discovery_mode,
        ToolCount: raw.tool_count,
        ToolsHash: raw.tools_hash,
        ToolDiscoveryReason: raw.tool_discovery_reason,
        ToolSnapshotUtc: raw.tool_snapshot_utc,
        CommandHealth: raw.command_health,
        LastCommandSuccessUtc: raw.last_command_success_utc,
        LastCommandFailureUtc: raw.last_command_failure_utc,
        LastCommandFailureReason: raw.last_command_failure_reason,
        ProjectPath: raw.project_path,
        ProjectRoot: raw.project_root,
        ConnectionPath: raw.connection_path,
        LastHeartbeat: raw.last_heartbeat,
        MatchesProject: normalizeProjectRootForCompare(projectRoot) === normalizedProjectPath,
      };
    })
    .filter(Boolean)
    .sort((a, b) => b.LastWriteTimeMs - a.LastWriteTimeMs);
}

async function getUnityLensHealth(projectPath, timeoutSeconds = 8) {
  const response = await invokeUnityMcpToolJson(projectPath, "Unity.GetLensHealth", {}, { timeoutSeconds });
  return getToolObject(response);
}

function getUnityLensHealthReadinessSnapshot(lensHealth) {
  const lensData = valueOf(lensHealth, "data", "Data") || {};
  const bridgeStatus = valueOf(valueOf(lensData, "bridgeStatus", "BridgeStatus") || {}, "status", "Status") || null;
  const toolDiscoveryMode = valueOf(valueOf(lensData, "bridgeStatus", "BridgeStatus") || {}, "toolDiscoveryMode", "ToolDiscoveryMode") || null;
  const toolCount = Number(valueOf(lensData, "internalRegistryToolCount", "InternalRegistryToolCount") || 0);
  const editorStability = valueOf(lensData, "editorStability", "EditorStability") || {};
  const expectedRecovery = valueOf(lensData, "expectedRecovery", "ExpectedRecovery") || {};
  const success = valueOf(lensHealth, "success", "Success") === true;
  const isCompiling = valueOf(editorStability, "isCompiling", "IsCompiling") === true;
  const isUpdating = valueOf(editorStability, "isUpdating", "IsUpdating") === true;
  const isPlayingOrWillChangePlaymode = valueOf(editorStability, "isPlayingOrWillChangePlaymode", "IsPlayingOrWillChangePlaymode") === true;
  const isBuildingPlayer = valueOf(editorStability, "isBuildingPlayer", "IsBuildingPlayer") === true;
  const editorStable = valueOf(editorStability, "isStable", "IsStable") === true;
  const expectedRecoveryActive = valueOf(expectedRecovery, "isActive", "IsActive") === true;

  return {
    Timestamp: nowIso(),
    Success: success,
    IsCompiling: isCompiling,
    IsUpdating: isUpdating,
    IsPlaying: false,
    IsPlayingOrWillChangePlaymode: isPlayingOrWillChangePlaymode,
    IsBuildingPlayer: isBuildingPlayer,
    RuntimeProbeAvailable: false,
    RuntimeProbeHasAdvancedFrames: false,
    RuntimeProbeUpdateCount: 0,
    RuntimeProbeFixedUpdateCount: 0,
    RuntimeProbeUnscaledTime: 0,
    IdleReady: success && bridgeStatus === "ready" && editorStable && !isCompiling && !isUpdating && !isPlayingOrWillChangePlaymode && !isBuildingPlayer && !expectedRecoveryActive,
    PlayReadyByCount: false,
    RuntimeProbe: null,
    LensHealthSuccess: success,
    LensBridgeStatus: bridgeStatus,
    LensEditorStable: editorStable,
    LensExpectedRecoveryActive: expectedRecoveryActive,
    BridgeStatus: bridgeStatus,
    ToolDiscoveryMode: toolDiscoveryMode,
    ToolCount: toolCount,
  };
}

function getUnityCompactStateFromLensHealth(lensHealth) {
  const snapshot = getUnityLensHealthReadinessSnapshot(lensHealth);
  return {
    success: snapshot.Success,
    message: "Derived compact editor readiness from Unity.GetLensHealth.",
    data: {
      IsPlaying: snapshot.IsPlaying,
      IsPaused: false,
      IsCompiling: snapshot.IsCompiling,
      IsUpdating: snapshot.IsUpdating,
      IsPlayingOrWillChangePlaymode: snapshot.IsPlayingOrWillChangePlaymode,
      IsBuildingPlayer: snapshot.IsBuildingPlayer,
      IsEditorIdle: snapshot.IdleReady,
      RuntimeAdvanced: false,
      RuntimeProbe: null,
      ActiveSceneName: null,
      BridgeStatus: snapshot.BridgeStatus,
      BridgeReason: null,
      BridgeExpectedRecovery: snapshot.LensExpectedRecoveryActive,
      ToolDiscoveryMode: snapshot.ToolDiscoveryMode,
      ToolCount: snapshot.ToolCount,
    },
  };
}

function getCompactEditorStateSummary(editorState) {
  if (!editorState || valueOf(editorState, "success", "Success") !== true) {
    return null;
  }
  const data = valueOf(editorState, "data", "Data") || {};
  const probe = valueOf(data, "RuntimeProbe", "runtimeProbe") || null;
  return {
    IsPlaying: boolOf(data, "IsPlaying", "isPlaying"),
    IsCompiling: boolOf(data, "IsCompiling", "isCompiling"),
    IsUpdating: boolOf(data, "IsUpdating", "isUpdating"),
    BridgeStatus: valueOf(data, "BridgeStatus", "bridgeStatus") || null,
    ToolDiscoveryMode: valueOf(data, "ToolDiscoveryMode", "toolDiscoveryMode") || null,
    ActiveSceneName: valueOf(probe, "ActiveSceneName", "activeSceneName") || null,
    RuntimeProbeReady: !!probe && boolOf(probe, "IsAvailable", "isAvailable") && boolOf(probe, "HasAdvancedFrames", "hasAdvancedFrames"),
    RuntimeUpdateCount: Number(valueOf(probe, "UpdateCount", "updateCount") || 0),
  };
}

async function testUnityDirectEditorHealthy(projectPath, options = {}) {
  const timeoutSeconds = options.timeoutSeconds ?? 20;
  const consecutiveHealthyPolls = options.consecutiveHealthyPolls ?? 2;
  const pollIntervalSeconds = options.pollIntervalSeconds ?? 0.5;
  const deadline = Date.now() + Math.max(1, timeoutSeconds) * 1000;
  const attempts = [];
  let consecutiveHealthyObserved = 0;
  let lastState = null;
  let lastLensHealth = null;
  let lastError = null;

  await resetUnityMcpSession(projectPath);

  while (Date.now() < deadline) {
    try {
      const lensHealth = await getUnityLensHealth(projectPath, Math.max(8, Math.ceil(pollIntervalSeconds * 4)));
      lastLensHealth = lensHealth;
      lastState = getUnityCompactStateFromLensHealth(lensHealth);

      const snapshot = getUnityLensHealthReadinessSnapshot(lensHealth);
      const healthy = snapshot.IdleReady === true;
      attempts.push(snapshot);

      if (healthy) {
        consecutiveHealthyObserved += 1;
        if (consecutiveHealthyObserved >= consecutiveHealthyPolls) {
          return {
            success: true,
            message: "Lens health probes are healthy and the Unity editor is idle.",
            timeoutSeconds,
            pollIntervalSeconds,
            consecutiveHealthyPollsRequired: consecutiveHealthyPolls,
            consecutiveHealthyObserved,
            attempts,
            lastState,
            lastLensHealth,
            lastError,
          };
        }
      } else {
        consecutiveHealthyObserved = 0;
      }
    } catch (error) {
      lastError = error.message;
      consecutiveHealthyObserved = 0;
      attempts.push({
        Timestamp: nowIso(),
        Success: false,
        Error: lastError,
      });
    }

    await sleep(pollIntervalSeconds * 1000);
  }

  return {
    success: false,
    message: "Lens helper recovery probes did not reach a stable idle state before timeout.",
    timeoutSeconds,
    pollIntervalSeconds,
    consecutiveHealthyPollsRequired: consecutiveHealthyPolls,
    consecutiveHealthyObserved,
    attempts,
    lastState,
    lastLensHealth,
    lastError,
  };
}

async function checkUnityMcp(projectPath, options = {}) {
  const projectRoot = resolveProjectPath(projectPath);
  const normalizedProjectPath = normalizeForCompare(projectRoot);
  const lensBinary = getLensBinaryState();
  const codexConfig = getCodexUnityMcpConfig();
  const unityMcpSettings = getUnityMcpLocalSettings();
  const assistantPackageState = getUnityAssistantPackageState(projectRoot);
  const unityProcesses = getUnityProcesses();
  const unityRunning = unityProcesses.length > 0;
  const expectedReloadState = getUnityExpectedReloadState(projectRoot);
  let editorStatusBeacon = getUnityEditorStatusBeacon(projectRoot, true);
  let beaconWait = null;
  if (unityRunning && (testUnityEditorBeaconTransitionSnapshot(editorStatusBeacon) || testUnityEditorBeaconBuildSnapshot(editorStatusBeacon))) {
    beaconWait = await waitUnityEditorBeaconStable(projectRoot, 2, 0.25);
    if (beaconWait.lastSnapshot) {
      editorStatusBeacon = beaconWait.lastSnapshot;
    }
  }

  const beaconIndicatesTransition = testUnityEditorBeaconTransitionSnapshot(editorStatusBeacon) || (beaconWait && testUnityEditorBeaconTransitionSnapshot(beaconWait.lastSnapshot));
  const beaconIndicatesBuild = testUnityEditorBeaconBuildSnapshot(editorStatusBeacon) || (beaconWait && testUnityEditorBeaconBuildSnapshot(beaconWait.lastSnapshot));
  const editorLogPath = getUnityEditorLogPath();
  const editorLogTailLines = tailFile(editorLogPath, options.editorLogTail ?? 400);
  const webGlBuildState = getWebGlBuildProgressState(editorLogTailLines);

  const signals = {
    ApprovalPending: ["Awaiting user approval", "approval_pending", "Validation: Pending"],
    HandshakeFailed: ["Handshake failed", "Connection closed during write"],
    Disconnected: ["disconnected", "Connection closed"],
    ReadDisconnect: ["Connection closed during read"],
    DisposedTransport: ["Failed to write response: Cannot access a disposed object.", "Object name: 'NamedPipeTransport'."],
    AutoApproved: ["Connection auto-approved"],
    Connected: ["Client connected"],
    PipeReady: ["Created secure pipe"],
    AuthWarning: ["Project ID request failed", "401 (401)"],
  };
  const detectedSignals = [];
  for (const [name, patterns] of Object.entries(signals)) {
    const match = findSignalMatch(editorLogTailLines, patterns);
    if (match) {
      detectedSignals.push({ Name: name, Pattern: match.Pattern, LineIndex: match.LineIndex });
    }
  }

  const statusCandidates = getBridgeStatusCandidates(projectRoot);
  const selectedStatus = statusCandidates.find((status) => status.MatchesProject) || statusCandidates[0] || null;
  let lensHealth = null;
  let lensHealthError = null;
  let lensHealthOverridesBeacon = false;
  if (unityRunning && beaconIndicatesTransition && !beaconIndicatesBuild && selectedStatus?.Status === "ready" && lensBinary.exists) {
    try {
      lensHealth = await getUnityLensHealth(projectRoot);
      const data = valueOf(lensHealth, "data", "Data") || {};
      const bridgeStatus = valueOf(data, "bridgeStatus", "BridgeStatus") || {};
      const editorStability = valueOf(data, "editorStability", "EditorStability") || {};
      const expectedRecovery = valueOf(data, "expectedRecovery", "ExpectedRecovery") || {};
      if (
        valueOf(lensHealth, "success", "Success") === true &&
        valueOf(bridgeStatus, "status", "Status") === "ready" &&
        valueOf(editorStability, "isStable", "IsStable") === true &&
        valueOf(expectedRecovery, "isActive", "IsActive") !== true
      ) {
        lensHealthOverridesBeacon = true;
      }
    } catch (error) {
      lensHealthError = error.message;
    }
  }

  let classification = "Unavailable";
  let userActionRequired = true;
  let recommendedAction = "Send a notification and pause Unity editor mutations until the bridge is healthy.";
  let summary = "Unity MCP is unavailable.";
  let exitCode = 12;

  const approvalSignal = detectedSignals.find((signal) => signal.Name === "ApprovalPending");
  const handshakeSignal = detectedSignals.find((signal) => signal.Name === "HandshakeFailed");
  const bridgeCommandHealthFailed = selectedStatus && selectedStatus.CommandHealth === "failed";
  const bridgeTransportRecovering = selectedStatus && ["transport_recovering", "transport_degraded"].includes(selectedStatus.Status);
  let degradedAuthorityProbe = null;
  let degradedAuthorityProbeError = null;
  if (
    unityRunning &&
    lensBinary.exists &&
    !beaconIndicatesBuild &&
    !beaconIndicatesTransition &&
    webGlBuildState.Status !== "InProgress" &&
    (bridgeCommandHealthFailed || bridgeTransportRecovering)
  ) {
    try {
      degradedAuthorityProbe = await getUnityLensHealth(projectRoot, 30);
    } catch (error) {
      degradedAuthorityProbeError = error.message;
    }
  }
  const degradedAuthorityProbeOk = valueOf(degradedAuthorityProbe, "success", "Success") === true;

  if (!lensBinary.exists) {
    classification = "LensServerMissing";
    summary = `Unity MCP Lens server was not found at '${lensBinary.path}'.`;
    recommendedAction = "Open Unity and run Tools > Unity MCP Lens > Install/Refresh Lens Server, or set UNITY_MCP_LENS_PATH.";
    exitCode = 17;
  } else if (!lensBinary.executable) {
    classification = "LensServerNotExecutable";
    summary = `Unity MCP Lens server exists but is not executable at '${lensBinary.path}'.`;
    recommendedAction = "Run chmod +x on the Lens binary or reinstall it from Unity.";
    exitCode = 18;
  } else if (codexConfig && codexConfig.UsesRawRelay && !unityMcpSettings.DirectRelayExperimental) {
    classification = "CodexConfigMismatch";
    summary = "Codex is configured to launch the raw Unity relay directly instead of unity-mcp-lens.";
    recommendedAction = "Switch Codex MCP config to the unity-mcp-lens binary or the plugin launcher, then restart Codex.";
    exitCode = 13;
  } else if (approvalSignal) {
    classification = "ApprovalPending";
    summary = "Unity MCP is waiting for user approval in the Unity Editor.";
    recommendedAction = "Approve the Unity MCP connection in the Unity Editor, then retry the MCP call.";
    exitCode = 10;
  } else if (beaconIndicatesBuild) {
    classification = "BuildInProgress";
    userActionRequired = false;
    summary = "The editor status beacon reports an active Unity player-build transition.";
    recommendedAction = "Stop retrying MCP recovery during the active build. Monitor Editor.log, build artifacts, or the beacon until the editor returns to idle.";
    exitCode = 15;
  } else if (webGlBuildState.Status === "InProgress") {
    classification = "BuildInProgress";
    userActionRequired = false;
    summary = webGlBuildState.Summary;
    recommendedAction = "Stop retrying MCP recovery during the active WebGL build. Monitor Editor.log and build output artifacts until the build completes or fails.";
    exitCode = 15;
  } else if (beaconIndicatesTransition && !lensHealthOverridesBeacon) {
    classification = "EditorReloadingExpected";
    userActionRequired = false;
    summary = `The editor status beacon reports phase '${editorStatusBeacon.Phase}' and transient bridge churn is expected during this Unity transition.`;
    recommendedAction = "Wait for the editor status beacon to return to idle or playing, then retry the MCP call.";
    exitCode = 14;
  } else if (expectedReloadState.IsActive) {
    classification = "EditorReloadingExpected";
    userActionRequired = false;
    summary = "Unity is inside an expected compile/domain reload window and transient MCP disconnects are being treated as normal.";
    recommendedAction = "Wait for Unity to settle back to idle, then retry the MCP call.";
    exitCode = 14;
  } else if (handshakeSignal) {
    classification = "ReconnectRequired";
    summary = "Unity MCP reached the bridge, but the handshake failed before tools became available.";
    recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call.";
    exitCode = 11;
  } else if (unityRunning && (bridgeCommandHealthFailed || bridgeTransportRecovering) && degradedAuthorityProbeOk) {
    classification = "Ready";
    userActionRequired = false;
    summary = "Bridge status was degraded, but a fresh lightweight Lens authority probe succeeded.";
    recommendedAction = "Proceed with Lens tools using normal idle gating and longer post-reload probe budgets.";
    exitCode = 0;
  } else if (unityRunning && (bridgeCommandHealthFailed || bridgeTransportRecovering)) {
    classification = "ReconnectRequired";
    summary = degradedAuthorityProbeError
      ? `Bridge status is degraded, and the fresh lightweight Lens authority probe failed: ${degradedAuthorityProbeError}`
      : "Bridge status exists, but the latest direct command health signal is degraded.";
    recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call.";
    exitCode = 11;
  } else if (selectedStatus?.ExpectedRecoveryExpired && ["editor_reloading", "transport_recovering"].includes(selectedStatus.Status)) {
    classification = "ReconnectRequired";
    summary = `Bridge status remained in '${selectedStatus.Status}' past its expected recovery window.`;
    recommendedAction = "Reconnect or restart the Unity MCP bridge, then retry the MCP call.";
    exitCode = 11;
  } else if (selectedStatus?.Status === "ready" && unityRunning) {
    classification = "Ready";
    userActionRequired = false;
    summary = lensHealthOverridesBeacon
      ? "Lens health confirmed the bridge is ready and the editor is stable, overriding a lagging reload beacon."
      : "Bridge status reports ready and no blocking failure signal was found in the latest Unity log tail.";
    recommendedAction = "Proceed with a lightweight Unity MCP call.";
    exitCode = 0;
  } else if (!unityRunning) {
    classification = "UnityNotRunning";
    summary = "Unity editor is not running for this project.";
    recommendedAction = "Open the project in Unity and wait for the bridge to initialize.";
  } else if (selectedStatus?.Status) {
    classification = "BridgeNotReady";
    summary = `Bridge status file exists, but the current status is '${selectedStatus.Status}'.`;
    recommendedAction = "Wait for the bridge to become ready or reconnect it, then retry the MCP call.";
  }

  const result = {
    ServerName: options.serverName || "unity-mcp",
    ProjectPath: projectRoot,
    NormalizedProjectPath: normalizedProjectPath,
    UnityRunning: unityRunning,
    Classification: classification,
    UserActionRequired: userActionRequired,
    Summary: summary,
    RecommendedAction: recommendedAction,
    LensBinary: lensBinary,
    CodexConfig: codexConfig,
    BridgeStatus: selectedStatus,
    AssistantPackage: assistantPackageState,
    UnityMcpSettings: unityMcpSettings,
    EditorStatusBeacon: editorStatusBeacon,
    BeaconWait: beaconWait,
    LensHealth: lensHealth,
    LensHealthError: lensHealthError,
    DegradedAuthorityProbe: degradedAuthorityProbe,
    DegradedAuthorityProbeError: degradedAuthorityProbeError,
    LensHealthOverridesBeacon: lensHealthOverridesBeacon,
    ExpectedReloadState: expectedReloadState,
    DetectedSignals: detectedSignals,
    WebGLBuildState: webGlBuildState,
    EditorLogPath: pathExists(editorLogPath) ? editorLogPath : null,
    ExitCode: exitCode,
  };

  const compactResult = {
    ServerName: result.ServerName,
    ProjectPath: result.ProjectPath,
    Classification: classification,
    UserActionRequired: userActionRequired,
    Summary: summary,
    RecommendedAction: recommendedAction,
    LensBinary: lensBinary,
    CodexConfig: codexConfig
      ? {
          UsesWrapper: codexConfig.UsesWrapper,
          UsesRawRelay: codexConfig.UsesRawRelay,
          UsesLensBinary: codexConfig.UsesLensBinary,
          Command: codexConfig.Command,
        }
      : null,
    BridgeStatus: selectedStatus
      ? {
          Status: selectedStatus.Status,
          Reason: selectedStatus.Reason,
          ExpectedRecovery: selectedStatus.ExpectedRecovery,
          ExpectedRecoveryExpiresUtc: selectedStatus.ExpectedRecoveryExpiresUtc,
          ExpectedRecoveryExpired: selectedStatus.ExpectedRecoveryExpired,
          ProjectPath: selectedStatus.ProjectPath,
          ProjectRoot: selectedStatus.ProjectRoot,
          MatchesProject: selectedStatus.MatchesProject,
          ToolDiscoveryMode: selectedStatus.ToolDiscoveryMode,
          ToolCount: selectedStatus.ToolCount,
          CommandHealth: selectedStatus.CommandHealth,
        }
      : null,
    DegradedAuthorityProbe: degradedAuthorityProbe
      ? { Success: degradedAuthorityProbeOk, Error: degradedAuthorityProbeError }
      : degradedAuthorityProbeError
        ? { Success: false, Error: degradedAuthorityProbeError }
        : null,
    EditorStatusBeacon: editorStatusBeacon
      ? {
          Classification: editorStatusBeacon.Classification,
          Phase: editorStatusBeacon.Phase,
          Fresh: editorStatusBeacon.Fresh,
        }
      : null,
    LensHealth: lensHealth
      ? {
          Success: valueOf(lensHealth, "success", "Success") === true,
          BridgeStatus: valueOf(valueOf(lensHealth, "data", "Data")?.bridgeStatus || valueOf(lensHealth, "data", "Data")?.BridgeStatus || {}, "status", "Status") || null,
          OverridesBeacon: lensHealthOverridesBeacon,
          Error: lensHealthError,
        }
      : lensHealthError
        ? { Success: false, Error: lensHealthError, OverridesBeacon: false }
        : null,
    ExpectedReloadState: expectedReloadState
      ? {
          IsActive: expectedReloadState.IsActive,
          Reason: expectedReloadState.Reason,
        }
      : null,
    AssistantPackage: assistantPackageState
      ? {
          Mode: assistantPackageState.Mode,
          Summary: assistantPackageState.Summary,
          Dependency: assistantPackageState.DependencyValue,
          Path: assistantPackageState.ResolvedFileDependencyPath,
        }
      : null,
    UnityMcpSettings: unityMcpSettings
      ? {
          WrapperMode: unityMcpSettings.WrapperMode,
          AllowManualWrapper: unityMcpSettings.AllowManualWrapper,
          AllowCachedToolsFallback: unityMcpSettings.AllowCachedToolsFallback,
          DirectRelayExperimental: unityMcpSettings.DirectRelayExperimental,
        }
      : null,
    DiagnosticsHint: "Rerun with --IncludeDiagnostics for the full bridge payload.",
  };

  return { result, compactResult, exitCode };
}

function escapeCSharpString(value) {
  return String(value).replace(/\\/g, "\\\\").replace(/"/g, '\\"');
}

function newUnityArtifactDirectory(projectPath, prefix = "unity-playtest") {
  const projectName = path.basename(resolveProjectPath(projectPath));
  const stamp = new Date().toISOString().replace(/[-:]/g, "").replace(/\..+$/, "").replace("T", "-");
  const directory = path.join(os.tmpdir(), "codex-unity", `${prefix}-${projectName}-${stamp}`);
  ensureDir(directory);
  return directory;
}

async function waitForFile(filePath, timeoutMilliseconds = 2000, pollMilliseconds = 200) {
  const started = Date.now();
  while (Date.now() - started <= timeoutMilliseconds) {
    try {
      const stat = fs.statSync(filePath);
      if (stat.size > 0) {
        return { Exists: true, Length: stat.size, WaitedMilliseconds: Date.now() - started };
      }
    } catch (_error) {
    }
    await sleep(Math.max(50, pollMilliseconds));
  }
  return { Exists: false, Length: 0, WaitedMilliseconds: Date.now() - started };
}

async function captureDesktop(filePath) {
  ensureDir(path.dirname(filePath));
  if (process.platform === "darwin") {
    const result = await runProcess("screencapture", ["-x", "-t", "png", filePath], { timeoutMs: 10000 });
    return result.code === 0 && pathExists(filePath);
  }
  if (process.platform === "linux") {
    for (const command of [
      { bin: "gnome-screenshot", args: ["-f", filePath] },
      { bin: "import", args: ["-window", "root", filePath] },
    ]) {
      const result = await runProcess(command.bin, command.args, { timeoutMs: 10000 });
      if (result.code === 0 && pathExists(filePath)) {
        return true;
      }
    }
  }
  return false;
}

module.exports = {
  bridgeScriptsDir,
  sleep,
  nowIso,
  parseCliArgs,
  getArg,
  getArgString,
  getArgNumber,
  getArgBool,
  getArgArray,
  toBool,
  resolveProjectPath,
  resolveUnityRelativePath,
  ensureDir,
  readJsonFile,
  writeJsonFile,
  pathExists,
  getLensBinaryState,
  inferRequiredPacks,
  ensureUnityToolPacks,
  setUnityToolPacksExact,
  resetUnityMcpSession,
  shutdownUnityMcpSessions,
  invokeUnityMcpToolJson,
  getToolObject,
  valueOf,
  boolOf,
  getUnityEditorState,
  getUnityCompactEditorState,
  getUnityLensHealth,
  getUnityConsoleEntries,
  convertToUnityRunCommandScript,
  invokeUnityRunCommandObject,
  getUnityRunCommandPlayModeExecution,
  getUnityExpectedReloadState,
  setUnityExpectedReloadState,
  clearUnityExpectedReloadState,
  getUnityCompileAffectingChanges,
  getUnityEditorBuildSettingsScenes,
  testUnityBuildSceneList,
  waitUnityEditorIdle,
  waitUnityCompileReloadCycle,
  waitUnityPlayReady,
  getUnityEditorStatusBeacon,
  testUnityEditorBeaconTransitionSnapshot,
  testUnityEditorBeaconBuildSnapshot,
  waitUnityEditorBeaconStable,
  getUnityReadinessSnapshot,
  getWebGlBuildProgressState,
  getUnityEditorLogPath,
  tailFile,
  getUnityAssistantPackageState,
  getCompactEditorStateSummary,
  testUnityDirectEditorHealthy,
  checkUnityMcp,
  escapeCSharpString,
  newUnityArtifactDirectory,
  waitForFile,
  captureDesktop,
};
