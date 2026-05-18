---
name: "unity-mcp-bridge"
description: "Lens Dev Plugin v0.1.6. Use when Codex is working in a Unity project and needs fast file-backed Unity health, bridge diagnostics, safe stop contracts, or recovery diagnosis before touching Unity editor state. Prefer Unity.Editor.HealthCheckFast, the owned unity-mcp-lens stdio server, Command Center status, and explicit user escalation only when Unity or the bridge really needs intervention."
---

# Unity MCP Bridge

Use this skill as the operational guide for the local Unity MCP bridge and the owned `unity-mcp-lens` MCP server. The Unity bridge is still the authority for editor mutations and tool execution. This skill standardizes how to verify it, diagnose failures, notify the user, and keep all Unity MCP access on the Lens path.

## Version Marker

- Plugin guidance version: `Lens Dev Plugin v0.1.6`
- Expected installed Lens host: `0.1.0-alpha.24` or newer
- If Codex shows an older Lens Dev Plugin version, refresh the plugin cache from the repo-local source before trusting this skill's installed copy.

## Fast Health First

Start bridge-sensitive work with file-backed health before any Unity-backed call:

- Prefer `Unity.Editor.HealthCheckFast` as the first safe diagnostic when the tool is available.
- Use `Unity.Bridge.ListConnections` for file-backed bridge/editor-health candidates and selected-bridge diagnostics.
- Do not use broad tool discovery, `Unity.RunCommand`, play-mode entry, or other Unity-backed probes to answer basic health.
- Respect the stop contract fields: `safeToContinue`, `agent_should_stop`, `user_action_required`, `recommendedNextAction`, `safe_next_actions`, `unsafe_next_actions`, and `reason`.
- If `agent_should_stop=true`, stop poking Unity. Safe next actions are limited to waiting, opening Command Center, listing connections, or an explicit recovery workflow requested by the user.
- Treat stale or foreign malformed status files as warnings when a fresh matching bridge/editor-health pair exists. Fresh malformed files relevant to the selected project still block as `malformed_status`.
- If the MCP wrapper in the current Codex chat has a closed transport after a host refresh, restart the connector or start a fresh chat; the installed host can still be validated directly from `%USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe`.

## Preferred Topology

- Required for Codex-side helper scripts: `Codex/other MCP client -> unity-mcp-lens -> Unity bridge`
- The repo plugin source at `.agents/plugins/lens-dev-plugin` is the skill and launcher source of truth.
- Legacy relay may exist as a package-side compatibility lane, but helper scripts must not use it.
- Default assumption going forward: `unity-mcp-lens` is the only supported helper-script transport.

## Workflow

1. Read the repo-local backlog at `docs/unity-mcp-backlog.md` if it exists.
2. If direct Lens tools are available, call `Unity.Editor.HealthCheckFast` before Unity-backed tools.
   - Continue only when `safeToContinue=true`.
   - If the state is `editor_busy_healthy`, wait rather than escalating.
   - If the state is `bridge_unavailable`, use `Unity.Bridge.ListConnections` and Command Center status before trying any bridge-backed action.
   - If the state is `unity_alive_stale_unresponsive`, `unity_missing`, `process_missing`, `pid_reused`, or recent relevant `malformed_status`, stop editor-facing work and report the recommended next action.
3. Start with the shared check script when direct tools are unavailable or when you need a scriptable preflight, not an improvised MCP probe:
   - macOS/Linux repo-local Unity work: `node unity-dev-assistant/scripts/Check-UnityDevSession.js`
   - Windows repo-local Unity work: `unity-dev-assistant/scripts/Check-UnityDevSession.ps1`
   - macOS/Linux bridge maintenance: `node unity-mcp-bridge/scripts/Check-UnityMcp.js`
   - Windows bridge maintenance: `unity-mcp-bridge/scripts/Check-UnityMcp.ps1`
   - checks are compact by default; add `--IncludeDiagnostics` or `-IncludeDiagnostics` only when you need the deep editor payload
