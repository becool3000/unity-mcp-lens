# Authoring Drift

Use this when a scene or prefab looks right in edit mode but changes in play mode.

## Typical symptoms

- A child visual lines up in edit mode and jumps on play
- A collider size or offset snaps back at runtime
- A probe, marker, or anchor looks correct in the scene and moves when gameplay starts
- A scene tweak seems to "not stick" even though the asset saved correctly

## Default hypothesis

Assume a runtime owner is rewriting the authored value until proven otherwise.

Common owners:

- tuning `ScriptableObject` values applied on `Awake`, `Start`, or reset paths
- controller components that recompute collider geometry
- animator helpers that reposition child visuals
- scene reset code that teleports objects back to authored spawn markers
- runtime UI fallback builders that recreate groups, panels, or bindings in `Awake` or `OnEnable`

## Fast path

1. Compare the same object in edit mode and play mode with paired direct probes through `Invoke-UnityRunCommand.ps1`.

```powershell
$code = @'
var target = GameObject.Find("Rider");
var child = target != null ? target.transform.Find("OnFootVisual") : null;
var box = target != null ? target.GetComponent<BoxCollider2D>() : null;
result.Log("STATE::target={0};child={1};offset={2};size={3}",
    target != null ? target.transform.localPosition.ToString("F3") : "null",
    child != null ? child.localPosition.ToString("F3") : "null",
    box != null ? box.offset.ToString("F3") : "null",
    box != null ? box.size.ToString("F3") : "null");
'@
$script = Join-Path $PWD ".agents\plugins\lens-dev-plugin\skills\unity-dev-assistant\scripts\Invoke-UnityRunCommand.ps1"
powershell -ExecutionPolicy Bypass -File $script -ProjectPath "$PWD" -Code $code
```

Run the probe once in edit mode, then enter play mode and run the same probe again. Compare the logged values directly.

2. If the values differ, search the repo for writes to the drifting field:
   - `localPosition`
   - `localScale`
   - collider `size`
   - collider `offset`
   - probe or marker transforms
3. Choose one source of truth.
4. If the fix is scene-specific, prefer a scene-owned setup component over pushing the override into global tuning.

## Visual ownership triage order

When a sprite, tint, or scale change does not stick, check the ownership chain in this order:

1. prefab root local scale
2. child renderer local scale
3. serialized authored baseline fields such as `authoredScaleBaseline`
4. runtime-computed multiplier or normalization path
5. final renderer bounds and screen footprint

Use `Get-UnityVisualOwnership.ps1` for the quick snapshot when the package tool is available.

If the issue is an art swap:

1. import or reconfigure the sprite asset
2. bind the serialized sprite reference
3. verify the prefab field with `Verify-UnityPrefabSerializedFields.ps1`
4. verify runtime tint separately
5. only then retune pulse, rotation, or other presentation motion

Do not mix sprite replacement and motion debugging into the same first probe. Split ownership from motion so the failing layer is obvious.

## Runtime-created UI overrides authored UI

If HUD or overlay layout changes do not stick:

1. Search for runtime hierarchy creation or rebinding:
   - `Ensure*Hierarchy`
   - `Create*Group`
   - `AutoAssignReferences`
   - `Awake`
   - `OnEnable`
2. Confirm the desired UI subtree actually exists in the saved scene.
3. If the subtree is missing or partial, repair it as scene-owned UI first.
4. Rebind the controller's serialized refs to the scene objects.
5. Save and verify the scene on disk before removing the runtime fallback.

For authorable UI, persistent scene ownership is usually the correct source of truth. Runtime fallback creation should be a last-resort repair path, not the primary authoring model.

## Heuristic

- Use data assets for systemic behavior: movement numbers, forces, timers, reusable tuning
- Use scene or prefab setup components for scene-authored placement: child visual offsets, collider geometry, probes, anchors, spawn markers

If both systems write the same field, play mode drift is expected.

## Good fix pattern

- Identify the runtime owner
- Preserve the authored value in a dedicated setup component
- Apply the authored value after tuning or reset code runs
- Re-run the edit-versus-play comparison and confirm the values match

## Scene debugger note

If the drift only shows up in a specific UI or screen state, prefer a scene-owned debugger component that can lock the scene into that state deterministically:

- preview the exact authored screen state without mutating real gameplay progression
- capture screenshots from a stable state instead of waiting for timing-sensitive transitions
- add hitbox overlays, binding validation, and click diagnostics close to the authored scene objects
