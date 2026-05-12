# Tool Packs And MCP Surface

Lens keeps the default tool surface small. The `foundation` pack is always
active and currently exports `18` tools for health, pack control, detail refs,
console/resource reads, script validation, compact project information, and
host-local recovery from Unity's Script Updating Consent modal.

Common packs:

- `console` for compact console inspection.
- `project` for project/package/import metadata, validation, missing script/reference checks, Input System diagnostics, package compatibility, input-action inspection, and active input handler preview/apply.
- `scene` for scene and GameObject inspection/editing, including the Phase 8 split GameObject TSAM surface, serialized-reference/object-reference assignment tools, explicit dirty/save tools, prefab instantiate/bind workflows, and read-only serialized-reference verification.
- `ui` for UI Toolkit reads, uGUI hierarchy/layout preview/apply authoring, canvas prefab authoring, raycast/layout verification, and read-only screen-layout or resolution-matrix verification.
- `runtime` for play-mode runtime probes, visual bounds snapshots, pointer/scroll smoke verification, and explicit play-mode exit.
- `scripting` for scripts, edits, command execution, and structured `Unity.RunCommand` return payloads.
- `assets` for asset/resource workflows, including sprite-sheet import/slicing/binding and Sprite-array binding verification.
- `debug` for diagnostics and profiling.
- `full` for admin/debug operations that should not be default.

Use `Unity.ListToolPacks` to inspect available packs and `Unity.SetToolPacks` to replace the active non-foundation pack set. Lens enforces a maximum of two non-foundation packs at once.

Current live metadata baselines:

- `foundation`: `18` exported tools.
- `foundation + scene`: `47` exported tools.
- `foundation + ui`: `35` exported tools.
- `foundation + runtime`: `29` exported tools.
- `project`: `27` exported tools.
- `foundation + assets`: `31` exported tools.
- `debug`: `28` exported tools.

Pack additions must not change the `foundation` baseline unless the metadata
audit and workflow docs are updated at the same time.

TSAM-covered tools should emit `normalization`, `service`, `adapter`, and
`result_shaping` telemetry rows. Prefer read-only project diagnostics and
preview/apply mutation pairs over custom `Unity.RunCommand` snippets when a
split tool exists for the workflow.

See [TSAM refactor direction](../docs/TSAM.md) for the layer responsibilities
and current split-tool surfaces.

Large results should return a compact preview with `detailRef` when full detail is available. Use `Unity.ReadDetailRef` only when the preview is insufficient.

Use `Unity.GetLensUsageReport` from the `debug` pack when validating payload
size, bridge churn, pack transitions, tool snapshots, and TSAM stage coverage.

