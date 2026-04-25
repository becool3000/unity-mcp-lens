# Getting Started

## Install as a local package

Add Unity MCP Lens to a Unity project's `Packages/manifest.json`:

```json
"com.becool3000.unity-mcp-lens": "file:C:/dev/unity-mcp-lens"
```

Use a relative path if your project and package checkout live near each other:

```json
"com.becool3000.unity-mcp-lens": "file:../unity-mcp-lens"
```

## Configure the MCP server

Open **Tools > Unity MCP Lens > Open Settings**.

The Lens server installs to:

```text
~/.unity/unity-mcp-lens/
```

The preferred MCP client entry is `unity-mcp-lens`, launched directly without `--mcp`.

The default model-facing tool surface is the `foundation` pack. Use
`Unity.ListToolPacks` and `Unity.SetToolPacks` to expand temporarily for
project, scene, scripting, assets, UI, console, or debug work.

## Relationship to the official Assistant package

Lens is no longer installed as `com.unity.ai.assistant`. If you want Unity's official Assistant UI or cloud-generation features, install the official Assistant package separately. Lens owns the MCP bridge, MCP server, tool packs, compact outputs, and local editor/dev tools.

