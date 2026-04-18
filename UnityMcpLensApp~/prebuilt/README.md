# Unity MCP Lens Prebuilt Server Artifacts

Release builds should place self-contained `unity-mcp-lens` server artifacts here so Unity can install the server without requiring a local .NET SDK.

Expected layout:

```text
UnityMcpLensApp~/prebuilt/osx-x64/unity_mcp_lens_mac_x64
UnityMcpLensApp~/prebuilt/osx-arm64/unity_mcp_lens_mac_arm64
UnityMcpLensApp~/prebuilt/win-x64/unity_mcp_lens_win.exe
UnityMcpLensApp~/prebuilt/linux-x64/unity_mcp_lens_linux
```

The Unity installer first looks for `prebuilt/<runtime-identifier>/`, copies that directory to `~/.unity/unity-mcp-lens/`, reconciles the expected binary name, copies `unity-mcp-lens.json`, and sets executable permissions on macOS/Linux. If no matching prebuilt directory exists, it falls back to `dotnet publish`.

When committing binary artifacts, store them through Git LFS and preserve executable bits for macOS/Linux files.
