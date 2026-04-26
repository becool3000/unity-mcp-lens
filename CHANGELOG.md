# Changelog

All notable Unity MCP Lens package changes are documented here.

## [Unreleased]

### Added

- Added public TSAM refactor documentation for the Tool, Service, Adapter, Model direction, covering split GameObject tools, Input System diagnostics and active input handler preview/apply, package compatibility, input-action asset inspection, UI hierarchy/layout preview/apply tools, scene serialized-reference binding, usage reporting, and TSAM stage telemetry.
- Added Phase 8 split GameObject TSAM tools for inspect, component reads, preview/apply mutation, create, and delete behind the `scene` pack.
- Added Phase 10 project/Input System tools for diagnostics and active input handler preview/apply behind the `project` pack.
- Added Phase 11 `project`-pack diagnostics for `Unity.Project.PackageCompatibility` and `Unity.InputActions.InspectAsset`.
- Added Phase 12 `ui` tools for `Unity.UI.PreviewEnsureHierarchy`, `Unity.UI.ApplyEnsureHierarchy`, `Unity.UI.PreviewLayoutProperties`, `Unity.UI.ApplyLayoutProperties`, and `Unity.UI.VerifyScreenLayout`.
- Added Phase 12 `scene` tools for `Unity.Scene.PreviewBindSerializedReferences` and `Unity.Scene.ApplyBindSerializedReferences`.
- Added Lens usage reporting for payload, bridge, pack transition, tool snapshot, and TSAM stage coverage analysis.
- Added metadata audit coverage for foundation, scene, ui, project, and debug pack surfaces, including required tools, schema checks, and read-only annotations.
- Added center-based `Unity.UI.VerifyScreenLayout` relative-position relations: `right_of_center`, `left_of_center`, `above_center`, and `below_center`.
- Added `Invoke-UnityMcpBatch` repo-local helpers for ordered multi-tool smoke/workflow calls that reuse one Lens session.
- Added compact `Unity.RunCommand` log summaries with per-block counts, first warning/error lines, truncation flags, and detail refs.

### Changed

- Kept `foundation` as the narrow default export surface with a `12` tool baseline.
- Raised the `foundation + scene` baseline from `30` to `32` tools and added a `foundation + ui` baseline of `22` tools.
- Raised the `project` pack smoke baseline from `19` to `21` tools while keeping `foundation` and `scene` unchanged.
- Replaced the public `Unity.UI.EnsureNamedHierarchy` and `Unity.UI.SetLayoutProperties` registrations with split preview/apply tool pairs.
- Extended `Unity.InputSystem.Diagnostics` with compatibility signals and concrete `.inputactions` wrapper metadata.
- Collapsed known benign repeated Input System integration-test log-skip lines into one informational compatibility issue so healthy package diagnostics stay `ok`.
- Excluded the in-flight `Unity.GetLensUsageReport` request from self-analysis so appended usage-report reruns no longer classify their own final call as unmatched.
- Updated Codex workflow guidance to prefer the Phase 11 `project` tools for package/import/Input System diagnostics and active input handler work.
- Improved `Unity.RunCommand` output to support structured `ExecutionResult.ReturnResult(...)` payloads plus explicit result-serialization failure classification.
- Improved `Unity.ManageEditor WaitForStableEditor` output contracts toward stage-aware compact results with detail refs.
- Suppressed restart-required noise for no-op active input handler preview/apply results.
- Improved `Check-UnityDevSession` so it distinguishes direct MCP health from helper-path degradation and can recommend `ProceedWithDirectLensTools` when wrappers are the only failing layer.
- Improved `Sync-UnityScriptChanges` so it no longer fails up front on a transient `console` pack restore and can recover through direct Lens health plus compact editor-state probes.
- Improved `Invoke-UnityRunCommand` so healthy play mode can bypass helper-side idle gating and still return structured `ReturnResult(...)` payloads.
- Improved payload accounting so tool snapshot rows and usage-report rows can record the actual shaped payload size instead of reporting only raw source-object size.
- Improved `Unity.GetLensUsageReport` findings with a positive shaping signal and top-savings summaries when eligible rows record `rawBytes > shapedBytes`.
- Reduced helper recovery-path churn by deriving compact editor readiness from `Unity.GetLensHealth` when direct MCP health is enough.
- Improved compact default results for Input System diagnostics, UI hierarchy preview/apply, scene serialized-reference binding preview/apply, and UI screen-layout verification, with full data preserved behind detail refs.
- Reduced repeated smoke/session churn by allowing the batch helper to switch exact pack sets inside one Lens session instead of launching separate helper processes.
- Improved `Unity.RunCommand` log-heavy responses so compilation, execution, and captured console logs are short previews by default with full logs behind detail refs.
- Improved `Unity.ReadConsole` summary output so grouped rows stay inline while full scanned entries are available behind `Unity.ReadDetailRef`.
- Improved `Unity.RunCommand` validate mode so `IncludeLocalFixedCode=false` omits rewritten code inline and preserves it behind `localFixedCodeDetailRef`.

