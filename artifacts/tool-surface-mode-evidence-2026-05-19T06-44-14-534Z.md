# MCP Tool Surface Mode Evidence

Captured: 2026-05-19T06:44:24.389Z
Host: `C:\unity-mcp-lens\UnityMcpLensApp~\src\UnityMcpLens\bin\Debug\net8.0\UnityMcpLens.exe`
Fake full tool count: 80

## Verdict

- Protocol-level static-all hands the client the full tool list at startup: **yes**.
- Client-resilient facade escape hatch ready: **yes**.
- Foundation exposes List/Invoke/BatchInvoke facades: **yes**.
- Static-all exposes representative missed native tools: **yes**.
- Menu/List/Describe fallback guidance present: **yes**.
- Repo-local plugin manifest fresh: **yes**.
- Direct Codex prompt/context injection observed: **no**. This artifact proves what the MCP host sends to a client over tools/list. It cannot prove whether Codex injects every received tool schema into model context without a Codex-side prompt/tool-snapshot trace.
- Static-all startup register requested the full surface: **yes**.
- Static-all avoided startup pack-restore before first tools/list: **yes**.
- Static `Unity.SetToolPacks(["assets"])` preserved full surface without list-changed: **yes**.

## Startup tools/list

| Mode | Tool count | Response bytes | Approx response tokens | Descriptor bytes | Schema approx tokens |
| --- | ---: | ---: | ---: | ---: | ---: |
| dynamic_packs | 28 | 22631 | 5658 | 22572 | 3947 |
| static_all | 89 | 69667 | 17417 | 69608 | 12172 |

## Facade presence

| Mode | Unity_Tools_List | Unity_Tools_Invoke | Unity_Tools_BatchInvoke |
| --- | --- | --- | --- |
| dynamic_packs startup | present | present | present |
| static_all startup | present | present | present |

## Representative static-all tools present at startup

- `Unity_Tools_List`: present
- `Unity_Tools_Invoke`: present
- `Unity_Tools_BatchInvoke`: present
- `Unity_Project_PackageCompatibility`: present
- `Unity_Project_BlockedLanguageScan`: present
- `Unity_Tests_Run`: present
- `Unity_Editor_SetPlayMode`: present
- `Unity_Asset_Search`: present
- `Unity_GameObject_Inspect`: present
- `Unity_UI_VerifyScreenLayout`: present
- `Unity_GetLensUsageReport`: present

## Plugin manifest discovery hint

- Path: `.agents/plugins/lens-dev-plugin/manifest.json`
- Fresh against generator: **yes**
- Tool count: 154
- Source of truth: `discovery_hint_only`
- Execution source of truth: `Lens host tools/list and Unity bridge manifest`
- Static-all configured: **yes**

Required manifest tools:

- `Unity_Tools_List`: present
- `Unity_Tools_Invoke`: present
- `Unity_Tools_BatchInvoke`: present
- `Unity_Tools_Describe`: present
- `Unity_Tools_Menu`: present
- `Unity_Project_BlockedLanguageScan`: present
- `Unity_Tests_Run`: present

## Facade fallback guidance

- `menu`: fallback=yes, invoke=yes, batch=yes
- `listFacade`: fallback=yes, invoke=yes, batch=yes
- `describe`: fallback=yes, invoke=yes, batch=yes

## Resilience interpretation

- Escape hatch ready: **yes**.
- `Unity_Tools_List` gives clients a compact live index when direct tool tables are stale.
- `Unity_Tools_Invoke` and `Unity_Tools_BatchInvoke` keep missed native tools callable through one stable surface.

## Interpretation

If a client inserts every `tools/list` descriptor into model-visible context, `static_all` has the startup cost shown above. If the client keeps tool schemas outside prompt context and exposes them through a native tool table, the protocol payload still grows but model context may not grow by the same amount. This script can prove the first hop; it cannot inspect Codex Desktop's hidden prompt assembly.
