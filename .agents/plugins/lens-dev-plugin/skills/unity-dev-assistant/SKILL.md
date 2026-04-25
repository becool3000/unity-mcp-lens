---
name: "unity-dev-assistant"
description: "Use when Codex is working in a Unity project and needs the full Unity development workflow: beacon-first status checks, Lens bridge-authoritative editor access, explicit tool-pack selection, compile-idle gating, play-mode verification, runtime probes, autoplay smoke tests, screenshot-assisted validation, or edit-mode versus play-mode ownership-drift diagnosis."
---

# Unity Dev Assistant

Use this as the primary Unity workflow skill. It depends on [$unity-mcp-bridge](../unity-mcp-bridge/SKILL.md) for low-level bridge recovery and uses the screenshot skill as the desktop fallback when Unity-aware capture fails.

Default assumption going forward:
- preferred transport is `unity-mcp-lens`
- default model-facing tool surface is `foundation`
- pack expansion is explicit, narrow, and temporary
- the repo plugin source at `.agents/plugins/lens-dev-plugin` is the skill source of truth

Helper script selection:
- macOS/Linux: run the `.js` helper with `node`, for example `node scripts/Check-UnityDevSession.js --ProjectPath "$PWD"`
- Windows: run the matching `.ps1` helper with PowerShell
- When both exist, choose the platform-native helper automatically; keep both on the Lens path.

## Phase 8 GameObject Tool Preference

For covered GameObject workflows, prefer the split Phase 8 tools over `Unity.ManageGameObject`.

- Read/inspect: `Unity.GameObject.Inspect`, `Unity.GameObject.ListComponents`, `Unity.GameObject.GetComponent`
- Simple GameObject mutation: `Unity.GameObject.PreviewChanges`, then `Unity.GameObject.ApplyChanges`
- Component mutation: `Unity.GameObject.PreviewComponentChanges`, then `Unity.GameObject.ApplyComponentChanges`
- Lifecycle: `Unity.GameObject.PreviewCreate`, `Unity.GameObject.Create`, `Unity.GameObject.PreviewDelete`, `Unity.GameObject.Delete`
- Legacy fallback: use `Unity.ManageGameObject` only for compatibility paths or uncovered behavior.

With `foundation` plus `scene` active, the current Phase 8 scene surface exports `30` tools. Keep `foundation` as the default and activate `scene` only when scene/GameObject work is needed.

## Phase 11 Project/Package Tool Preference

For package/import/Input System and active input handler work, prefer the Phase 11
`project` tools before custom `Unity_RunCommand` probes, raw `Editor.log` grep,
or YAML edits.

- Diagnostics: `Unity.InputSystem.Diagnostics`
- Package compatibility: `Unity.Project.PackageCompatibility`
- Input actions asset inspection: `Unity.InputActions.InspectAsset`
- Preview backend changes: `Unity.ProjectSettings.PreviewActiveInputHandler`
- Apply backend changes: `Unity.ProjectSettings.SetActiveInputHandler`
- Use `Unity.RunCommand` only for project-specific probes not covered by Lens tools.
- Treat active input handler changes as editor-authored ProjectSettings mutations that may need script reload or editor restart before defines and devices settle.

## Quick Flow

1. Read the repo-local `docs/unity-mcp-backlog.md` if it exists.
2. Run `scripts/Check-UnityDevSession.js` on macOS/Linux or `scripts/Check-UnityDevSession.ps1` on Windows as the mandatory first command for Unity work in a fresh chat or after context loss.
   - Treat the editor-status beacon as the first source of truth for compile/import/play/build transitions when it exists.
   - Do not start with broad tool discovery or ad hoc MCP status probes when the beacon is fresh.
   - Only confirm MCP authority after the session check reports `BeaconIdle`, `BeaconStale`, or `BeaconMissing`.
   - Treat MCP as the authority for editor mutations and tool execution once the editor is idle enough to act.
   - Default output is compact and operator-focused. Use `-IncludeDiagnostics` only for explicit maintenance.
