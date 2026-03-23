---
uid: mcp-panel
---

# Unity MCP Configuration page reference

Use the **Unity MCP Configuration** page to enable the Model Context Protocol (MCP) client, open and edit the configuration file, refresh the settings, and review server status.

For setup, refer to [Configure and use MCP servers](xref:mcp-configure).

The **Unity MCP Configuration** page contains the following tools:

**General Settings**

This section contains the following setting:

| Setting	| Description |
| --------- | ----------- |
| **Enable MCP Tools** | Toggle to enable or disable the MCP client for the current project. |

**Server Configuration**

This section contains the following settings:

| Setting | Description |
| ------- | ----------- |
| **Open Config	File** | Opens the MCP configuration file for editing. |
| **Refresh Config File and Reload Servers** | Reloads the configuration and restarts servers. Use this after you make any edits. |

**Path Configuration**

This section contains the following settings:

| Setting | Description |
| ------- | ----------- |
| **Path Accessible by Unity** | Displays the PATH environment variable as seen by the Unity Editor process. This is often different from your shell PATH. |
| **User Path** | Provide the PATH that MCP servers use when launched from Unity. Paste the full PATH from your terminal or add specific directories (for example, C:\Program Files\Git\bin (macOS: `/opt/homebrew/bin`)) so Unity can resolve the required executables. When you change this field, select **Refresh Config File and Reload Servers** to apply it. |

**Servers**

This section contains the following settings:

| Setting | Description |
| ------- | ----------- |
| **Server Inspector** | Shows server status, messages, tool manifest, and connection details. If the server fails to start or connect, the inspector includes error details and a link to troubleshooting guidance. |
| **Server Name** |	The display name of the server from your configuration. If the name starts with a tilde (`~`), the server is hidden and won’t start. |
| **Server Status** | The current state of the server. For example, `FailedToStart` or `StartedSuccessfully`. |
| **Status Message** | Displays results with error text from the server. For example, `Failed to connect to server`. |
| **Tools** | Shows the number of tools discovered from the server’s manifest. It also lists the available tool names, descriptions, and parameters required by the server.|

## Additional resources

- [Model Context Protocol](xref:mcp-landing)
- [Troubleshooting MCP server issues](xref:mcp-troubleshooting)