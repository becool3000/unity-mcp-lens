---
name: "unity-mcp-bridge"
description: "Use when Codex is working in a Unity project and needs to verify, diagnose, or recover the local Unity MCP bridge before touching Unity editor state. Prefer the owned unity-mcp-lens stdio server, beacon-first status checks, event-driven manifest awareness, and explicit user escalation only when Unity or the bridge really needs intervention."
---

# Unity MCP Bridge

Use this skill as the operational guide for the local Unity MCP bridge and the owned `unity-mcp-lens` MCP server. The Unity bridge is still the authority for editor mutations and tool execution. This skill standardizes how to verify it, diagnose failures, notify the user, and keep all Unity MCP access on the Lens path.

## Preferred Topology

- Required for Codex-side helper scripts: `Codex/other MCP client -> unity_mcp_lens -> Unity bridge`
- Legacy relay remains a package-side compatibility lane, but helper scripts should not use it.
- Default assumption going forward: `unity-mcp-lens` is the only supported helper-script transport.

## Workflow

1. Read the repo-local backlog at `docs/unity-mcp-backlog.md` if it exists.
2. Start with the shared check script, not an improvised MCP probe:
   - for repo-local Unity work, `unity-dev-assistant/scripts/Check-UnityDevSession.ps1`
   - for explicit bridge-maintenance work, `unity-mcp-bridge/scripts/Check-UnityMcp.ps1`
   - both checks are compact by default; add `-IncludeDiagnostics` only when you need the deep editor payload
3. Check the local editor-status beacon first when it exists.
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
5. Attempt one lightweight MCP authority check only when bridge authority still needs to be confirmed.
   - Preferred Lens check: `Unity.ListToolPacks`
   - Fallback if Lens tools are not exposed yet: one narrow read-only Unity MCP tool already available in the session
6. If the MCP call succeeds, continue with Unity editor work.
7. If the MCP call fails or times out, run:

```powershell
$script = Join-Path $PWD ".agents\plugins\lens-dev-plugin\skills\unity-mcp-bridge\scripts\Check-UnityMcp.ps1"
powershell -ExecutionPolicy Bypass -File $script -ProjectPath "$PWD"
```

8. Wait briefly, then retry one lightweight authority check.
   - If the failure came immediately after `Sync-UnityScriptChanges.ps1`, a forced refresh, or package recompilation, still follow the health-check flow before assuming Unity is unavailable.
9. If the retry still fails and the check classifies the bridge as `EditorReloadingExpected`, wait for Unity to settle and retry instead of notifying the user.
10. If the retry still fails and the check classifies the bridge as `BuildInProgress`, stop retrying recovery. Switch to passive monitoring of `Editor.log`, the beacon, and any known build artifacts instead of notifying the user.
11. If the retry still fails and the check classifies the bridge as `ApprovalPending`, `ReconnectRequired`, `UnityNotRunning`, `BridgeNotReady`, or another hard-unavailable state, send a Windows notification:

```powershell
$script = Join-Path $PWD ".agents\plugins\lens-dev-plugin\skills\unity-mcp-bridge\scripts\Notify-UnityMcpActionRequired.ps1"
powershell -ExecutionPolicy Bypass -File $script -ProjectPath "$PWD"
```

12. Tell the user Unity MCP needs approval, reconnection, or editor recovery and pause Unity editor mutations until the bridge is healthy.

## Lens-Specific Rules

- Prefer `unity-mcp-lens` configured as:
  - Windows command: `%USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe`
  - macOS Intel command: `~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64`
  - macOS Apple Silicon command: `~/.unity/unity-mcp-lens/unity_mcp_lens_mac_arm64`
  - args: none
- Treat the legacy relay path as:
  - Windows command: `%USERPROFILE%\.unity\relay\relay_win.exe`
  - args: `--mcp`
- Do not burn probe budget on repeated broad tool discovery. Lens is designed to use event-driven manifest sync and narrow tool packs.
- Prefer the pack-control tools before asking for more surface area:
  - `Unity.ListToolPacks`
  - `Unity.SetToolPacks`
  - `Unity.ReadDetailRef`
- `foundation` is the default pack and is always on.
- At most two additional non-foundation packs should be active at once.
- When a tool result includes `detailRef`, use `Unity.ReadDetailRef` only when the preview/summary is insufficient. Do not immediately expand every large result.

## Classification Rules

- Treat `ApprovalPending` as user action required in Unity.
- Treat `BuildInProgress` as non-user-actionable when `Editor.log` still shows active WebGL Bee/wasm work and no later terminal build marker. Do not notify the user or keep retrying bridge recovery during that window.
- Treat `EditorReloadingExpected` as a transient state; wait for Unity compile/domain reload settle instead of notifying the user.
- Treat `ReconnectRequired` as user action required even if the bridge status file says `ready`.
- Treat `UnityNotRunning` or `BridgeNotReady` as unavailable; do not guess your way through scene or prefab work.
- Only treat the bridge as healthy when MCP succeeds or the check script reports `Ready` with no hard failure signals.

## Legacy Fallback Policy

- Do not use the legacy relay or any manual wrapper path for Codex helper-script work.
- If `unity-mcp-lens` is unavailable, stop Unity mutations and repair Lens instead of falling back silently.
- Legacy relay may still exist inside the Unity package for Assistant/Gateway compatibility, but it is not a valid Codex helper transport.

## Diagnostics

- Compact output is the default operator view. Reach for diagnostics mode only when the maintenance task actually requires raw editor detail.
- Inspect `%USERPROFILE%\.unity\mcp\connections\bridge-status-*.json` for the current bridge status.
- Inspect `%LOCALAPPDATA%\Unity\Editor\Editor.log` for approval, handshake, disconnect, compile, and auth signals.
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
- Until prebuilt Lens binaries are bundled, first-time Lens installation may publish from source and therefore requires a local .NET SDK 8+.
- When `Assets/Refresh`, `Assets/Reimport All`, package refresh, or script recompilation disrupt discovery, treat that as a temporary editor reload window. Wait for Unity to return to `IsCompiling=false` and `IsUpdating=false`, then retry a lightweight MCP authority check before escalating.
- When Unity stack traces point into `Packages/com.unity.ai.assistant/...`, patch the in-project package source that Unity is actually loading rather than an external mirror copy.
- When a repo intentionally uses an external patch source, search the live package folders and exclude `.codex-temp` snapshots before deciding where to patch.