### Known Follow-Up

- Use the batch helper for repeated smoke/workflow paths while keeping individual helper scripts stable for one-off tasks.
- Continue payload shaping for remaining `Unity.ManageEditor` edge cases and normalize `Unity.ReadDetailRef` handling in the batch helper.
- Decide whether default package assembly filtering should exclude doc/sample/test-support asmdefs from compact compatibility reads.
- Reduce reconnect-prone `Unity_ManageEditor` transport-noise during play transitions.
- Add reliable editor restart/reload orchestration and prefab/serialized-reference authoring workflows.

### Validation

- Phase 11 smoke passed with a residual payload-shaping warning on Unity `6000.4.3f1` in `D:\2DUnityNewGame`.
- Phase 12 helper-driven hardening smoke passed with a residual payload-shaping warning on Unity `6000.4.3f1` in `D:\2DUnityNewGame`.
- Phase 13 payload-shaping smoke passed the primary shaping target on Unity `6000.4.3f1` in `D:\2DUnityNewGame`: `NoShapingRecorded=false`, `89,643` bytes saved (`42.58%`) in the focused scope, and tool snapshot shaping reduced `100,016` raw bytes to `9,481` shaped bytes.
- Phase 14 compact-result and batch-helper smoke passed on Unity `6000.4.3f1` in `D:\2DUnityNewGame`: `NoShapingRecorded=false`, `26,541` bytes saved (`52.49%`) in the focused scope, `7` saving rows, and batch churn reduced to `3` connections, `6` schema requests, and `4` pack transitions.
- Phase 15 log-compaction smoke passed on Unity `6000.4.3f1` in `D:\2DUnityNewGame`: `NoShapingRecorded=false`, `16,720` bytes saved (`29.66%`) in the focused scope, `Unity.RunCommand` saved `11,433` bytes (`65.69%`), `Unity.ReadConsole` saved `2,219` bytes (`77.00%`), with `0` unmatched requests and `0` happy-path failure rows.
- Metadata audit passed with `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`.
- Phase 11 package compatibility, input-actions inspection, diagnostics, preview, and set calls emitted complete TSAM stage coverage with no failure classes.
- Phase 12 UI hierarchy, scene binding, layout, and verify calls emitted complete TSAM stage coverage with no tool failure rows in the focused helper-driven scope.
- Phase 13 focused smoke emitted complete TSAM stage coverage with no failure rows for Input System diagnostics, UI hierarchy preview/apply, scene binding preview/apply, UI layout preview/apply, and UI verify.
- Phase 14 focused batch smoke emitted complete TSAM stage coverage with no failure rows for Input System diagnostics, UI hierarchy preview/apply, scene binding preview/apply, UI layout preview/apply, and UI verify.

## [0.1.0-alpha.1] - 2026-04-20

### Fixed

- Stabilized fresh Unity project imports by bundling a coherent Roslyn `3.11` dependency family.
- Added missing managed dependency coverage for the Roslyn support DLLs.
- Scoped bundled Roslyn/support DLLs to Editor import targets to avoid player/runtime exposure.
- Declared package dependencies needed by runtime/editor code, including Unity's Newtonsoft.Json package.

### Changed

- Reset package versioning to the standalone Unity MCP Lens alpha line.
- Split Unity MCP Lens into the standalone package id `com.becool3000.unity-mcp-lens`.
- Renamed owned assemblies and namespaces to `Becool.UnityMcpLens.*`.
- Moved active package code to `Editor/Lens` and `Runtime`.
- Removed copied Assistant UI/runtime/cloud/generator/search/sample folders from the standalone package.
- Removed the bundled legacy relay binaries from Lens; Lens now installs only the owned `unity-mcp-lens` server.
- Added migration guidance and package identity static checks.

### Preserved

- Standard MCP server identity `unity-mcp-lens`.
- Tool names, tool packs, detail refs, schema caching, and compact health surface.
- Editor access under `Tools > Unity MCP Lens` and `Project Settings > Tools > Unity MCP Lens`.
