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
- Split Phase 8 GameObject TSAM tools behind the `scene` pack.
- Phase 11 project/package/import diagnostics and active input handler tools behind the `project` pack.
- Phase 12 UI authoring, scene serialized-reference binding, and screen-layout verification tools behind the `ui` and `scene` packs.

Lens does not own Unity's official Assistant chat UI, cloud asset generation, Assistant Gateway workflows, or Assistant-specific UI. Install the official Assistant package separately if you want those features.

## Install In A Unity Project

Use this repo as a local package source:

```json
"com.becool3000.unity-mcp-lens": "file:C:/dev/unity-mcp-lens"
```

or with a relative path:

```json
"com.becool3000.unity-mcp-lens": "file:../unity-mcp-lens"
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

## TSAM Direction

Lens is being refactored incrementally around TSAM surfaces: Tool, Service,
Adapter, and Model. The goal is not a rewrite. The goal is to move the
workflows that create the most agent friction into compact, typed,
telemetry-covered tools while keeping older broad tools available as
compatibility fallbacks.

TSAM-covered tools are expected to:

- keep the public MCP schema narrow and explicit at the Tool layer
- put planning and validation in Services
- isolate Unity API and reflection access in Adapters
- return stable typed Models instead of drifting anonymous result shapes
- emit `normalization`, `service`, `adapter`, and `result_shaping` telemetry rows
- prefer preview/apply pairs for mutations and read-only tools for diagnostics

Current TSAM surfaces:

- `scene`: Phase 8 split GameObject inspection, component reads, preview/apply mutation, create, and delete.
- `scene`: Phase 12 serialized-reference preview/apply binding.
- `project`: Phase 11 package compatibility, input-action asset inspection, Input System diagnostics, and active input handler preview/apply.
- `ui`: Phase 12 uGUI hierarchy/layout preview/apply authoring and read-only screen-layout verification.

The legacy broad tools remain available where split coverage is not complete,
but new high-use workflows should move into TSAM slices first.

## Validation

Useful static checks:

```powershell
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpPackageIdentity.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpStandaloneBoundary.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpToolOwnership.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpLensPresentation.ps1
```

Useful live metadata audit, run against a Unity host project with the Lens
server installed and the editor idle:

```powershell
dotnet run --project Tools~/UnityMcpLensPackSwitchBenchApp~/UnityMcpLensPackSwitchBench.csproj -c Release -p:UseAppHost=false -- --project-path C:\Path\To\UnityProject --server-path C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe --metadata-audit
```

## Status

Lens is usable but still evolving. The current stable direction is to keep the
default `foundation` surface narrow, preserve the pack baselines, and continue
moving high-friction workflows into compact TSAM tools.

Current live metadata baselines:

- `foundation`: `12` exported tools.
- `foundation + scene`: `32` exported tools.
- `foundation + ui`: `22` exported tools.
- `project`: `21` exported tools.
- `debug`: `22` exported tools.

Current near-term work is focused on bridge/session churn, payload shaping,
structured console and RunCommand result quality, restart/reload orchestration,
prefab authoring, and extending the `project` pack beyond Input System into
missing-script, reference, and import-side-effect diagnostics.

This is not an official Unity release channel.

## License

This project is licensed under the MIT License.

`unity-mcp-lens` is maintained as a standalone MCP package that can be installed alongside Unity's official Assistant package. It is not an official Unity release channel.
