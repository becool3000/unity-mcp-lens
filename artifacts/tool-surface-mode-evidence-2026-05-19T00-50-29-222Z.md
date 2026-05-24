# MCP Tool Surface Mode Evidence

Captured: 2026-05-19T00:50:32.360Z
Host: `C:\unity-mcp-lens\UnityMcpLensApp~\src\UnityMcpLens\bin\Debug\net8.0\UnityMcpLens.exe`
Fake full tool count: 80

## Verdict

- Protocol-level static-all hands the client the full tool list at startup: **yes**.
- Direct Codex prompt/context injection observed: **no**. This artifact proves what the MCP host sends to a client over tools/list. It cannot prove whether Codex injects every received tool schema into model context without a Codex-side prompt/tool-snapshot trace.
- Static-all startup register requested the full surface: **yes**.
- Static-all avoided startup pack-restore before first tools/list: **yes**.
- Static `Unity.SetToolPacks(["assets"])` preserved full surface without list-changed: **yes**.

## Startup tools/list

| Mode | Tool count | Response bytes | Approx response tokens | Descriptor bytes | Schema approx tokens |
| --- | ---: | ---: | ---: | ---: | ---: |
| dynamic_packs | 25 | 21401 | 5351 | 21342 | 3797 |
| static_all | 89 | 70775 | 17694 | 70716 | 12426 |

## Representative static-all tools present at startup

- `Unity_Project_PackageCompatibility`: present
- `Unity_Editor_SetPlayMode`: present
- `Unity_Asset_Search`: present
- `Unity_GameObject_Inspect`: present
- `Unity_UI_VerifyScreenLayout`: present
- `Unity_GetLensUsageReport`: present

## Interpretation

If a client inserts every `tools/list` descriptor into model-visible context, `static_all` has the startup cost shown above. If the client keeps tool schemas outside prompt context and exposes them through a native tool table, the protocol payload still grows but model context may not grow by the same amount. This script can prove the first hop; it cannot inspect Codex Desktop's hidden prompt assembly.
