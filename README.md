# unity-mcp-lens

`unity-mcp-lens` is a standalone Unity package for running a focused MCP bridge between the Unity Editor and external agents such as Codex, Claude Code, Cursor, and other standard MCP clients.

The package id is:

```json
"com.becool3000.unity-mcp-lens"
```

Editor UI is Lens-owned and lives under **Tools > Unity MCP Lens** and **Project Settings > Tools > Unity MCP Lens**.

## What Lens Owns

- An owned MCP-only stdio server named `unity-mcp-lens`.
- A Unity-side bridge with event-driven manifest sync.
- Session-scoped tool packs with a narrow `foundation` default.
- Compact tool outputs with `detailRef` expansion for large payloads.
- Local Unity editor/dev tools for console, project, scene, UI, scripting, assets, diagnostics, and package workflows.
- Payload and bridge telemetry for measuring context and control-plane noise.

Lens does not own Unity's official Assistant chat UI, cloud asset generation, Assistant Gateway workflows, or Assistant-specific UI. Install the official Assistant package separately if you want those features.

## Install In A Unity Project

Use this repo as a local package source:

```json
"com.becool3000.unity-mcp-lens": "file:C:/UnityAIAssistantPatch"
```

or with a relative path:

```json
"com.becool3000.unity-mcp-lens": "file:../UnityAIAssistantPatch"
```

If an older project points this checkout at the Assistant package id, replace that dependency with the Lens package id above.

## MCP Server

The owned Lens server installs under:

```text
~/.unity/unity-mcp-lens/
```

The MCP client command should point directly at the installed Lens binary, for example on Windows:

```text
C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe
```

Do not pass `--mcp` to the Lens server. That argument belongs to the legacy Unity relay path, not Lens.

## Side-by-side With Official Assistant

Lens now uses its own package id and owned assembly names so it can coexist with the official `com.unity.ai.assistant` package.

Expected split:

- Official Assistant package: Assistant UI, cloud/generation features, official Unity workflows.
- Unity MCP Lens: MCP bridge, MCP server, tool packs, compact outputs, local editor/dev tools.

Standalone Lens does not bundle or install the legacy Unity relay. If a project still needs the legacy relay, use the official Assistant package for that lane.

## Project Structure

- `Editor/Lens/`: Unity editor bridge, settings UI, tool registry, tool packs, and Lens tools.
- `Runtime/`: small runtime helpers used by Lens tools.
- `UnityMcpLensApp~/`: owned MCP-only stdio server source.
- `Tools~/`: benchmark and static validation scripts.
- `Documentation~/`: package docs.
- `docs/`: repo-maintenance notes and audits.

## Validation

Useful static checks:

```powershell
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpPackageIdentity.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpStandaloneBoundary.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpToolOwnership.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpLensPresentation.ps1
```

## Status

Lens is usable but still evolving. The high-priority direction is to keep reducing bridge chatter, keep the default tool surface narrow, and make tool output compact and recoverable by default.

This is not an official Unity release channel.
