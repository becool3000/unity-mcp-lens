# Authoring-First Phase 7

Phase 7 makes the authoring-first behavior explicit in Lens skills, examples,
dogfood prompts, smoke workflows, and metadata baselines. The intent is simple:
agents should act like careful Unity developers in the editor before reaching
for generated scripts.

## Policy

- Durable scene and asset work should prefer authored GameObjects, components,
  prefabs, serialized fields, object references, presets, package-backed
  components, and reusable project assets.
- Runtime-created objects are valid for gameplay spawning and temporary Play
  Mode verification. They are not a substitute for production scene, prefab, or
  asset authoring.
- Custom script generation requires a reuse insufficiency report covering
  existing scene objects, prefabs, project scripts, built-in components,
  installed package components, compatible presets, and relevant missing package
  capabilities.
- Edit Mode tools author durable content. Play Mode tools verify behavior.
- Preview durable edit-mode changes before applying them. Save only through an
  explicit save tool or an explicit save contract.

## Metadata Audit Baselines

The current expected pack counts are:

- `foundation`: `21` exported tools.
- `foundation + scene`: `58` exported tools.
- `foundation + ui`: `39` exported tools.
- `foundation + runtime`: `33` exported tools.
- `foundation + project`: `43` exported tools.
- `foundation + assets`: `56` exported tools.

Any pack membership change must update the pack-switch metadata audit, required
tool assertions, this document, and the Lens skills in the same change.

## Authoring Examples

### Camera

Use `Unity.Authoring.SuggestReusePlan` for the intent first. If no existing
camera or prefab solves it, use `Unity.GameObject.PreviewCreate` with
`objectKind=camera`, then `Unity.GameObject.Create`, and read
`Unity.Scene.GetDirtyState`. Use `Unity.GameObject.PreviewChanges` and
`Unity.GameObject.ApplyChanges` for transform or serialized camera fields. Do
not call `Unity.Scene.Save` unless the user explicitly wants the scene saved.

### Light

Resolve whether an existing light, preset, or package-backed lighting solution
matches the request. If new authored content is needed, create it through
`Unity.GameObject.PreviewCreate` and `Unity.GameObject.Create` with
`objectKind=light`. Tune fields through preview/apply property tools and report
the dirty state after each mutation.

### UI Canvas And EventSystem

Prefer authored uGUI objects over runtime fallback hierarchy creation. Create
the canvas with `objectKind=canvas`, create the event system with
`objectKind=eventSystem`, then configure UI layout through the UI preview/apply
tools. Verify layout through `Unity.UI.VerifyScreenLayout` or
`Unity.UI.VerifyScreenLayoutMatrix`.

### Reusable Prefab

Prefer project or package prefabs when discovery finds a match. For a new
reusable prefab, author a scene object through preview/apply tools, then use
`Unity.Prefab.CreateFromSceneObject` or the prefab authoring workflow. Inspect
the resulting prefab with `Unity.Prefab.Inspect` and report scene dirty state
separately from prefab asset state.

### Prefab Overrides

Instantiate with `Unity.Prefab.Instantiate`, modify instance fields with
`Unity.Prefab.SetSerializedProperties` or scene property tools as appropriate,
then call `Unity.Prefab.GetOverrides`. Use
`Unity.Prefab.ExplainOverrides`, `Unity.Prefab.PreviewApplyOverrides`, or
`Unity.Prefab.PreviewRevertOverrides` before applying selected overrides. Broad
apply/revert and nested prefab risks must be reported before mutation.

### Sprite Atlases And UI Slices

After importing or generating UI sprite sheets, prefer
`Unity.Asset.VerifySpriteSlicesAndReferences` before hand-reading importer
metadata or prefab YAML. It verifies importer settings, expected slice
names/rects/alpha, and prefab `Image`/`SpriteRenderer` references without
reimporting, saving, or mutating assets.

### Package-Backed Component

Use `Unity.Component.ResolveCapability` and `Unity.Package.ResolveCapability`
for the requested behavior. If a missing package is the best match, call
`Unity.Package.PreviewInstallForCapability` and wait for explicit approval
before installation. If the package is installed, inspect the component schema
with `Unity.Component.InspectSchema`, then add and configure it through
component preview/apply tools.

