# unity-mcp-lens

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

---

## Status

Active refactor.

Stable foundation. Expanding TSAM coverage and payload shaping.

---

## License

MIT
