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
- `foundation` now targets `17` tools, `foundation + scene` now targets `41` tools, `foundation + ui` now targets `33`, `foundation + runtime` targets `23`, and the current `project` smoke baseline is `26` tools.

## Phase 12 UI And Scene Binding Truth

The current Phase 12 authoring surface adds split UI hierarchy/layout preview/apply tools, scene serialized-reference preview/apply binding tools, UI screen-layout verification, and structured `Unity.RunCommand` return values.

- Prefer `Unity.UI.PreviewEnsureHierarchy` and `Unity.UI.ApplyEnsureHierarchy` over the removed one-shot UI hierarchy tool.
- Prefer `Unity.UI.PreviewLayoutProperties` and `Unity.UI.ApplyLayoutProperties` over the removed one-shot UI layout tool.
- Prefer `Unity.Scene.PreviewBindSerializedReferences` and `Unity.Scene.ApplyBindSerializedReferences` for scene object-reference fields and arrays before low-level `Unity.Scene.SetSerializedProperties`.
- Prefer `Unity.UI.VerifyScreenLayout` for measured HUD/layout assertions instead of ad hoc screen-rect probes.
- Keep `Unity.Scene.SetSerializedProperties` as the low-level fallback, not the first authoring path.

## Phase 14 Payload And Batch Helper Truth

Phase 14 keeps the public tool surface stable and makes high-volume TSAM results compact by default.

- Compact default results are expected for `Unity.InputSystem.Diagnostics`, UI hierarchy preview/apply, scene serialized-reference binding preview/apply, and `Unity.UI.VerifyScreenLayout`.
- Full bulky data should remain available through `detailRef` when the bridge detail store is available.
- Use `Invoke-UnityMcpBatch` for focused smoke/workflow sequences that need multiple project/ui/scene/debug calls in one Lens session.
- Pack baselines after the static-all reliability push: `foundation=17`, `foundation+scene=41`, `foundation+ui=33`, `foundation+runtime=23`, `project=26`, `foundation+assets=30`, and live `debug=28`.
- Current Phase 14 smoke baseline: `NoShapingRecorded=false`, `7` saving rows, `50,566` raw bytes -> `24,025` shaped bytes, `3` connections, `6` schema requests, and `4` pack transitions.

## Phase 15 RunCommand And Console Truth

Phase 15 keeps the public tool surface stable and makes log-heavy probe results compact by default.

- `Unity.RunCommand` inline `compilationLogs`, `executionLogs`, and `consoleLogs` are short previews by default.
- Use `logSummary` for byte counts, line counts, truncation flags, detail refs, first warning/error lines, and severity counts.
- `Unity.ReadConsole` summary results should keep grouped rows inline and store full scanned entries behind `detailRef`.
- `IncludeLocalFixedCode=false` must omit rewritten code inline in both `execute` and `validate` modes while preserving `localFixedCodeDetailRef`.
- Current Phase 15 smoke baseline: `NoShapingRecorded=false`, `16,720` bytes saved, `Unity.RunCommand` saved `11,433` bytes (`65.69%`), and `Unity.ReadConsole` saved `2,219` bytes (`77.00%`).
- Direct `Unity.ReadDetailRef` resolves RunCommand and ReadConsole details.

## Phase 16 Smoke Host And DetailRef Truth

Phase 16 moves the active long-running smoke host to `D:\TintPaint`.

- Use `D:\TintPaint` for new focused smoke tests unless a task explicitly needs the older `D:\2DUnityNewGame` fixtures.
- Metadata audit on TintPaint before Phase 17 passed with `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`; Phase 17 intentionally raises scene/ui and adds runtime.
- `Check-UnityDevSession.ps1` should settle to `ProceedWithLensHelpers` after recoverable play/reload windows.
- The batch helper now treats unwrapped `Unity.ReadDetailRef` structured payloads as successful steps.
- Large detail-ref payloads should be summarized in batch output, not inlined by default.
- `Unity.ManageEditor.WaitForStableEditor` should keep final stability state compact inline and store attempts/full editor state behind detail refs.
- Current Phase 16 smoke baseline: `NoShapingRecorded=false`, `12,419` bytes saved, `Unity.RunCommand` saved `5,969` bytes (`50.93%`), and `Unity.ManageEditor.WaitForStableEditor` saved `2,498` bytes (`62.69%`).
- Longer TintPaint dogfood baseline: `NoShapingRecorded=false`, `2,362,465` bytes saved (`78.41%`) across `1999` rows; top savings were tool snapshots and `Unity.ManageEditor.WaitForStableEditor`.
- Watch for usage-report presentation gaps: large inline `packSetTransitions` arrays and `tsamCoverage=[]` despite coverage rows.
- Current missing-tool pressure from the TintPaint HUD pass has moved to hardening: Game view resolution reflection, scroll delivery reliability, nested prefab readback clarity, and recovered transition presentation.

## Phase 17 UI/Scene/Runtime Truth

Phase 17 addresses the highest TintPaint dogfood pain without widening `foundation`.

- Prefer `Unity.UI.PreviewCreateCanvasPrefab` and `Unity.UI.ApplyCreateCanvasPrefab` before custom `Unity.RunCommand` prefab-authoring scripts.
- Prefer `Unity.Scene.PreviewInstantiatePrefabAndBind` and `Unity.Scene.ApplyInstantiatePrefabAndBind` before custom scene instantiation/binding scripts.
- Prefer `Unity.UI.VerifyRaycastAndLayout` for UI blocking and hit-test assertions before ad hoc hierarchy probes.
- Prefer `Unity.UI.VerifyScreenLayoutMatrix` when responsive UI layout must be proven across Game view sizes; it restores the original selected Game view size by default.
- Prefer `Unity.Scene.VerifySerializedReferences` when nested prefab instance references need read-only verification; it distinguishes inherited prefab refs, local overrides, local null overrides, actual nulls, and non-prefab instances.
- Prefer `Unity.PlayMode.PointerInputSmoke` for pointer-path evidence in play mode; it reports observed Input System state, scroll state, UI/world hit evidence, and optional sampled gameplay state.
- Prefer `Unity.Editor.ExitPlayMode` or `Exit-UnityPlayMode.ps1` for play-mode cleanup; avoid custom `Unity.RunCommand` stop snippets.
- `Unity.GetLensUsageReport` compact output should summarize large pack-transition lists and report TSAM coverage summary data.
- Prefer `Unity.UI.QueryRuntimeLayout` for runtime UI layout/state readback and `Unity.UI.InvokeControl` for play-mode button/slider/toggle actions before writing custom `Unity.RunCommand` snippets.

## Phase 18 Asset Sprite Pipeline Truth

Phase 18 addresses the BeeSurvivors Roach BeeCool sprite-upgrade workflow.

- Prefer `Unity.Asset.PreviewImportSpriteSheetAndBind` before applying sprite-sheet import/slicing/binding changes.
- Use `Unity.Asset.ApplyImportSpriteSheetAndBind` to persist importer metadata and ScriptableObject Sprite-array bindings after the preview plan is acceptable.
- `Unity.Asset.ImportSpriteSheetAndBind` is a compatibility facade; it previews by default and applies only with `mode=apply` or `apply=true`.
- Prefer `Unity.Asset.VerifySpriteArrayBinding` over custom `Unity.RunCommand` or YAML reads when verifying Sprite-array counts, names, texture names, or texture GUIDs.
- The `assets` pack metadata-audit baseline is `foundation+assets=30`.
- Phase 18 asset tools must keep pass/fail and changed-count data inline while storing full importer metadata, sprite rows, and serialized-field readback behind `detailRef`.

## BeeSurvivors Reliability Follow-Up Truth

This batch addresses the BeeSurvivors Lens dogfood findings from 2026-05-07.

- Prefer `Unity.Tools.Describe` for live tool schema and pack requirement discovery, especially when Codex dynamic indexing is stale.
- Prefer `Unity.Tools.Menu` for compact pack-oriented navigation. In `UNITY_MCP_LENS_TOOL_SURFACE_MODE=static_all`, all enabled real tools are natively exposed up front and `Unity.SetToolPacks` is a compatibility no-op.
- For Codex, prefer `static_all` as the reliability path; raw host binaries still default to `dynamic_packs` for clients that handle `tools/list_changed` correctly.
- `Unity.Tools.Describe` or `Unity.Tools.ActivateAndVerify` proving a tool exists does not prove the current Codex thread has indexed it as callable. If a described active-pack tool is not callable after `Unity.SetToolPacks`, classify the issue as client dynamic-indexing drift and use helper scripts or `Invoke-UnityMcpBatch` while preserving the raw host evidence.
- `Unity.SetToolPacks` should report whether `notifications/tools/list_changed` was emitted. If Codex still cannot call the described tool after that notification, do not keep widening packs; collect the active packs, manifest/profile version, and missing callable tool name.
- Prefer `Unity.Editor.SyncScripts` via `Sync-UnityScriptChanges` for deterministic script refresh/compile waits; no changed paths plus no force must return quickly.
- For script sync, treat `newConsoleErrorCount` / `newConsoleErrorsDetected` as the failure signal. `staleConsoleErrorsPresent` means old console errors were present before the sync and should be reported without turning the sync into a hard failure by itself.
- Prefer `Unity.Editor.SetPlayMode` for enter and exit workflows; it reports transition state, runtime-advance evidence, reconnect notes, and console-error counts.
- Prefer `Unity.Bridge.ListConnections` when a bridge retry may have selected the wrong project or when stale status files are suspected.
- Prefer `Unity.Asset.SetSerializedProperties` for ScriptableObject/data asset scalar and object-reference bindings before falling back to `Unity.RunCommand`.
- Prefer `Unity.Runtime.QueryObjects` for play-mode component counts and sample paths before writing project-specific runtime count snippets.
- Keep compactor behavior default-compatible. Quieter `detailRef` creation is opt-in at noisy callers such as `Unity.RunCommand` and `Unity.ReadConsole`.
- Treat old `BeaconMissing` status as non-blocking when MCP health is ready, and remember helper pack state is per Lens helper session.
- Installed plugin cache versions can move after hygiene work. The repo-local `.agents/plugins/lens-dev-plugin` remains source of truth; if an installed-cache path such as `0.1.1` is missing, locate the active cache version instead of assuming the old path.

## GitNexus Preflight

- If `npx gitnexus status` reports this repo is unindexed or stale, run `npx gitnexus analyze` before symbol edits.
- If impact for a new or recently added symbol returns target-not-found, re-run `npx gitnexus analyze`, then rerun impact.
- Keep deeper diff-aware new-symbol behavior as upstream GitNexus work; do not implement it in Lens.

## Maintenance Rules

- Any pack membership change must update the metadata audit expected counts and required-tool assertions.
- Any TSAM-covered tool path must emit `normalization`, `service`, `adapter`, and `result_shaping` telemetry rows.
- `Unity.RunCommand` result metadata must distinguish validation, compilation, execution, result serialization, and transport/unknown failures.
- `Unity.RunCommand` log previews must preserve enough inline summary data to decide pass/fail without forcing full detail reads.
- `Unity.ManageEditor WaitForStableEditor` should keep inline output compact and store full attempt/state detail behind detail refs.
- New compact-result work should preserve pass/fail decision data inline and move only bulky evidence/readback arrays behind detail refs.
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
