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

For Unity workflow health checks, use the `unity-dev-assistant` helper path:

```powershell
.agents/plugins/lens-dev-plugin/skills/unity-dev-assistant/scripts/Check-UnityDevSession.ps1
```

or on macOS/Linux:

```bash
node .agents/plugins/lens-dev-plugin/skills/unity-dev-assistant/scripts/Check-UnityDevSession.js
```

## Important Constraints

- Keep helper scripts on the Lens path.
- Do not use the manual wrapper or legacy relay as the normal Codex transport.
- Keep the default pack surface narrow and expand packs explicitly.
- Use `Unity.ReadDetailRef` only when a compact preview is insufficient.
- Treat Unity compile/import/reload windows as expected recovery windows, not as reasons to spam tool discovery.
- Prefer Phase 10 project tools for Input System and active input handler work before custom `Unity_RunCommand` probes or YAML edits.
- Keep `foundation` at `12` exported tools and `foundation + scene` at `30` exported tools unless a deliberate pack-surface change updates the metadata audit.

## Current Tool Surface Reality

- `foundation` is the default and always active.
- `scene` contains the Phase 8 split GameObject TSAM surface.
- `project` contains project/package diagnostics, missing script/reference checks, Input System diagnostics, and active input handler preview/apply.
- `debug` contains usage reporting through `Unity.GetLensUsageReport`.
- `Unity.ManageGameObject` remains a compatibility fallback for uncovered split-tool behavior.
- Helper scripts are still important for orchestration-heavy flows such as session checks, script sync, play-mode entry, and long-running build/reload monitoring.

## Current Dogfood Priorities

- Reduce helper-script session/setup churn and repeated schema requests.
- Fix payload shaping for large `Unity.ManageEditor` and tool snapshot rows.
- Extend `Unity.InputSystem.Diagnostics` so package, define, device, `.inputactions`, wrapper, and recent log failures can be diagnosed in one call.
- Keep `Unity.ProjectSettings.PreviewActiveInputHandler` and `Unity.ProjectSettings.SetActiveInputHandler` as the editor-authored backend change path.
- Suppress restart-required noise for active input handler no-op preview/apply results.
- Improve `Unity.RunCommand` failure-stage metadata and detail refs.
- Add reliable restart/reload orchestration with save/dirty handling and bridge reacquire.
- Add first-class prefab authoring and serialized reference inspect/bind/verify workflows.

## Latest Phase 10 Smoke

The 2026-04-24 smoke against `D:\2DUnityNewGame` on Unity `6000.4.3f1` passed
with warnings. Metadata audit passed with `foundation=12`, `foundation+scene=30`,
`project=19`, and `debug=22`. Phase 10 diagnostics, preview, and set calls all
emitted complete TSAM stage coverage and zero failure classes.

Follow-ups from that smoke:

- No-op active input handler preview/apply still reports restart-required messaging.
- Payload telemetry still reports no shaping recorded.
- The check-session helper path is under `unity-dev-assistant/scripts`, not `unity-mcp-bridge/scripts`.

## Maintenance

When changing package identity or tool exposure, run:

```powershell
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpPackageIdentity.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpStandaloneBoundary.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpToolOwnership.ps1
powershell -ExecutionPolicy Bypass -File Tools~/Test-McpLensPresentation.ps1
```

For live tool-pack metadata, schema, read-only annotations, and required-tool
coverage, run the pack-switch helper app in metadata audit mode against an idle
Unity host project:

```powershell
dotnet run --project Tools~/UnityMcpLensPackSwitchBenchApp~/UnityMcpLensPackSwitchBench.csproj -c Release -p:UseAppHost=false -- --project-path C:\Path\To\UnityProject --server-path C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe --metadata-audit
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
