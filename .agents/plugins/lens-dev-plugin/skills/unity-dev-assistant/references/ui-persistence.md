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
3. Save the scene.
4. Verify the subtree and refs exist on disk.
5. Only then remove or disable runtime fallback creation.

Do not start by retuning layout values while fallback creation is still active.

## Repair pattern

When a subtree is incomplete:

- preserve complete authored subtrees
- recreate only the missing or incomplete branch
- rebind the controller refs deterministically
- mark the scene dirty and save immediately

This is the shared pattern behind end-screen groups, leaderboard groups, pause overlays, and similar authored HUD clusters.

## Recommended tools

- `Ensure-UnityUiHierarchy.ps1` to create or repair the named UI subtree under a scene root
- `Set-UnitySceneSerializedProperties.ps1` to bind serialized scene refs
- `Set-UnityUiLayout.ps1` to move or resize authored UI after the hierarchy is persistent
- `Unity.UI.GetLayoutSnapshot` to verify authored layout and screen rects

## Verification

After repair, verify both:

- the target subtree exists in the saved scene file
- the controller's serialized refs point at scene objects, not transient runtime objects

If the hierarchy exists on disk but still moves on play, switch back to `authoring-drift.md` and compare edit-mode versus play-mode ownership.
