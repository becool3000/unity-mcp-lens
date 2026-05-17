---
name: "unity-dev-assistant"
description: "Lens Dev Plugin v0.1.6. Use when Codex is working in a Unity project and needs the full Unity development workflow: fast HealthCheckFirst status checks, Lens bridge-authoritative editor access, explicit tool-pack selection, compile-idle gating, safe paused StepVerifier play-mode checks, runtime probes, FallingSands Main Menu pack selection, FallingSands GPU probes, screenshot-assisted validation, or edit-mode versus play-mode ownership-drift diagnosis."
---

# Unity Dev Assistant

Use this as the primary Unity workflow skill. It depends on [$unity-mcp-bridge](../unity-mcp-bridge/SKILL.md) for low-level bridge recovery and uses the screenshot skill as the desktop fallback when Unity-aware capture fails.

## Version Marker

- Plugin guidance version: `Lens Dev Plugin v0.1.6`
- Expected installed Lens host: `0.1.0-alpha.24` or newer
- If Codex shows an older Lens Dev Plugin version, refresh the plugin cache from the repo-local source before trusting this skill's installed copy.

Default assumption going forward:
- preferred transport is `unity-mcp-lens`
- default model-facing tool surface is `foundation`
- pack expansion is explicit, narrow, and temporary
- the repo plugin source at `.agents/plugins/lens-dev-plugin` is the skill source of truth

## Safe Lens Workflow Truth

- First contact is `Unity.Editor.HealthCheckFast` when the tool is available. Continue only when `safeToContinue=true`; if `agent_should_stop=true`, stop editor-facing work and report the recommended next action.
- Use `Unity.Bridge.ListConnections` for file-backed diagnostics. Stale malformed status files are warning context when fresh matching bridge/editor health exists; fresh relevant malformed status remains blocking.
- Prefer `Unity.PlayMode.StepVerifier` for smoke checks that need Play Mode. Its default is paused stepping; do not allow free-running wall-clock simulation unless the user explicitly needs it.
- Prefer `Unity.Workflow.SelectPackThroughMainMenu` when FallingSands pack selection must go through the real Main Menu UI. It clicks the pack button through runtime UI tools and verifies the active runtime pack without `Unity.RunCommand`.
- Treat console deltas as the pass/fail surface: new errors fail, stale errors/warnings are context unless the task asks to clean them up.
- Prefer `Unity.Editor.RecoverFromHang` with `diagnoseOnly=true` before any recovery action. Do not kill, restart, or clean scratch artifacts without explicit user permission.
- Prefer `Unity.Workflow.RunGpuSimulationProbe` for FallingSands garden-style deterministic GPU checks instead of ad hoc `Unity.RunCommand`.
- Prefer `Unity.Workflow.VerifyRuntimePackSelection` after pack selection, scene reload, or static-state reset when a smoke check depends on a specific runtime pack.
- Use `Unity.RunCommand` preflight/risk labels before risky snippets and favor existing Lens workflow tools when they cover the task.

Helper script selection:
- macOS/Linux: run the `.js` helper with `node`, for example `node scripts/Check-UnityDevSession.js --ProjectPath "$PWD"`
- Windows: run the matching `.ps1` helper with PowerShell
- When both exist, choose the platform-native helper automatically; keep both on the Lens path.
- On Windows, prefer `-StepsPath` for multi-line batch JSON. The PowerShell batch wrapper accepts `-StepsJson`, but generated temp JSON files avoid shell quote damage.
- Windows helper boolean parameters accept normal nested-call values such as `-WaitForEditorIdle true`, `-WaitForEditorIdle 1`, `-IncludeInactive $true`, or `-IncludeInactive:$true`.

## Authoring-First Policy

For durable Unity work, prefer existing authored surfaces before generating scripts.

