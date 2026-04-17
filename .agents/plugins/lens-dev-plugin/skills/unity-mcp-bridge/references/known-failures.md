# Known Unity MCP Failures

## Approval pending
- Signals: `Awaiting user approval`, `approval_pending`, `Validation: Pending`
- Meaning: Unity is waiting for the user to approve the connection.
- Action: Notify the user and pause Unity editor mutations until approval is completed.

## Expected compile/domain reload window
- Signals: a live `Temp/CodexUnity/expected-reload.json` marker, `IsCompiling = true` or `IsUpdating = true`, or transient disconnect signals such as `Connection closed during read` / disposed `NamedPipeTransport` immediately after external script edits.
- Meaning: Unity is reloading assemblies or refreshing assets and short MCP disconnects are expected.
- Action: Classify this as `EditorReloadingExpected`, wait for Unity to settle, and retry without notifying the user unless the reload window expires and failures continue.

## Wrapper fallback while direct MCP is still healthy
- Signals: wrapper diagnostics report `toolDiscoveryMode = cached-reload-fallback` (or the legacy `lastToolSource = cached-reload-fallback`), sync/wait helpers time out, but direct `Unity_ManageEditor GetState` probes still succeed and show a healthy editor/runtime.
- Meaning: the stdio wrapper or a wrapper-side wait path is stale during reload churn, but Unity itself is still healthy enough to continue through direct/manual MCP.
- Action: Classify this as `WrapperUnhealthyDirectMcpOk`, continue on the direct/manual MCP path, and do not notify the user unless direct probes also fail.

## Ready status but broken handshake
- Signals: bridge status JSON reports `ready`, while `Editor.log` shows `Handshake failed` or `Connection closed during write`
- Meaning: The named pipe exists, but the Codex-to-Unity handshake did not complete.
- Action: Treat this as `ReconnectRequired`, notify the user, and retry only after the bridge is re-established.

## Active WebGL build in progress
- Signals: `Editor.log` still shows active Bee/WebGL markers such as `C_WebGL_wasm`, `Link_WebGL_wasm`, or `[585/596] ... wasm`, and there is no later terminal success or failure marker.
- Meaning: Unity is still building even if MCP responses are flaky or empty during the long compile/link phase.
- Action: Classify this as `BuildInProgress`, stop retrying bridge recovery, and monitor `Editor.log` plus any known build output/report/artifact paths until a terminal result appears.

## Raw relay configured directly in Codex
- Signals: `C:\Users\<user>\.codex\config.toml` points `unity-mcp` directly at `relay_win.exe --mcp`; manual wrapper tests succeed but the live Codex MCP tool still times out.
- Meaning: Codex is bypassing the local stdio wrapper that adapts the relay to MCP framing.
- Action: If direct relay experimental mode is not enabled in `C:\Users\<user>\.codex\unity-mcp-settings.json`, switch back to the wrapper transport. If the experiment is enabled, treat this as an intentional test configuration and benchmark stability before keeping it.

## Timeout without a clear bridge error
- Signals: Codex MCP call times out; status file is missing or stale; `Editor.log` has no decisive approval or handshake message
- Meaning: The bridge is unavailable or stuck in an unknown state.
- Action: Notify the user, pause editor mutations, and re-check after Unity or the relay reconnects.

## Auth warning in Editor.log
- Signals: `Project ID request failed`, `401 (401)`
- Meaning: Unity logged an upstream auth warning; this may or may not be the root cause of the current MCP failure.
- Action: Record the warning, but do not mark the bridge healthy unless MCP itself succeeds.

## 2026-03-15 - Codex desktop custom server startup and package-source mismatch

### Symptoms
- `list_mcp_resources(server:"unity-mcp")` times out during startup or Codex reports the custom MCP server is unavailable.
- The manual wrapper path still works, which can make the failure look like a Unity bridge issue even when the Codex desktop client is the broken layer.
- `Unity.ReadConsole` continues to fail after a package patch because Unity is loading a different package copy than the one that was edited.

### Root causes seen in this repo
- Codex custom MCP server `Working directory` was set to a nonexistent path, so the desktop app could fail before spawning the wrapper.
- Codex desktop integrated MCP used JSONL, while the older wrapper assumed framed stdio only.
- Unity stack traces were coming from `Packages/com.unity.ai.assistant/...`, but an external mirror copy was patched first, so the live editor never picked up the fix.
- Asset refresh or reimport temporarily invalidated Unity discovery files and made healthy bridge code look disconnected.

### Recovery / mitigation
- In Codex MCP server settings, use a valid existing `Working directory` and an absolute `node.exe` command path.
- Keep the wrapper transport-compatible with both Codex desktop JSONL and manual framed stdio if both entry points are used.
- After refresh or reimport, wait for Unity to finish compiling/importing before deciding the bridge is unhealthy.
- Use `Unity_GetConsoleLogs` to inspect package-side tool failures when `Unity.ReadConsole` cannot initialize itself.
- Patch the exact in-project package file referenced by Unity stack traces when debugging `com.unity.ai.assistant` source.
