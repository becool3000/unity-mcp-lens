#!/usr/bin/env node

const { spawn } = require("child_process");
const fs = require("fs");
const os = require("os");
const path = require("path");

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

const lensPath = process.env.UNITY_MCP_LENS_PATH || getDefaultLensPath();

if (!fs.existsSync(lensPath)) {
  console.error(
    `Unity MCP Lens server not found at '${lensPath}'. Open Unity and run Tools > Unity MCP Lens > Install/Refresh Lens Server, or set UNITY_MCP_LENS_PATH.`
  );
  process.exit(4);
}

const child = spawn(lensPath, process.argv.slice(2), {
  cwd: process.cwd(),
  env: process.env,
  stdio: "inherit",
  windowsHide: true,
});

child.on("error", (error) => {
  console.error(`Failed to start unity-mcp-lens at '${lensPath}': ${error.message}`);
  process.exit(1);
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
    return;
  }

  process.exit(code ?? 0);
});