- Inspect existing scene objects, prefabs, project scripts, package components, presets, and missing-package capabilities before authoring new behavior.
- Use `Unity.Authoring.SuggestReusePlan`, `Unity.Component.Search`, `Unity.Component.ResolveCapability`, `Unity.Component.InspectSchema`, and `Unity.Scene.FindComponents` before custom script generation.
- A custom script requires a reuse insufficiency report covering scene objects, prefabs, project scripts, built-in components, installed package components, compatible presets, and relevant missing packages.
- Use edit-mode tools for durable scene, prefab, serialized-field, object-reference, preset, importer, and package-backed authoring.
- Use play-mode tools only for verification: snapshots, safe method invocation, temporary smoke harnesses, console deltas, and Game view captures.
- Runtime-created objects are gameplay or smoke-test equipment; do not use them as a substitute for production scene or prefab authoring.
- Preview edit-mode mutations before applying them. Report scene, prefab, and asset dirty state separately. Save only through an explicit save tool or an explicit save contract.

## Phase 8 GameObject Tool Preference

For covered GameObject workflows, prefer the split Phase 8 tools over `Unity.ManageGameObject`.

- Read/inspect: `Unity.GameObject.Inspect`, `Unity.GameObject.ListComponents`, `Unity.GameObject.GetComponent`
- Simple GameObject mutation: `Unity.GameObject.PreviewChanges`, then `Unity.GameObject.ApplyChanges`
- Component mutation: `Unity.GameObject.PreviewComponentChanges`, then `Unity.GameObject.ApplyComponentChanges`
- Lifecycle: `Unity.GameObject.PreviewCreate`, `Unity.GameObject.Create`, `Unity.GameObject.PreviewDelete`, `Unity.GameObject.Delete`
- Scene authoring templates: use `objectKind=empty`, `primitive`, `camera`, `light`, `canvas`, or `eventSystem` instead of generating setup scripts.
- Legacy fallback: use `Unity.ManageGameObject` only for compatibility paths or uncovered behavior.

With `foundation` plus `scene` active, the current scene baseline exports `50` tools. Keep `foundation` as the default and activate `scene` only when scene/GameObject work is needed.

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
- For FallingSands Main Menu pack selection, prefer `Unity.Workflow.SelectPackThroughMainMenu`. For other play-mode UI state and interaction, prefer `Unity.UI.QueryRuntimeLayout` and `Unity.UI.InvokeControl` before project-specific `Unity.RunCommand` snippets.
- Treat active input handler changes as editor-authored ProjectSettings mutations that may need script reload or editor restart before defines and devices settle.

## Authoring-First Tool Preference

Use the Phase 1-6 authoring surfaces as the first path for durable work:

- Scene authoring: `Unity.GameObject.PreviewCreate`/`Create`, object/change/component preview/apply tools, `Unity.Scene.SetSerializedProperties`, `Unity.Scene.PreviewAssignObjectReferences`, `Unity.Scene.ApplyAssignObjectReferences`, `Unity.Scene.GetDirtyState`, and `Unity.Scene.Save`.
- Component reuse: `Unity.Component.Search`, `Unity.Component.ResolveCapability`, `Unity.Component.InspectSchema`, `Unity.Scene.FindComponents`, and `Unity.Authoring.SuggestReusePlan`.
- Prefabs and overrides: `Unity.Prefab.Inspect`, `Unity.Prefab.Instantiate`, `Unity.Prefab.CreateFromSceneObject`, `Unity.Prefab.GetOverrides`, selected preview/apply or preview/revert override tools, and `Unity.Prefab.SetSerializedProperties`.
- Presets and copy-from-existing: `Unity.Preset.Search`, `Unity.Preset.Inspect`, `Unity.Preset.PreviewCreate`/`Create` for reusable component preset assets, preset preview/apply tools, and scene/prefab component serialized-value copy tools with explicit `referencePolicy`.
- Package capabilities: `Unity.Package.ResolveCapability` and `Unity.Package.PreviewInstallForCapability`; installation remains preview-only until the user explicitly approves.
- Workflow wrappers: `Unity.Workflow.AuthorSceneObject`, `Unity.Workflow.AuthorPrefab`, `Unity.Workflow.ConfigureExistingComponent`, and `Unity.Workflow.RunPlayModeVerification` when a higher-level authoring flow is useful. These wrappers still preserve discovery, preview/apply, dirty-state, and verification evidence.

