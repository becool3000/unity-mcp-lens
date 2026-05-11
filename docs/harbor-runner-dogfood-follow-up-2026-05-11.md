# Harbor Runner Dogfood Follow-Up - 2026-05-11

## Scope

Implemented the Harbor Runner dogfood follow-up in `D:\unity-mcp-lens`, with repo-local `.agents/plugins/lens-dev-plugin` kept as the editable plugin source of truth. `D:\Harbor Runner` was used as the acceptance project.

## Tool Surface Changes

- Added `Unity.Scene.QueryObjects` to the `scene` pack for read-only scene object queries, component filters, serialized field reads, compact rows, omitted counts, and `detailRef` support.
- Added `Unity.Runtime.InvokeComponentMethod` to the `runtime` pack for play-mode public instance method calls with typed JSON args, optional waits, before/after state summaries, and console delta checks.
- Added `Unity.Menu.InvokeAndWaitStable` to the `console` pack for menu execution followed by editor stability, scene dirty-state, asset refresh, and console-delta evidence.
- Updated metadata policy, pack catalog, required-tool assertions, and audit baselines for the new tools: `foundation+scene=41`, `foundation+runtime=23`.
- Extended explicit `[McpSchema]` support to typed static tools and generic class tools so generated MCP metadata can stay lower-camel and audit-friendly.

## Contract Fixes

- `Unity.Editor.SyncScripts` now treats scheduled refresh as `success:true`, `status:"pending_refresh"`, `readyForFollowUp:false`, with a wait recommendation. Helpers wait for post-refresh editor idle and fail only if that follow-up wait or new console-error check fails.
- `Unity.UI.VerifyScreenLayout` now surfaces assertion failures as failed verification while preserving layout data; `Verify-UnityUiScreenLayout.ps1` exits nonzero and includes `assertionsPassed:false`.
- `Unity.Tools.ActivateAndVerify` now distinguishes bridge-visible server state from MCP client-callable state with `clientCallableVerified:false` and `clientCallableState:"unknown"`.
- `Enter-UnityPlayMode` helpers now replace stale failed `playReady` summaries after successful degraded fallback.
- `Test-UnityBuildSceneList` gained `-Strict` / `--Strict`; strict mode fails when `exactMatch:false`.

## Helper And Docs Updates

- Updated Windows and JS helpers for script sync, UI layout verification, build-scene strict mode, play-mode entry summaries, and tool-pack fallback mapping.
- Updated Lens skills/docs with:
  - static-all host visibility caveat
  - helper fallback matrix
  - `Verify-UnityUiScreenLayout.ps1` JSON examples
  - `pending_refresh` semantics
  - strict build-scene mode
  - stale bridge interpretation

## Verification Run

- `dotnet build unity-mcp-lens.sln` passed.
- `node Tools~\Test-McpDynamicToolExposure.js` passed.
- Dynamic metadata audit passed with `foundation=17`, `scene=41`, `ui=33`, `runtime=23`, `project=26`, `assets=30`, `debug=32`.
- `git diff --check` passed, with only existing line-ending warnings.
- `Unity.Editor.SyncScripts` smoke passed for no-op, scheduled refresh, and forced refresh paths.
- `Unity.Scene.QueryObjects` smoke in `D:\Harbor Runner` counted `6` `Route Buoy ` objects and read `PlayerInteractor.interactionRadius`.
- `Unity.Runtime.InvokeComponentMethod` smoke in Play Mode called `JobManager.InitializeJob`, `TryAcceptJob`, `TryLoadCargo`, and `TryCompleteDelivery`; the three job mutations returned `true` with no new console errors.
- `Unity.Menu.InvokeAndWaitStable` smoke ran `Harbor Runner/Rebuild MVP Scene`, verified the expected scene path, dirty clear, and clean console.
- `Verify-UnityUiScreenLayout.ps1` passed for a valid HUD assertion and exited `1` with `assertionsPassed:false` for an intentionally impossible assertion.
- `Test-UnityBuildSceneList.ps1` stayed successful in non-strict mismatch mode, failed in strict mismatch mode, and passed in strict exact-match mode.
- Harbor Runner final console check returned `0` entries.
- `npx gitnexus detect-changes --repo unity-mcp-lens --scope all` completed and reported broad critical scope: `28` files, `51` symbols, `25` affected flows.

## Known Notes

- A deliberate compile-error sync smoke was not run in this pass.
- `Assets/Refresh` can close the bridge transport during domain reload; the follow-up health probe recovered to `Ready` after the reload window.
- `AGENTS.md` and `CLAUDE.md` were already dirty before this implementation pass and were not part of the planned Lens changes.
