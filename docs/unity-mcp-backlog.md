# Unity MCP Lens Backlog

This is the repo-local backlog that Codex workflow skills should read before
starting Lens development work. It reflects the current package state and the
latest dogfood findings.

---

## Latest BeeSurvivors Dynamic Surface Dogfood Finding

Date: 2026-05-08

Host project:

- `D:\BeeSurvivors`
- Active scene `RoachWars`

Finding:

- Lens was usable end-to-end for health, pack listing, console reads, project info, script validation, script sync, play entry, runtime advancement, play exit, and final readiness.
- Codex dynamic tool exposure remained unreliable after pack switches: `Unity.Tools.Describe` proved tools such as `Unity.Editor.SetPlayMode` and `Unity.Editor.SyncScripts` existed, but Codex did not expose them as callable in the active thread.
- The previous `0.1.1` installed skill path was stale after plugin cache cleanup; plugin cache versions can move after source plugin refreshes, with `0.1.3` carrying the static-all/menu skill guidance.
- `Unity.Editor.SyncScripts` could report failure when refresh was scheduled and old console errors were already present, even when no new compile/import errors were proven.
- Play entry recovered from a transient `Pipe is broken` pack-restore/readiness poll, but helper output still included too much nested recovery state for routine dogfood reports.

Implemented response in this branch:

- `Unity.Editor.SyncScripts` now separates initial/final/new console error counts. Stale pre-existing console errors are warnings, while only newly counted console errors set `consoleErrorsDetected=true`.
- `Unity.Editor.SyncScripts` now emits an explicit `adapter` TSAM stage to close the observed `n/s/a/r` coverage gap for script sync.
- `Unity.SetToolPacks` responses now include `clientSurface` guidance and whether the native host emitted `notifications/tools/list_changed`, making client-side dynamic-indexing failures easier to classify.
- `Sync-UnityScriptChanges.js` now surfaces the new/stale console-error fields from `Unity.Editor.SyncScripts`.
- `Enter-UnityPlayMode.js` and `Enter-UnityPlayMode.ps1` now default successful runs to compact play-request/readiness summaries, with full nested detail available via `--IncludeDetails` / `-IncludeDetails` or automatically on failure.

Still needed:

- Codex Desktop still needs client-side verification/fix for dynamic tool indexing after `notifications/tools/list_changed`; Lens can emit the notification and provide manifest evidence, but cannot force Codex to update the current thread's callable tool table.
- Add a focused smoke that activates `runtime`, checks `Unity.Tools.Describe(Unity.Editor.SetPlayMode)`, captures whether Codex/tool_search exposes the callable, and records the client-side result separately from raw host correctness.

---

## Latest BeeSurvivors Dogfood Finding

Date: 2026-05-07

Host project:

- `D:\BeeSurvivors`
- Branch `BeeSurvivorsRoachWars`
- Unity `6000.4.5f1`
- Active scene `RoachWars`

Finding:

- Helper workflows were usable, but direct model-facing tools repeatedly returned `Pipe is broken` while helper health and batch calls reported the bridge/editor were ready.
- `Invoke-UnityMcpBatch.ps1 -StepsPath` worked; `-StepsJson` was fragile for multi-line JSON on Windows.
- PowerShell helper boolean parameters such as `-WaitForEditorIdle` and `-IncludeInactive` were fragile in nested calls.
- `Sync-UnityScriptChanges.ps1` could succeed after recovery while burying forced-cycle pipe/beacon failures in a large payload.

Implemented response in this branch:

