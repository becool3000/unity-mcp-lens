# unity-mcp-lens

Active TSAM refactor. Stable for experimentation and real workflows, but still evolving.

`unity-mcp-lens` is a token-conscious MCP bridge for Unity. It gives coding agents like Codex, Claude Code, Cursor, and other MCP clients a smaller, safer way to inspect and change Unity projects without constantly falling back to noisy custom editor probes.

The core idea is **TSAM**: **Tool, Service, Adapter, Model**.

TSAM turns broad Unity MCP tools into compact, typed, auditable workflows:

- smaller MCP surfaces by default  
- safer preview/apply mutation flows  
- compact outputs with `detailRef` expansion for large data  
- typed contracts that are easier to keep stable  
- telemetry for payload size, bridge churn, pack churn, recovery events, and TSAM stage coverage  

The result is less custom `Unity.RunCommand` code, fewer oversized Unity payloads, and more predictable agent behavior while Unity recompiles, reloads domains, enters play mode, imports packages, or mutates serialized scene state.

---

## Package

```json
"com.becool3000.unity-mcp-lens"
```

Editor UI lives under **Tools > Unity MCP Lens** and **Project Settings > Tools > Unity MCP Lens**.

---

## TSAM Refactor Direction

TSAM means **Tool, Service, Adapter, Model**.

- **Tool**: MCP-facing entry point. Normalizes inputs, calls service, shapes results, emits telemetry.  
- **Service**: Workflow layer. Plans reads, previews, applies, validation, verification.  
- **Adapter**: Unity API boundary. Touches GameObjects, assets, settings, serialized state, editor systems.  
- **Model**: Typed request/result/plan structures that keep contracts stable.  

This is an incremental refactor. Legacy tools remain available while high-use workflows move into TSAM tool packs.

---

## Why This Matters

Broad Unity tools are expensive:

- too much surface area  
- large payloads  
- unclear failure states  

TSAM reduces that with:

- smaller outputs  
- clearer workflows  
- safer mutation patterns  
- measurable telemetry  

---

## Quick Start

1. Clone this repo  

2. Add to Unity `Packages/manifest.json`:

```json
"com.becool3000.unity-mcp-lens": "file:../unity-mcp-lens"
```

3. In Unity:  
**Tools > Unity MCP Lens > Install/Refresh Lens Server**

4. Point your MCP client to:

```
~/.unity/unity-mcp-lens/<platform binary>
```

Example:

```
C:\Users\<you>\.unity\unity-mcp-lens\unity_mcp_lens_win.exe
```

5. Verify:

```powershell
powershell -ExecutionPolicy Bypass -File .agents/plugins/lens-dev-plugin/skills/unity-dev-assistant/scripts/Check-UnityDevSession.ps1 -ProjectPath C:\Path\To\UnityProject
```

---

## First Proof

Run one simple workflow:

- Activate `project` pack  
- Run `Unity.InputSystem.Diagnostics`  
- Inspect compact result  
- Expand with `detailRef` if needed  
- Run `Unity.GetLensUsageReport`  

This shows the TSAM loop: narrow tool → compact result → optional expansion → telemetry.

For repeated smoke or workflow checks, use the repo-local batch helper so
multiple project/ui/scene/debug calls share one Lens session:

```powershell
powershell -ExecutionPolicy Bypass -File .agents/plugins/lens-dev-plugin/skills/unity-dev-assistant/scripts/Invoke-UnityMcpBatch.ps1 -ProjectPath C:\Path\To\UnityProject -StepsPath C:\Path\To\steps.json
```

---

## Example Workflow

- Activate `project` pack  
- Run diagnostics tools  
- Use compact result first  
- Expand only when needed  
- Validate using telemetry  

---

## What Lens Owns

- MCP stdio server  
- Unity bridge  
- Tool packs  
- Compact outputs  
- Telemetry system  

---

## Before vs After TSAM

**Before**
- broad tools  
- large payloads  
- unclear mutation paths  

**After**
- explicit packs  
- typed models  
- preview/apply flows  
- compact outputs  
- telemetry visibility  

---

## Benchmark And Telemetry

TSAM is measured, not assumed.

Telemetry includes:

- payload size  
- bridge churn  
- tool usage  
- stage coverage  

Telemetry is part of the TSAM refactor. `Unity.GetLensUsageReport` reports
payload rows, bridge request/response rows, pack transitions, tool snapshots,
detail refs, shaping metadata, and TSAM stage coverage.

Existing dogfood data is tracked in [docs/Telemetry](docs/Telemetry). Current
recorded signals include:

- Phase 11 focused smoke on Unity `6000.4.3f1`: metadata audit passed; compact rerun span was `44` rows; bridge churn was `1` connection, `0` setup cycles, and `0` unmatched requests; TSAM coverage was complete for package compatibility, input-actions inspection, diagnostics, preview, and set.
- Phase 12 helper-driven smoke on Unity `6000.4.3f1`: metadata audit passed with `foundation=12`, `foundation+scene=32`, `foundation+ui=22`, `project=21`, and `debug=22`; rerun scope was `358` rows; bridge churn was `25` connections, `0` setup cycles, and `0` unmatched requests; TSAM coverage was complete for UI hierarchy, scene binding, layout, and verify.
- Phase 13 payload-shaping smoke on Unity `6000.4.3f1`: rerun scope was `244` rows; payload size was `210,510` raw bytes -> `120,867` shaped bytes; recorded savings were `89,643` bytes (`42.58%`); `NoShapingRecorded=false`; the largest measured win was tool snapshot shaping at `100,016` raw bytes -> `9,481` shaped bytes.
- Phase 14 compact-result smoke on Unity `6000.4.3f1`: batch rerun scope was `98` rows; payload size was `50,566` raw bytes -> `24,025` shaped bytes; recorded savings were `26,541` bytes (`52.49%`); `7` eligible rows saved bytes; the batch helper reduced churn to `3` connections, `6` schema requests, and `4` pack transitions.
- Phase 15 log-compaction smoke on Unity `6000.4.3f1`: focused scope was `27` rows; payload size was `56,370` raw bytes -> `39,650` shaped bytes; recorded savings were `16,720` bytes (`29.66%`); `Unity.RunCommand` saved `11,433` bytes (`65.69%`) and `Unity.ReadConsole` saved `2,219` bytes (`77.00%`) in explicit `tool_result` rows.
- Payload shaping is still underway for remaining editor-state edge cases, but the high-volume TSAM result path and log-heavy probe path now default to compact inline data with full data behind `detailRef`.

Future benchmark reports should include:

```text
Scope:
Unity version:
Host project:
Tool packs:
Payload size:
Result shaping savings:
Tool calls:
Bridge/session churn:
Pack transitions:
Error/recovery events:
TSAM stage coverage:
Known caveats:
```
---

## Status

Active refactor.

Stable foundation. Expanding TSAM coverage and payload shaping.

---

## License

MIT
