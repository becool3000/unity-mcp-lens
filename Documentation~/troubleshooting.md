# Troubleshooting

## Lens server does not install

Open **Tools > Unity MCP Lens > Install/Refresh Lens Server**. If no prebuilt binary is bundled for your platform, the package publishes the server from `UnityMcpLensApp~` and requires a local .NET SDK 8 or newer. Codex-side access should not require .NET once the native binary exists under `~/.unity/unity-mcp-lens/`.

## MCP client cannot connect

Verify the MCP client launches the Lens binary directly and does not use the legacy relay arguments:

```text
Windows command: %USERPROFILE%\.unity\unity-mcp-lens\unity_mcp_lens_win.exe
macOS Intel command: ~/.unity/unity-mcp-lens/unity_mcp_lens_mac_x64
macOS Apple Silicon command: ~/.unity/unity-mcp-lens/unity_mcp_lens_mac_arm64
Arguments: none
```

For the Codex plugin, the MCP config should launch:

```text
node ./skills/unity-mcp-bridge/scripts/Launch-UnityMcpLens.js
```

## Unity is compiling or reloading

Wait for Unity to finish compiling, importing, or reloading before retrying editor mutations. Lens health and bridge status are available through `Unity.GetLensHealth` once the bridge is reachable.

## Input System or active input backend failures

Activate the `project` pack and run `Unity.InputSystem.Diagnostics` before
custom `Unity.RunCommand` probes, raw `Editor.log` grep, or YAML edits. The
diagnostics tool reports active input handler state, scripting define signals,
Input System package status, loaded assembly/type status, optional devices,
optional `.inputactions` assets, and optional recent editor-log signals.

Use `Unity.ProjectSettings.PreviewActiveInputHandler` before changing the
backend, then `Unity.ProjectSettings.SetActiveInputHandler` for the
editor-authored PlayerSettings mutation. Backend changes often require script
reload or editor restart before defines and devices settle.

## Restart or reload after package and settings changes

Unity domain reload and process exit can close MCP transports before a tool call
returns. Save dirty assets/scenes first, expect the bridge to disconnect during
reload or quit, and reacquire the bridge after the editor reports idle again.
Reliable save/quit/relaunch/reacquire orchestration is still active follow-up
work.

## Large or noisy outputs

Prefer compact state and detail refs. If a wait, editor-state, console, or
usage-report result includes a `detailRef`, read it only when the inline summary
is insufficient. Large `Unity.ManageEditor` rows and repeated tool snapshots are
known payload-shaping targets.

## Legacy relay

Standalone Lens does not bundle or install the legacy Unity relay. If you need the official relay path, install the official Assistant package and keep it separate from Lens.
