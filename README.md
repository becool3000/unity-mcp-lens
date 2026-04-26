# unity-mcp-lens

`unity-mcp-lens` is a standalone Unity package for running a focused MCP bridge between the Unity Editor and external agents such as Codex, Claude Code, Cursor, and other standard MCP clients.

The package id is:

```json
"com.becool3000.unity-mcp-lens"
```

Editor UI is Lens-owned and lives under **Tools > Unity MCP Lens** and **Project Settings > Tools > Unity MCP Lens**.

## TSAM Refactor Direction

TSAM means **Tool, Service, Adapter, Model**. In Lens, it is the incremental
refactor path for turning broad Unity MCP tools into smaller, typed, easier to
audit workflows behind explicit tool packs.

- **Tool**: the MCP-facing entry point. It owns the public schema, normalizes inputs, calls the service, shapes compact results, and emits telemetry.
- **Service**: the workflow layer. It plans reads, previews, applies, validation, and verification.
- **Adapter**: the Unity API boundary. It touches GameObjects, assets, project settings, packages, serialized objects, logs, editor state, and other Unity surfaces.
- **Model**: the typed request, result, and plan structures that keep contracts stable instead of drifting through anonymous objects.

This is not a full rewrite. Legacy broad tools remain available for compatibility
and escape hatches while painful, high-use workflows move into compact TSAM
tools. More detail lives in [docs/TSAM.md](docs/TSAM.md).

## Why This Matters For Codex-Style Unity Agents

Unity agents need to stay oriented while Unity recompiles, reloads domains,
enters play mode, imports packages, and mutates serialized scene state. Broad
tools make that expensive: they expose too much surface, return large payloads,
and make failures harder to classify.

TSAM gives agents a smaller MCP surface, safer preview/apply mutation flows,
compact default outputs, typed contracts, `detailRef` expansion for large data,
and telemetry that shows payload size, bridge churn, pack churn, and recovery
events. The result is less custom `Unity.RunCommand` code for common workflows
and more auditable Unity changes.

## Quick Start

1. Clone this repository.

2. Add Lens to a Unity project's `Packages/manifest.json`:

```json
"com.becool3000.unity-mcp-lens": "file:C:/dev/unity-mcp-lens"
```

Use a relative path if your project and package checkout live near each other:

```json
"com.becool3000.unity-mcp-lens": "file:../unity-mcp-lens"
```

3. In Unity, run **Tools > Unity MCP Lens > Install/Refresh Lens Server**.

4. Point your MCP client at the installed Lens server with no `--mcp` argument:

```text
~/.unity/unity-mcp-lens/<platform binary>
```

For example on Windows:

```text
C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe
```

5. Verify the bridge and editor state from this repo:

```powershell
powershell -ExecutionPolicy Bypass -File .agents/plugins/lens-dev-plugin/skills/unity-dev-assistant/scripts/Check-UnityDevSession.ps1 -ProjectPath C:\Path\To\UnityProject
```

6. Run the live metadata audit against an idle Unity host project:

```powershell
dotnet run --project Tools~/UnityMcpLensPackSwitchBenchApp~/UnityMcpLensPackSwitchBench.csproj -c Release -p:UseAppHost=false -- --project-path C:\Path\To\UnityProject --server-path C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe --metadata-audit
```

## Example Workflow

For package, import, or Input System diagnosis, prefer the `project` pack before
custom editor scripts or raw `Editor.log` grep:

1. Activate the `project` pack with `Unity.SetToolPacks`.
2. Run `Unity.InputSystem.Diagnostics`, `Unity.Project.PackageCompatibility`, or `Unity.InputActions.InspectAsset`.
3. Use the compact result first; read `detailRef` only when the preview is insufficient.
4. Activate `debug` and run `Unity.GetLensUsageReport` to confirm TSAM stage coverage and inspect payload/session cost.

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

## Before vs After TSAM

Before TSAM, common agent workflows leaned on broad tools, larger payloads,
manual probes, and harder-to-audit mutation paths. That made it slower to find
the real failure stage when Unity was compiling, importing packages, or
changing serialized state.

After TSAM, common workflows move into explicit packs with typed models,
read-only diagnostics, preview/apply mutation pairs, compact default output,
`detailRef` expansion for large data, and telemetry rows for
`normalization`, `service`, `adapter`, and `result_shaping`.

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

Useful live metadata audit, run against a Unity host project with the Lens
server installed and the editor idle:

```powershell
dotnet run --project Tools~/UnityMcpLensPackSwitchBenchApp~/UnityMcpLensPackSwitchBench.csproj -c Release -p:UseAppHost=false -- --project-path C:\Path\To\UnityProject --server-path C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe --metadata-audit
```

## Benchmark And Telemetry

Telemetry is part of the TSAM refactor. `Unity.GetLensUsageReport` reports
payload rows, bridge request/response rows, pack transitions, tool snapshots,
detail refs, shaping metadata, and TSAM stage coverage.

Existing dogfood data is tracked in [docs/Telemetry](docs/Telemetry). Current
recorded signals include:

- Phase 11 focused smoke on Unity `6000.4.3f1`: metadata audit passed; compact rerun span was `44` rows; bridge churn was `1` connection, `0` setup cycles, and `0` unmatched requests; TSAM coverage was complete for package compatibility, input-actions inspection, diagnostics, preview, and set.
- Phase 12 helper-driven smoke on Unity `6000.4.3f1`: metadata audit passed with `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`; rerun scope was `358` rows; bridge churn was `25` connections, `0` setup cycles, and `0` unmatched requests; TSAM coverage was complete for UI hierarchy, scene binding, layout, and verify.
- Phase 13 payload-shaping smoke on Unity `6000.4.3f1`: rerun scope was `244` rows; payload size was `210,510` raw bytes -> `120,867` shaped bytes; recorded savings were `89,643` bytes (`42.58%`); `NoShapingRecorded=false`; the largest measured win was tool snapshot shaping at `100,016` raw bytes -> `9,481` shaped bytes.
- Payload shaping is still underway. Phase 13 proves measurable savings for tool snapshots and usage reports, but large tool execution/result rows still need compact shaping and detail refs.

Future benchmark reports should include:

```text
Scope:
Unity version:
Host project:
Tool packs:
Payload size:
Result shaping savings:
Tool calls:
Bridge/session churn:
Pack transitions:
Error/recovery events:
TSAM stage coverage:
Known caveats:
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
