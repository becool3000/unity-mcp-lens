# Fork Notes

This repository is a local fork of `com.unity.ai.assistant 2.3.0-pre.2`.

The current fork strategy is:
- use Unity's official `2.3.0-pre.2` package as the base
- keep the fork-only MCP and relay stability work that upstream still does not cover
- keep the custom MCP tool surface that has no official replacement yet

Use this folder as a local Unity package source in other projects.

Manifest entry:

```json
"com.unity.ai.assistant": "file:C:/UnityAIAssistantPatch"
```

Or with a relative path from another project's `Packages/manifest.json`:

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

Fork-specific behavior retained on top of the official `2.3` package:
- Delays MCP initialization until the editor is in a stable state and retries initialization with backoff.
- Delays relay startup and reconnect while Unity is compiling, importing, building, or changing play mode.
- Serializes managed MCP server startup and retries failed initial launches once after a short delay.
- Preserves cached MCP tool snapshots when tool discovery temporarily fails or returns empty.
- Publishes richer bridge status metadata for reload, recovery, and tool-discovery state.
- Keeps the custom MCP tools for sprite import, serialized property editing, runtime diagnostics, UI diagnostics, and project diagnostics.

Official `2.3` functionality now used as the base:
- Graceful relay client shutdown and disposal.
- Batch-mode MCP auto-approve support.
- Late gateway upgrade handling after domain reload.
- `Unity.Web.Fetch` and `Unity.GetDependency`.
- Official 2D capture and improved multi-angle scene capture tooling.

Operational notes:
- Clone with Git LFS enabled because the bundled relay binaries are stored through LFS.
- The relay binaries are upstream Unity binaries; this fork only patches the Unity-side managed code around them.
- Keep the package version at the official upstream version and record fork-specific context in this repo rather than changing the package name or semver.
- When Unity ships a newer package, diff this repo against the update and port forward only the still-needed fork behavior.
