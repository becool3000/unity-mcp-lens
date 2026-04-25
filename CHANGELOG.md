# Changelog

All notable Unity MCP Lens package changes are documented here.

## [Unreleased]

### Added

- Added Phase 8 split GameObject TSAM tools for inspect, component reads, preview/apply mutation, create, and delete behind the `scene` pack.
- Added Phase 10 project/Input System tools for diagnostics and active input handler preview/apply behind the `project` pack.
- Added Phase 11 `project`-pack diagnostics for `Unity.Project.PackageCompatibility` and `Unity.InputActions.InspectAsset`.
- Added Phase 12 `ui` tools for `Unity.UI.PreviewEnsureHierarchy`, `Unity.UI.ApplyEnsureHierarchy`, `Unity.UI.PreviewLayoutProperties`, `Unity.UI.ApplyLayoutProperties`, and `Unity.UI.VerifyScreenLayout`.
- Added Phase 12 `scene` tools for `Unity.Scene.PreviewBindSerializedReferences` and `Unity.Scene.ApplyBindSerializedReferences`.
- Added Lens usage reporting for payload, bridge, pack transition, tool snapshot, and TSAM stage coverage analysis.
- Added metadata audit coverage for foundation, scene, ui, project, and debug pack surfaces, including required tools, schema checks, and read-only annotations.

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

### Known Follow-Up

- Reduce helper-script session/setup churn, repeated `get_tool_schema` requests, and pack transition noise.
- Improve payload shaping so large editor-state and tool-snapshot rows produce measurable savings.
- Decide whether default package assembly filtering should exclude doc/sample/test-support asmdefs from compact compatibility reads.
- Finish Phase 12 smoke validation and investigate the helper-driven forced-refresh `Undo.SetCurrentGroupName` assertion seen while syncing into the host project.
- Add reliable editor restart/reload orchestration and prefab/serialized-reference authoring workflows.

### Validation

- Phase 11 smoke passed with a residual payload-shaping warning on Unity `6000.4.3f1` in `D:\2DUnityNewGame`.
- Metadata audit passed with `foundation=12`, `foundation+scene=30`, `project=21`, and `debug=22`.
- Phase 11 package compatibility, input-actions inspection, diagnostics, preview, and set calls emitted complete TSAM stage coverage with no failure classes.

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
