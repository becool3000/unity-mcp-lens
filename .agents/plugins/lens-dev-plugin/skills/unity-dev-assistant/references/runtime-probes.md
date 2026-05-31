# Runtime Probes

Use runtime Lens tools first for Play Mode verification: `Unity.PlayMode.InteractionSmoke`
for bounded manual-style UI/pointer/key/wait/snapshot/capture sequences,
`Unity.Runtime.QueryObjects`, `Unity.Runtime.GetComponentSnapshot`,
`Unity.Runtime.InvokeComponentMethod`, `Unity.Runtime.SetComponentProperty`, and
`Unity.Runtime.AddTemporaryComponent`.
Use `Invoke-UnityRunCommand.js` on macOS/Linux or `Invoke-UnityRunCommand.ps1`
on Windows only when the native runtime tools cannot answer the question.

Inside `ExecutionResult`, supported methods are `RegisterObjectCreation`, `RegisterObjectModification`, `DestroyObject`, `Log`, `LogWarning`, `LogError`, and `ReturnResult`. There is no `result.Fail(...)`; use `result.LogError(...)` and return, or throw an exception when execution should fail. Use `result.ReturnResult(...)` for structured probe data.

If a mutating runtime probe or editor-side `Unity_RunCommand` times out, inspect on-disk or scene state before retrying. The command transport can die after Unity already applied part of the mutation.

Prefer probes that answer one question each:

- current control mode
- mount state
- progress counters
- object positions and velocities
- nearest pickup or obstacle distances
- harness status and summary

For game-state transitions such as death, victory, level-up, or milestone triggers, probe around the simulation step:

- capture previous state before simulation
- capture current state after simulation
- run the transition-side effect from the post-simulation edge, not only from the top-level state branch at frame start

If the UI shows a new state but the side effect did not happen, suspect that the transition was detected too early in the frame.

For visual ownership diagnostics, prefer authoring and runtime Lens tools before writing custom probe code:

- `Unity.Asset.PreviewImportSpriteSheetAndBind`, `Unity.Asset.ApplyImportSpriteSheetAndBind`, `Unity.Asset.VerifySpriteArrayBinding`, and `Unity.Asset.VerifySpriteSlicesAndReferences` for sprite-sheet import, slice, atlas-reference, and binding workflows
- `Unity.Prefab.Inspect`, `Unity.Prefab.GetOverrides`, and prefab serialized-property tools for narrow prefab asset checks
- `Get-UnityVisualOwnership.ps1` for runtime scale, tint, sprite, bounds, and baseline inspection

Helper scripts are acceptable when they wrap the Lens path or when the current
client session cannot expose the native tool directly.

For non-test manual sanity checks, prefer `Unity.PlayMode.InteractionSmoke`
before custom Play Mode `Unity_RunCommand` snippets. Keep each run short and
bounded: enter Play Mode only when needed, click or invoke one UI control,
press a key such as `C` or `Escape` only when that interaction is relevant,
collect snapshots/captures/console delta, and exit Play Mode by default.
Treat an unsupported key delivery result as evidence, not success.

After a mutating `Unity_RunCommand`, run one narrow verification probe first. Do not jump straight to a broad scene or playtest validation pass.

For preview scenes and art checks, prefer deterministic state-lock probes over observation-only probes:

- set the animator/controller to the exact state to compare
- pause autoplay or timer-driven toggles when possible
- capture idle and walk separately instead of racing scene auto-cycle

Diagnostic rule for broken sprite imports:

- if importer slicing is suspect, bypass asset sprites in the test harness and create preview sprites from `Texture2D -> Sprite.Create()`
- use that only for diagnostics or preview scenes, not as the default gameplay asset pipeline

Autoplay harness expectation:

- GameObject name defaults to `AutoplayPickupPlaytest`
- Type name defaults to `BikeRunner.AutoplayPickupPlaytest`
- Expected properties when present:
  - `Status`
  - `IsComplete`
  - `FinalSummary`
  - `MountedPickupCount`
  - `OnFootPickupCount`
  - `RiderlessBikePickupCount`

If a repo uses a different harness contract, override the type and object names in `Run-UnityAutoplayPlaytest.ps1` instead of rewriting the orchestration flow.
