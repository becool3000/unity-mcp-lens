# Changelog

All notable Unity MCP Lens package changes are documented here.

## [0.1.0-alpha.1] - 2026-04-20

### Fixed

- Stabilized fresh Unity project imports by bundling a coherent Roslyn `3.11` dependency family.
- Added missing managed dependency coverage for the Roslyn support DLLs.
- Scoped bundled Roslyn/support DLLs to Editor import targets to avoid player/runtime exposure.
- Declared package dependencies needed by runtime/editor code, including Unity's Newtonsoft.Json package.

### Changed

- Reset package versioning to the standalone Unity MCP Lens alpha line.
- Split Unity MCP Lens into the standalone package id `com.becool3000.unity-mcp-lens`.
- Renamed owned assemblies and namespaces to `Becool.UnityMcpLens.*`.
- Moved active package code to `Editor/Lens` and `Runtime`.
- Removed copied Assistant UI/runtime/cloud/generator/search/sample folders from the standalone package.
- Removed the bundled legacy relay binaries from Lens; Lens now installs only the owned `unity-mcp-lens` server.
- Added migration guidance and package identity static checks.

### Preserved

- Standard MCP server identity `unity-mcp-lens`.
- Tool names, tool packs, detail refs, schema caching, and compact health surface.
- Editor access under `Tools > Unity MCP Lens` and `Project Settings > Tools > Unity MCP Lens`.