- Added `Unity.Tools.Describe` as a foundation read-only live manifest/schema/pack-requirement description tool.
- Added `Unity.Tools.ActivateAndVerify` as a foundation mutating fallback that activates packs and reports host-visible expected-tool verification for client dynamic-indexing drift.
- Added `Unity.Editor.SyncScripts` as the scripting-pack deterministic script refresh/compile wait path; no changed paths with no force is a fast no-op.
- Added `Unity.Editor.SetPlayMode` as the runtime-pack high-level enter/exit workflow with transition, runtime-advance, reconnect, console-error, and elapsed-time evidence.
- Updated helper wrappers so script sync and play-mode enter/exit use the new tools and rely on per-session pack auto-priming.
- Reduced routine `detailRef` noise through an opt-in compactor threshold for `Unity.ReadConsole` and `Unity.RunCommand`; default compactor behavior remains unchanged.
- Documented that the old editor-status beacon may be retired/missing, helper pack state is per session/process, and stale Codex dynamic indexing should fall back to helper calls or `Unity.Tools.Describe`.

Earlier implemented response:

- Native Lens host now treats stale bridge pipe failures on safe/read-only direct tools as reconnectable and retries once.
- Direct host bridge discovery now excludes stale ready status files, quarantines just-failed pipes/status files for 30 seconds, restores active packs after reconnect, and reports host/bridge diagnostics in structured transport errors.
- Mutating direct tools no longer retry after a request may have reached Unity; they return `UNITY_MCP_TRANSPORT_ERROR` with `retrySafe=false` and `maybeApplied=true`.
- Lens server install refresh now republishes/copies even when metadata versions match, and repo-dev source newer than bundled prebuilt forces a source publish instead of reinstalling a stale prebuilt.
- `Check-UnityDevSession` now treats fresh successful Lens health authority probes as ready even when stale Editor.log handshake/auth lines are still visible in the diagnostic tail.
- After Codex reload, direct model-facing Lens calls succeeded against `D:\BeeSurvivors`: `Unity.GetLensHealth`, `Unity.ListToolPacks`, `Unity.SetToolPacks` to `foundation+assets`, and `Unity.ListResources` for `Assets/Data/RoachWars`.
- PowerShell helper boolean parameters now normalize common nested-call values such as `$true`, `true`, `1`, `yes`, and switch-style values.
- PowerShell batch wrapper writes `-StepsJson` to a temp steps file before invoking Node, and the Node parser gives Windows-specific guidance on JSON parse failures.
- Script sync output now includes top-level `warnings`, `warningCount`, `warningSummary`, and `recoveredWithWarnings` when recovery hid transient pipe/beacon failures.

Implemented response in Phase 18:

- Asset-pack tools now cover sprite-sheet import/slicing/binding and sprite-array binding verification:
  - `Unity.Asset.PreviewImportSpriteSheetAndBind`
  - `Unity.Asset.ApplyImportSpriteSheetAndBind`
  - `Unity.Asset.ImportSpriteSheetAndBind` compatibility facade, preview by default and apply only with `mode=apply` or `apply=true`
  - `Unity.Asset.VerifySpriteArrayBinding`
- Live BeeSurvivors helper-batch validation passed after Unity recompiled the package:
  - helper health reported `internalRegistryToolCount=83`, editor stable, bridge ready, and active `foundation` pack.
  - `Unity.Asset.Search` found `Assets/Resources/Player/RoachBeeCoolSurvive-Sheet.png` and `Assets/Data/RoachWars/RoachPresentationConfig.asset`.
  - `Unity.Asset.PreviewImportSpriteSheetAndBind` returned compact preview data plus a `detailRef` for the Roach sheet/config binding.
  - `Unity.Asset.VerifySpriteArrayBinding` passed for Roach `PlayerFrames` using `RoachBeeCoolSurvive-Sheet`.
  - `Unity.Asset.VerifySpriteArrayBinding` passed for main `PlayerFrames` using `BeeCoolSurvive-Sheet`.
  - `Unity.GetLensUsageReport` showed complete TSAM coverage for `Unity.Asset.PreviewImportSpriteSheetAndBind` and `Unity.Asset.VerifySpriteArrayBinding`, with zero failed TSAM rows and `NoShapingRecorded=false`.

Implemented response in Phase 19:

