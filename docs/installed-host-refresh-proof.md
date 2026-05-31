# Installed Host Refresh And Proof

Use this workflow when Lens package source has changed, new tools should exist,
or Codex appears to be using a stale installed MCP host. The goal is to prove the
installed executable and raw MCP tool registry before trusting the current Codex
client surface.

## Normal Path

Run the one-command wrapper from the Lens repo:

```powershell
C:\unity-mcp-lens\Tools~\Invoke-LensInstalledHostRefreshWorkflow.ps1 -ProjectPath "D:\Glitch Patrol" -WaitForHostExitSeconds 10
```

The wrapper:

- Checks repo metadata against `%USERPROFILE%\.unity\unity-mcp-lens\unity-mcp-lens.json`.
- Detects running processes that are using the installed host executable.
- Refreshes the installed host only when the executable is not in use.
- Runs a raw installed-host MCP proof against the target Unity project.
- Reports `readyForCodexReconnect=true` only after refresh/current proof and raw registry proof succeed.

Do not kill Unity or installed host processes from this workflow. If the wrapper
returns `blocked_running_host`, reconnect or restart the Codex MCP host, close
clients using the installed executable, then rerun the wrapper.

## Lower-Level Helpers

Use these only when debugging a specific stage.

```powershell
C:\unity-mcp-lens\Tools~\Refresh-LensInstalledHost.ps1 -CheckOnly
C:\unity-mcp-lens\Tools~\Refresh-LensInstalledHost.ps1
C:\unity-mcp-lens\Tools~\Test-LensInstalledHostProof.ps1 -ProjectPath "D:\Glitch Patrol"
```

`Refresh-LensInstalledHost.ps1` proves stale/current state and refreshes the
installed executable when safe. It refuses to overwrite
`unity_mcp_lens_win.exe` while that exact installed path is running.

`Test-LensInstalledHostProof.ps1` launches the installed host directly, sends
raw MCP `initialize` and `tools/list`, optionally calls `Unity_Tools_List`, and
checks the expected host version and tool names.

## Result States

- `current`: installed metadata and executable freshness already match the repo.
- `refreshed`: the installed executable was updated and metadata matches the repo.
- `ready`: raw installed-host proof passed.
- `check_stale`: check-only mode found the installed host stale.
- `blocked_running_host`: the installed executable is in use, so refresh was skipped.
- `version_mismatch`: raw installed-host proof launched an older host version.
- `missing_expected_tools`: raw `tools/list` did not include required tools.
- `tools_list_failed`: the host initialized, but raw `tools/list` could not complete for the selected project.

Treat any non-ready state as a safe stop for Unity-facing work. Use only safe
actions such as reconnecting Codex MCP, opening Command Center, checking
`Unity.Editor.HealthCheckFast`, or inspecting `Unity.Bridge.ListConnections`.

## Proof Requirements

Successful proof means:

- Installed host metadata version matches repo `UnityMcpLensApp~/unity-mcp-lens.json`.
- Raw MCP `initialize` reports the expected server version.
- Raw MCP `tools/list` completes for the target project.
- Expected fallback/foundation tools are present:
  `Unity_Tools_List`, `Unity_Tools_Invoke`, `Unity_Tools_BatchInvoke`,
  `Unity_Editor_SyncScripts`, `Unity_Project_BlockedLanguageScan`, and
  `Unity_Tests_Run`.
- New phase tools expected by the current work are present, such as
  `Unity_PlayMode_InteractionSmoke`.

Codex `tool_search` can still be stale after raw proof succeeds. If the raw
host sees the tool but Codex does not, call through `Unity.Tools.Invoke` or
`Unity.Tools.BatchInvoke`, or reconnect the Codex MCP host.

## Local File Package Notes

For projects that consume Lens through a local dependency such as
`file:C:/unity-mcp-lens`, Command Center and installer code must resolve the
real package root through Unity Package Manager metadata. The installed host is
still the executable under `%USERPROFILE%\.unity\unity-mcp-lens`; package source
freshness is not enough by itself.

After Lens package source edits, combine this installed-host proof with
`Unity.Editor.SyncScripts` or `Sync-UnityScriptChanges.ps1` using
`expectedTools` when Unity has to reload package assemblies.
