# Troubleshooting

## Lens server does not install

Open **Tools > Unity MCP Lens > Install/Refresh Lens Server**. If no prebuilt binary is bundled for your platform, the package publishes the server from `UnityMcpLensApp~` and requires a local .NET SDK 8 or newer. Codex-side access should not require .NET once the native binary exists under `~/.unity/unity-mcp-lens/`.

## MCP client cannot connect

Verify the MCP client launches the Lens binary directly and does not use the legacy relay arguments:

```text
Windows command: %USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe
macOS Intel command: ~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64
macOS Apple Silicon command: ~/.unity/unity-mcp-lens/unity_mcp_lens_mac_arm64
Arguments: none
```

For the Codex plugin, the MCP config should launch:

```text
node ./skills/unity-mcp-bridge/scripts/Launch-UnityMcpLens.js
```

## Unity is compiling or reloading

Wait for Unity to finish compiling, importing, or reloading before retrying editor mutations. Lens health and bridge status are available through `Unity.GetLensHealth` once the bridge is reachable.

## Legacy relay

Standalone Lens does not bundle or install the legacy Unity relay. If you need the official relay path, install the official Assistant package and keep it separate from Lens.