## Quick Flow

1. Read the repo-local `docs/unity-mcp-backlog.md` if it exists.
2. If direct Lens tools are available, call `Unity.Editor.HealthCheckFast` before Unity-backed tools.
   - Continue only when `safeToContinue=true`.
   - If `editor_busy_healthy`, wait for compile/import/build/play-transition to settle.
   - If `bridge_unavailable`, inspect `Unity.Bridge.ListConnections` and Command Center status before trying bridge-backed tools.
   - If `agent_should_stop=true`, stop poking Unity and report the provided `recommendedNextAction`.
3. Run `scripts/Check-UnityDevSession.js` on macOS/Linux or `scripts/Check-UnityDevSession.ps1` on Windows when direct Lens tools are unavailable, after context loss, or when a scriptable preflight is useful.
   - Treat the editor-status beacon as the first source of truth for compile/import/play/build transitions when it exists.
   - `BeaconMissing` is not a blocker by itself on projects where the old beacon is retired; continue to MCP health before escalating.
   - Do not start with broad tool discovery or ad hoc MCP status probes when the beacon is fresh.
   - Only confirm MCP authority after the session check reports `BeaconIdle`, `BeaconStale`, or `BeaconMissing`.
   - Treat MCP as the authority for editor mutations and tool execution once the editor is idle enough to act.
   - Treat `ProceedWithDirectLensTools` as direct MCP is healthy, but the helper wrapper path is degraded.
   - `ProceedWithDirectLensTools` requires `DirectHealthProbe.DirectToolReady=true`.
   - If a direct model-facing tool returns `Pipe is broken` while helper health says the bridge and editor are ready, use `Invoke-UnityMcpBatch` for read-only/helper-driven verification, record a transport issue, and retry one lightweight direct Lens probe after the native host reconnects.
   - If `DirectHealthProbe.TransportFailure=true` and helper health is not ready, follow bridge recovery and stop editor-facing work.
   - Default output is compact and operator-focused. Use `-IncludeDiagnostics` only for explicit maintenance.
4. If the bridge is unhealthy, follow [$unity-mcp-bridge](../unity-mcp-bridge/SKILL.md) recovery and stop editor-facing work.
5. Before real Unity work, keep the exported tool surface narrow:
   - start in `foundation`
   - use `Unity.ListToolPacks` to inspect available packs
   - use `Unity.Tools.Menu` for compact pack-oriented navigation when it is available
   - use `Unity.SetToolPacks` only when the task truly needs a wider tool surface
   - keep at most two additional non-foundation packs active
   - If `Unity.SetToolPacks` succeeds and `Unity.Tools.Describe` shows an active-pack tool but Codex still cannot call it, treat that as Codex client dynamic-indexing drift. Use the repo helper scripts or `Invoke-UnityMcpBatch` and report active packs, manifest/profile version, and the missing callable tool.
   - If the host is launched with `UNITY_MCP_LENS_TOOL_SURFACE_MODE=static_all`, all enabled Lens tools are exposed natively up front; `Unity.SetToolPacks` is a compatibility no-op, so use `Unity.Tools.Menu` plus direct real tool calls instead of dynamic pack switching.
6. Suggested pack mapping:
   - console investigation: `console`
   - project scans and validation: `project`
   - GameObjects, scenes, prefabs, hierarchy work: `scene`
   - UI hierarchy, rects, raycasts, captures: `ui`
   - scripts, resource reads, edits: `scripting`
   - imports, assets, prefabs, external content: `assets`
   - profiler and deep diagnostics: `debug`
7. Before any editor mutation, import, play request, or capture, wait for editor idle through fast health or the shared helpers:
   - `IsCompiling = false`
   - `IsUpdating = false`
   - `3` consecutive healthy polls
   - `1.0s` post-idle settle
   `Unity_RunCommand` is the exception in healthy play mode: use the helper, let it prove direct Lens health plus compact play-state health, and allow it to bypass helper-side idle wait when safe.
