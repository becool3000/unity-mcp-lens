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
- persist scene changes when the tool contract says it does
- return compact readback or validation data

Read-only diagnostic tools should stay read-only. Project/package diagnosis,
input-action inspection, and screen-layout verification are examples where the
agent should inspect first instead of running custom editor code.

---

## Tool Packs

`foundation` is the narrow default surface and is always active.

Pack-specific TSAM work is used to keep the MCP surface small:

- `scene`: split GameObject tools and scene serialized-reference binding.
- `project`: package/import diagnostics, Input System diagnostics, input-action asset inspection, and active input handler tools.
- `ui`: uGUI hierarchy/layout preview/apply authoring and screen-layout verification.
- `debug`: usage reports, payload analysis, and TSAM stage coverage inspection.

Current metadata baselines are:

- `foundation`: `12` exported tools.
- `foundation + scene`: `32` exported tools.
- `foundation + ui`: `22` exported tools.
- `project`: `21` exported tools.
- `debug`: `22` exported tools.

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

Known current gap: focused smoke reports still show `NoShapingRecorded=true`,
so payload shaping is underway and should not be described as complete.

---

## Implemented Surfaces

Current TSAM-covered surfaces include:

- Split GameObject tools for inspect, component reads, preview/apply mutation, create, and delete.
- Project/Input System tools for diagnostics and active input handler preview/apply.
- Package compatibility and input-action asset inspection.
- UI hierarchy/layout preview/apply tools and screen-layout verification.
- Scene serialized-reference preview/apply binding.
- Usage reporting for payload, bridge, pack transition, tool snapshot, detail-ref, and TSAM stage coverage analysis.

Broad legacy tools remain available where split coverage is incomplete.

