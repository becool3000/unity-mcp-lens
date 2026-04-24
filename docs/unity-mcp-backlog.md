# Unity MCP Lens Backlog

This is the repo-local backlog that Codex workflow skills should read before
starting Lens development work. It reflects the current package state and the
latest dogfood findings.

---

## Current Baselines

- `foundation` exports `12` tools.
- `foundation + scene` exports `30` tools.
- Latest Phase 10 metadata audit passed with `project=19` and `debug=22`.
- Phase 8 split GameObject tools are in the `scene` pack.
- Phase 10 Input System and active input handler tools are in the `project` pack.
- Project-pack additions must not widen the default `foundation` surface.
- TSAM tools must emit `normalization`, `service`, `adapter`, and `result_shaping` coverage rows.

---

## Latest Phase 10 Smoke

Date: 2026-04-24

Result: passed with warnings.

Host project:

- `D:\2DUnityNewGame`
- Unity `6000.4.3f1`

Pack/export result:

- Initial active packs: `foundation`.
- Smoke active packs: `foundation + project`.
- Metadata audit: pass.
- Export counts: `foundation=12`, `foundation+scene=30`, `project=19`, `debug=22`.
- Phase 10 schemas and read-only metadata validated for:
  - `Unity_InputSystem_Diagnostics`
  - `Unity_ProjectSettings_PreviewActiveInputHandler`
  - `Unity_ProjectSettings_SetActiveInputHandler`

Diagnostics result:

- Active input handler: `both`, raw value `2`, source `ProjectSettings.m_ActiveInputHandler`.
- Input System package: `com.unity.inputsystem@1.17.0`.
- Assembly/type load: OK.
- Devices found: Keyboard, Mouse, Xbox Controller.
- Asset checked: `Assets/Input/SandPrototypeControls.inputactions`.
- Asset summary: `1` map, `4` actions, `18` bindings, `2` control schemes.

Preview/apply result:

- `legacy`: current `both`, requested `legacy`, `willModify=true`.
- `inputSystem`: current `both`, requested `inputSystem`, `willModify=true`.
- `both`: current `both`, requested `both`, `willModify=false`.
- Safe apply used `mode=both`, `save=true`, `requestScriptReload=false`.
- Apply was a no-op and readback stayed `both`.

Telemetry result:

- Compact rerun span: lines `236..314`, `78` rows.
- Bridge churn in compact rerun: `3` connections, `2` pack transitions, `0` setup cycles, `4` schema requests.
- Phase 10 TSAM coverage was complete for diagnostics, preview, and set.
- Phase 10 failure classes: none.

Smoke warnings:

- No-op `both` preview/apply still reports `restartRequired=true` and `restart_required`, which is noisy when `willModify=false` or `applied=false`.
- Payload report still shows `NoShapingRecorded=true`; expanded diagnostics was about `4.4 KB` raw/shaped.
- The session-check helper path is `unity-dev-assistant/scripts/Check-UnityDevSession.*`, not under `unity-mcp-bridge/scripts`.
- Host project already had `ProjectSettings/ProjectSettings.asset` changed from `activeInputHandler: 0` to `2`; timestamp predates the smoke.

---

## P0

### Bridge And Helper Session Churn

Observed dogfood signals:

- `34` bridge connections.
- `127` setup cycles.
- `184` pack-set transitions.
- `356` `get_tool_schema` requests.

Work:

- Reuse helper-script MCP sessions where possible.
- Avoid pack changes when the requested pack set is already active.
- Avoid repeated schema pulls when the tool snapshot hash has not changed.
- Make reload/play transition transport closures clear instead of alarming.

### Payload Shaping

Observed dogfood signals:

- `0.00%` recorded shaping savings.
- Latest Phase 10 smoke still reported `NoShapingRecorded=true`.
- `Unity_ManageEditor` emitted payload rows above `220 KB`.
- Tool snapshots contributed about `2.50 MB` raw payload across `29` rows.

Work:

- Verify whether telemetry records shaped payloads or raw payloads.
- Keep `Unity.ManageEditor WaitForStableEditor` inline output compact.
- Store full attempts and full editor state behind detail refs.
- Reduce routine tool snapshot payload cost.

---

## P1

### Input System Diagnosis

Current tools:

- `Unity.InputSystem.Diagnostics`
- `Unity.ProjectSettings.PreviewActiveInputHandler`
- `Unity.ProjectSettings.SetActiveInputHandler`

Work:

- Suppress restart-required messaging for active input handler no-op preview/apply results.
- Extend diagnostics to cover package version/source/cache path, editor compatibility signals, assembly/type-load errors, scripting defines, device count, keyboard/gamepad presence, `.inputactions` maps/bindings, wrapper generation status, and recent Input System editor-log signals.
- Keep active input backend changes editor-authored through PlayerSettings/SerializedObject, with preview, readback, save state, restart/reload warning, and expected define signals.

### RunCommand And Console Results

Work:

- Make `Unity.RunCommand` failure stage and `errorKind` consistent across validation, compilation, execution, result serialization, transport/unknown, and unexpected exceptions.
- Compact compilation, execution, and console logs by default.
- Store full logs behind detail refs.
- Add or improve structured recent-console reads so package/import errors do not require raw `Editor.log` grep.

### Restart And Reload Orchestration

Work:

- Add save/dirty handling before reload or quit.
- Treat domain reload and process exit as expected transport-loss windows.
- Reacquire the editor and bridge after restart.
- Keep relaunch orchestration outside Unity where possible because the exiting editor cannot report its own restart.

---

## P2

### Prefab And Serialized Reference Authoring

Work:

- Add prefab-aware inspect/preview/apply workflows.
- Add serialized reference inspect/bind/verify tools.
- Support durable player/character prefab authoring, child rig setup, tweak point exposure, and saved reference validation.

### Project Diagnostics Beyond Input

Work:

- Expand project-pack TSAM diagnostics for package compatibility, missing scripts, reference validation, and import side effects.
- Keep results compact and read-first.

### Scene Tool Dogfooding

Work:

- Exercise split GameObject tools on real scene authoring.
- Keep `Unity.ManageGameObject` as a compatibility fallback until split coverage is proven.

---

## Deferred

- Broad new scene CRUD before project-state reliability improves.
- Large visual debugging features before console, prefab, and serialized reference workflows improve.
- Full architecture rewrites.