### Preset Or Copy-From-Existing

For "make this like that one" requests, search presets with
`Unity.Preset.Search`, inspect compatible presets, preview reusable preset
creation with `Unity.Preset.PreviewCreate` when no compatible preset exists, and
preview application with `Unity.Preset.PreviewApplyToComponent`. For
object-to-object copying, use `Unity.Scene.PreviewCopyComponentSerializedValues` or
`Unity.Prefab.PreviewCopyComponentSerializedValues`, then apply only accepted
field changes with an explicit reference policy.

## Dogfood Prompts

These prompts should fail the dogfood run if the agent writes or proposes a
custom script before a reuse check and reuse insufficiency report.

- "Make the main camera follow the player smoothly."
  Expected first path: `Unity.Authoring.SuggestReusePlan`,
  `Unity.Component.ResolveCapability`, scene component search, prefab/preset
  search, then existing component configuration or a reuse insufficiency report.
- "Add gamepad movement input to this project."
  Expected first path: Input System diagnostics, capability/package resolution,
  input-action inspection, and package install preview when needed.
- "Add a pause menu with a button and keyboard/gamepad navigation."
  Expected first path: canvas/EventSystem authoring, UI hierarchy/layout tools,
  existing/preset button styling, and Play Mode verification.
- "Make this prefab variant use a different material but keep nested prefab
  boundaries safe."
  Expected first path: prefab inspect, override listing, selected override
  preview, and explicit nested-risk reporting.
- "Make this light match the hallway light."
  Expected first path: component schema inspection plus preset or copy
  serialized values workflow with an explicit object-reference policy. If a
  reusable preset should be captured first, use preset preview/create rather
  than `Unity.RunCommand`.
- "Use Cinemachine for a follow camera if available."
  Expected first path: component/package capability resolution, missing package
  preview if absent, component schema inspection, then authoring tools.

## Smoke Workflows

### Scene Authoring And Save Discipline

1. Preview and create an empty object, primitive, camera, light, canvas, and
   EventSystem through `Unity.GameObject.PreviewCreate` and
   `Unity.GameObject.Create`.
2. After every create/apply call, read or assert the returned dirty state and
   call `Unity.Scene.GetDirtyState`.
3. Verify no scene save occurs until `Unity.Scene.Save` is called explicitly.

### Component Reuse Before Script

1. Run `Unity.Authoring.SuggestReusePlan` for a "follow camera" intent.
2. Inspect candidate schemas with `Unity.Component.InspectSchema`.
3. Search the current scene with `Unity.Scene.FindComponents`.
4. Confirm the result names provider, confidence, setup requirements, schema
   availability, and whether a script is actually necessary.

### Package Capability Awareness

1. Resolve a package-backed capability such as Cinemachine, Input System,
   TextMeshPro, URP, UI Toolkit, or NavMesh.
2. If missing, preview installation only and report package id, version,
   compatibility, install risk, compile/import impact, and fallback plan.
3. Do not install until explicitly approved.

### Prefab Override Safety

1. Inspect a prefab and instantiate it into a scene.
2. Mutate one instance field, then list overrides with
   `Unity.Prefab.GetOverrides`.
3. Preview applying one selected override and preview reverting another.
4. Verify broad apply/revert and nested prefab risks appear before mutation.
5. Report scene dirty state separately from prefab asset dirty state.

### Preset And Copy

1. Search and inspect compatible presets for a camera, light, renderer, layout,
   or button.
2. When capturing a reusable component setup, preview/create the Preset asset
   with `Unity.Preset.PreviewCreate` and `Unity.Preset.Create`.
3. Preview preset application or serialized-value copy.
4. Verify incompatible fields are skipped with explanations.
5. Apply only accepted fields and report reference handling through
   `referencePolicy`.

### Play Mode Verification

1. Finish durable Edit Mode authoring first.
2. Enter Play Mode through Lens play-mode tools or wrappers.
3. Verify behavior with runtime snapshots, safe method invocation, temporary
   smoke harnesses, console deltas, or Game view captures.
4. Treat runtime temporary components as test equipment only; do not save scenes,
   prefabs, or production assets from runtime verification.