8. After external edits to compile-affecting files (`*.cs`, `*.asmdef`, `*.asmref`, `*.rsp`, package manifest changes), run `scripts/Sync-UnityScriptChanges.js` on macOS/Linux or `scripts/Sync-UnityScriptChanges.ps1` on Windows before the next Unity-side action.
   - The helper calls `Unity.Editor.SyncScripts`; model-facing calls now wait through scheduled refresh/reload windows and should return `readyForFollowUp=true` only when the editor is safe for follow-up Unity actions.
   - Empty `changedPaths` with no force should still be a fast no-op.
   - Treat transient pack-restore or compact-state failures during an expected reload window as recoverable unless direct Lens health also fails.
   - Treat `newConsoleErrorsDetected=true` as the sync failure signal. Treat `staleConsoleErrorsPresent=true` as old console state unless new errors are also reported.
   - If compile/play behavior looks wrong after file edits, run `scripts/Test-UnitySourceFileIntegrity.ps1` on Windows to check for NUL-byte or invalid UTF-8 source corruption before interpreting bridge failures.
9. Prefer direct MCP tools through the Lens path by default.
   - Use helper scripts for orchestration-heavy flows such as long builds, autoplay, or deterministic screenshot capture.
   - Those helper scripts must also stay on the Lens path; do not bounce into legacy relay or stale fallback behavior.
   - When a known workflow needs multiple project/ui/scene/debug calls, prefer `scripts/Invoke-UnityMcpBatch.js` on macOS/Linux or `scripts/Invoke-UnityMcpBatch.ps1` on Windows so the steps share one Lens session.
10. For large tool outputs, prefer summary/preview first.
   - If a result exposes `detailRef`, call `Unity.ReadDetailRef` only when the preview is insufficient.
   - Do not immediately expand every large payload.
11. For telemetry and agent-cost checks, activate `debug` only when needed and use `Unity.GetLensUsageReport`.
   - Capture a marker before smoke work.
   - Re-run with `sinceLine` after the smoke sequence.
   - Confirm TSAM actions emit `normalization`, `service`, `adapter`, and `result_shaping` rows.
12. For art from Krita, use the handoff path:
   - `ensure_krita_bridge.py`
   - `export_krita_state_to_unity.py`
   - `Import-UnitySpriteState.ps1`
13. For long custom builds or exports, validate the exact enabled build-scene list first with `scripts/Test-UnityBuildSceneList.js --ExpectedScenes ...` on macOS/Linux, or `scripts/Test-UnityBuildSceneList.ps1 -ExpectedScenes ...` on Windows.
14. For play mode smoke and runtime advancement, prefer `Unity.PlayMode.StepVerifier`. Use paused stepping by default, request exact `warmupSteps` and `steps`, capture console delta, and exit or restore state explicitly. Use `Unity.Editor.SetPlayMode` helpers only when a workflow specifically needs normal Play Mode lifecycle behavior outside StepVerifier.
15. For `Unity_RunCommand`, use preflight/risk labels first, then `scripts/Invoke-UnityRunCommand.js` on macOS/Linux or `scripts/Invoke-UnityRunCommand.ps1` on Windows instead of hand-escaping JSON, and prefer small focused probes over one large validation script. In Play Mode, prefer `Unity.PlayMode.StepVerifier`, `Unity.Runtime.InvokeComponentMethod`, `Unity.Runtime.SetComponentProperty`, and `Unity.Runtime.AddTemporaryComponent` before using `Unity_RunCommand` for explicit runtime smoke actions.
   - In healthy play mode, the helper should skip its own idle-wait gate and run directly.
   - Prefer `result.ReturnResult(...)` for structured probe output; do not promote probe data to warning logs just to make it visible.
   - Treat `compilationLogs`, `executionLogs`, and `consoleLogs` as compact previews. Use `logSummary` first, then `Unity.ReadDetailRef` only when full log text is needed.
16. For console reads, prefer direct `Unity.ReadConsole` through MCP.
   - Default to summary/small reads.
   - Treat summary output as the decision surface: counts, grouped rows, cursor, and any compacting `detailRef`.
   - Use `Unity.ReadDetailRef` only if the result was compacted and the full payload matters.
   - Reach for `scripts/Get-UnityConsole.js` on macOS/Linux or `scripts/Get-UnityConsole.ps1` on Windows only when the task explicitly needs the helper path or Lens is unavailable.
