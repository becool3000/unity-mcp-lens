---
uid: unity-mcp-get-started
---

# Get started with Unity MCP

Set up Unity MCP and connect your preferred AI client to enable AI-driven control of the Unity Editor.

## Prerequisites

- Unity 6 (6000.0) or later with the `com.unity.ai.assistant` package installed.
- An MCP-compatible AI client such as [Claude Code](https://docs.anthropic.com/en/docs/agents-and-tools/claude-code/overview), [Cursor](https://www.cursor.com/), [Windsurf](https://windsurf.com/), or [Claude Desktop](https://claude.ai/download).

## Step 1: Verify Unity setup

1. Open your Unity project with the AI Assistant package installed.
2. Go to **Edit** > **Project Settings** > **AI** > **Unity MCP**.
3. Confirm the **Unity Bridge** status shows **Running** (green indicator).

The bridge starts automatically when the editor loads. If it shows **Stopped**, select **Start**.

The relay binary is automatically installed to `~/.unity/relay/` when the editor starts. This is the executable that MCP clients launch.

## Step 2: Configure your MCP client

The **Integrations** section in the Unity MCP settings page can automatically configure supported clients. Expand **Integrations**, find your client, and select **Configure**.

Alternatively, configure your client manually using the relay binary path:

### Claude Code

Add the following to your MCP server configuration:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "~/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64",
      "args": ["--mcp"]
    }
  }
}
```

### Cursor

In **Cursor Settings** > **MCP**, add a new server:

```json
{
  "mcpServers": {
    "unity-mcp": {
      "command": "~/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64",
      "args": ["--mcp"]
    }
  }
}
```

### Platform-specific paths

| Platform | Relay executable path |
|----------|-----------------------|
| macOS (Apple Silicon) | `~/.unity/relay/relay_mac_arm64.app/Contents/MacOS/relay_mac_arm64` |
| macOS (Intel) | `~/.unity/relay/relay_mac_x64.app/Contents/MacOS/relay_mac_x64` |
| Windows | `%USERPROFILE%\.unity\relay\relay_win.exe` |
| Linux | `~/.unity/relay/relay_linux` |

The `--mcp` flag is required. It tells the relay binary to operate as an MCP server (as opposed to its other modes).

## Step 3: Approve the connection

When an external MCP client connects for the first time, Unity shows a **Pending Connection** in the Unity MCP settings page. You must approve it before the client can invoke tools.

1. Go to **Edit** > **Project Settings** > **AI** > **Unity MCP**.
2. In the **Pending Connections** section, review the client information.
3. Select **Accept** to approve or **Deny** to reject.

Previously approved clients reconnect automatically without requiring re-approval.

> [!NOTE]
> AI Gateway connections (from the Unity Assistant) are automatically approved and do not require manual approval.

## Step 4: Test the connection

1. Start Unity and open your project.
2. Start your MCP client.
3. Verify the connection:
   - The Unity MCP settings page shows the client under **Connected Clients**.
   - Your MCP client lists Unity MCP tools (for example, `Unity_ManageScene`, `Unity_ManageGameObject`).

### Test with a simple command

In your MCP client, try:

```
Read the Unity console messages and summarize any warnings or errors.
```

The client should use the `Unity_ReadConsole` tool to fetch Unity's console output.

## Additional resources

- [Unity MCP overview](xref:unity-mcp-overview)
- [Register custom MCP tools](xref:unity-mcp-tool-registration)
- [Troubleshoot Unity MCP issues](xref:unity-mcp-troubleshooting)
