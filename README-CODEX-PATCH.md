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
- Prefer the Phase 11 `project` tools for package/import/Input System and active input handler work before custom `Unity_RunCommand` probes, raw `Editor.log` grep, or YAML edits.
- Prefer the Phase 12 `ui` and split `scene` binding tools for persistent HUD hierarchy, serialized scene references, and screen-layout verification before custom `Unity_RunCommand` editor scripts.
- Keep `foundation` at `12` exported tools, `foundation + scene` at `32`, and `foundation + ui` at `22` unless a deliberate pack-surface change updates the metadata audit.

## Current Tool Surface Reality

- `foundation` is the default and always active.
- `scene` contains the Phase 8 split GameObject TSAM surface.
- `scene` also contains the Phase 12 serialized-reference preview/apply binding pair.
- `ui` contains the Phase 12 split UI hierarchy/layout authoring tools plus read-only screen-layout verification.
- `project` contains project/package/import diagnostics, missing script/reference checks, Input System diagnostics, input-action asset inspection, package compatibility, and active input handler preview/apply.
- `debug` contains usage reporting through `Unity.GetLensUsageReport`.
- `Unity.ManageGameObject` remains a compatibility fallback for uncovered split-tool behavior.
- Helper scripts are still important for orchestration-heavy flows such as session checks, script sync, play-mode entry, and long-running build/reload monitoring.

## Current Dogfood Priorities

- Reduce helper-script session/setup churn and repeated schema requests.
- Fix payload shaping for large `Unity.ManageEditor` and tool snapshot rows.
- Reduce noisy repeated package/editor-log signals so healthy compatibility reads stay high signal.
- Keep `Unity.ProjectSettings.PreviewActiveInputHandler` and `Unity.ProjectSettings.SetActiveInputHandler` as the editor-authored backend change path.
- Keep `Unity.Project.PackageCompatibility` and `Unity.InputActions.InspectAsset` as the preferred package/import read surface before raw `Editor.log` grep.
- Improve `Unity.RunCommand` failure-stage metadata, detail refs, and structured `ReturnResult(...)` output.
- Add reliable restart/reload orchestration with save/dirty handling and bridge reacquire.
- Dogfood the new Phase 12 UI authoring/binding/verification path on a real host project without custom editor C#.

## Latest Completed Smoke

The 2026-04-24 smoke against `D:\2DUnityNewGame` on Unity `6000.4.3f1` passed
with a residual payload-shaping warning. Metadata audit passed with `foundation=12`, `foundation+scene=30`,
`project=21`, and `debug=22`. `Unity.Project.PackageCompatibility`,
`Unity.InputActions.InspectAsset`, `Unity.InputSystem.Diagnostics`, and the
active input handler preview/set tools all emitted complete TSAM stage coverage
with zero failure classes.

Highlights from that smoke:

- Package compatibility reported `com.unity.inputsystem@1.17.0` with matching manifest and registered versions.
- Input-action inspection returned concrete wrapper metadata:
  `generateWrapperCode=false`, `wrapperClassName=SandPrototypeControls`,
  `wrapperCodePath=Assets/Scripts/SandPrototype/SandPrototypeControls.cs`.
- Compact compatibility summary now collapses repeated `Unity.InputSystem.IntegrationTests.dll` skip lines into one informational issue with overall status `ok`.
- No-op active input handler preview/apply now returns `restartRequired=false`.
- Post-smoke usage reporting now excludes its own in-flight request, so the final `Unity_GetLensUsageReport` call does not appear as unmatched.

Remaining follow-ups:

- Payload telemetry still reports no shaping recorded.
- Phase 12 UI authoring, scene binding, and structured `RunCommand` return-value smoke is the next validation gate.

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
