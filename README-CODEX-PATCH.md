# Technical Fork Notes

This file is the technical companion to the main repo README.

Primary project overview, ownership, and direction now live in:

- [README.md](README.md)

## Baseline

This repository is still rooted in Unity's official `com.unity.ai.assistant 2.3.0-pre.2` package.

The fork strategy is:

- keep Unity's official package as the compatibility baseline
- keep fork-only MCP and relay stability work that upstream still does not cover
- evolve the owned `unity-mcp-lens` path beside the legacy relay path

## Local Package Use

Use this folder as a local Unity package source:

```json
"com.unity.ai.assistant": "file:C:/UnityAIAssistantPatch"
```

Or relatively:

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

## Fork-Specific Behavior Retained

- Delays MCP initialization until the editor is stable and retries with backoff.
- Delays relay startup and reconnect while Unity is compiling, importing, building, or changing play mode.
- Serializes managed MCP server startup and retries failed initial launches once after a short delay.
- Preserves cached MCP tool snapshots when discovery temporarily fails or returns empty.
- Publishes richer bridge status metadata for reload, recovery, and tool-discovery state.
- Adds the owned `unity-mcp-lens` stdio server path with event-driven manifest sync and bridge-owned manifest versioning.
- Adds session-scoped MCP tool packs with a narrow `foundation` default surface and explicit pack-control meta-tools.
- Keeps custom MCP tools for sprite import, serialized property editing, runtime diagnostics, UI diagnostics, project diagnostics, and tile workflows.
- Adds payload/bridge telemetry and assistant usage tooling for benchmark work.

## Operational Notes

- Clone with Git LFS enabled because the bundled relay binaries are tracked through LFS.
- The upstream relay binaries are still Unity binaries; this fork patches the managed Unity-side code and now adds a separate owned Lens server path.
- The owned Lens server source lives under `UnityMcpLensApp~` and installs to `~/.unity/unity-mcp-lens/`.
- The legacy relay path remains under `~/.unity/relay/` when enabled.
- Generated MCP config writes side-by-side entries for Lens and legacy so clients can migrate without breaking fallback compatibility.
- Keep the package version at the upstream version unless there is a strong reason not to; carry fork identity in repo docs and tooling instead of inventing a fake package semver.
