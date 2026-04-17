# Workflow

Use this sequence for Unity project work:

1. Run `Check-UnityDevSession.ps1` as the canonical first command.
   - Treat the default output as the normal operator path.
   - Add `-IncludeDiagnostics` only when you are doing explicit bridge or wrapper maintenance.
2. Treat the beacon/session check as the opening status source.
   - Do not replace it with `list_mcp_resources`, `Unity_ManageEditor GetState`, or another ad hoc MCP status probe when the beacon is fresh.
3. If bridge health is not ready, defer to `$unity-mcp-bridge` and stop.
4. If the session check reports a transition or active build, wait or monitor first instead of spending the first probe budget on MCP retries.
5. If the bridge is ready but the built-in MCP client is stale, continue with the manual wrapper path instead of asking the user to reconnect Unity again.
6. If the bridge or sync helper reports `WrapperUnhealthyDirectMcpOk`, continue on the direct/manual MCP path and do not escalate to the user.
7. Before any editor mutation, import, `Unity_RunCommand`, playtest, or screenshot, wait for compile/update idle:
   - `IsCompiling = false`
   - `IsUpdating = false`
   - `3` consecutive healthy polls
   - `1.0s` post-idle settle delay
8. After external edits to compile-affecting files, run `Sync-UnityScriptChanges.ps1` and let it either observe the natural compile/reload or force a refresh/recompile and settle.
9. If the sync wrapper path hangs or reports failure, verify direct `Unity_ManageEditor GetState` health before giving up. Two healthy idle state checks are enough to continue on the direct MCP path.
10. Before a long custom build, validate the exact enabled build-scene list with `Test-UnityBuildSceneList.ps1` or `Check-UnityDevSession.ps1 -ExpectedScenes ...`.
11. If art is coming from Krita, prefer the handoff path:
   - `ensure_krita_bridge.py`
   - `export_krita_state_to_unity.py`
   - `Import-UnitySpriteState.ps1`
12. For play mode, treat success as two separate checks:
   - play mode entered
   - runtime probe advanced and settled
13. A play-request disconnect is not enough to declare failure. If follow-up state or runtime probes show advancing play, treat it as a successful but degraded transition.
14. For validation, pair runtime state with visuals so gameplay bugs are not misclassified as bridge bugs.
15. For visual comparison scenes, prefer deterministic state-lock code over timer-based autoplay.
16. Prefer scene-owned debugger components for project-specific UI or screen-state preview, and use generic MCP tools for reusable diagnostics such as hit regions, layout snapshots, and reference audits.
17. If a mutating `Unity_RunCommand` launches a long WebGL build, prefer `Invoke-UnityRunCommand.ps1 -MonitorBuildMode WebGL` with any known output/report/artifact paths so the wrapper can switch to passive monitoring if MCP stdout drops.
18. If a mutating `Unity_RunCommand` times out outside the long-build monitor path, inspect on-disk or scene state before retrying; Unity may have applied part of the change before the transport died.
19. Prefer smaller Unity mutations and smaller validation probes over one large command, especially asset creation vs scene save and tiny runtime checks vs giant diagnostics.
20. When reading console output, treat MCP, relay, and package chatter as secondary until you have ruled out real compiler or gameplay errors.
21. When inspecting a local `com.unity.ai.assistant` fork, search the live package folders first and exclude `.codex-temp` snapshots unless the maintenance task explicitly needs snapshot history.

Stop conditions:

- The exact enabled build-scene list does not match the requested long-build scene list
- Bridge classification is `BuildInProgress`; stop retrying recovery and monitor the active build instead
- Bridge classification is neither `Ready`, `EditorReloadingExpected`, nor `WrapperUnhealthyDirectMcpOk`
- Manual wrapper is also unhealthy
- Unity never reaches idle or never reaches advancing play-ready state
- Unity crashes or the bridge repeatedly returns an empty tool list after refresh
