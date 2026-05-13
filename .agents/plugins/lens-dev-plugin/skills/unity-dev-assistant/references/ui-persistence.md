# UI Persistence

Use this when the user wants to move, resize, or restyle HUD/UI directly in the scene and the changes do not survive play mode.

## Default hypothesis

Assume a runtime fallback is recreating or rebinding the UI hierarchy until proven otherwise.

Typical signals:

- the scene looks right in edit mode but a different layout appears in play mode
- serialized refs on the controller are null or point to transient objects
- `Awake`, `OnEnable`, or reset helpers contain methods like `Ensure*Hierarchy`, `Create*Group`, or `AutoAssignReferences`
- the desired UI subtree is missing from the saved scene file

## Preferred fix order

1. Ensure the UI subtree exists as scene-owned objects.
2. Bind serialized refs on the scene controller to those scene objects.
3. Preview the durable edit, apply the accepted change, and save only when persistence is explicitly requested or accepted.
4. Verify the subtree and refs exist on disk.
5. Only then remove or disable runtime fallback creation.

Do not start by retuning layout values while fallback creation is still active.

## Repair pattern

When a subtree is incomplete:

- preserve complete authored subtrees
- recreate only the missing or incomplete branch through UI or GameObject preview/apply tools
- rebind the controller refs deterministically through scene reference assignment tools
- report scene dirty state after the mutation and save only through `Unity.Scene.Save` when requested

This is the shared pattern behind end-screen groups, leaderboard groups, pause overlays, and similar authored HUD clusters.

## Recommended tools

- `Unity.GameObject.PreviewCreate` / `Unity.GameObject.Create` with `objectKind=canvas` or `eventSystem` for missing scene-owned UI roots
- `Unity.UI.PreviewEnsureHierarchy` / `Unity.UI.ApplyEnsureHierarchy` to create or repair named UI subtrees under a scene root
- `Unity.Scene.PreviewAssignObjectReferences` / `Unity.Scene.ApplyAssignObjectReferences` to bind serialized scene refs
- `Unity.Scene.VerifySerializedReferences` to verify nested prefab instance refs without confusing inherited prefab refs for nulls
- `Unity.UI.PreviewLayoutProperties` / `Unity.UI.ApplyLayoutProperties` to move or resize authored UI after the hierarchy is persistent
- `Verify-UnityUiScreenLayout.ps1`, `Unity.UI.VerifyScreenLayout`, or `Unity.UI.VerifyScreenLayoutMatrix` to verify authored layout assertions and screen rects
  Use strict `below`/`above` for non-overlap relations, and `below_center`/`above_center` for in-card label placement such as count text inside quick-slot HUD cards.

Use the helper scripts only when direct tool exposure is unavailable in the current client session; the helpers should stay on the same Lens preview/apply path.

## Verification

After repair, verify both:

- the target subtree exists in the saved scene file
- the controller's serialized refs point at scene objects, not transient runtime objects

If the hierarchy exists on disk but still moves on play, switch back to `authoring-drift.md` and compare edit-mode versus play-mode ownership.
