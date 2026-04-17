# Screenshots

Default screenshot strategy for playtesting is hybrid:

- wait for editor idle before capture
- apply a short warmup before probing or capturing
- default to paused-frame Unity-aware capture first
- optionally step forward one or more deterministic frames before capture
- use `ScreenCapture.CaptureScreenshotIntoRenderTexture(...)` for the Unity-aware path
- stage Unity-aware PNG writes through a temp-root file before moving them into the per-run artifact folder
- fall back to desktop capture quickly only when Unity-aware capture fails

Recommended timings:

- editor idle stability: `3` polls
- idle poll interval: `0.5s`
- post-idle settle delay: `1.0s`
- capture warmup: `1.0s`

Capture path strategy:

1. Unity-aware capture first
   - pause play mode before capture when supported by active package/editor state
   - optionally step one or more frames (`StepFramesBeforeCapture`) while paused for deterministic movement states
   - capture the paused frame into a temporary render texture, then read back and write the PNG from that texture
   - prefer relative project paths for Unity-side screenshot writes unless the caller explicitly needs a different staging location
   - pair it with `Unity_ManageEditor GetState`
   - optionally run `PreCaptureCode` to lock scene state before the shot
   - optionally run a custom runtime probe after the state lock
2. Desktop fallback second
   - call the screenshot skill PowerShell helper against the active Unity window when Unity-aware capture fails

Failure handling:

- Treat custom probe failure as a soft-fail by default.
- Treat Unity-aware capture timeout as a tool limitation, not a broken playtest.
- Allow a short on-disk flush window after the Unity-aware command returns before declaring failure.
- Move the staged Unity PNG into the artifact directory only after it has landed on disk.
- If the Unity PNG appears during that flush window after a transport timeout, treat the shot as Unity-aware success instead of falling back.
- Still write the artifact manifest when probe or Unity-aware capture fails.
- Use desktop fallback only as the reliability backup when Unity-side capture fails after pause/step execution and cleanup.

Outputs should be grouped per run:

- `state-<label>.json`
- `precapture-<label>.json` when pre-capture setup was requested
- `probe-<label>.json` when a custom probe was requested
- `game-<label>.png` for Unity-aware captures
- `desktop-<label>.png` for desktop fallback captures
- `artifact-<label>.json` manifest tying them together

Capture knobs:

- `PausePlaymodeForCapture` (default: `$true`): pause editor play mode before capture.
- `StepFramesBeforeCapture` (default: `0`): advance a deterministic number of frames before rendering.
- `CapturePauseAndStepOnly` (default: `$false`): pause/step without writing a Unity-aware PNG.
- `UnityCaptureTimeoutSeconds` (default: `45`): timeout for the Unity-aware capture command.
- Unity-aware PNG writes may land up to about `1-2s` after the run-command response, so the capture script polls briefly before marking them missing.
