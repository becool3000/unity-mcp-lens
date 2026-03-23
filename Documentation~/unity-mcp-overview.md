---
uid: unity-mcp-overview
---

# Unity MCP

Enable AI agents to interact directly with the Unity Editor through the Model Context Protocol (MCP).

Unity MCP connects large language model (LLM)-based agents, such as Claude Code and Cursor, to the Unity Editor through standardized MCP tools. It helps automate tasks such as project management, scene creation, asset management, and code generation.

Unity MCP is built on the open [Model Context Protocol](https://modelcontextprotocol.io/), which defines a secure, structured way for AI agents to interact with external tools. With Unity MCP, AI clients can safely query Unity data, execute commands, and build in-editor workflows.

## How Unity MCP works

When Unity starts, the MCP bridge launches automatically and opens a local IPC channel (named pipes on Windows, Unix sockets on macOS/Linux). The relay binary, installed to `~/.unity/relay/`, runs as an MCP server process started by AI clients. It connects to the bridge and exposes Unity's capabilities as MCP tools.

### Architecture overview

```
AI Client (Cursor, Claude Code, etc.)
    |
    | MCP protocol (stdio)
    |
Relay binary (~/.unity/relay/) with --mcp flag
    |
    | IPC (named pipe / Unix socket)
    |
Unity Editor (MCP Bridge)
    |
    | McpToolRegistry
    |
Registered tools (built-in + custom)
```

### Connection security

- **AI Gateway connections**: Automatically approved with no user interaction required.
- **Direct connections** (external MCP clients): Allowed but require user approval through a dialog in Project Settings. Previously approved clients are remembered.

## Key features

- **Tool registration system**: Create and register MCP tools using attributes, interfaces, or runtime APIs.
- **Built-in tools**: Automate Unity tasks such as scene management, asset operations, script editing, and console access.
- **Dynamic discovery**: Tools are detected and registered automatically at editor startup.
- **Connection security**: Direct connections require explicit user approval; gateway connections are trusted.
- **Multi-client support**: Multiple MCP clients can connect to the same Unity instance simultaneously.

## Project Settings

Configure the MCP bridge in **Edit** > **Project Settings** > **AI** > **Unity MCP**. The settings page shows:

- Bridge status and start/stop controls
- Connected and pending client connections
- Tool list with enable/disable toggles
- Client integration configuration

> [!NOTE]
> The **MCP Servers** page (under **AI** > **MCP Client**) configures MCP servers that the Unity Assistant *connects to*. The **Unity MCP** page configures the MCP server that Unity *exposes* to external AI clients.

## Additional resources

- [Get started with Unity MCP](xref:unity-mcp-get-started)
- [Register custom MCP tools](xref:unity-mcp-tool-registration)
- [Troubleshoot Unity MCP issues](xref:unity-mcp-troubleshooting)
