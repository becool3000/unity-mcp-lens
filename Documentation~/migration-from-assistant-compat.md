# Migration From Assistant Compatibility Mode

Earlier Lens builds were consumed through the official Assistant package id. Standalone Lens uses its own package id:

```json
"com.becool3000.unity-mcp-lens": "file:C:/dev/unity-mcp-lens"
```

Remove the old local dependency entry if present:

```json
"com.unity.ai.assistant": "file:C:/dev/unity-mcp-lens"
```

## Settings migration

On first load, Lens attempts a one-time copy from:

```text
ProjectSettings/Packages/com.unity.ai.assistant/Settings.json
```

to:

```text
ProjectSettings/Packages/com.becool3000.unity-mcp-lens/Settings.json
```

The old file is not deleted automatically.

## MCP client config

Prefer the `unity-mcp-lens` MCP server entry. It should launch the installed Lens binary directly:

```text
~/.unity/unity-mcp-lens/unity_mcp_lens_win.exe
```

Do not pass `--mcp` to the Lens server.

## Side-by-side with official Assistant

Standalone Lens is designed to coexist with the official `com.unity.ai.assistant` package. Official Assistant functionality should remain in the official package. Lens should remain under **Tools > Unity MCP Lens** and **Project Settings > Tools > Unity MCP Lens**.

