---
name: unity-mcp-lens-development
description: Develop, test, and improve Unity MCP Lens tools, packs, bridge behavior, package UI, and Unity editor automation workflows. Use when working on Lens itself, adding or debugging Lens MCP tools, changing tool packs, validating bridge behavior, or making Unity editor-authored persistent changes for Lens projects.
---

# Unity MCP Lens Development

## Source Of Truth

The repo-local Codex plugin is the only editable source of truth for Lens workflow skills:

- `.agents/plugins/lens-dev-plugin/skills/unity-dev-assistant`
- `.agents/plugins/lens-dev-plugin/skills/unity-mcp-bridge`
- `.agents/plugins/lens-dev-plugin/skills/unity-mcp-lens-development`

Do not edit installed Codex cache copies or standalone `$CODEX_HOME/skills` copies. If the app shows duplicate Lens skills, remove the duplicates and regenerate the plugin cache from this repo.

## Prime Directive

Dogfood Lens. Use the Lens MCP bridge for Unity editor state inspection and editor mutations whenever Unity work is involved.

Do not bypass Lens by inventing runtime bootstrap code, temporary scene constructors, manual wrapper paths, or one-off editor hacks just to finish the task.

## Allowed Work Paths

- Use MCP/Lens tools for Unity editor actions.
- Edit source files directly only when writing package code, scripts, C# tools, tests, docs, or config.
- Use normal shell commands for git, static checks, package builds, text search, and non-Unity file maintenance.
- Use Unity editor-authored persistent changes for scenes, prefabs, assets, settings, and bindings.

## Persistence Rule

Build like a human Unity developer would:

- Create or edit scene objects in edit mode.
- Save scenes and prefabs.
- Bind serialized references.
- Update importer settings.
- Create assets on disk.
- Verify serialized or persistent state afterward.

Do not rely on runtime-only bootstrap creation for durable project structure unless the user explicitly asks for runtime generation architecture.

## Phase 8 Tool Truth

The current Phase 8 scene surface is the split GameObject TSAM surface. With `foundation` plus `scene` active, the smoke baseline is `30` exported tools.

- Prefer `Unity.GameObject.Inspect`, `ListComponents`, and `GetComponent` for reads.
- Prefer preview/apply pairs for mutation: `PreviewChanges`/`ApplyChanges`, `PreviewComponentChanges`/`ApplyComponentChanges`, `PreviewCreate`/`Create`, and `PreviewDelete`/`Delete`.
- Keep `Unity.ManageGameObject` compatible as the legacy facade and fallback for uncovered behavior.
- Use `debug` plus `Unity.GetLensUsageReport` for telemetry baselines and appended-row smoke reporting.

## Phase 11 Project Tool Truth

The current Phase 11 project surface includes package/import/Input System diagnostics and active input handler controls.

- Prefer `Unity.Project.PackageCompatibility` for read-only package version, assembly, and compatibility checks.
- Prefer `Unity.InputActions.InspectAsset` for `.inputactions` summary, binding, and wrapper-generation inspection.
- Prefer `Unity.InputSystem.Diagnostics` for one-call Input System package, assembly, device, `.inputactions`, define, compatibility, and editor-log signals.
- Prefer `Unity.ProjectSettings.PreviewActiveInputHandler` before changing the active input backend.
- Use `Unity.ProjectSettings.SetActiveInputHandler` for editor-authored active input backend changes; do not hand-edit `ProjectSettings.asset` as the first path.
- `foundation` remains `12` tools, `foundation + scene` now targets `32` tools, `foundation + ui` now targets `22`, and the current `project` smoke baseline remains `21` tools.

## Phase 12 UI And Scene Binding Truth

The current Phase 12 authoring surface adds split UI hierarchy/layout preview/apply tools, scene serialized-reference preview/apply binding tools, UI screen-layout verification, and structured `Unity.RunCommand` return values.

- Prefer `Unity.UI.PreviewEnsureHierarchy` and `Unity.UI.ApplyEnsureHierarchy` over the removed one-shot UI hierarchy tool.
- Prefer `Unity.UI.PreviewLayoutProperties` and `Unity.UI.ApplyLayoutProperties` over the removed one-shot UI layout tool.
- Prefer `Unity.Scene.PreviewBindSerializedReferences` and `Unity.Scene.ApplyBindSerializedReferences` for scene object-reference fields and arrays before low-level `Unity.Scene.SetSerializedProperties`.
- Prefer `Unity.UI.VerifyScreenLayout` for measured HUD/layout assertions instead of ad hoc screen-rect probes.
- Keep `Unity.Scene.SetSerializedProperties` as the low-level fallback, not the first authoring path.

## Maintenance Rules

- Any pack membership change must update the metadata audit expected counts and required-tool assertions.
- Any TSAM-covered tool path must emit `normalization`, `service`, `adapter`, and `result_shaping` telemetry rows.
- `Unity.RunCommand` result metadata must distinguish validation, compilation, execution, result serialization, and transport/unknown failures.
- `Unity.ManageEditor WaitForStableEditor` should keep inline output compact and store full attempt/state detail behind detail refs.
- Smoke prompts must cover split tools, the legacy facade, metadata annotations, usage telemetry, and the `MeshFilter.mesh` edit-mode warning regression.
- Commit package behavior fixes separately from skill/plugin hygiene changes.

## Missing Tool Rule

If the task requires a Unity editor action and no Lens tool can do it cleanly:

1. Stop the Unity-facing work.
2. Do not work around it through runtime bootstrap code or manual editor simulation.
3. Write a short missing-tool report.
4. Include the proposed tool name, pack, inputs, output contract, safety rules, and validation test.
5. Ask whether to implement the Lens tool next.

## Missing Tool Report Format

- Needed action:
- Why existing Lens tools are insufficient:
- Proposed tool:
- Pack:
- Inputs:
- Output:
- Persistence/safety rules:
- Compactness/detailRef behavior:
- Smoke test:
