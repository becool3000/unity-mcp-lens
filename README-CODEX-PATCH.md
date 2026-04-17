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

Codex MCP settings should launch the Lens binary directly with no arguments.

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
`unity-mcp-lens-development` skills. The vendored `.mcp.json` intentionally uses
a disabled source-run template so this repo does not commit a machine-specific
absolute path. For daily use, install or refresh the Lens server from Unity
first so `~/.unity/unity-mcp-lens/` contains the platform-specific binary. For
an Intel Mac, the installed command is:

```text
~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64
```
