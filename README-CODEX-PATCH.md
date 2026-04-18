# Codex And Lens Notes

Unity MCP Lens is the supported MCP path for Codex in this repository.

Preferred topology:

```text
Codex or other MCP client -> unity-mcp-lens stdio server -> Unity Lens bridge
```

The Lens server is installed under:

```text
~/.unity/unity-mcp-lens/
```

Codex MCP settings should launch the Lens binary directly with no arguments, or use the repo-local plugin launcher:

```text
node .agents/plugins/lens-dev-plugin/skills/unity-mcp-bridge/scripts/Launch-UnityMcpLens.js
```

## Important Constraints

- Keep helper scripts on the Lens path.
- Do not use the manual wrapper or legacy relay as the normal Codex transport.
- Keep the default pack surface narrow and expand packs explicitly.
- Use `Unity.ReadDetailRef` only when a compact preview is insufficient.
- Treat Unity compile/import/reload windows as expected recovery windows, not as reasons to spam tool discovery.

## Maintenance

When changing package identity or tool exposure, run:

```powershell
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpPackageIdentity.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpStandaloneBoundary.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpToolOwnership.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpLensPresentation.ps1
```

## Repo-local Codex Plugin

The Codex plugin source for Lens is vendored at:

```text
.agents/plugins/lens-dev-plugin/
```

The repo-local marketplace entry is:

```text
.agents/plugins/marketplace.json
```

The plugin bundles the `unity-mcp-bridge`, `unity-dev-assistant`, and
`unity-mcp-lens-development` skills. The vendored `.mcp.json` launches a small
Node shim that resolves the installed platform-specific binary under
`~/.unity/unity-mcp-lens/`, so the plugin no longer needs a local .NET SDK or a
source checkout at runtime. For an Intel Mac, the resolved command is:

```text
~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64
```

On Apple Silicon it resolves:

```text
~/.unity/unity-mcp-lens/unity_mcp_lens_mac_arm64
```

Set `UNITY_MCP_LENS_PATH` only when testing a nonstandard binary location.
