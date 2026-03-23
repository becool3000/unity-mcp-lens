---
uid: mcp-overview
---

# MCP integration in Assistant

Connect Assistant to Model Context Protocol (MCP) servers so that artificial intelligence (AI) can call external tools and data during chat.

Use MCP in Assistant to connect MCP servers that expose tools with parameters and outputs. When you enable MCP and the configured servers start successfully, Assistant can discover and invoke those tools to help with tasks, such as inspecting Git history or running command-line tools on your machine.

MCP servers are external and implementation-specific. Tool names, required parameters, and outputs can vary between servers and some servers might return partial results or unclear errors. If tool behavior differs from your expectations, validate the server in a terminal or with MCP Inspector, then mirror that working setup in Unity. For more information, refer to [Troubleshooting MCP server issues](xref:mcp-troubleshooting).

MCP enables Assistant to perform the following tasks:

- Discover tools exposed by connected MCP servers.
- Call tools with parameters defined by the server.
- Use tool output in responses and workflows.
- Extend Assistant with external capabilities that aren't built into Unity.
- Keep data local by running servers on your machine and controlling environment access.

## How Assistant uses MCP tools

After you enable MCP and refresh a working configuration, Assistant reads each server’s tool manifest to discover the tools and their parameters. During a conversation, Assistant selects the tools based on your prompt, supplies arguments, and displays the server output in the conversation.

If a tool returns unexpected output, Assistant displays the tool's response in the conversation but can't fix the server-side behavior automatically.

## Additional resources

- [Configure MCP servers](xref:mcp-configure)
- [Unity MCP Configuration page reference](xref:mcp-panel)
- [Troubleshooting MCP server issues](xref:mcp-troubleshooting)