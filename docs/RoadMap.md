# Roadmap

This project is evolving incrementally. The current goal is not a broad rewrite;
it is to make real Unity agent work faster, less noisy, and easier to recover
when Unity reloads, recompiles, or changes project/package state.

---

## Current State

- Standalone package id: `com.becool3000.unity-mcp-lens`.
- Preferred MCP transport: the owned `unity-mcp-lens` stdio server.
- Default Codex model-facing tool surface: `static_all` (`foundation+full`); raw host default remains dynamic packs.
- Current `foundation` baseline: `17` exported tools.
- Current `foundation + scene` baseline: `41` exported tools.
- Current `foundation + ui` baseline: `33` exported tools.
- Current `foundation + runtime` baseline: `23` exported tools.
- Static-all host surface: `UNITY_MCP_LENS_TOOL_SURFACE_MODE=static_all` exposes `foundation+full` at startup while agents navigate through `Unity.Tools.Menu`.
- Current Phase 8 surface: split GameObject TSAM tools for inspect, component reads, preview/apply mutation, create, and delete.
- Current Phase 12 surface: split UI hierarchy/layout preview/apply, scene serialized-reference preview/apply binding, UI screen-layout verification, center-based UI verify relations, and structured `Unity.RunCommand` return values.
- Current Phase 17+ surface: UI canvas prefab preview/apply, runtime UI query/invoke tools, scene prefab instantiate/bind preview/apply, UI raycast/layout verification, Game view resolution-matrix layout verification, scene serialized-reference verification, play-mode pointer/scroll smoke verification, explicit play-mode exit, and compact usage-report pack-transition summaries.
- Current Phase 11 surface: project/Input System diagnostics, package compatibility, input-action asset inspection, and active input handler preview/apply.
- Current validation surface: static package checks plus a metadata audit in the pack-switch helper app.
- Current telemetry surface: payload stats, bridge request/response rows, compact `tool_result` savings rows, tool snapshot rows, pack transition rows, detail-ref rows, and TSAM stage rows.

---

## What Recent Dogfooding Showed

The productive paths for the latest Unity work were helper scripts plus
`Unity.RunCommand`, not split scene CRUD. `Check-UnityDevSession`,
`Sync-UnityScriptChanges`, `Invoke-UnityRunCommand`, and
`Enter-UnityPlayMode` were useful for health checks, compile/idle loops,
editor probes, live ProjectSettings mutation, and play-mode smoke checks.

The 2026-04-25 hardening pass improved the helper path itself:

- `Check-UnityDevSession` now separates direct MCP health from helper-path health and can recommend `ProceedWithDirectLensTools` when wrappers degrade but the bridge is still usable.
- `Sync-UnityScriptChanges` no longer fails up front on a transient `console` pack restore; it can wait through the reload cycle and recover through direct Lens health.
- `Invoke-UnityRunCommand` now skips helper-side idle gating in healthy play mode and still returns structured `ReturnResult(...)` payloads.
- `Unity.UI.VerifyScreenLayout` now supports `right_of_center`, `left_of_center`, `above_center`, and `below_center` in addition to the strict non-overlap relations.
- `Unity.UI.VerifyScreenLayoutMatrix` verifies the same layout assertions across fixed Game view resolutions and restores the original selected size by default.
- `Unity.Scene.VerifySerializedReferences` distinguishes prefab-inherited refs, local overrides, local null overrides, actual nulls, and non-prefab scene refs.
- `Unity.PlayMode.PointerInputSmoke` now supports scroll input plus optional gameplay-state sampling/assertions.
- `Unity.Editor.ExitPlayMode` and `Exit-UnityPlayMode.ps1` provide first-class play cleanup with recovered-transition reporting.

The largest delays came from project-state and lifecycle work:

- Input System failures required combining probes, `Editor.log` grep, package pinning, and asset edits before the root causes were clear.
- YAML edits to `ProjectSettings.asset` did not update Unity's live serialized PlayerSettings state or scripting defines.
- Restarting after package/settings changes required OS-level process recovery because menu quit timed out.
- `Unity.RunCommand` sometimes reported confusing stage metadata when execution failed after successful compilation.
- Prefab authoring, serialized reference binding, and durable tweakable character prefab work still lacked a comfortable Lens workflow.

The latest usage report also showed operational cost to attack next:

- `34` bridge connections.
- `127` setup cycles.
- `184` pack-set transitions.
- `356` `get_tool_schema` requests.
- `0.00%` recorded payload shaping savings on eligible payload rows.
- Large `Unity.ManageEditor` rows above `220 KB`.
- `Unity.ProjectSettings.*` and `Unity.InputSystem.Diagnostics` emitted complete TSAM stage coverage for the exercised calls.

A focused Phase 11 smoke test then passed with a residual payload-shaping warning:

