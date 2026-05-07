# Codex Dynamic Tool Indexing Evidence

Date: 2026-05-06

## Finding

The Lens host correctly changes the MCP tool surface after `Unity.SetToolPacks(["assets"])`, but Codex Desktop did not expose the newly listed asset tools through `tool_search` in the current dogfood thread.

This is different from the earlier server-startup exposure problem. Foundation tools now index after Codex restart. The remaining gap is post-startup dynamic tool refresh after `notifications/tools/list_changed`.

## Raw Host Repro

Run against the installed native host and a live Unity project:

```powershell
Tools~\Export-McpDynamicToolIndexingEvidence.ps1 -ProjectPath D:\BeeSurvivors
```

Optional JSON capture:

```powershell
Tools~\Export-McpDynamicToolIndexingEvidence.ps1 -ProjectPath D:\BeeSurvivors -OutputPath .\artifacts\phase20-dynamic-tool-indexing-evidence.json
```

The script performs this raw MCP sequence:

1. `initialize`
2. `Unity_SetToolPacks([])`
3. `tools/list`
4. `Unity_SetToolPacks(["assets"])`
5. wait for `notifications/tools/list_changed`
6. `tools/list`

The host-side contract passes only when:

- the assets pack switch succeeds
- `notifications/tools/list_changed` is observed
- the second `tools/list` grows from the foundation surface
- the second `tools/list` includes:
  - `Unity_Asset_PreviewImportSpriteSheetAndBind`
  - `Unity_Asset_ApplyImportSpriteSheetAndBind`
  - `Unity_Asset_ImportSpriteSheetAndBind`
  - `Unity_Asset_VerifySpriteArrayBinding`
- `Unity_Asset_VerifySpriteArrayBinding.expectedSpriteNames` has `items: { type: "string" }`

## Codex Comparison

After the raw host evidence passes, run the same pack switch through Codex:

1. Call `Unity.GetLensHealth`.
2. Call `Unity.SetToolPacks(["assets"])`.
3. Search for `Unity_Asset_VerifySpriteArrayBinding` with `tool_search`.

Expected Codex result:

- `tool_search` exposes `Unity_Asset_VerifySpriteArrayBinding`.
- The verifier is callable as a direct Lens tool.

Observed on 2026-05-06 after Unity and Codex restart:

- `Unity.GetLensHealth` succeeded.
- `Unity.SetToolPacks(["assets"])` succeeded with `toolCount=25`.
- Raw installed-host `tools/list` included all Phase 18 asset tools.
- `tool_search` still did not expose `Unity_Asset_VerifySpriteArrayBinding`.

## Interpretation

When the raw host evidence passes and Codex still does not expose the asset verifier, the likely failing layer is Codex Desktop dynamic tool indexing after an MCP `tools/list_changed` notification, not Lens bridge discovery, pack switching, or tool schema generation.
