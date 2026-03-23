---
uid: mcp-configure
---

# Configure MCP servers

Configure Model Context Protocol (MCP) servers and use their tools in Assistant conversations.

When you configure MCP, Assistant can find and use tools provided by the MCP servers that run on your local machine. This helps when Assistant needs access to external systems, such as Git, local files, or custom automation tools.

## Prerequisites

Before you start, make sure you meet the following prerequisites:

- Understand and know how to use the preferred MCP server.
- Install the required MCP server and any required dependencies on your machine.
- Confirm the server command runs successfully in a terminal.
- (Recommended) [Install and use MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector) to validate server behavior outside Unity.

To configure the MCP servers, follow these steps:

1. In the Unity Editor, select **Edit** > **Project Settings** > **AI** > **MCP Servers**.

   The **Unity MCP Configuration** page opens.
2. Select **Open Config File** to open the MCP configuration file.

3. In the configuration file, enable MCP and add or edit the server entries.

   The MCP configuration file uses the following structure:

   | JSON field | Type | Description |
   | ---------- | ---- | ----------- |
   | `enabled` | Boolean | Enables or disables MCP integration in Unity. If `false`, Unity doesn’t start or connect to MCP servers. |
   | `path` | String | Extra PATH entries that Unity uses when it starts the MCP servers. On macOS/Linux, separate the entries with `:`. On Windows, separate the entries with `;`. |
   | `mcpServers` | Object | Collection of MCP servers to which Unity can connect. Each key is the display name of the server. Prefix a server name with `~` to hide it from Unity. |
   | `mcpServers.<serverName>.command` | String | Required for `stdio` servers. Executable that Unity runs to start the MCP server. This must match the command that works in your terminal or MCP Inspector. |
   | `mcpServers.<serverName>.args` | Array of strings |	Optional for `stdio` servers. It specifies the arguments passed to the command. Include the same flags and arguments you validated in a terminal or MCP Inspector. |
   | `mcpServers.<serverName>.env` | Object | Optional for `stdio` servers. It specifies the environment variables passed to the MCP server process. |
   | `mcpServers.<serverName>.type`	| String | Specifies the transport type. It must be `stdio` for local servers. |

   > [!NOTE]
   > To keep a server in your configuration file but prevent Unity from starting it, prefix the server name with a tilde (~). For more information, refer to [Hide a server from Unity with ~](#hide-a-server-from-unity-with-).

4. Save the configuration file.

5. Go back to the **Unity MCP Configuration** page and configure the PATH values under the **Path Configuration** section.


   1. Review **Path accessible by Unity** to verify the PATH that the Unity Editor currently uses.

   2. In **User Path**, paste the full shell PATH or add directories for the required executables so that Unity can resolve the same toolchain as your terminal.

      For example, if the server uses `uvx`, ensure Unity can resolve the `uvx` executable path (such as `/opt/homebrew/bin` on macOS).

   > [!NOTE]
   > Some servers require multiple executables to be available. If a server works in your terminal but fails in Unity, the issue is commonly related to PATH. For more information, refer to [Troubleshooting MCP server issues](xref:mcp-troubleshooting).

6. Select **Refresh Config File and Reload Servers** to apply changes and start the MCP server.

   The **Servers** section lists each server with its status.
7. Select a server and then select **Inspect**.

   - If the server start successfully, **Inspect** shows the server details and the tools it exposes.
   - If the server fails to start or connect, **Inspect** shows the error details and logs.
8.  If you encounter an error, select [**View troubleshooting documentation**](xref:mcp-troubleshooting).

You have successfully configured the MCP servers and can start to use their tools in Assistant.

## Example configuration file

The following example shows a typical configuration file. Replace values with the command and arguments required by your MCP server.

```
{
  "enabled": true,
  "path": "",
  "mcpServers": {
    "git": {
      "type": "stdio",
      "command": "uvx",
      "args": [
        "mcp-server-git"
      ],
      "env": {}
    }
  }
}
```

## Hide a server from Unity

If you want to keep an MCP server in your configuration but don't want Unity discover or start it, follow these steps:

1. In the MCP configuration file, prefix the server name with a tilde (**~**), for example, `~Example Git MCP`.
2. Save the configuration and select **Refresh Config File and Reload Servers**.

   Unity hides the server entry.

## Use MCP tools in Assistant

After your server is running, you can use it through Assistant to ask questions that require external tool calls.

For example, `Use the Git MCP server to show the last 10 commits in this repository: <path>.` or `Summarize recent changes in the repo at <path>.`.

If Assistant uses the wrong folder or returns unexpected output, include explicit paths and confirm expected tool parameters in the MCP Client inspector.

## Additional resources

- [Unity MCP Configuration page reference](xref:mcp-panel)
- [Troubleshooting MCP server issues](xref:mcp-troubleshooting)
- [MCP Inspector](https://modelcontextprotocol.io/docs/tools/inspector)