- `Unity.Asset.VerifySpriteArrayBinding` now declares `expectedSpriteNames.items = { type: "string" }` for stricter client-compatible JSON Schema.
- Metadata audit now rejects any exported input schema property that declares `type: "array"` without an `items` schema.
- `Tools~/Test-McpDynamicToolExposure.ps1` covers the direct host sequence: initialize, foundation `tools/list`, `Unity.SetToolPacks(["assets"])`, `notifications/tools/list_changed`, and a second `tools/list` that exposes the Phase 18 asset tools.
- Required `foundation+assets=24` metadata assertions continue to include `Unity.Asset.PreviewImportSpriteSheetAndBind`, `Unity.Asset.ApplyImportSpriteSheetAndBind`, `Unity.Asset.ImportSpriteSheetAndBind`, and `Unity.Asset.VerifySpriteArrayBinding`.

Implemented response in Phase 20:

- Added `Tools~/Export-McpDynamicToolIndexingEvidence.ps1` to capture raw installed-host MCP evidence for dynamic pack switching and post-`tools/list_changed` tool exposure.
- Added `docs/codex-dynamic-tool-indexing-evidence.md` with the raw-host repro, Codex `tool_search` comparison, and interpretation rule for separating Lens host correctness from Codex client indexing behavior.
- Latest evidence points away from Lens host/bridge/schema behavior: raw `tools/list` exposes the asset verifier with strict schema, while Codex `tool_search` still does not surface it after restart.

Still needed:

- 2026-05-06 post-Unity/Codex-restart direct check: `Unity.GetLensHealth` and `Unity.SetToolPacks(["assets"])` succeeded with `foundation+assets` active, but `tool_search` still did not expose `Unity.Asset.VerifySpriteArrayBinding`.
- Raw installed-host MCP evidence after the same restart: initial `tools/list` returned `13` foundation tools, `Unity.SetToolPacks(["assets"])` succeeded with `toolCount=25`, host emitted `notifications/tools/list_changed`, follow-up `tools/list` returned `25` tools, all Phase 18 asset tools were present, and `expectedSpriteNames` included `items: { type: "string" }`.
- Escalate or investigate Codex Desktop dynamic-tool indexing/refresh after MCP `notifications/tools/list_changed`; Lens-side raw host contract now has a repeatable evidence script.
- Continue watching for `Unity_ManageEditor` transport-noise during domain reload/play transitions; safe calls now recover, but maybe-applied mutating calls intentionally require state verification before retry.

---

## Current Baselines

