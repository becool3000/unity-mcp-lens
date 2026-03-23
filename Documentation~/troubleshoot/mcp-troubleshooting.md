---
uid: mcp-troubleshooting
---

# Troubleshooting MCP server issues

Resolve Model Context Protocol (MCP) server integration issues by validating server behavior, aligning configuration, and fixing PATH or environment issues in the Unity Editor.

If the same command works in [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector) but fails only in the Unity Editor, the issue is likely related to the Unity integration. File a [bug report](https://support.unity.com/hc/en-us/articles/206336985-How-do-I-submit-a-bug-report) with Unity and include your configuration file, server logs, and the command that works in MCP Inspector.

If you need basic setup steps, refer to [Configure MCP servers](xref:mcp-configure). For Unity MCP Configuration page settings, refer to [Unity MCP Configuration page reference](xref:mcp-panel).

## Local `stdio` server fails to start after you refresh configuration

This issue occurs when you select **Refresh Config File and Reload Servers** on the **Unity MCP Configuration** page and a local `stdio` server fails to start.

### Symptoms

You might notice one or more of the following symptoms:

- The **Unity MCP Configuration** page reports `Executable not found in $PATH: "<name>"`.
- The **Unity MCP Configuration** page reports a process launch error.
- The server doesn't start and no tools appear.

### Cause

This issue can occur when:

- The MCP server isn't installed exactly as its repository instructions require.
- Unity can't access the required executables because it uses a different PATH than your terminal.
- The command or flags in Unity don't match the command that works in a terminal.

### Resolution

To resolve this issue, follow these steps:

1. Install the MCP server exactly as described in its repository documentation.
1. Run the MCP server manually to confirm it works using one of the following methods:

   - Run the MCP server command in a terminal.
   - Start the server in [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector) and validate the tools and responses.

1. In the Unity Editor, select **Open Config File** on the **Unity MCP Configuration** page.
1. Update the MCP configuration to use the exact same executable, arguments, and flags that worked in the terminal/MCP Inspector.
1. To configure PATH in Unity, follow these steps:

   1. On the **Unity MCP Configuration** page, under **Path Configuration**, compare **User Path** and **Path Accessible by Unity**.
   1. In **User Path**, add the directories for the required executables so Unity can resolve the same toolchain.

      For example, if the server uses `uvx`, ensure Unity can resolve the `uvx` executable path (such as, `/opt/homebrew/bin` on macOS).

1. Select **Refresh Config File and Reload Servers** and check the **Servers** section for updated status and messages.

If the server works in a terminal or MCP Inspector but still fails only in Unity, file a [Unity bug report](https://support.unity.com/hc/en-us/articles/206336985-How-do-I-submit-a-bug-report) and include your configuration file, server logs, and MCP Inspector output.

## Tools don't appear after configuration refresh

This issue occurs when the MCP server starts successfully, but Unity shows **Tools (0)** or Assistant can’t discover tools from the server.

### Symptoms

You might notice any of the following symptoms:

- The server appears connected but shows **Tools (0)**.
- Assistant doesn’t list tools from the server.
- Assistant can’t call tools from the server, even when prompted.

### Cause

Tool discovery depends on the tool manifest returned by the MCP server.

### Resolution

To resolve this issue, follow these steps:

1. On the **Unity MCP Configuration** page, in the **Server** section, select the server and select **Inspect**.
2. Review the server status, messages, and tool manifest.
3. Validate the server with [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector):

   1. Start the server.
   2. List the available tools.
   3. Verify that tools appear in the tool list.
4. If the tools don't appear in MCP Inspector, fix the MCP server installation or configuration before troubleshooting Unity.
5. If the tools appear in MCP Inspector but Unity shows **Tools (0)**, file a [Unity bug report](https://support.unity.com/hc/en-us/articles/206336985-How-do-I-submit-a-bug-report) and include the MCP configuration file, server logs, and MCP Inspector output.
6. Select **Refresh Config File and Reload Servers**.

## Tool output is incomplete or doesn't match expectations

This issue occurs when Assistant calls a tool, but the tool output is incomplete, unexpected, or doesn't match what you expected.

### Symptoms

You might notice any of the following symptoms:

- Assistant calls a tool but the response misses the expected data.
- The tool returns partial output.
- The tool fails and doesn't return an error.
- Assistant produces an incorrect response after calling the tool.

### Cause

MCP servers are implementation-specific. Different servers require different parameters and handle errors differently. If the server output is incomplete or unclear, Assistant might assume the tool performed successfully.

### Resolution

To resolve this issue, follow these steps:

1. In the Assistant conversation, expand the tool call details to review the tool name, arguments, and tool output.
2. On the **Unity MCP Configuration** page, select the server and select **Inspect** to review the tool manifest and confirm the required parameters and types.
3. Update your prompt with explicit arguments the tool expects, such as:

   - Repository folder path
   - File path
   - Project directory

4. Validate the tool behavior in MCP Inspector using the same parameters and compare the outputs with Assistant.
5. If the server output is incorrect in MCP Inspector, fix the MCP server or report the issue to the server author.

## Server works in MCP Inspector but fails only in Unity

This issue occurs when the MCP server starts successfully in MCP Inspector, but fails when you start it from the Unity Editor.

### Symptoms

You might notice any of the following symptoms:

- The server starts successfully in MCP Inspector and tools list correctly.
- The server fails in Unity after you select **Refresh Config File and Reload Servers**.
- Unity shows errors, but the server command works outside Unity.

### Cause

This can occur due to a Unity integration issue or a mismatch between the Unity configuration and the command used in MCP Inspector.

### Resolution

To resolve this issue, follow these steps:

1. Confirm the server starts successfully in MCP Inspector and tools are available.
1. In the Unity Editor, select **Open Config File** on the **Unity MCP Configuration** page.
1. Update the MCP configuration to match the same command, arguments, and flags that worked in MCP Inspector.
1. Under **Path Configuration**, ensure that **Path Accessible by Unity** includes all the required executable directories.
1. Add any missing directories to **User Path**.
1. Select **Refresh Config File and Reload Servers**.

If the server still fails only in the Unity Editor with an identical command and PATH, file a [Unity bug report](https://support.unity.com/hc/en-us/articles/206336985-How-do-I-submit-a-bug-report) and include your configuration file, server logs,, and MCP Inspector output.

## Additional resources

- [MCP integration in Assistant](xref:mcp-overview)
- [Configure MCP servers](xref:mcp-configure)
- [Unity MCP Configuration page reference](xref:mcp-panel)
- [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector)