# Unity AI Assistant Patch

Patched fork of `com.unity.ai.assistant` `2.1.0-pre.1` for teams that need a local package source with more reliable Unity MCP and relay startup behavior.

This repository keeps Unity's package layout intact and focuses on editor-side stability fixes around startup, reconnect, and broken-session recovery. It is not an official Unity repository or release channel.

## Why This Fork Exists

Unity editor state changes such as compile, import, and play mode transitions can leave Assistant's MCP and relay session in a broken or tool-less state. This fork adds guardrails so initialization waits for a stable editor state and recovers more predictably when startup fails.

## Patch Highlights

- Delays MCP initialization until the editor is in a stable state.
- Delays relay startup and reconnect while Unity is compiling, importing, or changing play mode.
- Serializes MCP server startup and retries once instead of starting every server at once.
- Treats empty-tool MCP sessions as broken and forces recovery.
- Uses less aggressive relay client disposal.

## Install

Clone this repository with Git LFS enabled, then reference it as a local package in `Packages/manifest.json`.

```bash
git lfs install
git clone https://github.com/becool3000/unity-ai-assistant-patch.git
```

```json
"com.unity.ai.assistant": "file:../UnityAIAssistantPatch"
```

If your clone lives elsewhere, use the appropriate absolute or relative path.

## Notes

- This fork patches Unity-side managed code around the bundled relay binaries. It does not rebuild or replace the relay binaries themselves.
- Official package documentation is still available at [docs.unity3d.com/Packages/com.unity.ai.assistant@latest](https://docs.unity3d.com/Packages/com.unity.ai.assistant@latest).
- Before moving to a newer upstream package version, diff this fork against the update and keep only the fixes that are still needed.
- Additional fork-specific notes are in `README-CODEX-PATCH.md`.