- `foundation` exports `18` tools, including `Unity.Tools.Menu`, `Unity.Tools.Describe`, `Unity.Tools.ActivateAndVerify`, and the host-local Script Updating Consent modal recovery tool.
- `foundation + scene` now targets `50` tools, including scene authoring primitives, object-reference assignment, explicit dirty/save tools, prefab instantiate/bind, and `Unity.Scene.FindComponents` for read-only scene component reuse discovery.
- `foundation + ui` now targets `35` tools, including `Unity.UI.QueryRuntimeLayout` and `Unity.UI.InvokeControl`.
- `foundation + runtime` now targets `29` tools, including `Unity.Editor.SetPlayMode`.
- Latest expected metadata audit baseline keeps `foundation + project=37` with component reuse discovery, package capability awareness, workflow wrappers, and active project diagnostics.
- Latest expected metadata audit baseline keeps `foundation + assets=45` with prefab authoring/overrides, presets, copy workflows, asset/resource workflows, and sprite-sheet binding tools.
- `debug` remains observed at `28` tools.
- `UNITY_MCP_LENS_TOOL_SURFACE_MODE=static_all` starts the host at `foundation+full`; `Unity.SetToolPacks` is then a no-op and agents should use `Unity.Tools.Menu` for compact navigation while calling real tools directly.
- Phase 8 split GameObject tools are in the `scene` pack.
- Phase 12 scene serialized-reference preview/apply binding tools are in the `scene` pack.
- Phase 12 UI hierarchy/layout preview/apply tools and `Unity.UI.VerifyScreenLayout` are in the `ui` pack.
- Phase 11 package/import/Input System diagnostics and active input handler tools are in the `project` pack.
- Authoring-First component reuse tools are in the `project` pack: `Unity.Component.Search`, `Unity.Component.ResolveCapability`, `Unity.Component.InspectSchema`, `Unity.Scene.FindComponents`, and `Unity.Authoring.SuggestReusePlan`.
- Authoring-First package capability and workflow tools are in the `project` pack.
- Authoring-First prefab, preset, and copy-from-existing tools are in the `assets` pack.
- Project-pack additions must not widen the default `foundation` surface.
- TSAM tools must emit `normalization`, `service`, `adapter`, and `result_shaping` coverage rows.
- The helper path now distinguishes direct MCP health from wrapper degradation, and `Invoke-UnityRunCommand` can bypass idle wait in healthy play mode.
- Phase 14 payload telemetry records measurable compact-result savings for large TSAM results, and the batch helper reduces repeated smoke/session churn.
- Phase 15 payload telemetry records measurable compact-log savings for `Unity.RunCommand` and `Unity.ReadConsole` summary results.
- Phase 16 moves the active long-running smoke host to `D:\TintPaint`, normalizes batch-helper `Unity.ReadDetailRef` results, and records measurable `Unity.ManageEditor.WaitForStableEditor` savings.
- Phase 17+ addresses the highest TintPaint dogfood pain with compact usage-report pack transition summaries, clearer TSAM coverage presentation, durable uGUI canvas prefab authoring, scene prefab instantiate/bind, UI raycast/layout and resolution-matrix verification, scene serialized-reference verification, play-mode pointer/scroll smoke verification, explicit play-mode exit, and Phase 18 asset sprite-sheet binding workflows.

---

## Active Codex Desktop MCP Exposure Finding

Date: 2026-05-05

Status: source hygiene patched; Codex Desktop tool exposure still needs app-side verification after refresh/reload.

Finding:

- Codex Desktop can launch plugin MCP servers from `C:\WINDOWS\system32`, so the repo plugin `.mcp.json` must not depend on `node ./skills/unity-mcp-bridge/scripts/Launch-UnityMcpLens.js`.
- The installed plugin cache and repo source now point the plugin MCP server at the installed native Lens binary: `%USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe`.
- The repo plugin MCP server key is now `unity_mcp_lens` instead of the legacy `codex_side_mcp_client_for_unity`, and plugin version bumps are used when Codex Desktop needs to refresh the cached plugin package.
- The native MCP host now returns bootstrap foundation tool descriptors if the Unity bridge is not ready during initial `tools/list`, preventing Codex from indexing an empty tool surface.
- The native MCP host now exposes `Unity.Editor.ScriptUpdatingConsentModal` as a host-local foundation recovery tool so Codex can detect Unity's `Script Updating Consent` popup and accept `Yes, just for these files` for an expected Codex-triggered script refresh while the Unity bridge is blocked.
- Direct Lens MCP handshake succeeds and Unity bridge beacon is ready for `D:\BeeSurvivors`, but the current Codex thread's `thread_dynamic_tools` table still lacks Unity tools.

Next verification:

- Reload or refresh Codex Desktop plugin/MCP state, then check `tool_search` for `Unity_ListToolPacks Unity_GetLensHealth` and inspect `thread_dynamic_tools` for the new thread.

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

## Latest Phase 13 Payload Shaping Smoke

Date: 2026-04-26

Result: passed the primary shaping target, with residual helper/session churn.

Host project:

- `D:\2DUnityNewGame`
- Unity `6000.4.3f1`

Telemetry result:

- Focused scope: lines `2201..2446`, `244` rows.
- Payload rows: `68`; TSAM coverage rows: `176`.
- Payload size: `210,510` raw bytes -> `120,867` shaped bytes.
- Recorded savings: `89,643` bytes (`42.58%`).
- `PayloadRowsWithSavings=2`.
- `NoShapingRecorded=false`.
- Top savings:
  - `Bridge.RefreshToolsSnapshotIfNeeded`: `100,016` raw bytes -> `9,481` shaped bytes, saving `90,535` bytes (`90.52%`).
  - `Unity.GetLensUsageReport`: `1,394` raw bytes -> `1,215` shaped bytes, saving `179` bytes (`12.84%`).