- Metadata audit passed.
- Export counts were `foundation=12`, `foundation+scene=30`, `project=21`, `debug=22`.
- `Unity.Project.PackageCompatibility` and `Unity.InputActions.InspectAsset` worked end to end in a real Unity host project.
- `Unity.InputSystem.Diagnostics` now verified active input handler, package status, assembly/type load, devices, `.inputactions` summary, wrapper metadata, and compatibility signals in one call.
- Known benign repeated `Unity.InputSystem.IntegrationTests.dll` log-skip lines are now collapsed to one informational compatibility issue, so healthy package diagnostics stay `ok`.
- Preview/apply no-op flows now return `restartRequired=false` with no restart warning noise.
- The post-smoke usage report now excludes its own in-flight request, so the final `Unity_GetLensUsageReport` call no longer appears as unmatched in scope.
- Compact telemetry showed complete TSAM coverage and no Phase 11 failures.
- Follow-up remains for `NoShapingRecorded=true` and possible default filtering for doc/sample/test-support package asmdefs.

A focused helper-driven Phase 12 hardening smoke then passed with a residual payload-shaping warning:

- Metadata audit passed with `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`.
- Helper-driven no-op preview/apply flows for UI hierarchy, scene binding, and UI layout all returned `applied=false` and `willModify=false`.
- `Verify-UnityUiScreenLayout` passed in play mode using `inside_screen`, `ordered_stack`, and `below_center`.
- `Invoke-UnityRunCommand` returned structured HUD placement data through the helper path with `playModeBypass.applied=true`.
- The focused usage-report slice still recorded `NoShapingRecorded=true`; its only failure class was a `Unity_ManageEditor` disposed-transport response during a reconnect-prone play transition.

A focused Phase 13 payload-shaping smoke then proved the first measurable
payload accounting win:

- Scope contained `244` rows with `68` payload rows and `176` TSAM coverage rows.
- Payload size was `210,510` raw bytes -> `120,867` shaped bytes, saving `89,643` bytes (`42.58%`).
- `NoShapingRecorded=false`.
- Tool snapshot shaping reduced `100,016` raw bytes to `9,481` shaped bytes.
- Residual helper churn remained: `12` connections, `25` schema requests, and `12` pack transitions.

A focused Phase 14 compact-result smoke then reduced both result size and
control-plane churn:

- Scope contained `98` rows with `51` payload rows and `47` TSAM coverage rows.
- Payload size was `50,566` raw bytes -> `24,025` shaped bytes, saving `26,541` bytes (`52.49%`).
- `PayloadRowsWithSavings=7` across Input System diagnostics, UI hierarchy preview/apply, scene binding preview/apply, UI verify, and usage reporting.
- The new batch helper ran `9` ordered project/ui/scene/debug steps with `3` connections, `6` schema requests, `4` pack transitions, `0` unmatched requests, and `0` failure rows.
- Detail-ref readback was verified for a compacted full scene-binding result.

A focused Phase 15 compact-log smoke then covered the log-heavy probe path:

- Scope contained `27` rows with `6` payload rows and `21` coverage rows.
- Payload size was `56,370` raw bytes -> `39,650` shaped bytes, saving `16,720` bytes (`29.66%`).
- `Unity.RunCommand` saved `11,433` bytes (`65.69%`) while preserving `80` execution log lines, `40` captured console warning lines, and structured `returnedData`.
- `Unity.ReadConsole` summary saved `2,219` bytes (`77.00%`) with grouped inline rows and full scanned entries behind `detailRef`.
- Direct `Unity.ReadDetailRef` read back both RunCommand and ReadConsole detail payloads.
- Expected-failure smoke confirmed stable compilation, execution, and result-serialization failure stages and error kinds.

A focused Phase 16 smoke then moved the active long-running smoke host to
`D:\TintPaint` and covered batch detail-ref normalization plus editor-stability
shaping:

