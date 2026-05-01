# Tool Packs And MCP Surface

Lens keeps the default tool surface small. The `foundation` pack is always
active. The Unity bridge contributes `13` core tools for health, pack control,
detail refs, same-session batch workflows, console/resource reads, script
validation, and compact project information. Phase 19 adds two
foundation-visible local recovery tools in the standalone Lens server, so the
current live foundation surface is `15` tools. Phase 20 adds two more
foundation-visible local recovery tools for explicit frozen-editor detection
and recovery, raising the live foundation surface to `17` tools.

Common packs:

- `console` for compact console inspection.
- `project` for project/package/import metadata, validation, missing script/reference checks, Input System diagnostics, package compatibility, input-action inspection, and active input handler preview/apply.
- `scene` for scene and GameObject inspection/editing, including the Phase 8 split GameObject TSAM surface, Phase 12 serialized-reference binding tools, prefab instantiate/bind workflows, UnityEvent binding, and save/readback.
- `ui` for UI Toolkit reads, uGUI hierarchy/layout preview/apply authoring, button authoring, canvas prefab authoring, raycast/layout verification, read-only screen-layout verification, legacy Text visual audits, and legacy Font import/bind preview/apply.
- `runtime` for play-mode runtime probes, visual bounds snapshots, pointer-input smoke verification, and paint-surface interaction verification.
- `scripting` for scripts, edits, command execution, and structured `Unity.RunCommand` return payloads.
- `assets` for asset/resource workflows, including ScriptableObject preview/apply creation and update.
- `debug` for diagnostics and profiling.
- `full` for admin/debug operations that should not be default.

Use `Unity.ListToolPacks` to inspect available packs and `Unity.SetToolPacks` to replace the active non-foundation pack set. Lens enforces a maximum of two non-foundation packs at once.

Use `Unity.Editor.DetectNativeModals` and
`Unity.Editor.ResolveSceneReloadPrompt` when a native Unity dialog blocks the
bridge before normal tool execution can start. These tools are implemented in
the standalone Lens server and are intentionally available before Unity bridge
bootstrap.

Use `Unity.Editor.DetectFrozenEditor` and
`Unity.Editor.RecoverFrozenEditor` when Unity is non-responsive and no native
modal is visible. Recovery is explicit-only: helpers may recommend
`RecoverFrozenEditor`, but they do not kill or reopen Unity automatically.

Use `Unity.Batch.ExecuteWorkflow` when a known ordered workflow needs multiple
tools in one Lens connection. The batch tool validates or infers required packs
per step and restores the original active packs when the workflow completes.

Current live metadata baselines:

- `foundation`: `17` exported tools.
- `foundation + scene`: `43` exported tools.
- `foundation + ui`: `35` exported tools.
- `foundation + runtime`: `20` exported tools.
- `project`: `26` exported tools.
- `foundation + assets`: `29` exported tools.
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

