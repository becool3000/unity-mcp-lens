# TSAM Refactor

TSAM means **Tool, Service, Adapter, Model**.

In Unity MCP Lens, TSAM is the direction for moving broad, hard-to-audit Unity
MCP tools into smaller, typed, telemetry-covered workflows behind explicit tool
packs. It is not a full rewrite. Legacy broad tools stay available while
high-friction workflows move into split tools.

---

## Layers

```text
Tool -> Service -> Adapter -> Model
```

### Tool

The Tool layer is the MCP-facing entry point.

It owns:

- public MCP schema
- input normalization
- service invocation
- compact result shaping
- `normalization` and `result_shaping` telemetry

Tools should keep the model-facing surface narrow and stable. They should not
hide large Unity state dumps in routine inline results.

### Service

The Service layer owns workflow and decision logic.

It plans:

- reads
- previews
- applies
- validation
- verification

Services should decide what should happen before Unity APIs are touched, and
they should keep mutation behavior explicit enough to audit.

### Adapter

The Adapter layer is the Unity API boundary.

It touches Unity surfaces such as:

- GameObjects and components
- scenes and prefabs
- assets and importers
- project settings
- packages
- serialized objects and object references
- editor state, logs, and play-mode state

Adapters should keep Unity reflection and editor API details out of the public
tool contract.

### Model

The Model layer defines typed request, result, plan, and validation structures.

Models keep contracts stable across agents and audits. They avoid drifting
anonymous objects, especially for preview/apply plans and diagnostic results.

---

## Preview And Apply

TSAM mutation tools should prefer preview/apply pairs.

Preview tools:

- are read-only
- validate targets and inputs
- return a deterministic plan or diff
- report whether applying would modify Unity state
- do not dirty or save scenes/assets

Apply tools:

- perform the planned mutation
- report whether anything changed
- dirty durable editor state when content changes
- save scenes/assets only when the tool contract exposes an explicit save request
- return compact readback or validation data

Read-only diagnostic tools should stay read-only. Project/package diagnosis,
input-action inspection, and screen-layout verification are examples where the
agent should inspect first instead of running custom editor code.

---

## Tool Packs

`foundation` is the narrow default surface and is always active.

Pack-specific TSAM work is used to keep the MCP surface small:

- `scene`: split GameObject tools, scene serialized-reference/object-reference assignment, explicit dirty/save tools, prefab instantiate/bind workflows, read-only serialized-reference verification, and scene component reuse discovery.
- `project`: package/import diagnostics, component reuse discovery, Input System diagnostics, input-action asset inspection, and active input handler tools.
- `ui`: uGUI hierarchy/layout preview/apply authoring, canvas prefab authoring, raycast/layout verification, runtime UI query/invoke tools, and screen-layout or resolution-matrix verification.
- `runtime`: play-mode runtime probes, visual bounds snapshots, pointer/scroll input smoke verification, and explicit play-mode exit.
- `assets`: asset/resource workflows, sprite-sheet import/slicing/binding preview/apply, and Sprite-array binding verification.
- `debug`: usage reports, payload analysis, and TSAM stage coverage inspection.

Current metadata baselines are:

- `foundation`: `18` exported tools.
- `foundation + scene`: `48` exported tools.
- `foundation + ui`: `35` exported tools.
- `foundation + runtime`: `29` exported tools.
- `project`: `31` exported tools.
- `foundation + assets`: `31` exported tools.
- `debug`: `28` exported tools.

Pack membership changes should update metadata audit expectations and workflow
docs in the same change.

---

## Compact Outputs And detailRef

TSAM tools should return compact summaries by default.

Large results should expose enough inline data for the next agent decision and
store full detail behind a `detailRef` when the bridge supports it. Agents can
then call `Unity.ReadDetailRef` only when the compact result is insufficient.

This keeps routine tool calls smaller while preserving full detail for audits
and deeper investigation.

Current compact-by-default TSAM result targets include Input System diagnostics,
UI hierarchy preview/apply, scene serialized-reference binding preview/apply,
UI screen-layout verification, `Unity.RunCommand` log blocks, and
`Unity.ReadConsole` summary reads, plus asset sprite-sheet import/bind and
Sprite-array binding verification. These inline results should contain enough
data for pass/fail decisions while moving bulky device, binding, log, corner,
and readback rows behind `detailRef`.

---

## Telemetry Stages

TSAM tools should emit coverage rows for these stages:

- `normalization`
- `service`
- `adapter`
- `result_shaping`

`Unity.GetLensUsageReport` in the `debug` pack is the current way to inspect
payload size, shaping metadata, bridge churn, pack transitions, tool snapshots,
detail refs, and TSAM stage coverage.

Current state: Phase 16 smoke records `NoShapingRecorded=false` and shows
measurable savings for tool snapshots, usage reports, large TSAM tool results,
log-heavy probe/console results, and editor-stability waits. The focused Phase
16 smoke on `D:\TintPaint` shaped `60,520` raw bytes to `48,101` bytes, saving
`12,419` bytes (`20.52%`). `Unity.RunCommand` saved `5,969` bytes (`50.93%`)
and `Unity.ManageEditor.WaitForStableEditor` saved `2,498` bytes (`62.69%`) in
explicit `tool_result` rows.

Use `Invoke-UnityMcpBatch` for repeated smoke/workflow calls that span packs.
The Phase 14 batch smoke ran `9` ordered project/ui/scene/debug steps with `3`
connections, `6` schema requests, `4` pack transitions, and no unmatched
requests or failure rows.

The batch helper now handles unwrapped `Unity.ReadDetailRef` structured payloads
as successful steps and summarizes large detail payloads instead of inlining
them in passing smoke output.

A longer TintPaint dogfood session later shaped `3,012,895` raw bytes to
`650,430` bytes, saving `2,362,465` bytes (`78.41%`). That session also showed
that compact usage reports still need better default summaries for large pack
transition lists and clearer TSAM coverage presentation when coverage rows
exist.

---

## Implemented Surfaces

Current TSAM-covered surfaces include:

- Split GameObject tools for inspect, component reads, preview/apply mutation, create, and delete.
- Project/Input System tools for diagnostics and active input handler preview/apply.
- Package compatibility and input-action asset inspection.
- UI hierarchy/layout preview/apply tools and screen-layout verification.
- UI canvas prefab preview/apply tools and raycast/layout verification.
- Scene serialized-reference preview/apply binding.
- Scene prefab instantiate/bind preview/apply tools.
- Play-mode pointer-input smoke verification.
- Asset sprite-sheet import/bind preview/apply tools and Sprite-array binding verification.
- Compact `Unity.RunCommand` log summaries and `Unity.ReadConsole` summary reads.
- Usage reporting for payload, bridge, pack transition, tool snapshot, detail-ref, and TSAM stage coverage analysis.

Broad legacy tools remain available where split coverage is incomplete.