Workflow result:

- `Check-UnityDevSession.ps1` returned `ProceedWithLensHelpers` and `DirectMcpHealthy=true` after the reload window settled.
- `Sync-UnityScriptChanges.ps1` completed a forced refresh and recovered via direct Lens health.
- `Ensure-UnityUiHierarchy.ps1`, `Bind-UnitySceneSerializedReferences.ps1`, and `Set-UnityUiLayout.ps1` preview/apply paths no-op cleanly with `willModify=false` and `applied=false`.
- `Unity.UI.VerifyScreenLayout` passed in edit mode with `inside_screen`, `ordered_stack`, and `below_center`.

TSAM result:

- Full TSAM coverage with zero failure rows for:
  - `Unity.InputSystem.Diagnostics`
  - `Unity.UI.PreviewEnsureHierarchy`
  - `Unity.UI.ApplyEnsureHierarchy`
  - `Unity.Scene.PreviewBindSerializedReferences`
  - `Unity.Scene.ApplyBindSerializedReferences`
  - `Unity.UI.PreviewLayoutProperties`
  - `Unity.UI.ApplyLayoutProperties`
  - `Unity.UI.VerifyScreenLayout`

Residual churn:

- Bridge requests/responses: `88` / `88`.
- Connections: `12`.
- Setup cycles: `0`.
- `get_tool_schema` requests: `25`.
- Pack transitions: `12`.
- Unmatched requests: `1`, a `Unity_ManageEditor` domain-reload transport close during the expected forced script-refresh window.
- Large tool execution/result rows still need compact shaping and detail refs.

---

## Latest Phase 14 Compact TSAM And Batch Helper Smoke

Date: 2026-04-26

Result: passed compact-result and batch-helper acceptance targets.

Host project:

- `D:\2DUnityNewGame`
- Unity `6000.4.3f1`

Pack/export result:

- Metadata audit: pass.
- Export counts unchanged: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, `debug=22`.

Telemetry result:

- Focused scope: from fresh marker line `2592`, `98` rows.
- Payload rows: `51`; TSAM coverage rows: `47`.
- Payload size: `50,566` raw bytes -> `24,025` shaped bytes.
- Recorded savings: `26,541` bytes (`52.49%`).
- `PayloadRowsWithSavings=7`.
- `NoShapingRecorded=false`.
- Top savings:
  - `Unity.Scene.ApplyBindSerializedReferences`: `7,261` raw bytes -> `466` shaped bytes, saving `6,795` bytes (`93.58%`).
  - `Unity.Scene.PreviewBindSerializedReferences`: `7,261` raw bytes -> `468` shaped bytes, saving `6,793` bytes (`93.55%`).
  - `Unity.UI.ApplyEnsureHierarchy`: `4,689` raw bytes -> `432` shaped bytes, saving `4,257` bytes (`90.79%`).
  - `Unity.UI.PreviewEnsureHierarchy`: `4,689` raw bytes -> `434` shaped bytes, saving `4,255` bytes (`90.74%`).
  - `Unity.UI.VerifyScreenLayout`: `7,394` raw bytes -> `5,085` shaped bytes, saving `2,309` bytes (`31.23%`).
  - `Unity.InputSystem.Diagnostics`: `4,823` raw bytes -> `2,870` shaped bytes, saving `1,953` bytes (`40.49%`).

Batch/session result:

- `Invoke-UnityMcpBatch` ran `9` ordered project/ui/scene/debug steps in one workflow.
- Connections: `3`.
- `get_tool_schema` requests: `6`.
- Pack transitions: `4`.
- Tool snapshot rows: `0`.
- Unmatched requests: `0`.
- Failure rows: `0`.