3. If the bridge is unhealthy, follow [$unity-mcp-bridge](../unity-mcp-bridge/SKILL.md) recovery and stop editor-facing work.
4. Before real Unity work, keep the exported tool surface narrow:
   - start in `foundation`
   - use `Unity.ListToolPacks` to inspect available packs
   - use `Unity.SetToolPacks` only when the task truly needs a wider tool surface
   - keep at most two additional non-foundation packs active
5. Suggested pack mapping:
   - console investigation: `console`
   - project scans and validation: `project`
   - GameObjects, scenes, prefabs, hierarchy work: `scene`
   - UI hierarchy, rects, raycasts, captures: `ui`
   - scripts, resource reads, edits: `scripting`
   - imports, assets, prefabs, external content: `assets`
   - profiler and deep diagnostics: `debug`
6. Before any editor mutation, import, `Unity_RunCommand`, play request, or capture, wait for editor idle through the shared helpers:
   - `IsCompiling = false`
   - `IsUpdating = false`
   - `3` consecutive healthy polls
   - `1.0s` post-idle settle
7. After external edits to compile-affecting files (`*.cs`, `*.asmdef`, `*.asmref`, `*.rsp`, package manifest changes), run `scripts/Sync-UnityScriptChanges.js` on macOS/Linux or `scripts/Sync-UnityScriptChanges.ps1` on Windows before the next Unity-side action.
8. Prefer direct MCP tools through the Lens path by default.
   - Use helper scripts for orchestration-heavy flows such as long builds, autoplay, or deterministic screenshot capture.
   - Those helper scripts must also stay on the Lens path; do not bounce into legacy relay or stale fallback behavior.
9. For large tool outputs, prefer summary/preview first.
   - If a result exposes `detailRef`, call `Unity.ReadDetailRef` only when the preview is insufficient.
   - Do not immediately expand every large payload.
10. For telemetry and agent-cost checks, activate `debug` only when needed and use `Unity.GetLensUsageReport`.
   - Capture a marker before smoke work.
   - Re-run with `sinceLine` after the smoke sequence.
   - Confirm TSAM actions emit `normalization`, `service`, `adapter`, and `result_shaping` rows.
11. For art from Krita, use the handoff path:
   - `ensure_krita_bridge.py`
   - `export_krita_state_to_unity.py`
   - `Import-UnitySpriteState.ps1`
12. For long custom builds or exports, validate the exact enabled build-scene list first with `scripts/Test-UnityBuildSceneList.js --ExpectedScenes ...` on macOS/Linux, or `scripts/Test-UnityBuildSceneList.ps1 -ExpectedScenes ...` on Windows.
13. For play mode, use `scripts/Enter-UnityPlayMode.js` on macOS/Linux or `scripts/Enter-UnityPlayMode.ps1` on Windows, require runtime advancement plus a short warmup, and treat transient disconnects during play transition as recoverable until the runtime probe proves success or failure.
14. For `Unity_RunCommand`, use `scripts/Invoke-UnityRunCommand.js` on macOS/Linux or `scripts/Invoke-UnityRunCommand.ps1` on Windows instead of hand-escaping JSON, and prefer small focused probes over one large validation script.
15. For console reads, prefer direct `Unity.ReadConsole` through MCP.
   - Default to summary/small reads.
   - Use `Unity.ReadDetailRef` if the result was compacted and the full payload matters.
   - Reach for `scripts/Get-UnityConsole.js` on macOS/Linux or `scripts/Get-UnityConsole.ps1` on Windows only when the task explicitly needs the helper path or Lens is unavailable.
16. For menu operations, prefer the direct Unity tool surface when available. Use `scripts/Invoke-UnityMenuItem.ps1` only when there is no direct tool or when a script is operationally safer for the specific task.
17. For art swaps and prefab binding, split the work into two steps:
   - sprite import or serialized reference binding
   - motion or presentation retuning
   Do not mix both concerns in one broad probe unless you already know the ownership chain.
18. When authored scale, tint, sprite assignment, or motion does not stick, use the visual-ownership triage path before changing values again:
   - prefab local scale
   - child renderer local scale
   - serialized authored baseline fields such as `authoredScaleBaseline`
   - runtime-computed multiplier or override path
   - final renderer bounds / screen footprint