- Metadata audit passed with unchanged baselines: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`.
- `Check-UnityDevSession` settled to `ProceedWithLensHelpers` after an initial recoverable play-transition window.
- Payload size was `60,520` raw bytes -> `48,101` shaped bytes, saving `12,419` bytes (`20.52%`).
- `Unity.ManageEditor.WaitForStableEditor` saved `2,498` bytes (`62.69%`) with compact inline stability state and detail refs for attempts/full state.
- `Invoke-UnityMcpBatch` now handles unwrapped `Unity.ReadDetailRef` structured payloads as successful steps and summarizes large detail payloads instead of inlining them.

The first longer TintPaint dogfood session then showed the savings are material
at real workflow scale:

- Scope contained `1999` rows with `216` payload rows and `1783` coverage rows.
- Payload size was `3,012,895` raw bytes -> `650,430` shaped bytes, saving `2,362,465` bytes (`78.41%`).
- `Unity.ManageEditor.WaitForStableEditor` alone saved `727,569` bytes (`96.51%`) across repeated waits.
- The main workflow gaps were durable uGUI prefab creation, scene prefab instantiation/binding, UI raycast verification, pointer-input smoke verification, and usage-report presentation for large pack-transition lists and TSAM coverage summaries; the TintPaint Brush HUD follow-up adds first-class tools for these UI/input/reference/play-exit gaps, with hardening still needed through live dogfood.

---

## Near-Term Priorities

### 1. Bridge And Session Hygiene

- Use `Invoke-UnityMcpBatch` when a smoke or workflow has multiple known steps that can share one Lens session.
- Avoid unnecessary pack flips and repeated setup cycles.
- Reduce schema churn after the manifest is already known.
- Treat domain reload transport closure as expected only when the editor state explains it.
- Keep helper-path degradation distinct from direct bridge health so wrappers stop escalating recoverable play/reload states into false failures.
- Keep installed helper cache guidance clear so agents use the repo-local helper path when cache copies lag behind the branch.

### 2. Payload Shaping Correctness

- Keep compact TSAM results as the default for high-volume preview/apply and diagnostic tools.
- Store full wait attempts, editor state, bindings, devices, logs, and measured geometry behind detail refs when inline data is enough for pass/fail decisions.
- Continue compact shaping for remaining editor-state edge cases.
- Keep telemetry presentation clear when `tool_execution` rows record already-compacted responses and explicit `tool_result` rows carry the savings proof.
- Keep batch-helper detail-ref summaries compact so passing smokes do not inline large payloads.
- Summarize large `packSetTransitions` arrays inline and expose full transition rows behind detail refs.
- Fix usage-report TSAM coverage presentation when coverage rows exist but `tsamCoverage` is empty.

### 3. Project/Package Diagnostic Follow-Through

- Keep `Unity.InputSystem.Diagnostics`, `Unity.Project.PackageCompatibility`, and `Unity.InputActions.InspectAsset` as the first-stop read-only diagnosis path before raw `Editor.log` grep or custom `Unity.RunCommand` probes.
- Keep known benign repeated package log-skip lines informational rather than warning/error compatibility status.
- Decide whether default package assembly filtering should exclude doc/sample/test-support asmdefs from the compact compatibility view.
- Keep `Unity.ProjectSettings.PreviewActiveInputHandler` and `Unity.ProjectSettings.SetActiveInputHandler` as the editor-authored path for active input backend changes.

### 4. RunCommand And Console Result Quality

- Keep `Unity.RunCommand` failure stage and `errorKind` unambiguous across validation, compilation, execution, result serialization, transport/unknown, and unexpected exceptions.
- Dogfood `ExecutionResult.ReturnResult(...)` so focused probes no longer rely on warning-level console output to return structured measurements.
- Keep compilation, execution, and console logs compact with detail refs for full output.
- Add or improve structured recent-console reads so critical package/import errors do not require raw `Editor.log` grep.
- Keep the play-aware helper preflight keyed to direct Lens health plus compact editor state, not to stale reconnect-classification state.

### 5. UI Authoring Dogfood And Recovery

- Replace custom editor-C# HUD authoring flows with the new Phase 12 UI and scene binding tools.
- Turn recurring custom `Unity.RunCommand` UI authoring scripts into TSAM preview/apply tools, starting with durable uGUI prefab creation and scene prefab instantiation/binding.
- Add read-only UI raycast/layout verification for hit-test stacks, UI blocking, and overlap checks.
- Keep `ui`-pack preview/apply flows deterministic and compact.
- Keep helper-driven verification stable for `Unity.UI.VerifyScreenLayout`, including center-based relations for in-card labels and similar HUD layouts.

### 6. Runtime Input Verification

- Add play-mode pointer-input smoke tooling for Input System mouse/touch paths.
- Report observed input state, UI hit, world hit, and sampled gameplay result in compact verify output.
- Keep direct gameplay-method calls as a fallback only when synthetic input cannot prove the actual input path.

### 7. Restart And Reload Orchestration

- Add a reliable save/quit/relaunch/reacquire workflow around the helper scripts.
- Keep any in-editor quit tool explicit about dirty state and expected transport loss.
- Prefer an external orchestrator for relaunch and bridge-ready verification, because Unity cannot report after its own process exits.

---

## Mid-Term Priorities

### 8. Prefab And Serialized Reference Authoring

- Add prefab-aware inspect/preview/apply workflows for durable prefab edits.
- Add first-class serialized reference inspect/bind/verify tools.
- Support common authoring workflows such as child rig setup, animation hook validation, and saved reference verification without custom project editor utilities.

### 9. Project Diagnostics Beyond Input

- Expand the `project` pack in the same TSAM style for missing scripts, reference validation, and import side effects now that package compatibility and input-action inspection are covered.
- Keep these diagnostics read-first and compact by default.
- Avoid growing the default `foundation` surface.

### 10. Scene Tool Dogfooding

- Exercise the split GameObject TSAM tools on real scene work before expanding them further.
- Keep `Unity.ManageGameObject` as a compatibility fallback until split coverage has been proven on practical authoring tasks.

---

## Deferred

- More scene CRUD breadth before project-state reliability improves.
- Large visual debugging surfaces before console, serialized reference, and prefab workflows are stable.
- Full architectural rewrites.

---

## Definition Of Progress

Progress is:

- fewer bridge reconnects and setup cycles during ordinary work
- fewer schema requests and pack transitions per task
- smaller shaped payloads for common health, wait, and editor-state calls
- one-call diagnosis for common Input System and package-state failures
- clearer tool failure stages and recoverable detail refs
- fewer custom `RunCommand` snippets for project settings, prefab, and reference workflows