4. Check the local editor-status beacon when it exists.
   - If the beacon reports a fresh compile/import/reload/play/build transition, treat that as the primary status signal and avoid an immediate extra MCP probe.
   - If the beacon is idle, stale, or missing, continue with the normal MCP health-check flow.
   - Do not begin a fresh Unity chat with a broad tool-discovery request when the beacon is fresh.
4. Prefer the beacon’s Lens bridge fields when present:
   - `status`
   - `connection_path`
   - `last_heartbeat`
   - `bridge_session_id`
   - `manifest_version`
   - `profile_catalog_version`
   - `supports_tool_sync_lens`
   - `last_tools_changed_utc`
5. Attempt one lightweight MCP authority check only when bridge authority still needs to be confirmed and fast health did not already provide a safe answer.
   - Preferred Lens check: `Unity.ListToolPacks`
   - Fallback if Lens tools are not exposed yet: one narrow read-only Unity MCP tool already available in the session
   - `Check-UnityMcp` reports `Ready` only after `ReadyAuthorityProbe.DirectToolReady=true` when a ready bridge status can be probed.
   - If `ReadyAuthorityProbe.TransportFailure=true` or the error is `Pipe is broken`, treat the status file as stale optimism and follow reconnect recovery.
   - If helper health remains ready while direct model-facing tools report `Pipe is broken`, prefer helper batch for read-only verification, record the direct transport issue, and retry one lightweight direct Lens probe after reconnect.
6. If the MCP call succeeds, continue with Unity editor work.
7. If the MCP call fails or times out, run:

```powershell
$script = Join-Path $PWD ".agents\plugins\lens-dev-plugin\skills\unity-mcp-bridge\scripts\Check-UnityMcp.ps1"
& $script -ProjectPath "$PWD"
```

On macOS/Linux:

```bash
node .agents/plugins/lens-dev-plugin/skills/unity-mcp-bridge/scripts/Check-UnityMcp.js --ProjectPath "$PWD"
```

8. Wait briefly, then retry one lightweight authority check.
   - If the failure came immediately after `Sync-UnityScriptChanges.js`/`Sync-UnityScriptChanges.ps1`, a forced refresh, or package recompilation, still follow the health-check flow before assuming Unity is unavailable.
9. If the retry still fails and the check classifies the bridge as `EditorReloadingExpected`, wait for Unity to settle and retry instead of notifying the user.
10. If the retry still fails and the check classifies the bridge as `BuildInProgress`, stop retrying recovery. Switch to passive monitoring of `Editor.log`, the beacon, and any known build artifacts instead of notifying the user.
11. If the retry still fails and the check classifies the bridge as `ApprovalPending`, `ReconnectRequired`, `UnityNotRunning`, `BridgeNotReady`, or another hard-unavailable state, send a Windows notification:

```powershell
$script = Join-Path $PWD ".agents\plugins\lens-dev-plugin\skills\unity-mcp-bridge\scripts\Notify-UnityMcpActionRequired.ps1"
& $script -ProjectPath "$PWD"
```

12. Tell the user Unity MCP needs approval, reconnection, or editor recovery and pause Unity editor mutations until the bridge is healthy.

## Recovery Discipline

- Start recovery with `Unity.Editor.RecoverFromHang` using `diagnoseOnly=true`.
- On Windows, prefer `scripts/Recover-UnityEditorSession.ps1 -DiagnoseOnly` for the safe first pass. A non-diagnose run wraps `Unity.Editor.RecoverFromHang`, permits restart, and waits for a fresh stable Lens-ready editor before success.
- Do not kill, restart, or clean scratch artifacts unless the user explicitly requested those destructive options.
- Use `-AllowKillUnity` only when a stale or hung Unity PID is confirmed and the user has accepted that recovery path.
- Never rely on a live Unity call to decide whether Unity is hung; use health files, process identity, heartbeat age, and bridge state.
- Use Command Center when the user needs a visible status dashboard, server refresh, or explanation of bridge/editor state.

## Lens-Specific Rules

- Prefer `unity-mcp-lens` configured as:
  - Codex Desktop plugin command: the installed native Lens binary, not a relative `./skills/...` launcher path
  - Windows command: `%USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe`
  - macOS Intel command: `~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64`
  - macOS Apple Silicon command: `~/.unity/unity-mcp-lens/unity_mcp_lens_mac_arm64`
  - Helper-script launcher: `node <plugin-dir>/skills/unity-mcp-bridge/scripts/Launch-UnityMcpLens.js` only when the caller supplies an absolute script path or a known plugin working directory
  - args: none
  - Codex plugin env: `UNITY_MCP_LENS_TOOL_SURFACE_MODE=static_all`
- Treat the legacy relay path as:
  - Windows command: `%USERPROFILE%\.unity\relay\relay_win.exe`
  - args: `--mcp`
- Do not burn probe budget on repeated broad tool discovery. In Codex, the plugin defaults to `static_all`; use `Unity.Tools.Menu` as the compact facade for navigation, but remember host-visible tools may still be absent from the current client-callable table.
- Prefer the bootstrap tools before asking for more surface area:
  - `Unity.Editor.HealthCheckFast`
  - `Unity.Bridge.ListConnections`
  - `Unity.ListToolPacks`
  - `Unity.ReadDetailRef`
  - `Unity.Tools.Menu`
  - `Unity.Tools.Describe`
  - `Unity.Tools.ActivateAndVerify`
- `foundation` is the default pack and is always on.
- At most two additional non-foundation packs should be active at once.
- Recommended Codex host mode `UNITY_MCP_LENS_TOOL_SURFACE_MODE=static_all` starts with `foundation+full` and makes `Unity.SetToolPacks` a compatibility no-op. In that mode, use `Unity.Tools.Menu` for compact pack-oriented navigation and call real tools directly when the client exposes them; otherwise use helper scripts or `Invoke-UnityMcpBatch`.
- Helper pack state is per Lens session/process. New helper invocations should auto-prime required packs through the exact map or `Unity.Tools.Describe`; do not assume a pack selected in a previous helper process is still active.
- If Codex dynamic indexing is stale after `notifications/tools/list_changed`, use `Unity.Tools.ActivateAndVerify`, `Invoke-UnityMcpTool.js`, `Invoke-UnityMcpBatch`, or `Unity.Tools.Describe` to query the live manifest/schema/pack requirements until the client refreshes.
- When `Unity.SetToolPacks` reports `clientSurface.expectedRefresh=true`, Lens has done the server-side part by emitting `notifications/tools/list_changed`. If Codex still cannot call a described tool, record it as client indexing drift rather than a bridge pack failure.
- Installed Codex plugin cache versions can move. The repo-local `.agents/plugins/lens-dev-plugin` source stays authoritative; locate the active cache version only when debugging Codex's installed view.
- When a tool result includes `detailRef`, use `Unity.ReadDetailRef` only when the preview/summary is insufficient. Do not immediately expand every large result.
- `Unity.RunCommand`, `Unity.ReadConsole`, and `Unity.ManageEditor WaitForStableEditor` are expected to return compact, stage-aware results. Treat detail refs as the source for full logs, full scanned console entries, or full editor-state attempts when needed.
- For known multi-step smoke or workflow sequences, prefer `Invoke-UnityMcpBatch` so one Lens session can activate the needed exact pack sets and avoid repeated schema/session churn.
- On Windows, call helper scripts directly from PowerShell with `& script.ps1 ...`; avoid nesting `powershell -File` inside an existing PowerShell session. Prefer `Invoke-UnityMcpBatch.ps1 -StepsPath <file>` for multi-line JSON. `-StepsJson` is accepted but shell quoting can strip JSON property quotes before Lens sees the payload.

## Classification Rules

- Treat `ApprovalPending` as user action required in Unity.
- Treat `BuildInProgress` as non-user-actionable when `Editor.log` still shows active WebGL Bee/wasm work and no later terminal build marker. Do not notify the user or keep retrying bridge recovery during that window.
- Treat `BeaconMissing` as non-blocking when the old editor-status beacon is retired or absent; continue with MCP health and only escalate on hard bridge/editor failures.
- Treat `EditorReloadingExpected` as a transient state; wait for Unity compile/domain reload settle instead of notifying the user.
- Treat `ReconnectRequired` as user action required even if the bridge status file says `ready`.
- Treat `UnityNotRunning` or `BridgeNotReady` as unavailable; do not guess your way through scene or prefab work.
- Only treat the bridge as healthy when MCP succeeds or the check script reports `Ready` with no hard failure signals.

## Legacy Fallback Policy

- Do not use the legacy relay or any manual wrapper path for Codex helper-script work.
- If `unity-mcp-lens` is unavailable, stop Unity mutations and repair Lens instead of falling back silently.
- Legacy relay may still exist inside the Unity package for Assistant/Gateway compatibility, but it is not a valid Codex helper transport.
- Do not maintain standalone copies of Lens skills under `$CODEX_HOME/skills`; the repo plugin is the canonical distribution path.

## Diagnostics

- Compact output is the default operator view. Reach for diagnostics mode only when the maintenance task actually requires raw editor detail.
- Compact TSAM results may include `rawBytes`, `shapedBytes`, `sha256`, `detailAvailable`, and `detailRef`; use those fields as shaping proof before expanding detail.
- For `Unity.RunCommand`, inspect `logSummary` before expanding log detail refs.
- For `Unity.ReadConsole`, use summary counts/grouped rows first and expand the scanned-entry detail ref only when source lines or full stack traces matter.
- For package/import/Input System failures, prefer `project` pack tools (`Unity.Project.PackageCompatibility`, `Unity.InputActions.InspectAsset`, `Unity.InputSystem.Diagnostics`, and the active input handler preview/apply tools) before resorting to ad hoc editor probes or raw `Editor.log` grep.
- Inspect `%USERPROFILE%\.unity\mcp\connections\bridge-status-*.json` for the current bridge status.
- Inspect `%LOCALAPPDATA%\Unity\Editor\Editor.log` for approval, handshake, disconnect, compile, and auth signals.
- On macOS inspect `~/Library/Logs/Unity/Editor.log` for the same signals.
- Check installed MCP binaries:
  - `%USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe`
  - `~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64`
  - `~/.unity/unity-mcp-lens/unity_mcp_lens_mac_arm64`
  - `%USERPROFILE%\.unity\relay\relay_win.exe`
- Use [references/known-failures.md](./references/known-failures.md) for recurring failure patterns and recovery guidance.

## Improvement Loop

- During normal feature work, do not self-edit this skill.
- When a new bridge issue appears, append it to the repo-local `docs/unity-mcp-backlog.md` with symptom, detection signal, workaround, proposed skill change, and status.
- During explicit skill-maintenance work, review backlog entries from active repos and fold reusable fixes into this skill or its scripts.

## Codex Desktop Notes (Lens)

- If the custom MCP server `Working directory` is invalid, Codex may fail before the MCP server starts. Prefer a real project path or a stable existing directory such as the user profile.
- The Lens MCP server is a standard MCP stdio server. It does not need `--mcp`.
- The Codex plugin launcher should use the installed native Lens binary and should not require a local .NET SDK. First-time Unity-side Lens installation only requires .NET SDK 8+ when no matching prebuilt server artifact is bundled.
- When `Assets/Refresh`, `Assets/Reimport All`, package refresh, or script recompilation disrupt discovery, treat that as a temporary editor reload window. Wait for Unity to return to `IsCompiling=false` and `IsUpdating=false`, then retry a lightweight MCP authority check before escalating.
- When Unity stack traces point into `Packages/com.unity.ai.assistant/...`, patch the in-project package source that Unity is actually loading rather than an external mirror copy.
- When a repo intentionally uses an external patch source, search the live package folders and exclude `.codex-temp` snapshots before deciding where to patch.
