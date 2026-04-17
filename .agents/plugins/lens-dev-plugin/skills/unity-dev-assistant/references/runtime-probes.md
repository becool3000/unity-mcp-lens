# Runtime Probes

Use `Invoke-UnityRunCommand.ps1` for short, reusable runtime probes.

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

For visual ownership diagnostics, prefer the helper wrappers before writing custom probe code:

- `Import-UnitySpriteAsset.ps1` for deterministic importer settings
- `Verify-UnityPrefabSerializedFields.ps1` for narrow serialized-field checks on prefab assets
- `Get-UnityVisualOwnership.ps1` for runtime scale, tint, sprite, bounds, and baseline inspection

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