17. For menu operations, prefer the direct Unity tool surface when available. Use `scripts/Invoke-UnityMenuItem.ps1` only when there is no direct tool or when a script is operationally safer for the specific task.
18. For art swaps and prefab binding, split the work into two steps:
   - sprite import or serialized reference binding
   - motion or presentation retuning
   Do not mix both concerns in one broad probe unless you already know the ownership chain.
   Prefer `Unity.Asset.PreviewImportSpriteSheetAndBind`, `Unity.Asset.ApplyImportSpriteSheetAndBind`, and `Unity.Asset.VerifySpriteArrayBinding` before importer scripts, YAML reads, or project-specific `Unity.RunCommand` binding probes.
19. When authored scale, tint, sprite assignment, or motion does not stick, use the visual-ownership triage path before changing values again:
   - prefab local scale
   - child renderer local scale
   - serialized authored baseline fields such as `authoredScaleBaseline`
   - runtime-computed multiplier or override path
   - final renderer bounds / screen footprint
20. When the user wants to resize, reposition, or restyle HUD/layout objects directly, prefer persistent scene-owned UI groups over runtime `Ensure*Hierarchy` fallbacks:
   - ensure the authored subtree exists in the scene
   - bind serialized scene refs deterministically
   - save the scene through `Unity.Scene.Save` only when the user has accepted the durable edit or explicitly requested persistence
   - verify the subtree exists on disk before removing or disabling fallback creation
21. For deterministic sprite importer and binding changes, prefer `Unity.Asset.PreviewImportSpriteSheetAndBind`, `Unity.Asset.ApplyImportSpriteSheetAndBind`, and `Unity.Asset.VerifySpriteArrayBinding`; use importer helper scripts only when the native tool surface is unavailable.
22. For narrow prefab field verification after a sprite or property mutation, prefer `Unity.Prefab.Inspect`, `Unity.Prefab.GetOverrides`, and prefab serialized-property tools; use `scripts/Verify-UnityPrefabSerializedFields.ps1` as a helper fallback.
23. For runtime visual ownership inspection, use `scripts/Get-UnityVisualOwnership.ps1`, which wraps `Unity.Runtime.GetVisualBoundsSnapshot` with ownership output enabled.
24. For scene object-reference fields or arrays that should bind to authored scene objects, prefer `Unity.Scene.PreviewAssignObjectReferences` and `Unity.Scene.ApplyAssignObjectReferences`; use `scripts/Bind-UnitySceneSerializedReferences.ps1` only as a helper fallback.
25. For persistent scene UI subtree repair or creation, prefer `Unity.UI.PreviewEnsureHierarchy` and `Unity.UI.ApplyEnsureHierarchy`; use `scripts/Ensure-UnityUiHierarchy.ps1` only as a helper fallback.
26. For deterministic UI layout edits on authored scene objects, prefer `Unity.UI.PreviewLayoutProperties` and `Unity.UI.ApplyLayoutProperties`; use `scripts/Set-UnityUiLayout.ps1` only as a helper fallback.
27. For measured HUD/layout assertions such as inside-screen, right-of, below, below-center, or ordered-stack checks, use `scripts/Verify-UnityUiScreenLayout.ps1` or `Unity.UI.VerifyScreenLayout`; when a layout matrix is required, use `Unity.UI.VerifyScreenLayoutMatrix`.
   - Keep strict `right_of`, `left_of`, `above`, and `below` for non-overlap rect semantics.
   - Use `right_of_center`, `left_of_center`, `above_center`, or `below_center` for “visually higher/lower within the same card” cases such as count labels inside HUD slots.
