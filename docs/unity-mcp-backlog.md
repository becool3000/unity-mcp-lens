# Unity MCP Lens Backlog

This is the repo-local backlog that Codex workflow skills should read before
starting Lens development work. It reflects the current package state and the
latest dogfood findings.

---

## Current Baselines

- `foundation` exports `12` tools.
- `foundation + scene` now targets `32` tools.
- `foundation + ui` now targets `22` tools.
- Latest completed metadata audit passed with `project=21` and `debug=22`; Phase 12 raises the expected `scene` and `ui` counts.
- Phase 8 split GameObject tools are in the `scene` pack.
- Phase 12 scene serialized-reference preview/apply binding tools are in the `scene` pack.
- Phase 12 UI hierarchy/layout preview/apply tools and `Unity.UI.VerifyScreenLayout` are in the `ui` pack.
- Phase 11 package/import/Input System diagnostics and active input handler tools are in the `project` pack.
- Project-pack additions must not widen the default `foundation` surface.
- TSAM tools must emit `normalization`, `service`, `adapter`, and `result_shaping` coverage rows.
- The helper path now distinguishes direct MCP health from wrapper degradation, and `Invoke-UnityRunCommand` can bypass idle wait in healthy play mode.

---

## Latest Phase 11 Smoke

Date: 2026-04-24

Result: passed with a residual payload-shaping warning.

Host project:

- `D:\2DUnityNewGame`
- Unity `6000.4.3f1`

Pack/export result:

- Initial active packs: `foundation`.
- Smoke active packs: `foundation + project`.
- Metadata audit: pass.
- Export counts: `foundation=12`, `foundation+scene=30`, `project=21`, `debug=22`.
- Phase 11 schemas and read-only metadata validated for:
  - `Unity_InputSystem_Diagnostics`
  - `Unity_Project_PackageCompatibility`
  - `Unity_InputActions_InspectAsset`
  - `Unity_ProjectSettings_PreviewActiveInputHandler`
  - `Unity_ProjectSettings_SetActiveInputHandler`

Package/import result:

- `Unity.Project.PackageCompatibility` returned `com.unity.inputsystem@1.17.0` with matching manifest and registered versions.
- Package assembly signals returned `3` rows and all reported `loaded=true`, `typeLoadOk=true`.
- `Unity.InputActions.InspectAsset` returned `Assets/Input/SandPrototypeControls.inputactions` with `1` map, `4` actions, `18` bindings, and `2` control schemes.
- Wrapper generation now reports concrete importer metadata:
  - `generateWrapperCode=false`
  - `wrapperClassName=SandPrototypeControls`
  - `wrapperCodePath=Assets/Scripts/SandPrototype/SandPrototypeControls.cs`

Diagnostics/preview result:

- `Unity.InputSystem.Diagnostics` returned:
  - active input handler `both`, raw value `2`, source `ProjectSettings.m_ActiveInputHandler`
  - package `com.unity.inputsystem@1.17.0`
  - assembly/type load OK
  - `3` devices: Keyboard, Mouse, Xbox Controller
  - the same `.inputactions` asset summary and wrapper metadata as the dedicated inspect tool
- `Unity.ProjectSettings.PreviewActiveInputHandler` for `both` returned `willModify=false` and `restartRequired=false`.
- `Unity.ProjectSettings.SetActiveInputHandler` for `both` remained a no-op with `applied=false` and `restartRequired=false`.

Telemetry result:

- Compact rerun span: lines `1279..1324`, `44` rows.
- Bridge churn in compact rerun: `1` connection, `0` setup cycles, `0` unmatched requests.
- Phase 11 TSAM coverage was complete for:
  - `Unity.Project.PackageCompatibility`
  - `Unity.InputActions.InspectAsset`
  - `Unity.InputSystem.Diagnostics`
  - `Unity.ProjectSettings.PreviewActiveInputHandler`
  - `Unity.ProjectSettings.SetActiveInputHandler`
- Failure classes: none.

Smoke notes:

- Package compatibility and diagnostics now collapse the repeated `Unity.InputSystem.IntegrationTests.dll` skip lines into one informational compatibility issue, and overall status stays `ok`.
- The post-smoke usage report now excludes its own in-flight request and no longer classifies the final `Unity_GetLensUsageReport` call as unmatched.
- Payload report still shows `NoShapingRecorded=true`.

---

## Latest Phase 12 Hardening Smoke

Date: 2026-04-25

Result: passed with a residual payload-shaping warning.

Host project:

- `D:\2DUnityNewGame`
- Unity `6000.4.3f1`

Pack/export and helper-path result:

- Metadata audit: pass.
- Export counts: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, `debug=22`.
- `Check-UnityDevSession.ps1` reports `DirectMcpHealthy=true` and `ProceedWithLensHelpers` in the settled idle state.
- `Sync-UnityScriptChanges.ps1` now tolerates transient helper degradation: this smoke recovered through a temporary `console` pack timeout by using direct Lens health and compact editor-state probes instead of failing the whole workflow.

UI/binding/layout result:

- `Ensure-UnityUiHierarchy.ps1` preview/apply no-op cleanly for the quick-select HUD subtree under `Quick Select Canvas`.
- `Bind-UnitySceneSerializedReferences.ps1` preview/apply no-op cleanly for `SandQuickSelectHud`.
- `Set-UnityUiLayout.ps1` preview/apply no-op cleanly for `Quick Select Panel`.

Play-mode verification result:

- `Enter-UnityPlayMode.ps1` succeeded after an expected reconnect-prone play transition.
- `Verify-UnityUiScreenLayout.ps1` passed with:
  - `inside_screen` on the panel
  - `ordered_stack` for slots
  - `below_center` for all four count labels relative to their slot cards
- `Invoke-UnityRunCommand.ps1` now bypasses helper-side idle wait in healthy play mode and returned structured `returnedData` inline:
  - `panelIsRightOfMap=true`
  - `panelGapFromMap=24`

Telemetry result:

- Focused rerun scope: lines `1432..1790`, `358` rows.
- Bridge churn in focused rerun: `25` connections, `0` setup cycles, `0` unmatched requests.
- Full TSAM coverage with zero failure rows for:
  - `Unity.UI.PreviewEnsureHierarchy`
  - `Unity.UI.ApplyEnsureHierarchy`
  - `Unity.Scene.PreviewBindSerializedReferences`
  - `Unity.Scene.ApplyBindSerializedReferences`
  - `Unity.UI.PreviewLayoutProperties`
  - `Unity.UI.ApplyLayoutProperties`
  - `Unity.UI.VerifyScreenLayout`
- Failure classes in scope: one `coverage_bridge_command_response` row for `Unity_ManageEditor` with `disposed_transport` during a reconnect-prone play transition.

Smoke notes:

- The new center-based verify relation fixed the HUD count-label case without weakening strict `below`.
- `Invoke-UnityRunCommand` preflight now keys off direct `Unity.GetLensHealth` plus compact editor state, not stale reconnect-classification state.
- Payload report still shows `NoShapingRecorded=true`.

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
- Latest Phase 11 smoke still reported `NoShapingRecorded=true`.
- `Unity_ManageEditor` emitted payload rows above `220 KB`.
- Tool snapshots contributed about `2.50 MB` raw payload across `29` rows.

Work:

- Verify whether telemetry records shaped payloads or raw payloads.
- Keep `Unity.ManageEditor WaitForStableEditor` inline output compact.
- Store full attempts and full editor state behind detail refs.
- Reduce routine tool snapshot payload cost.

---

## P1

### UI Authoring And Structured Probe Returns

Current tools:

- `Unity.UI.PreviewEnsureHierarchy`
- `Unity.UI.ApplyEnsureHierarchy`
- `Unity.UI.PreviewLayoutProperties`
- `Unity.UI.ApplyLayoutProperties`
- `Unity.UI.VerifyScreenLayout`
- `Unity.Scene.PreviewBindSerializedReferences`
- `Unity.Scene.ApplyBindSerializedReferences`

Work:

- Dogfood the full Phase 12 HUD authoring flow in `D:\2DUnityNewGame` without custom editor C#.
- Keep no-op apply responses truly clean: `applied=false`, no unnecessary dirty/save.
- Make `Unity.RunCommand` structured `ReturnResult(...)` the preferred probe return path over console-warning abuse.
- Keep helper-driven runtime probes play-aware so healthy play mode does not get blocked by idle-wait wrappers.
- Investigate the remaining reconnect-prone `Unity_ManageEditor` disposed-transport row in the focused play-mode scope.

### Input System Diagnosis

Current tools:

- `Unity.InputSystem.Diagnostics`
- `Unity.Project.PackageCompatibility`
- `Unity.InputActions.InspectAsset`
- `Unity.ProjectSettings.PreviewActiveInputHandler`
- `Unity.ProjectSettings.SetActiveInputHandler`

Work:

- Keep benign repeated package log-skip lines collapsed to informational issues rather than warning/error compatibility status.
- Decide whether package assembly filtering should exclude doc/sample-style asmdefs such as `DocCodeSamples.Tests` from the default compatibility surface.
- Keep package/import/Input System diagnosis read-only and compact so these tools stay preferred over raw `Editor.log` grep and custom `Unity.RunCommand` probes.
- Keep active input backend changes editor-authored through PlayerSettings/SerializedObject, with preview, readback, save state, restart/reload warning, and expected define signals.

### RunCommand And Console Results

Work:

- Make `Unity.RunCommand` failure stage and `errorKind` consistent across validation, compilation, execution, result serialization, transport/unknown, and unexpected exceptions.
- Compact compilation, execution, and console logs by default.
- Store full logs behind detail refs.
- Add or improve structured recent-console reads so package/import errors do not require raw `Editor.log` grep.
- Keep the play-mode helper bypass keyed to direct Lens health and compact editor state, even when `IsPlayingOrWillChangePlaymode` keeps the editor-stability label at `play_transition`.

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

- Expand project-pack TSAM diagnostics beyond Input System to missing scripts, reference validation, and import side effects.
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