TSAM result:

- Full TSAM coverage with zero failure rows for:
  - `Unity.InputSystem.Diagnostics`
  - `Unity.UI.PreviewEnsureHierarchy`
  - `Unity.UI.ApplyEnsureHierarchy`
  - `Unity.Scene.PreviewBindSerializedReferences`
  - `Unity.Scene.ApplyBindSerializedReferences`
  - `Unity.UI.PreviewLayoutProperties`
  - `Unity.UI.ApplyLayoutProperties`
  - `Unity.UI.VerifyScreenLayout`

Smoke notes:

- Compact inline outputs were enough to decide pass/fail without reading detail refs.
- `Unity.ReadDetailRef` successfully read one full compacted scene-binding result detail.
- UI layout result rows stayed small and did not need artificial shaping.
- Individual helper scripts still have value for one-off tasks; use the batch helper when a smoke/workflow has multiple known steps.

---

## Latest Phase 15 RunCommand And Console Compact Log Smoke

Date: 2026-04-26

Result: passed compact-log happy path, with one helper follow-up.

Host project:

- `D:\2DUnityNewGame`
- Unity `6000.4.3f1`

Pack/export result:

- Metadata audit: pass.
- Export counts unchanged: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, `debug=22`.

Telemetry result:

- Focused happy-path scope: from fresh marker line `262`, `27` rows.
- Payload rows: `6`; coverage rows: `21`.
- Payload size: `56,370` raw bytes -> `39,650` shaped bytes.
- Recorded savings: `16,720` bytes (`29.66%`).
- `NoShapingRecorded=false`.
- Connections: `2`.
- Pack transitions: `3`.
- Unmatched requests: `0`.
- Failure rows: `0`.
- Top savings:
  - `Unity.RunCommand`: `17,405` raw bytes -> `5,972` shaped bytes, saving `11,433` bytes (`65.69%`).
  - `Unity.ReadConsole`: `2,882` raw bytes -> `663` shaped bytes, saving `2,219` bytes (`77.00%`).
  - `Unity.GetLensUsageReport`: `13,119` raw bytes -> `10,051` shaped bytes, saving `3,068` bytes (`23.39%`).

Smoke notes:

- Successful `Unity.RunCommand` emitted `80` execution log lines, `40` captured console warning lines, and inline structured `returnedData`.
- Inline logs are short previews; `logSummary` carries counts, first warning/error lines, truncation flags, and detail refs.
- `Unity.ReadConsole` summary returns counts and grouped rows inline while full scanned entries move behind `detailRef`.
- Direct `Unity.ReadDetailRef` resolved both RunCommand and ReadConsole detail payloads.
- Separate expected-failure smoke confirmed stable `failureStage`/`errorKind` values for compilation, execution, and result serialization.
- Follow-up from this smoke was batch-helper normalization for unwrapped `Unity.ReadDetailRef` payloads; Phase 16 resolved that path.

---

## Latest Phase 16 Batch DetailRef And Editor Stability Smoke

Date: 2026-04-28

Result: passed on the new long-running smoke host.

Host project:

- `D:\TintPaint`
- Unity host was idle after an initial recoverable play-transition window.

Pack/export result:

