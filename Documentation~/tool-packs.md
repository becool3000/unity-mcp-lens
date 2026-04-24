# Tool Packs And MCP Surface

Lens keeps the default tool surface small. The `foundation` pack is always
active and currently exports `12` tools for health, pack control, detail refs,
console/resource reads, script validation, and compact project information.

Common packs:

- `console` for compact console inspection.
- `project` for project/package metadata, validation, missing script/reference checks, Input System diagnostics, and active input handler preview/apply.
- `scene` for scene and GameObject inspection/editing, including the Phase 8 split GameObject TSAM surface.
- `ui` for UI Toolkit and uGUI authoring diagnostics.
- `scripting` for scripts, edits, and command execution.
- `assets` for asset/resource workflows.
- `debug` for diagnostics and profiling.
- `full` for admin/debug operations that should not be default.

Use `Unity.ListToolPacks` to inspect available packs and `Unity.SetToolPacks` to replace the active non-foundation pack set. Lens enforces a maximum of two non-foundation packs at once.

The current `foundation + scene` smoke baseline is `30` exported tools.
Project-pack additions must not change the `foundation` or `foundation + scene`
baselines unless the metadata audit and workflow docs are updated at the same
time.

Large results should return a compact preview with `detailRef` when full detail is available. Use `Unity.ReadDetailRef` only when the preview is insufficient.

Use `Unity.GetLensUsageReport` from the `debug` pack when validating payload
size, bridge churn, pack transitions, tool snapshots, and TSAM stage coverage.

