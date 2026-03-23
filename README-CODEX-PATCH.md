# Fork Notes

This repository is a local fork of `com.unity.ai.assistant 2.1.0-pre.1` created to stabilize Unity MCP and relay startup behavior in editor workflows that use Codex-style agents and tool calls.

Use this folder as a local Unity package source in other projects.

Manifest entry:

```json
"com.unity.ai.assistant": "file:C:/UnityAIAssistantPatch"
```

Or with a relative path from another project's `Packages/manifest.json`:

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

Behavior changes in this fork:
- Delays MCP initialization until the editor is in a stable state.
- Delays relay startup and reconnect while Unity is compiling, importing, or changing play mode.
- Serializes MCP server startup and retries once instead of loading them all at once.
- Treats empty-tool MCP sessions as broken and forces recovery.
- Uses less aggressive relay client disposal.

Operational notes:
- Clone with Git LFS enabled because the bundled relay binaries are stored through LFS.
- The relay binaries are upstream Unity binaries; this fork only patches the Unity-side managed code around them.
- This fork is best treated as a targeted workaround. If Unity ships a newer package, diff this repo against the update before replacing it.
