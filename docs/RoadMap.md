# Roadmap

This project is evolving incrementally. The current goal is not a broad rewrite;
it is to make real Unity agent work faster, less noisy, and easier to recover
when Unity reloads, recompiles, or changes project/package state.

---

## Current State

- Standalone package id: `com.becool3000.unity-mcp-lens`.
- Preferred MCP transport: the owned `unity-mcp-lens` stdio server.
- Default model-facing tool surface: `foundation`.
- Current `foundation` baseline: `12` exported tools.
- Current `foundation + scene` baseline: `30` exported tools.
- Current Phase 8 surface: split GameObject TSAM tools for inspect, component reads, preview/apply mutation, create, and delete.
- Current Phase 10 surface: project/Input System diagnostics plus active input handler preview/apply.
- Current validation surface: static package checks plus a metadata audit in the pack-switch helper app.
- Current telemetry surface: payload stats, bridge request/response rows, tool snapshot rows, pack transition rows, and TSAM stage rows.

---

## What Recent Dogfooding Showed

The productive paths for the latest Unity work were helper scripts plus
`Unity.RunCommand`, not split scene CRUD. `Check-UnityDevSession`,
`Sync-UnityScriptChanges`, `Invoke-UnityRunCommand`, and
`Enter-UnityPlayMode` were useful for health checks, compile/idle loops,
editor probes, live ProjectSettings mutation, and play-mode smoke checks.

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

A focused Phase 10 smoke test then passed with warnings:

- Metadata audit passed.
- Export counts were `foundation=12`, `foundation+scene=30`, `project=19`, `debug=22`.
- `Unity.InputSystem.Diagnostics` verified active input handler, defines, package status, assembly/type load, devices, and a `.inputactions` asset.
- Preview/apply flows worked without `Unity.RunCommand` or YAML edits.
- Compact telemetry showed complete TSAM coverage and no Phase 10 failures.
- Follow-up remains for no-op restart-required noise and `NoShapingRecorded=true`.

---

## Near-Term Priorities

### 1. Bridge And Session Hygiene

- Reuse helper-script MCP sessions more effectively.
- Avoid unnecessary pack flips and repeated setup cycles.
- Reduce schema churn after the manifest is already known.
- Treat domain reload transport closure as expected only when the editor state explains it.

### 2. Payload Shaping Correctness

- Make `Unity.ManageEditor WaitForStableEditor` keep inline output compact.
- Store full wait attempts and full editor state behind detail refs.
- Verify payload telemetry records the shaped result, not only the raw result.
- Keep tool snapshots compact enough that routine pack switching does not dominate context cost.

### 3. Project/Input System Reliability

- Extend `Unity.InputSystem.Diagnostics` into a one-call diagnosis path for backend, defines, package version, loaded assembly/type status, device count, `.inputactions` binding/wrapper signals, and recent log errors.
- Keep `Unity.ProjectSettings.PreviewActiveInputHandler` and `Unity.ProjectSettings.SetActiveInputHandler` as the editor-authored path for active input backend changes.
- Report post-apply readback, expected define changes, and restart/reload requirements clearly.
- Suppress restart-required warnings when preview/apply is a no-op.

### 4. RunCommand And Console Result Quality

- Make `Unity.RunCommand` failure stage and `errorKind` unambiguous across validation, compilation, execution, result serialization, transport/unknown, and unexpected exceptions.
- Keep compilation, execution, and console logs compact with detail refs for full output.
- Add or improve structured recent-console reads so critical package/import errors do not require raw `Editor.log` grep.

### 5. Restart And Reload Orchestration

- Add a reliable save/quit/relaunch/reacquire workflow around the helper scripts.
- Keep any in-editor quit tool explicit about dirty state and expected transport loss.
- Prefer an external orchestrator for relaunch and bridge-ready verification, because Unity cannot report after its own process exits.

---

## Mid-Term Priorities

### 6. Prefab And Serialized Reference Authoring

- Add prefab-aware inspect/preview/apply workflows for durable prefab edits.
- Add first-class serialized reference inspect/bind/verify tools.
- Support common authoring workflows such as child rig setup, animation hook validation, and saved reference verification without custom project editor utilities.

### 7. Project Diagnostics Beyond Input

- Expand the `project` pack in the same TSAM style for package compatibility, missing scripts, reference validation, and import side effects.
- Keep these diagnostics read-first and compact by default.
- Avoid growing the default `foundation` surface.

### 8. Scene Tool Dogfooding

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
