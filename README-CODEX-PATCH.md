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
- Treat `Check-UnityDevSession` as a split signal: `ProceedWithLensHelpers` means the helper path is healthy, while `ProceedWithDirectLensTools` means direct MCP is healthy and only the wrapper path is degraded.
- Prefer the Phase 11 `project` tools for package/import/Input System and active input handler work before custom `Unity_RunCommand` probes, raw `Editor.log` grep, or YAML edits.
- Prefer the Phase 12 `ui` and split `scene` binding tools for persistent HUD hierarchy, serialized scene references, and screen-layout verification before custom `Unity_RunCommand` editor scripts.
- Prefer `Invoke-UnityMcpBatch` for repeated smoke/workflow checks that span project, ui, scene, and debug packs, so one Lens session can cover the ordered steps.
- Prefer helper-driven `Invoke-UnityRunCommand` for runtime probes now that it can bypass idle-wait gating in healthy play mode and preserve structured `ReturnResult(...)` payloads.
- Treat `Unity.RunCommand` and `Unity.ReadConsole` inline logs as compact previews. Use `logSummary` and `Unity.ReadDetailRef` only when full log text or full scanned console entries are needed.
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

- Use the batch helper for repeated smoke/workflow calls instead of separate helper processes when the steps are known up front.
- Continue payload shaping for remaining editor-state edge cases now that the large TSAM targets and log-heavy probe/console paths show measurable savings.
- Reduce noisy repeated package/editor-log signals so healthy compatibility reads stay high signal.
- Keep `Unity.ProjectSettings.PreviewActiveInputHandler` and `Unity.ProjectSettings.SetActiveInputHandler` as the editor-authored backend change path.
- Keep `Unity.Project.PackageCompatibility` and `Unity.InputActions.InspectAsset` as the preferred package/import read surface before raw `Editor.log` grep.
- Keep `Unity.RunCommand` failure-stage metadata, detail refs, compact `logSummary`, and structured `ReturnResult(...)` output stable.
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

Latest Phase 12 hardening smoke on 2026-04-25:

- Metadata audit passed with `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`.
- `Sync-UnityScriptChanges.ps1` recovered through a transient `console` pack timeout by falling back to direct Lens health and compact editor-state probes instead of failing the workflow.
- Helper-driven `Ensure-UnityUiHierarchy.ps1`, `Bind-UnitySceneSerializedReferences.ps1`, and `Set-UnityUiLayout.ps1` all no-op cleanly with `applied=false` and `willModify=false` on the existing quick-select HUD.
- `Verify-UnityUiScreenLayout.ps1` passed in play mode using `inside_screen`, `ordered_stack`, and the new `below_center` relation for slot count labels.
- `Invoke-UnityRunCommand.ps1` now bypasses idle wait in healthy play mode, returns `playModeBypass.applied=true`, and surfaced structured `returnedData` with `panelIsRightOfMap=true` and `panelGapFromMap=24`.

Latest Phase 13 payload-shaping smoke on 2026-04-26:

- Focused scope lines `2201..2446` contained `244` rows with `68` payload rows and `176` TSAM coverage rows.
- Payload telemetry now reports `NoShapingRecorded=false`, with `210,510` raw bytes shaped to `120,867` bytes and `89,643` bytes saved (`42.58%`).
- Top savings were `Bridge.RefreshToolsSnapshotIfNeeded` snapshot shaping: `100,016` raw bytes to `9,481` shaped bytes, saving `90,535` bytes (`90.52%`).
- `Check-UnityDevSession.ps1` returned `ProceedWithLensHelpers` with `DirectMcpHealthy=true` after the reload window settled.
- `Sync-UnityScriptChanges.ps1` completed a forced refresh and recovered via direct Lens health.
- Helper-driven UI hierarchy, scene binding, and layout no-op apply paths still return `willModify=false` and `applied=false`.
- `Unity.UI.VerifyScreenLayout` passed in edit mode with `inside_screen`, `ordered_stack`, and `below_center`.

Latest Phase 14 compact-result and batch-helper smoke on 2026-04-26:

- Metadata audit passed with unchanged baselines: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`.
- The batch helper ran `9` ordered project/ui/scene/debug steps in one workflow and completed successfully.
- Focused scope from fresh marker line `2592` contained `98` rows with `51` payload rows and `47` TSAM coverage rows.
- Payload telemetry reported `NoShapingRecorded=false`, with `50,566` raw bytes shaped to `24,025` bytes and `26,541` bytes saved (`52.49%`).
- `PayloadRowsWithSavings=7`; savings now include Input System diagnostics, UI hierarchy preview/apply, scene binding preview/apply, UI verify, and usage-report results.
- Batch control-plane churn was `3` connections, `6` schema requests, `4` pack transitions, `0` tool snapshot rows, `0` unmatched requests, and `0` failure rows.
- `Unity.ReadDetailRef` successfully read a full compacted scene-binding result detail, confirming the compact inline result did not discard audit data.

Remaining follow-ups:

- Continue compact shaping for remaining `Unity.ManageEditor` edge cases.
- Normalize `Unity.ReadDetailRef` handling in the batch helper; direct MCP reads already resolve detail refs correctly, but the batch helper currently treats the unwrapped structured detail payload as a failed step.
- Individual helper scripts still open separate sessions; prefer `Invoke-UnityMcpBatch` when a smoke/workflow has multiple known steps.
- Some lower-level `tool_execution` rows still report `rawBytes == shapedBytes` because they record already-compacted responses; use `tool_result` savings rows for compact-result proof.

Latest Phase 15 log-compaction smoke on 2026-04-26:

- Metadata audit stayed unchanged: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`.
- Focused happy-path scope from fresh marker line `262` contained `27` rows with `6` payload rows and `21` coverage rows.
- Payload telemetry reported `NoShapingRecorded=false`, with `56,370` raw bytes shaped to `39,650` bytes and `16,720` bytes saved (`29.66%`).
- `Unity.RunCommand` saved `11,433` bytes (`65.69%`) in an explicit `tool_result` row while preserving `80` execution log lines and `40` captured console warning lines behind detail refs.
- `Unity.ReadConsole` summary saved `2,219` bytes (`77.00%`) with grouped inline rows and full scanned entries behind a detail ref.
- Direct `Unity.ReadDetailRef` resolved both the RunCommand execution-log detail payload and the ReadConsole full scanned-entry payload.
- Expected-failure smoke confirmed `failureStage=compilation`, `failureStage=execution`, and `failureStage=result_serialization` with stable `errorKind` values and compact log summaries.

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
