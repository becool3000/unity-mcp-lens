# Changelog

All notable Unity MCP Lens package changes are documented here.

## [Unreleased]

### Added

- Added Phase 8 split GameObject TSAM tools for inspect, component reads, preview/apply mutation, create, and delete behind the `scene` pack.
- Added Phase 10 project/Input System tools for diagnostics and active input handler preview/apply behind the `project` pack.
- Added Lens usage reporting for payload, bridge, pack transition, tool snapshot, and TSAM stage coverage analysis.
- Added metadata audit coverage for foundation, scene, project, and debug pack surfaces, including required tools, schema checks, and read-only annotations.

### Changed

- Kept `foundation` as the narrow default export surface with a `12` tool baseline.
- Kept `foundation + scene` at a `30` tool smoke baseline while adding project-pack capabilities separately.
- Updated Codex workflow guidance to prefer Phase 10 project tools for Input System and active input handler work.
- Improved `Unity.RunCommand` and `Unity.ManageEditor WaitForStableEditor` output contracts toward stage-aware compact results with detail refs.

### Known Follow-Up

- Reduce helper-script session/setup churn, repeated `get_tool_schema` requests, and pack transition noise.
- Improve payload shaping so large editor-state and tool-snapshot rows produce measurable savings.
- Extend Input System diagnostics to cover package compatibility, type-load failures, wrapper generation conflicts, devices, bindings, and recent log signals in one call.
- Suppress restart-required messaging for no-op active input handler preview/apply results.
- Add reliable editor restart/reload orchestration and prefab/serialized-reference authoring workflows.

### Validation

- Phase 10 smoke passed with warnings on Unity `6000.4.3f1` in `D:\2DUnityNewGame`.
- Metadata audit passed with `foundation=12`, `foundation+scene=30`, `project=19`, and `debug=22`.
- Phase 10 diagnostics, preview, and set calls emitted complete TSAM stage coverage with no failure classes.

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
