# Play Mode

Treat play mode as a three-step check:

1. Wait for editor idle.
2. Enter play mode.
3. Prove the runtime is advancing and settled.

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
- If play enters but the runtime probe does not advance, treat that as a play/runtime problem, not a healthy playtest.
- After scene open, asset import, external script edits, or script compile, let Unity finish settling before entering play.
- After external script edits, run `Sync-UnityScriptChanges.js` on macOS/Linux or `Sync-UnityScriptChanges.ps1` on Windows before requesting play mode.
- After stopping play mode, expect one recovery pass before follow-up Unity tool calls.
- If focus-sensitive stalls appear again, capture runtime probe data and treat it as a playtest environment issue before blaming MCP transport.