- Metadata audit: pass.
- Export counts unchanged: `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, `debug=22`.

Telemetry result:

- Focused happy-path scope: from fresh marker line `394`, `35` rows.
- Payload size: `60,520` raw bytes -> `48,101` shaped bytes.
- Recorded savings: `12,419` bytes (`20.52%`).
- `NoShapingRecorded=false`.
- Unmatched requests: `0`.
- Failure rows: `0`.
- Top savings:
  - `Unity.RunCommand`: `11,720` raw bytes -> `5,751` shaped bytes, saving `5,969` bytes (`50.93%`).
  - `Unity.GetLensUsageReport`: `16,843` raw bytes -> `12,562` shaped bytes, saving `4,281` bytes (`25.42%`).
  - `Unity.ManageEditor.WaitForStableEditor`: `3,985` raw bytes -> `1,487` shaped bytes, saving `2,498` bytes (`62.69%`).

Smoke notes:

- `Check-UnityDevSession.ps1` settled to `ProceedWithLensHelpers` with direct MCP, manual wrapper, and helper health all true.
- `Unity.RunCommand` returned structured data inline while compacting execution logs behind detail refs.
- `Unity.ManageEditor.WaitForStableEditor` now returns compact stability state with `attemptsDetailRef` and `fullStateDetailRef`.
- The batch helper now treats unwrapped `Unity.ReadDetailRef` structured payloads as successful steps.
- Large detail payloads are summarized in batch output instead of inlined; small detail payloads can still be included.
- `Unity.ReadConsole` had no entries in this clean TintPaint scope, so console detail-ref normalization was validated but no console savings row was produced.

---

## Latest TintPaint Dogfood Session

Date: 2026-04-28

Result: Lens is stable enough for continued real Unity work, with UI/prefab and
runtime pointer-input gaps.

Host project:

- `D:\TintPaint`
- Report time: `2026-04-28T13:57:37-06:00`

Telemetry result:

- Scope: `lastRows=2000`, excluding request `c35f449cd32f41708ffb082238f66598`.
- Rows: `1999`; payload rows: `216`; coverage rows: `1783`.
- Payload size: `3,012,895` raw bytes -> `650,430` shaped bytes.
- Recorded savings: `2,362,465` bytes (`78.41%`).
- `NoShapingRecorded=false`.
- Connections: `42`.
- Setup cycles: `89`.
- `get_tool_schema` requests: `293`.
- Pack transitions: `147`.
- Unmatched requests: `2`, both `domain_reload_transport_close`.
- Failure classes: `Unity_ManageEditor disposed_transport=6`; `get_tool_schema transport_closed_before_response=1`.
- `tsamCoverage=[]` despite `1783` coverage rows, which is a telemetry presentation bug.

Top savings:

- `Bridge.RefreshToolsSnapshotIfNeeded`: `1,800,288` raw bytes -> `170,658` shaped bytes, saving `1,629,630` bytes (`90.52%`).
- `Unity.ManageEditor.WaitForStableEditor`: `753,846` raw bytes -> `26,277` shaped bytes, saving `727,569` bytes (`96.51%`).
- `Unity.RunCommand`: `11,720` raw bytes -> `5,751` shaped bytes, saving `5,969` bytes (`50.93%`).
- `Unity.GetLensUsageReport`: `24,845` raw bytes -> `20,011` shaped bytes, saving `4,834` bytes (`19.46%`).

Dogfood findings:

- `Unity.GetLensUsageReport` was useful and compact enough for totals, savings, top rows, failures, and churn.
- `Unity.ReadDetailRef` worked through `Invoke-UnityMcpBatch`; the usage-report detail ref resolved and was summarized instead of inlined.
- `Get-UnityConsole.ps1` / `Unity_ReadConsole` avoided raw `Editor.log` grep.
- Scene and prefab authoring still required custom `Unity.RunCommand` editor code for durable uGUI prefab creation, scene wiring, and reference checks.
- Runtime pointer verification is weak: synthetic `MouseState` did not drive the app's `Mouse.current` path reliably.
- `Unity.RunCommand` play-state restoration can make stop-play attempts misleading; `Unity_ManageEditor Action=Stop` remains the reliable exit path.
- Compact usage reports should summarize `packSetTransitions` by default and put the full list behind `detailRef`.

Implemented Phase 17 tool responses to this dogfood:

- `Unity.UI.PreviewCreateCanvasPrefab` / `Unity.UI.ApplyCreateCanvasPrefab` in `ui`: preview/apply durable uGUI canvas prefab authoring with node hierarchy specs and common Canvas/CanvasScaler/GraphicRaycaster setup.
- `Unity.Scene.PreviewInstantiatePrefabAndBind` / `Unity.Scene.ApplyInstantiatePrefabAndBind` in `scene`: preview/apply scene prefab instantiation plus ordered serialized reference binding.
- `Unity.UI.VerifyRaycastAndLayout` in `ui`: read-only raycast stack, top hit, blocking result, and optional layout assertions for screen points or UI object names.
- `Unity.PlayMode.PointerInputSmoke` in `runtime`: play-mode pointer/scroll smoke with observed Input System state, UI hit evidence, world raycast evidence, and optional gameplay-state assertions.
- `Unity.Editor.ExitPlayMode` in `runtime` plus `Exit-UnityPlayMode.ps1`: explicit stop/pause/unpause semantics with final editor state, not subject to `Unity.RunCommand` play-state restoration.
- `Unity.UI.VerifyScreenLayoutMatrix` in `ui`: fixed-resolution layout matrix checks with original Game view size restoration.
- `Unity.Scene.VerifySerializedReferences` in `scene`: read-only nested prefab reference verification with inherited/local/null status.

---

## P0

### Bridge And Helper Session Churn

Observed dogfood signals:

- `34` bridge connections.
- `127` setup cycles.
- `184` pack-set transitions.
- `356` `get_tool_schema` requests.

Work:

- Use `Invoke-UnityMcpBatch` for repeated smoke/workflow calls that can share one session.
- Avoid pack changes when the requested pack set is already active.
- Avoid repeated schema pulls when the tool snapshot hash has not changed.
- Make reload/play transition transport closures clear instead of alarming.
- Keep usage-report compact mode from inlining very large `packSetTransitions` lists.
- Fix `tsamCoverage=[]` presentation when coverage rows exist.

### Payload Shaping

Observed dogfood signals:

- `0.00%` recorded shaping savings.
- Latest Phase 11 smoke still reported `NoShapingRecorded=true`.
- `Unity_ManageEditor` emitted payload rows above `220 KB`.
- Tool snapshots contributed about `2.50 MB` raw payload across `29` rows.
- Phase 14 compact-result smoke now reports `NoShapingRecorded=false` and `7` saving rows, including UI hierarchy, scene binding, UI verify, and Input System diagnostics.
- Phase 15 compact-log smoke now reports `Unity.RunCommand` and `Unity.ReadConsole` `tool_result` savings.
- Phase 16 compact editor-stability smoke now reports `Unity.ManageEditor.WaitForStableEditor` savings and successful batch detail-ref reads.

Work:

- Keep `Unity.ManageEditor WaitForStableEditor` inline output compact.
- Store full attempts and full editor state behind detail refs.
- Reduce routine tool snapshot payload cost.
- Keep batch-helper detail-ref summaries compact so passing smokes do not inline large payloads.

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

- Add first-class durable uGUI prefab authoring and scene prefab instantiation/binding paths.
- Add read-only UI raycast/layout verification so UI blocking and hit-test behavior do not require custom hierarchy probes.
- Dogfood the full Phase 12 HUD authoring flow in `D:\TintPaint` without custom editor C# when that project has durable UI/HUD targets.
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

- Keep `Unity.RunCommand` failure stage and `errorKind` consistent across validation, compilation, execution, result serialization, transport/unknown, and unexpected exceptions.
- Keep compilation, execution, and console logs compact by default with full logs behind detail refs.
- Add or improve structured recent-console reads so package/import errors do not require raw `Editor.log` grep.
- Keep the play-mode helper bypass keyed to direct Lens health and compact editor state, even when `IsPlayingOrWillChangePlaymode` keeps the editor-stability label at `play_transition`.
- Do not use `Unity.RunCommand` as the primary way to exit play mode; prefer a dedicated helper/tool path that cannot be undone by play-state restoration.

### Runtime Input And Raycast Verification

Work:

- Add pointer-input smoke tooling that verifies observed Input System state, UI blocking, world hit, and sampled gameplay result.
- Keep this read-only/verify-oriented where possible, with explicit play-mode preconditions and compact result summaries.

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
