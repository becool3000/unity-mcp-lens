# Tool Packs And MCP Surface

Lens keeps the default tool surface small. The `foundation` pack is always active and includes health, pack control, and detail-ref tools.

Common packs:

- `console` for compact console inspection.
- `project` for project/package metadata.
- `scene` for scene and GameObject inspection/editing.
- `ui` for UI Toolkit and uGUI authoring diagnostics.
- `scripting` for scripts, edits, and command execution.
- `assets` for asset/resource workflows.
- `debug` for diagnostics and profiling.
- `full` for admin/debug operations that should not be default.

Use `Unity.ListToolPacks` to inspect available packs and `Unity.SetToolPacks` to replace the active non-foundation pack set. Lens enforces a maximum of two non-foundation packs at once.

Large results should return a compact preview with `detailRef` when full detail is available. Use `Unity.ReadDetailRef` only when the preview is insufficient.

