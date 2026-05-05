# Play Mode

Treat play mode as a three-step check:

1. Wait for editor idle.
2. Verify Unity source integrity for NUL-byte or invalid UTF-8 corruption.
3. Enter play mode.
4. Prove the runtime is advancing and settled.

Defaults:

- idle stability: `3` consecutive polls
- idle poll interval: `0.5s`
- post-idle settle delay: `1.0s`
- play-ready poll interval: `1.0s`
- post-play warmup: `1.0s`

Rules:

- `Unity_ManageEditor Play` may return `Connection disconnected`. That is not enough to declare failure.
- `Unity_ManageEditor Play` may also return a structured `transitioning_to_play` result with `ReconnectExpected = true`. Treat that as a recoverable transition, not a failure.
- Poll `Unity_ManageEditor GetCompactState` until:
  - `IsPlaying = true`
  - `RuntimeProbe.IsAvailable = true`
  - `RuntimeProbe.HasAdvancedFrames = true`
  - `UpdateCount >= 10` or `UnscaledTime` increases across polls
- If the play request disconnected but the follow-up runtime probe advanced, treat the play request as successful on a degraded path.
- If source integrity fails, stop before play mode and fix the reported `.cs` file; do not diagnose it as bridge instability.
- If play enters but the runtime probe does not advance, treat that as a play/runtime problem, not a healthy playtest.
- If play-ready fails, inspect the helper's inline `consoleErrors` summary before running separate console/log probes.
- After scene open, asset import, external script edits, or script compile, let Unity finish settling before entering play.
- After external script edits, run `Sync-UnityScriptChanges.js` on macOS/Linux or `Sync-UnityScriptChanges.ps1` on Windows before requesting play mode.
- After stopping play mode, expect one recovery pass before follow-up Unity tool calls.
- Prefer `Exit-UnityPlayMode.ps1` or `Unity.Editor.ExitPlayMode` for cleanup. Do not stop play mode through `Unity.RunCommand`, because RunCommand play-state restoration can obscure whether the editor actually stopped.
- If exit transport closes but the follow-up editor idle wait succeeds, report a recovered transition instead of a top-level failure.
- If focus-sensitive stalls appear again, capture runtime probe data and treat it as a playtest environment issue before blaming MCP transport.
