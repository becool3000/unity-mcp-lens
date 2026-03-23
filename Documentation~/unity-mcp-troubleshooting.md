---
uid: unity-mcp-troubleshooting
---

# Troubleshoot Unity MCP issues

Resolve issues that might occur when you set up or use the Unity MCP bridge with external AI clients.

## Bridge doesn't start

### Symptoms

The Unity MCP settings page (**AI** > **Unity MCP**) shows **Stopped** and the bridge does not start automatically.

### Cause

The bridge might fail to start due to script compilation errors, incomplete package installation, or because it was explicitly stopped.

### Resolution

1. Open the Unity **Console** and fix any compilation errors.
2. Go to **Edit** > **Project Settings** > **AI** > **Unity MCP** and select **Start**.
3. Verify the `com.unity.ai.assistant` package is installed in **Window** > **Package Manager**.
4. Restart the Unity Editor if the issue persists.

## MCP client can't connect

### Symptoms

Your AI client (Cursor, Claude Code, etc.) reports connection errors or can't find Unity tools.

### Cause

- Unity isn't running or the bridge didn't start.
- The relay binary is missing from `~/.unity/relay/`.
- The MCP client configuration points to the wrong executable path.
- The `--mcp` flag is missing from the client configuration args.

### Resolution

1. Confirm the bridge is running in **Project Settings** > **AI** > **Unity MCP**.
2. Verify the relay binary exists:
   - **macOS**: `~/.unity/relay/relay_mac_arm64.app/` (or `relay_mac_x64.app/`)
   - **Windows**: `%USERPROFILE%\.unity\relay\relay_win.exe`
   - **Linux**: `~/.unity/relay/relay_linux`
3. Verify your client configuration includes `"args": ["--mcp"]`.
4. Use the **Integrations** section in Unity MCP settings to reconfigure the client.
5. Restart both Unity and your MCP client.

## Connection pending / not approved

### Symptoms

Your MCP client connects but can't invoke any tools. The Unity MCP settings page shows the connection under **Pending Connections**.

### Cause

Direct connections from external MCP clients require user approval. The connection is waiting for you to accept it.

### Resolution

1. Go to **Edit** > **Project Settings** > **AI** > **Unity MCP**.
2. In the **Pending Connections** section, review the client details.
3. Select **Accept** to approve the connection.

Once approved, the client is remembered and reconnects automatically in future sessions.

## Tools not discovered

### Symptoms

The MCP client connects but lists no Unity tools, or some custom tools are missing.

### Cause

- Tool registration scripts have compilation errors.
- The `[McpTool]` attribute is missing or incorrectly applied.
- Custom tools are in assemblies not scanned by `TypeCache`.
- A tool is disabled in the Unity MCP settings.

### Resolution

1. Check the Unity **Console** for compilation errors in tool scripts.
2. Verify that tool methods are `public static` and decorated with `[McpTool]`.
3. For class-based tools, ensure they implement `IUnityMcpTool` or `IUnityMcpTool<T>` and have a parameterless constructor.
4. Check the **Tools** section in Unity MCP settings to ensure tools are enabled.
5. Enable **Show Debug Logs** in Unity MCP settings for detailed discovery information.

## Relay binary not installed

### Symptoms

The relay binary is missing from `~/.unity/relay/` and client configurations fail.

### Cause

The `ServerInstaller` runs at editor startup and copies relay binaries from the package. It might have been interrupted or the package directory is not accessible.

### Resolution

1. Restart the Unity Editor to trigger the installer.
2. Verify the package directory exists: `Packages/com.unity.ai.assistant/RelayApp~/`.
3. Check the Unity **Console** for installation warnings.
4. As a workaround, use the **Locate Server** button in Unity MCP settings to find the bundled binary and copy it manually.

## Performance issues

### Symptoms

Slow responses from Unity tools or timeout errors in the MCP client.

### Cause

Unity might be busy with heavy operations (asset imports, builds, compilation) or system resources are limited.

### Resolution

1. Wait for Unity to finish any ongoing operations (check the progress bar).
2. Restart Unity to clear cached state.
3. Reduce the number of concurrent MCP client connections.
4. Enable **Show Debug Logs** to identify bottlenecks.

## Enable debug logging

For detailed diagnostic information, enable debug logging in the Unity MCP settings page:

1. Go to **Edit** > **Project Settings** > **AI** > **Unity MCP**.
2. Check **Show Debug Logs**.

Debug logs include connection attempts, tool discovery details, command execution traces, and error information.

## Additional resources

- [Unity MCP overview](xref:unity-mcp-overview)
- [Get started with Unity MCP](xref:unity-mcp-get-started)
- [Register custom MCP tools](xref:unity-mcp-tool-registration)