28. For repeated smoke/workflow sequences, use `scripts/Invoke-UnityMcpBatch.js` or `scripts/Invoke-UnityMcpBatch.ps1` with an ordered JSON step list. Keep per-step outputs compact and read `detailRef` only when the passing summary is insufficient. On Windows, prefer `-StepsPath` for hand-written or multi-line JSON; `-StepsJson` is mainly for generated single-string payloads.
29. If a `Unity_RunCommand` starts a long WebGL build on Windows, pass `-MonitorBuildMode WebGL` plus any known output/report/artifact paths so the PowerShell helper can fall back to passive log/disk monitoring when MCP stdout becomes unreliable. On macOS/Linux, launch the build with the JS helper, then use the session check build monitor and `Editor.log` while the build is active.
30. For autoplay or scripted validation, use `scripts/Run-UnityAutoplayPlaytest.ps1`.
31. For screenshots, use `Unity.UI.CaptureGameView` when you need direct Game view evidence with play-state, camera/canvas, Game view size, console-delta, and timeout diagnostics. Use `scripts/Capture-UnityPlaytestArtifacts.js` on macOS/Linux or `scripts/Capture-UnityPlaytestArtifacts.ps1` on Windows for broader artifact capture with fallback paths.
32. When a scene looks correct in edit mode but different in play mode, treat runtime ownership drift as the default suspect before retuning values. Read `references/authoring-drift.md` and use a small runtime probe to compare the same fields in edit mode and play mode.
33. For score, initials, or other first-run gating backed by `PlayerPrefs`, distinguish a missing key from a saved `0` value. Use `HasKey` when deciding whether a flow is truly first-run.
34. When reading Unity console output, treat known MCP/package chatter as bridge self-noise unless real compiler or gameplay errors are mixed in.
35. For package/import/Input System failures, activate `project` and run `Unity.Project.PackageCompatibility`, `Unity.InputActions.InspectAsset`, or `Unity.InputSystem.Diagnostics` before editing `ProjectSettings.asset`, grepping `Editor.log`, or writing a custom probe.
36. For active input backend changes, use the preview/apply ProjectSettings tools and verify readback before restarting Unity.

## Scene Debugger Pattern

Prefer a scene-owned debugger component when a project needs fast UI or state iteration:

- use `Live` vs `Preview` modes instead of mutating real gameplay state
- drive authored UI through a snapshot model instead of branching inside the view
- prefer deterministic screenshot batches over timer-raced autoplay
- add hitbox overlays, binding validation, and click diagnostics at the scene level
- suppress auto-advance systems such as auto-level or autoplay while preview overrides are active
- keep project-specific state previews in the scene debugger, and use generic MCP tools only for reusable diagnostics

## Read Next When Needed

- `docs/authoring-first-phase-7.md` for authoring-first policy, examples, dogfood prompts, smoke workflows, and metadata baselines
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
- Default Lens host tool surface: `static_all` (`foundation+full`); set `UNITY_MCP_LENS_TOOL_SURFACE_MODE=dynamic_packs` only for clients that explicitly need dynamic pack switching
- Current `foundation` surface: `18` tools
- Current `foundation` + `scene` surface: `50` tools
- Current `foundation` + `ui` surface: `35` tools
- Current `foundation` + `runtime` surface: `29` tools
- Current `foundation` + `project` surface: `37` tools
- Current `foundation` + `assets` surface: `45` tools
- Prefer authoring-first discovery and reuse checks before generating scripts
- Prefer split GameObject TSAM tools before legacy `Unity.ManageGameObject`
- Prefer Phase 11 `project` tools for package/import/Input System diagnostics and active input handler changes
- Prefer component reuse discovery, package capability resolution, presets, copy-from-existing, prefab override tools, and workflow wrappers before custom editor-side probes
- Prefer Phase 12 `ui` and scene-binding tools for persistent HUD authoring, scene reference binding, and screen-layout verification before custom editor-side `Unity_RunCommand`
- Prefer `Invoke-UnityMcpBatch` for repeated multi-step smoke/workflow checks that span packs
- In `static_all`, start with `Unity.Tools.Menu` and call real native tools directly; `Unity.SetToolPacks` is a compatibility no-op, not a required step
- `static_all` host-visible does not guarantee the current Codex turn exposes every native tool as directly callable. If a described tool is missing from the client tool table, use the helper fallback: scene/runtime reads via `Invoke-UnityMcpBatch`, script refresh via `Sync-UnityScriptChanges`, UI layout via `Verify-UnityUiScreenLayout`, menu calls via `Unity.Menu.InvokeAndWaitStable` or `Invoke-UnityMcpBatch`, and project checks via the matching `Test-*` helper.
- Use `Unity.GetLensUsageReport` in `debug` for telemetry baselines, appended smoke rows, and TSAM stage coverage
- Session and bridge checks are compact by default; use `-IncludeDiagnostics` only for explicit maintenance
- `ProceedWithDirectLensTools` means the bridge status and fresh direct health probe are healthy even if the helper wrapper path is not
- `RepairBridge` with `DirectHealthProbe.TransportFailure=true` means the bridge status file may be ready, but direct Lens tool transport is not usable
- Status from the local editor-status beacon first when available; MCP remains the authority for mutations
- The repo-local `.agents/plugins/lens-dev-plugin` path is the helper source of truth. Installed Codex cache versions can move after plugin refresh; if a cache path is missing, locate the active `lens-dev-plugin` cache version instead of reusing a stale versioned path.
- Unity editor compile/import is the authority; do not run `dotnet build` as a Unity compile preflight
- Editor idle gating before all Unity-facing work except helper-driven `Unity_RunCommand` in healthy play mode
- Exact build-scene preflight before long custom builds when the intended scene list is known
- External script edits should be synced through `Unity.Editor.SyncScripts` via `Sync-UnityScriptChanges.js` on macOS/Linux or `Sync-UnityScriptChanges.ps1` on Windows before follow-up Unity actions
- Model-facing `Unity.Editor.SyncScripts` should return `readyForFollowUp=true` only after the host has waited through any scheduled refresh/reload window and verified no new console errors. If a raw/native `status=pending_refresh` appears, treat it as a lower-level scheduled state and wait for editor idle before parallel reads or mutations.
- `Verify-UnityUiScreenLayout.ps1` requires JSON arrays, for example: `-TargetsJson '[{"key":"hud","target":"HUD Canvas","searchMethod":"by_name"}]' -AssertionsJson '[{"type":"inside_screen","targetKey":"hud","margin":0}]'`
- Prefer `Unity.Bridge.ListConnections` for wrong-project or stale-status diagnosis before retrying project-wide reads
- If `Unity.Bridge.ListConnections` shows stale duplicate status files, trust the selected fresh connection/project/PID first and keep stale candidates only as recovery evidence.
- Prefer `Unity.Object.ResolveStablePath` before reusing a hierarchy path across scene, runtime, and UI tools; use its `stableId` or `indexedPath` when duplicate sibling names make plain paths ambiguous.
- Prefer `Unity.Asset.SetSerializedProperties` for ScriptableObject/data asset scalar and object-reference binding
- Prefer `Unity.Runtime.QueryObjects` for play-mode component counts and sample paths
- Prefer `Unity.Runtime.GetComponentSnapshot` for read-only public/serialized runtime component state before writing project-specific `Unity.RunCommand` snippets
- Prefer `Unity.Runtime.InvokeComponentMethod` for public instance smoke hooks, `Unity.Runtime.SetComponentProperty` for narrow runtime state changes, and `Unity.Runtime.AddTemporaryComponent` for play-mode-only harness attachment. Use `requireLensCallable=true` once a project has adopted `[LensCallable]` or `[LensSmokeAction]`.
- When `rg.exe` is blocked in the Codex desktop app context, prefer the shared PowerShell search fallback instead of retrying `rg`
- Hybrid snapshots for playtesting: Unity-aware first, desktop fallback second
- Prefer relative project paths for Unity-side screenshots and state captures
- Deterministic state-lock captures over timer-raced preview captures
- When edit mode and play mode disagree on transforms, colliders, or child visuals, suspect runtime ownership drift before changing values
- Known MCP/package console self-noise is not a gameplay signal by itself
- Prefer small probes, small mutations, and narrow captures over large `Unity_RunCommand` validation scripts
- In healthy play mode, prefer the helper-driven `Unity_RunCommand` bypass over forcing a failing idle wait
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
