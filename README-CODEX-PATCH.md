Unity AI Assistant local fork for Codex/Unity MCP stability.

Use this folder as a local Unity package source in other projects.

Manifest entry:

```json
"com.unity.ai.assistant": "file:C:/UnityAIAssistantPatch"
```

Or with a relative path from another project's `Packages/manifest.json`:

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

What this fork changes:
- Delays MCP initialization until the editor is in a stable state.
- Delays relay startup and reconnect while Unity is compiling, importing, or changing play mode.
- Serializes MCP server startup and retries once instead of loading them all at once.
- Treats empty-tool MCP sessions as broken and forces recovery.
- Uses less aggressive relay client disposal.

Notes:
- This is a temporary local workaround for `com.unity.ai.assistant 2.1.0-pre.1`.
- The underlying relay binaries are still Unity's binaries; this fork only patches the Unity-side managed code around them.
- If Unity ships a newer package, diff this fork against the update before replacing it.