19. When the user wants to resize, reposition, or restyle HUD/layout objects directly, prefer persistent scene-owned UI groups over runtime `Ensure*Hierarchy` fallbacks:
   - ensure the authored subtree exists in the scene
   - bind serialized scene refs deterministically
   - save the scene
   - verify the subtree exists on disk before removing or disabling fallback creation
20. For deterministic sprite importer changes, use `scripts/Import-UnitySpriteAsset.ps1`.
21. For narrow prefab field verification after a sprite or property mutation, use `scripts/Verify-UnityPrefabSerializedFields.ps1`.
22. For runtime visual ownership inspection, use `scripts/Get-UnityVisualOwnership.ps1`, which wraps `Unity.Runtime.GetVisualBoundsSnapshot` with ownership output enabled.
23. For scene object-reference fields or arrays that should bind to authored scene objects, use `scripts/Bind-UnitySceneSerializedReferences.ps1`.
24. For persistent scene UI subtree repair or creation, use `scripts/Ensure-UnityUiHierarchy.ps1`, which now targets the split Phase 12 preview/apply UI hierarchy tools.
25. For deterministic UI layout edits on authored scene objects, use `scripts/Set-UnityUiLayout.ps1`, which now targets the split Phase 12 preview/apply layout tools.
26. For measured HUD/layout assertions such as inside-screen, right-of, below, or ordered-stack checks, use `scripts/Verify-UnityUiScreenLayout.ps1` or `Unity.UI.VerifyScreenLayout`.
27. If a `Unity_RunCommand` starts a long WebGL build on Windows, pass `-MonitorBuildMode WebGL` plus any known output/report/artifact paths so the PowerShell helper can fall back to passive log/disk monitoring when MCP stdout becomes unreliable. On macOS/Linux, launch the build with the JS helper, then use the session check build monitor and `Editor.log` while the build is active.
28. For autoplay or scripted validation, use `scripts/Run-UnityAutoplayPlaytest.ps1`.
29. For screenshots, use `scripts/Capture-UnityPlaytestArtifacts.js` on macOS/Linux or `scripts/Capture-UnityPlaytestArtifacts.ps1` on Windows. It waits for idle, supports pre-capture state locks, prefers relative project paths, and falls back to desktop capture when Unity-aware capture is flaky.
30. When a scene looks correct in edit mode but different in play mode, treat runtime ownership drift as the default suspect before retuning values. Read `references/authoring-drift.md` and use a small runtime probe to compare the same fields in edit mode and play mode.
31. For score, initials, or other first-run gating backed by `PlayerPrefs`, distinguish a missing key from a saved `0` value. Use `HasKey` when deciding whether a flow is truly first-run.
32. When reading Unity console output, treat known MCP/package chatter as bridge self-noise unless real compiler or gameplay errors are mixed in.
33. For package/import/Input System failures, activate `project` and run `Unity.Project.PackageCompatibility`, `Unity.InputActions.InspectAsset`, or `Unity.InputSystem.Diagnostics` before editing `ProjectSettings.asset`, grepping `Editor.log`, or writing a custom probe.
34. For active input backend changes, use the preview/apply ProjectSettings tools and verify readback before restarting Unity.

## Scene Debugger Pattern

Prefer a scene-owned debugger component when a project needs fast UI or state iteration:

- use `Live` vs `Preview` modes instead of mutating real gameplay state
- drive authored UI through a snapshot model instead of branching inside the view
- prefer deterministic screenshot batches over timer-raced autoplay
- add hitbox overlays, binding validation, and click diagnostics at the scene level
- suppress auto-advance systems such as auto-level or autoplay while preview overrides are active
- keep project-specific state previews in the scene debugger, and use generic MCP tools only for reusable diagnostics

## Read Next When Needed

