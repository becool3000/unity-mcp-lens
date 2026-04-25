# Unity MCP Lens

Unity MCP Lens is a standalone Unity package for exposing a focused, low-noise MCP bridge to external agents.

It installs the owned `unity-mcp-lens` stdio server, presents editor controls under **Tools > Unity MCP Lens**, and keeps the model-facing tool surface narrow through explicit tool packs.

Current development is moving high-friction workflows into TSAM tools: Tool,
Service, Adapter, and Model. TSAM surfaces keep public schemas narrow, put
Unity-facing work behind adapters, emit stage telemetry, and use preview/apply
pairs for durable mutation. Existing broad tools remain available as
compatibility fallbacks while package, scene, UI, and project workflows move to
split tools.

## Start here

- [Getting started](getting-started.md)
- [Tool packs and MCP surface](tool-packs.md)
- [TSAM refactor direction](../docs/TSAM.md)
- [Migrating from the compatibility package](migration-from-assistant-compat.md)
- [Troubleshooting](troubleshooting.md)

