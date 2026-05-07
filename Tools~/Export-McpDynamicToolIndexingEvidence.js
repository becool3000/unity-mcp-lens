const childProcess = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

const requiredAssetTools = [
  "Unity_Asset_PreviewImportSpriteSheetAndBind",
  "Unity_Asset_ApplyImportSpriteSheetAndBind",
  "Unity_Asset_ImportSpriteSheetAndBind",
  "Unity_Asset_VerifySpriteArrayBinding",
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

    result[key] = next;
    index++;
  }

  return result;
}

function defaultHostPath() {
  if (process.platform === "win32") {
    return path.join(os.homedir(), ".unity", "unity-mcp-lens", "unity_mcp_lens_win.exe");
  }

  return path.join(os.homedir(), ".unity", "unity-mcp-lens", "unity_mcp_lens");
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
  constructor(hostPath, projectPath) {
    this.hostPath = hostPath;
    this.projectPath = projectPath;
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

  async initialize() {
    const result = await this.request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "phase20-dynamic-tool-indexing-evidence", version: "1.0.0" },
    });
    this.notify("notifications/initialized", {});
    return result;
  }

  notify(method, params = {}) {
    this.child.stdin.write(rpcFrame({ jsonrpc: "2.0", method, params }));
  }

  request(method, params = {}) {
    const id = this.nextId++;
    const payload = { jsonrpc: "2.0", id, method, params };
    return new Promise((resolve, reject) => {
      const timeout = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error(`Timed out waiting for ${method}. stderr=${this.stderr}`));
      }, 20000);
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
    return this.request("tools/list", {});
  }

  callTool(name, args = {}) {
    return this.request("tools/call", { name, arguments: args });
  }

  clearNotifications() {
    this.notifications = [];
  }

  async waitForNotification(method, timeoutMs = 3000) {
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
      if (this.notifications.some((notification) => notification.method === method)) return true;
      await new Promise((resolve) => setTimeout(resolve, 50));
    }

    return false;
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

function toolNames(toolsListResult) {
  return Array.isArray(toolsListResult.tools)
    ? toolsListResult.tools.map((tool) => tool.name).filter(Boolean).sort((a, b) => a.localeCompare(b))
    : [];
}

function structuredContent(result) {
  return result && typeof result === "object" ? result.structuredContent ?? null : null;
}

function pickVerifierSchema(toolsListResult) {
  const verifier = toolsListResult.tools?.find((tool) => tool.name === "Unity_Asset_VerifySpriteArrayBinding");
  return verifier?.inputSchema?.properties?.expectedSpriteNames ?? null;
}

function buildPresence(names) {
  return Object.fromEntries(requiredAssetTools.map((toolName) => [toolName, names.includes(toolName)]));
}

function schemaHasStringItems(schema) {
  return schema?.type === "array" && schema.items?.type === "string";
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  const hostPath = path.resolve(args.hostPath || process.env.UNITY_MCP_LENS_HOST || defaultHostPath());
  const projectPath = path.resolve(args.projectPath || process.env.UNITY_MCP_PROJECT_PATH || process.cwd());
  const outputPath = args.outputPath ? path.resolve(args.outputPath) : null;

  if (!fs.existsSync(hostPath)) {
    throw new Error(`Unity MCP Lens host was not found at '${hostPath}'.`);
  }
  if (!fs.existsSync(projectPath)) {
    throw new Error(`Unity project path was not found at '${projectPath}'.`);
  }

  const client = new RawMcpClient(hostPath, projectPath);
  try {
    const initializeResult = await client.initialize();

    await client.callTool("Unity_SetToolPacks", { Packs: [] });
    client.clearNotifications();

    const foundationList = await client.listTools();
    const foundationNames = toolNames(foundationList);
    const setAssetsResult = await client.callTool("Unity_SetToolPacks", { Packs: ["assets"] });
    const sawListChanged = await client.waitForNotification("notifications/tools/list_changed");
    const assetList = await client.listTools();
    const assetNames = toolNames(assetList);
    const assetPresence = buildPresence(assetNames);
    const verifierSchema = pickVerifierSchema(assetList);

    const setAssetsStructured = structuredContent(setAssetsResult);
    const hostContractPass =
      setAssetsStructured?.success === true &&
      sawListChanged &&
      foundationNames.length < assetNames.length &&
      requiredAssetTools.every((toolName) => assetPresence[toolName]) &&
      schemaHasStringItems(verifierSchema);

    const evidence = {
      capturedAtUtc: new Date().toISOString(),
      hostPath,
      projectPath,
      hostProcessId: client.child.pid,
      initializeServerInfo: initializeResult?.serverInfo ?? null,
      sequence: [
        "initialize",
        "Unity_SetToolPacks([])",
        "tools/list foundation",
        "Unity_SetToolPacks([\"assets\"])",
        "notifications/tools/list_changed",
        "tools/list foundation+assets",
      ],
      foundationTools: {
        count: foundationNames.length,
        hasAssetVerifier: foundationNames.includes("Unity_Asset_VerifySpriteArrayBinding"),
        names: foundationNames,
      },
      setAssetsResult: {
        success: setAssetsStructured?.success ?? null,
        message: setAssetsStructured?.message ?? null,
        activeToolPacks: setAssetsStructured?.data?.activeToolPacks ?? null,
        toolCount: setAssetsStructured?.data?.toolCount ?? null,
        manifestKind: setAssetsStructured?.data?.manifestKind ?? null,
        unchanged: setAssetsStructured?.data?.unchanged ?? null,
      },
      notifications: {
        sawToolsListChanged: sawListChanged,
        methods: client.notifications.map((notification) => notification.method),
      },
      assetTools: {
        count: assetNames.length,
        requiredPresence: assetPresence,
        verifierExpectedSpriteNamesSchema: verifierSchema,
        names: assetNames,
      },
      hostContractPass,
      codexComparison: {
        expectedManualCheck:
          "After running Unity_SetToolPacks([\"assets\"]) through Codex, tool_search should expose Unity_Asset_VerifySpriteArrayBinding.",
        observedInLatestDogfood:
          "Codex tool_search did not expose Unity_Asset_VerifySpriteArrayBinding after Unity/Codex restart, even though raw tools/list did.",
      },
    };

    const json = `${JSON.stringify(evidence, null, 2)}\n`;
    if (outputPath) {
      fs.mkdirSync(path.dirname(outputPath), { recursive: true });
      fs.writeFileSync(outputPath, json, "utf8");
    }

    process.stdout.write(json);
    if (!hostContractPass) {
      process.exitCode = 1;
    }
  } finally {
    await client.dispose().catch(() => {});
  }
}

main().catch((error) => {
  console.error(error.stack || error.message);
  process.exit(1);
});