- `references/workflow.md` for the end-to-end Unity task flow
- `references/playmode.md` for idle gating, play entry, runtime-advancement checks, and warmup rules
- `references/runtime-probes.md` for reusable `RunCommand` patterns and deterministic state-lock guidance
- `references/screenshots.md` for hybrid capture timing, file layout, and fallback rules
- `references/builds.md` for exact scene-list preflight and long WebGL build monitoring
- `references/advanced-package-fork.md` for local `com.unity.ai.assistant` fork detection and patch workflows
- `references/authoring-drift.md` for edit-mode versus play-mode mismatch triage, runtime ownership checks, and scene-owned setup patterns
- `references/ui-persistence.md` for persistent scene UI hierarchies, scene ref rebinding, and runtime fallback repair patterns

## Defaults

- Platform-native helpers first
- Mandatory first step for Unity work in a fresh chat: `scripts/Check-UnityDevSession.js` on macOS/Linux or `scripts/Check-UnityDevSession.ps1` on Windows
- Preferred transport: `unity-mcp-lens`
- Default exported tool surface: `foundation`
- Current `foundation` + `scene` surface: `32` tools
- Current `foundation` + `ui` surface: `22` tools
- Prefer split GameObject TSAM tools before legacy `Unity.ManageGameObject`
- Prefer Phase 11 `project` tools for package/import/Input System diagnostics and active input handler changes
- Prefer Phase 12 `ui` and scene-binding tools for persistent HUD authoring, scene reference binding, and screen-layout verification before custom editor-side `Unity_RunCommand`
- Expand packs explicitly, not heuristically
- Use `Unity.GetLensUsageReport` in `debug` for telemetry baselines, appended smoke rows, and TSAM stage coverage
- Session and bridge checks are compact by default; use `-IncludeDiagnostics` only for explicit maintenance
- Status from the local editor-status beacon first when available; MCP remains the authority for mutations
- Unity editor compile/import is the authority; do not run `dotnet build` as a Unity compile preflight
- Editor idle gating before all Unity-facing work
- Exact build-scene preflight before long custom builds when the intended scene list is known
- External script edits should be synced through `Sync-UnityScriptChanges.js` on macOS/Linux or `Sync-UnityScriptChanges.ps1` on Windows before follow-up Unity actions
- When `rg.exe` is blocked in the Codex desktop app context, prefer the shared PowerShell search fallback instead of retrying `rg`
- Hybrid snapshots for playtesting: Unity-aware first, desktop fallback second
- Prefer relative project paths for Unity-side screenshots and state captures
- Deterministic state-lock captures over timer-raced preview captures
- When edit mode and play mode disagree on transforms, colliders, or child visuals, suspect runtime ownership drift before changing values
- Known MCP/package console self-noise is not a gameplay signal by itself
- Prefer small probes, small mutations, and narrow captures over large `Unity_RunCommand` validation scripts
- After any mutating `Unity_RunCommand`, verify the intended asset, serialized field, or ownership chain with one narrow follow-up probe before doing broader validation
- For painted art, prefer importer and binding verification first, then switch runtime tint to white and verify the tint separately
- For authorable HUD or layout issues, ensure persistent scene UI groups and serialized refs first; only then remove or disable runtime hierarchy creation
- Verify authored UI subtrees on disk after save when replacing runtime fallback hierarchies with scene-owned UI
- Use `HasKey` for first-run best-score or initials flows; `0` alone is not proof that no prior save exists
- Temp output directories unless the user provided a path
- Long WebGL builds should be monitored from `Editor.log` and output artifacts after launch instead of spam-retrying MCP recovery during Bee/wasm compile-link phases
- Package forking and patching is advanced recovery, not the default workflow
- When searching a local assistant fork, treat `Editor/`, `Runtime/`, and the live package folders as the source of truth and exclude `.codex-temp` snapshot content unless a maintenance task explicitly says otherwise

## Package Debugging Note

- When debugging Unity MCP tools implemented inside `com.unity.ai.assistant`, treat the package file named in Unity stack traces plus the repo-local backlog note as the source of truth.
- In this repo, the active patch source is the current Lens checkout/workspace root, not an older embedded mirror copy.
- After patching package C# code, wait for Unity to finish compiling before re-running MCP tool smoke tests. A retest during `IsCompiling=true` is not meaningful.
